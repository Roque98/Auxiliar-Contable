using System.Collections.Generic;
using System.Linq;
using AuxiliarContable.Controllers;
using AuxiliarContable.Data;
using AuxiliarContable.EntityFrameworkCore;
using AuxiliarContable.Permissions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Volo.Abp;
using Volo.Abp.Domain.Entities;
using Volo.Abp.Features;
using Volo.Abp.MultiTenancy;
using NetArchTest.Rules;
using Shouldly;
using Volo.Abp.Application.Services;
using Xunit;

namespace AuxiliarContable;

/// <summary>
/// Reglas de arquitectura EJECUTABLES (Fase 3). Fallan el build de tests si
/// alguien rompe las capas. Son la red de seguridad contra la degradación por
/// agentes de IA.
/// </summary>
public class ArchitectureTests
{
    private static readonly System.Reflection.Assembly DomainAssembly = typeof(AuxiliarContable.Organizaciones.Organizacion).Assembly;
    private static readonly System.Reflection.Assembly ApplicationAssembly = typeof(AuxiliarContable.Organizaciones.OrganizacionAppService).Assembly;
    private static readonly System.Reflection.Assembly EntityFrameworkCoreAssembly = typeof(AuxiliarContableEntityFrameworkCoreModule).Assembly;
    private static readonly System.Reflection.Assembly HttpApiAssembly = typeof(AuxiliarContableController).Assembly;

