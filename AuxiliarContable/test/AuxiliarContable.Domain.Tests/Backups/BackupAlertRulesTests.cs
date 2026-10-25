using System;
using AuxiliarContable.Backups;
using AuxiliarContable.Notifications;
using Shouldly;
using Xunit;

namespace AuxiliarContable;

/// <summary>
/// El criterio que decide si hay que avisar de las copias.
///
/// <para>Se prueba aparte del worker a propósito: el worker solo lee archivos y escribe
/// avisos, pero <b>aquí</b> está la decisión, y una decisión mal puesta produce las dos
/// averías caras — callarse cuando la copia lleva semanas rota, o avisar cada seis horas
/// hasta que nadie lea los avisos.</para>
/// </summary>
public class BackupAlertRulesTests
{
    private static readonly DateTime Ahora = new(2026, 8, 26, 12, 0, 0, DateTimeKind.Utc);

    private static BackupStatus Copia(
        double haceHoras = 2, bool ok = true, string remoto = "ok", string etapa = "completado")
        => new()
        {
            LastRunUtc = Ahora.AddHours(-haceHoras),
            Ok = ok,
            Remote = remoto,
            Stage = etapa,
            Tables = 50
        };

    private static RestoreCheckStatus Verificacion(double haceDias = 5, bool ok = true)
        => new() { LastRunUtc = Ahora.AddDays(-haceDias), Ok = ok, Message = ok ? "ok" : "reventó" };

    [Fact]
    public void Con_todo_en_orden_no_se_avisa_de_nada()
    {
        // Lo que mas importa de un vigilante: callarse cuando no pasa nada. Si avisara de
        // rutina, el aviso del dia malo pasaria desapercibido entre los demas.
        var (titulo, _, _) = BackupAlertRules.Evaluar(Copia(), Verificacion(), Ahora);

        titulo.ShouldBeNull();
    }

    [Fact]
    public void Sin_archivo_de_estado_se_avisa()
    {
        // Es el caso mas facil de pasar por alto: no es que la copia falle, es que NADIE
        // la esta haciendo. Sin este aviso, "no hay noticias" pareceria buena noticia.
        var (titulo, _, sev) = BackupAlertRules.Evaluar(null, null, Ahora);

        titulo.ShouldNotBeNull();
        titulo!.ShouldContain("No hay ninguna copia");
        sev.ShouldBe(NotificationSeverity.Error);
    }

    [Fact]
    public void Una_copia_fallida_se_avisa_con_la_etapa()
    {
        var (titulo, cuerpo, sev) = BackupAlertRules.Evaluar(
            Copia(ok: false, etapa: "remoto"), Verificacion(), Ahora);

        // La etapa va en el titulo porque es lo que dice por donde empezar a mirar.
        titulo!.ShouldContain("remoto");
        cuerpo!.ShouldNotBeNullOrWhiteSpace();
        sev.ShouldBe(NotificationSeverity.Error);
    }

    [Fact]
    public void Una_copia_dentro_del_margen_no_se_considera_caducada()
    {
        // 30 h esta por encima de las 24 de la copia diaria pero por debajo del umbral:
        // el temporizador lleva un desfase aleatorio y un umbral de 24 h daria avisos
        // falsos por rozar el limite.
        BackupAlertRules.Evaluar(Copia(haceHoras: 30), Verificacion(), Ahora)
            .Titulo.ShouldBeNull();
    }

    [Fact]
    public void Pasado_el_umbral_si_se_avisa()
    {
        var (titulo, _, sev) = BackupAlertRules.Evaluar(
            Copia(haceHoras: BackupConsts.MaxBackupAgeHours + 1), Verificacion(), Ahora);

        titulo!.ShouldContain("demasiado tiempo");
        sev.ShouldBe(NotificationSeverity.Error);
    }

    [Fact]
    public void Una_copia_que_no_sale_del_servidor_se_avisa()
    {
        // Se hace bien, pero solo protege de un borrado accidental. Es media proteccion, y
        // media proteccion sin saberlo es peor que ninguna: se confia en ella.
        var (titulo, _, sev) = BackupAlertRules.Evaluar(
            Copia(remoto: "sin-gancho"), Verificacion(), Ahora);

        titulo!.ShouldContain("no está saliendo del servidor");
        sev.ShouldBe(NotificationSeverity.Error);
    }

    [Fact]
    public void Lo_mas_grave_manda_cuando_falla_mas_de_una_cosa()
    {
        // Copia fallida Y verificacion fallida a la vez: se cuenta la copia, que es lo que
        // hay que arreglar primero. Un aviso por cada cosa seria una avalancha justo
        // cuando hace falta claridad.
        var (titulo, _, _) = BackupAlertRules.Evaluar(
            Copia(ok: false), Verificacion(ok: false), Ahora);

        titulo!.ShouldContain("fallando");
    }

    [Fact]
    public void Una_verificacion_fallida_se_avisa_aunque_la_copia_este_bien()
    {
        // Es el caso traicionero: todo parece correcto —hay copias recientes y suben— pero
        // no sirven. Sin esto se descubriria el dia de restaurar de verdad.
        var (titulo, cuerpo, sev) = BackupAlertRules.Evaluar(
            Copia(), Verificacion(ok: false), Ahora);

        titulo!.ShouldContain("no se pudo restaurar");
        cuerpo!.ShouldContain("no es una copia");
        sev.ShouldBe(NotificationSeverity.Error);
    }

    [Fact]
    public void Una_verificacion_vieja_avisa_pero_con_menos_gravedad()
    {
        // Que haga tiempo que no se comprueba no significa que este rota: es una garantia
        // que ha caducado, no una averia. Marcarlo como Error igualaria su urgencia a la
        // de una copia que no se hace, y entonces el color dejaria de informar.
        var (titulo, _, sev) = BackupAlertRules.Evaluar(
            Copia(), Verificacion(haceDias: BackupConsts.MaxRestoreCheckAgeDays + 1), Ahora);

        titulo!.ShouldContain("no se comprueba");
        sev.ShouldBe(NotificationSeverity.Warning);
    }

    [Fact]
    public void Sin_verificacion_todavia_no_se_avisa_si_la_copia_va_bien()
    {
        // Recien montado el sistema aun no ha corrido la verificacion mensual. Avisar de
        // eso el primer dia seria ruido por algo que se arregla solo.
        BackupAlertRules.Evaluar(Copia(), null, Ahora).Titulo.ShouldBeNull();
    }
}
