using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AuxiliarContable.MotorFiscal;

/// <summary>Pone la cabecera <c>X-Cliente</c> con <see cref="ClienteDelMotor.Actual"/> en cada petición al motor.</summary>
public class ClienteDelMotorHandler : DelegatingHandler
{
    public const string Cabecera = "X-Cliente";

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (ClienteDelMotor.Actual is { } id && !request.Headers.Contains(Cabecera))
        {
            request.Headers.Add(Cabecera, id.ToString());
        }

        return base.SendAsync(request, cancellationToken);
    }
}
