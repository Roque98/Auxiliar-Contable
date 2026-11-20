using System;
using System.Threading;

namespace AuxiliarContable.MotorFiscal;

/// <summary>
/// El cliente (organización) por el que se llama al motor en este flujo asíncrono. El host lo manda en la cabecera
/// <c>X-Cliente</c> (solo el id, nunca RFC ni nombre) y el motor anota con él lo que cuesta Claude.
/// <code>using (ClienteDelMotor.Para(organizacionId)) { await _motor.AnalizarDocumentoAsync(...); }</code>
/// </summary>
public static class ClienteDelMotor
{
    private static readonly AsyncLocal<Guid?> Valor = new();

    public static Guid? Actual => Valor.Value;

    public static IDisposable Para(Guid organizacionId)
    {
        var antes = Valor.Value;
        Valor.Value = organizacionId;
        return new Restaurar(antes);
    }

    private sealed class Restaurar(Guid? antes) : IDisposable
    {
        public void Dispose() => Valor.Value = antes;
    }
}