    [Fact]
    public void Domain_no_debe_depender_de_Application_ni_de_EF_Core()
    {
        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "AuxiliarContable.Application",
                "AuxiliarContable.EntityFrameworkCore",
                "Microsoft.EntityFrameworkCore")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(FailMessage(result));
    }

    [Fact]
    public void Application_no_debe_depender_de_la_capa_de_infraestructura_EF_Core()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOnAny("AuxiliarContable.EntityFrameworkCore")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(FailMessage(result));
    }

    /// <summary>
    /// Los servicios propios heredan de <see cref="AuxiliarContableAppService"/>, que es lo que les da
    /// el recurso de localización y los ayudantes del proyecto.
    ///
    /// <para>⚠️ <b>La excepción son los servicios que SUSTITUYEN a uno de ABP.</b> Para poder
    /// hacer <c>override</c> de un método suyo hay que heredar de <b>su</b> clase, no de la
    /// nuestra — C# no tiene herencia múltiple. Es el patrón de
    /// <c>[Dependency(ReplaceServices = true)]</c> que el proyecto ya usa con
    /// <c>AuxiliarContableLoginModel</c> (§5.2).</para>
    ///
    /// <para>La lista es explícita a propósito: así añadir una excepción obliga a pasar por
    /// aquí y a justificarla, en vez de que la regla se vaya erosionando sola.</para>
    /// </summary>
    private static readonly string[] SustituyenAUnServicioDeAbp =
    [
        // La jerarquía Developer → Owner → Administrador → Usuario (ver `AuxiliarRoles`). Heredan
        // del servicio de ABP para sobrescribir solo lo que la jerarquía cambia y seguir
        // llamando a `base`, que es lo que mantiene el resto al día con el módulo.
        "AuxiliarContable.Identity.AuxiliarIdentityUserAppService",
        "AuxiliarContable.Identity.AuxiliarIdentityRoleAppService",
        "AuxiliarContable.Identity.AuxiliarPermissionAppService",
        // No revela por la API si un correo tiene cuenta (recuperar contraseña).
        "AuxiliarContable.Identity.AuxiliarAccountAppService",
    ];

    [Fact]
    public void Los_application_services_deben_heredar_de_la_clase_base_del_proyecto()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .That()
            .HaveNameEndingWith("AppService")
            .And().AreClasses()
            .And().AreNotAbstract()
            .And().DoNotHaveName(SustituyenAUnServicioDeAbp.Select(n => n.Split('.').Last()).ToArray())
            .Should()
            .Inherit(typeof(AuxiliarContableAppService))
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(FailMessage(result));
    }

    /// <summary>
    /// La otra mitad: que las excepciones existan de verdad y sigan siendo sustituciones.
    ///
    /// <para>Sin esto, un nombre que se quedara en la lista tras renombrar o borrar la clase
    /// abriría un agujero permanente en la regla de arriba, y nadie se enteraría.</para>
    /// </summary>
    [Fact]
    public void Toda_excepcion_declarada_existe_y_sigue_sustituyendo_a_ABP()
    {
        foreach (var nombre in SustituyenAUnServicioDeAbp)
        {
            var tipo = ApplicationAssembly.GetType(nombre);

            tipo.ShouldNotBeNull(
                $"'{nombre}' está declarado como excepción a la regla de la clase base y no " +
                "existe. Un nombre obsoleto en esa lista deja la regla abierta en silencio.");

            tipo!.BaseType.ShouldNotBeNull(nombre);
            tipo.BaseType!.Namespace!.StartsWith("Volo.Abp").ShouldBeTrue(
                $"'{nombre}' está exento de heredar de AuxiliarContableAppService porque sustituye a " +
                $"un servicio de ABP, pero hereda de '{tipo.BaseType.FullName}'. Si ya no " +
                "sustituye a uno, la exención sobra.");
        }
    }

    [Fact]
    public void Todo_application_service_es_un_ApplicationService_de_ABP()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .That()
            .HaveNameEndingWith("AppService")
            .And().AreClasses()
            .And().AreNotAbstract()
            .Should()
            .Inherit(typeof(ApplicationService))
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(FailMessage(result));
    }

    [Fact]
    public void Domain_no_abre_conexiones_de_base_de_datos()
    {
        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOnAny("Microsoft.Data.SqlClient", "System.Data.SqlClient")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(FailMessage(result));
    }

    [Fact]
    public void Application_no_abre_conexiones_de_base_de_datos()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOnAny("Microsoft.Data.SqlClient", "System.Data.SqlClient")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(FailMessage(result));
    }

    [Fact]
    public void Ningun_controller_accede_a_repositorios_directamente()
    {
        var result = Types.InAssembly(HttpApiAssembly)
            .That()
            .HaveNameEndingWith("Controller")
            .ShouldNot()
            .HaveDependencyOnAny("Volo.Abp.Domain.Repositories")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(FailMessage(result));
    }

    [Fact]
    public void El_factory_de_fuentes_externas_vive_en_la_capa_de_infraestructura()
    {
        var result = Types.InAssembly(EntityFrameworkCoreAssembly)
            .That()
            .ImplementInterface(typeof(IExternalDataSourceConnectionFactory))
            .Should()
            .ResideInNamespace("AuxiliarContable.Data")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(FailMessage(result));
    }

    /// <summary>
    /// Toda entidad propia con filtro global (multi-tenancy o soft delete) tiene que estar
    /// declarada como <c>DbSet</c> en <see cref="AuxiliarContableDbContext"/>.
    ///
    /// <para>⚠️ NO ES ESTILO, ES SEGURIDAD. ABP aplica sus filtros globales recorriendo las
    /// entidades que YA están en el modelo cuando se llama a <c>base.OnModelCreating()</c>
    /// —la primera línea del método—. EF descubre ahí las que tienen una propiedad
    /// <c>DbSet</c>; una entidad registrada solo más abajo con <c>builder.Entity&lt;X&gt;()</c>
    /// llega tarde y **se queda sin filtro**. Nada falla al compilar y las consultas empiezan
    /// a devolver las filas de todos los tenants.</para>
    ///
    /// <para>Pasó de verdad con <c>Notificacion</c>. Lo cazaron los tests de aislamiento,
    /// pero aquellos solo cubren las entidades que alguien se acordó de probar; esta regla
    /// cubre las que vengan.</para>
    /// </summary>
    [Fact]
    public void Toda_entidad_con_filtro_global_esta_declarada_como_DbSet()
    {
        var declaradas = typeof(AuxiliarContableDbContext)
            .GetProperties()
            .Where(p => p.PropertyType.IsGenericType &&
                        p.PropertyType.GetGenericTypeDefinition() == typeof(DbSet<>))
            .Select(p => p.PropertyType.GetGenericArguments()[0])
            .ToHashSet();

        var conFiltro = DomainAssembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(t => typeof(IEntity).IsAssignableFrom(t))
            .Where(t => typeof(IMultiTenant).IsAssignableFrom(t) ||
                        typeof(ISoftDelete).IsAssignableFrom(t))
            .ToList();

        var olvidadas = conFiltro.Where(t => !declaradas.Contains(t)).ToList();

        olvidadas.ShouldBeEmpty(
            "Estas entidades llevan filtro global pero no tienen DbSet en AuxiliarContableDbContext, " +
            "así que ABP NO les aplicará el filtro y sus consultas cruzarán tenants: " +
            string.Join(", ", olvidadas.Select(t => t.Name)));
    }

    /// <summary>
    /// Toda clase con <c>[RequiresFeature]</c> tiene que llevar TAMBIÉN <c>[Authorize]</c>
    /// — el "doble candado" de §6.966: la feature dice si el INQUILINO tiene el módulo
    /// contratado, el permiso dice si esta PERSONA puede usarlo.
    ///
    /// <para>⚠️ NO ES ESTILO. Sin el segundo candado, un rol que se quedó con una concesión
    /// de cuando el módulo SÍ estaba contratado seguiría entrando tras darse de baja —
    /// <c>[RequiresFeature]</c> no revisa permisos, y <c>[Authorize]</c> es lo único que
    /// corta la llamada aunque la concesión vieja siga en la base. Nada de esto falla al
    /// compilar: un servicio nuevo sin <c>[Authorize]</c> simplemente queda abierto a
    /// cualquiera que ya tuviera el permiso alguna vez.</para>
    /// </summary>
    [Fact]
    public void Toda_clase_con_RequiresFeature_lleva_tambien_Authorize()
    {
        var sinAuthorize = ApplicationAssembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false })
            .Where(t => t.GetCustomAttributes(typeof(RequiresFeatureAttribute), inherit: true).Length > 0)
            .Where(t => t.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: true).Length == 0)
            .ToList();

        sinAuthorize.ShouldBeEmpty(
            "Estas clases exigen una feature del inquilino pero no llevan [Authorize]: sin el " +
            "segundo candado, un permiso concedido antes de dar de baja el módulo seguiría " +
            "funcionando: " + string.Join(", ", sinAuthorize.Select(t => t.Name)));
    }

    /// <summary>
    /// Ningún DTO de entrada puede tener una propiedad que se llame como un valor de RUTA
    /// de MVC (<c>action</c>, <c>controller</c>, <c>area</c>, <c>page</c>).
    ///
    /// <para>⚠️ NO ES ESTILO. El enlazador de modelos rellena esas propiedades con el valor
    /// de la ruta —el nombre del método, del controlador…— aunque nadie las mande en la
    /// petición. Pasó de verdad: <c>GetSecurityLogsInput.Action</c> llegaba valiendo
    /// «GetList» y la consulta filtraba por <c>Action = 'GetList'</c>. Cero filas, ningún
    /// error, y la tabla llena.</para>
    ///
    /// <para>Los tests de integración NO lo cazan, porque llaman al application service
    /// directamente y ahí no hay enlazador de modelos. Solo se ve mirando el SQL generado
    /// de una petición HTTP real — o con esta regla.</para>
    /// </summary>
    [Fact]
    public void Ningun_DTO_de_entrada_usa_un_nombre_reservado_por_el_enrutado_de_MVC()
    {
        var reservados = new[] { "action", "controller", "area", "page", "handler" };

        var culpables = new List<string>();

        // Los DTO de ENTRADA se detectan por USO, no por nombre: son los que aparecen como
        // parametro de algun metodo de un application service. Filtrar por sufijo seria
        // frágil en los dos sentidos — dejaria fuera un input que no acabe en "Input" y
        // señalaria DTOs de salida, que nunca se enlazan desde una peticion.
        var contratos = typeof(AuxiliarContablePermissions).Assembly;

        var dtos = contratos.GetTypes()
            .Where(t => t.IsInterface && typeof(IApplicationService).IsAssignableFrom(t))
            .SelectMany(t => t.GetMethods())
            .SelectMany(m => m.GetParameters())
            .Select(p => p.ParameterType)
            .Where(t => t is { IsClass: true } && t.Assembly == contratos)
            .Distinct();

        foreach (var dto in dtos)
        {
            foreach (var prop in dto.GetProperties())
            {
                if (reservados.Contains(prop.Name.ToLowerInvariant()))
                {
                    culpables.Add($"{dto.Name}.{prop.Name}");
                }
            }
        }

        culpables.ShouldBeEmpty(
            "Estas propiedades las rellena el enrutado de MVC por su cuenta y el filtro " +
            "acabará aplicándose sin que nadie lo pida: " + string.Join(", ", culpables));
    }

    private static string FailMessage(TestResult result)
    {
        var names = result.FailingTypes?.Select(t => t.FullName) ?? Enumerable.Empty<string>();
        return "Tipos que violan la regla: " + string.Join(", ", names);
    }
}
