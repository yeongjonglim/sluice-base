namespace SluiceBase.Api.Extensions;

internal static class HttpRequestExtensions
{
    // The origin the client used. Behind a TLS-terminating proxy this is only right once forwarded
    // headers are configured, which OIDC sign-in already requires (see README "Behind a reverse proxy").
    public static string BaseUrl(this HttpRequest request) => $"{request.Scheme}://{request.Host}";
}
