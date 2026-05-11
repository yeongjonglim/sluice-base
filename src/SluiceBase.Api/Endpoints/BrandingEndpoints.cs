using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;
using SluiceBase.Core.Branding;

namespace SluiceBase.Api.Endpoints;

internal static class BrandingEndpoints
{
    private static readonly string[] SupportedExtensions =
        [".png", ".svg", ".jpg", ".jpeg", ".gif", ".webp", ".ico"];

    public static void Map(IEndpointRouteBuilder app)
    {
        var brandingGroup = app.MapGroup("/api/branding").AllowAnonymous();

        brandingGroup.MapGet("/",
                Ok<BrandingResponse> (IOptions<BrandingOptions> options) =>
                {
                    var branding = options.Value;
                    var logger = app is WebApplication webApp ? webApp.Logger : null;
                    return TypedResults.Ok(new BrandingResponse(
                        AppName: branding.AppName,
                        PrimaryColor: branding.GetValidatedPrimaryColor(logger),
                        LogoUrl: ResolveAssetUrl(branding.LogoUrl, "/branding/logo", "/api/branding/logo"),
                        FaviconUrl: ResolveAssetUrl(branding.FaviconUrl, "/branding/favicon", "/api/branding/favicon")));
                })
            .WithName("GetBranding");

        brandingGroup.MapGet("/logo",
                Results<FileStreamHttpResult, NotFound> () => ServeLocalAsset("/branding/logo"))
            .WithName("GetBrandingLogo")
            .AllowAnonymous();

        brandingGroup.MapGet("/favicon",
                Results<FileStreamHttpResult, NotFound> () => ServeLocalAsset("/branding/favicon"))
            .WithName("GetBrandingFavicon")
            .AllowAnonymous();
    }

    private static Results<FileStreamHttpResult, NotFound> ServeLocalAsset(string basePath)
    {
        foreach (var ext in SupportedExtensions)
        {
            var path = basePath + ext;
            if (File.Exists(path))
            {
                return TypedResults.File(File.OpenRead(path), GetContentType(ext));
            }
        }

        return TypedResults.NotFound();
    }

    private static string GetContentType(string ext) => ext switch
    {
        ".svg" => "image/svg+xml",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".webp" => "image/webp",
        ".ico" => "image/x-icon",
        _ => "image/png"
    };

    private static string? ResolveAssetUrl(
        string configUrl, string localBasePath, string serveEndpoint)
    {
        if (!string.IsNullOrEmpty(configUrl))
        {
            if (IsRemoteUrl(configUrl))
            {
                return configUrl;
            }
        }

        return HasLocalAsset(localBasePath) ? serveEndpoint : null;
    }

    private static bool HasLocalAsset(string basePath)
    {
        return SupportedExtensions.Any(ext => File.Exists(basePath + ext));
    }

    private static bool IsRemoteUrl(string url) =>
        url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        url.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
}

internal sealed record BrandingResponse(
    string AppName,
    string PrimaryColor,
    string? LogoUrl,
    string? FaviconUrl);