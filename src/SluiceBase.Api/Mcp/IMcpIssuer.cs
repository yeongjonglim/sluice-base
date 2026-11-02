using Microsoft.Extensions.Options;

namespace SluiceBase.Api.Mcp;

internal interface IMcpIssuer
{
    string BaseUrl();
}

internal sealed class McpIssuer(IHttpContextAccessor http, IOptions<McpOptions> options) : IMcpIssuer
{
    private readonly string? _configured = options.Value.GetValidatedPublicBaseUrl();

    public string BaseUrl()
    {
        if (_configured is not null)
        {
            return _configured;
        }

        var request = (http.HttpContext ?? throw new InvalidOperationException("No HTTP request to derive the base URL from.")).Request;
        return $"{request.Scheme}://{request.Host}";
    }
}
