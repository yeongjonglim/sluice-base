namespace SluiceBase.Api.Services;

internal interface IBaseUrlResolver
{
    string Resolve();
}

internal sealed class BaseUrlResolver(IHttpContextAccessor http, IConfiguration configuration) : IBaseUrlResolver
{
    public const string ConfigKey = "PublicBaseUrl";

    private readonly string? _configured = Validate(configuration[ConfigKey]);

    public string Resolve()
    {
        if (_configured is not null)
        {
            return _configured;
        }

        var request = (http.HttpContext ?? throw new InvalidOperationException("No HTTP request to derive the base URL from.")).Request;
        return $"{request.Scheme}://{request.Host}";
    }

    public static string? Validate(string? value, ILogger? logger = null)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            logger?.WarningPublicBaseUrlNotSet();
            return null;
        }

        if (Uri.TryCreate(value, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
            && string.IsNullOrEmpty(uri.UserInfo)
            && string.IsNullOrEmpty(uri.Query)
            && string.IsNullOrEmpty(uri.Fragment)
            && (uri.AbsolutePath == "/" || uri.AbsolutePath.Length == 0))
        {
            return uri.GetLeftPart(UriPartial.Authority);
        }

        logger?.WarningInvalidPublicBaseUrl(value);
        return null;
    }
}

internal static partial class BaseUrlResolverLoggerMessage
{
    [LoggerMessage(
        LogLevel.Warning,
        Message = "PublicBaseUrl '{PublicBaseUrl}' is not a valid origin (expected scheme://host[:port] with no path, query, fragment or user info). Ignoring it.")]
    public static partial void WarningInvalidPublicBaseUrl(this ILogger logger, string publicBaseUrl);

    [LoggerMessage(
        LogLevel.Warning,
        Message = "PublicBaseUrl is not set; absolute URLs such as the MCP OAuth issuer are derived from each request. Set it when running behind a reverse proxy.")]
    public static partial void WarningPublicBaseUrlNotSet(this ILogger logger);
}
