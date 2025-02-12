using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

public class PassThroughHandler : DelegatingHandler
{
    public PassThroughHandler(HttpMessageHandler innerHandler)
    {
        InnerHandler = innerHandler;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Simply pass the request down the handler chain.
        return base.SendAsync(request, cancellationToken);
    }
}
