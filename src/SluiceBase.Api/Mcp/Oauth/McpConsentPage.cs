using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Antiforgery;
using SluiceBase.Api.Mcp.Clients;
using SluiceBase.Api.Pages;
using SluiceBase.Core.Branding;

namespace SluiceBase.Api.Mcp.Oauth;

internal static class McpConsentPage
{
    private const int MaxDisplayNameLength = 80;

    // Tabler "sparkles", the icon the frontend uses for connecting AI tools.
    private const string SparklesIcon =
        """<svg xmlns="http://www.w3.org/2000/svg" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M16 18a2 2 0 0 1 2 2a2 2 0 0 1 2 -2a2 2 0 0 1 -2 -2a2 2 0 0 1 -2 2m0 -12a2 2 0 0 1 2 2a2 2 0 0 1 2 -2a2 2 0 0 1 -2 -2a2 2 0 0 1 -2 2m-7 12a6 6 0 0 1 6 -6a6 6 0 0 1 -6 -6a6 6 0 0 1 -6 6a6 6 0 0 1 6 6"/></svg>""";

    public static IResult Render(
        HttpContext ctx,
        AuthorizeRequest request,
        ResolvedClient client,
        string? userLabel,
        AntiforgeryTokenSet antiforgery,
        BrandingOptions branding)
    {
        var nonce = BrandedPage.SecureResponse(ctx.Response);

        return Results.Content(Html(nonce, request, client, userLabel, antiforgery, branding), "text/html; charset=utf-8");
    }

    private static string Html(
        string nonce,
        AuthorizeRequest request,
        ResolvedClient client,
        string? userLabel,
        AntiforgeryTokenSet antiforgery,
        BrandingOptions branding)
    {
        var e = HtmlEncoder.Default;
        var appName = e.Encode(branding.AppName);
        var (destination, destinationKind) = Describe(request.RedirectUri);

        var hidden = string.Concat(request.Fields()
            .Append(new(antiforgery.FormFieldName, antiforgery.RequestToken ?? ""))
            .Select(f => $"""<input type="hidden" name="{e.Encode(f.Key)}" value="{e.Encode(f.Value)}">"""));

        var host = e.Encode(client.VerifiedHost ?? client.ClientId);
        var displayName = DisplayName(client.DisplayName, e);
        var asUser = userLabel is null ? "" : $" as <strong>{e.Encode(userLabel)}</strong>";

        return $$"""
            <!doctype html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <meta name="robots" content="noindex">
            <title>Connect an AI tool · {{appName}}</title>
            <style nonce="{{nonce}}">
            {{BrandedPage.Styles(branding)}}
            .destination { background: var(--inset); border: 1px solid var(--border); border-radius: 4px; padding: 8px 12px; margin: 0 0 16px; }
            .destination .label { font-size: 12px; color: var(--dimmed); margin: 0 0 2px; }
            .destination .host { font: 600 16px/1.4 ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, "Liberation Mono", "Courier New", monospace;
              overflow-wrap: anywhere; margin: 0; }
            ul { margin: 0 0 16px; padding-left: 20px; }
            li { margin: 2px 0; }
            .alert { background: var(--alert-bg); color: var(--alert-text); border-radius: 4px; padding: 12px; margin: 0; }
            </style>
            </head>
            <body>
            <div class="page">
            <p class="brand">{{appName}}</p>
            <main>
            <div class="heading">
            <span class="heading-icon">{{SparklesIcon}}</span>
            <h1>Allow this AI tool to access {{appName}}?</h1>
            </div>
            <p><strong>{{host}}</strong> wants to use {{appName}}{{asUser}}.
            <span class="dimmed">It calls itself “<bdi>{{displayName}}</bdi>”. The name comes from the tool; the domain {{host}} is verified.</span></p>
            <div class="destination">
            <p class="label">Access will be handed to {{destinationKind}}</p>
            <p class="host">{{e.Encode(destination)}}</p>
            </div>
            <p>It will be able to:</p>
            <ul>
            <li>See the databases and schemas you have access to</li>
            <li>Run read-only queries with your permissions</li>
            <li>Submit update requests for review in your name</li>
            </ul>
            <p class="alert" role="note">Only allow this if you just started connecting an AI tool yourself. If you didn't, choose Deny.</p>
            <form method="post" action="/mcp/oauth/authorize">
            {{hidden}}
            <div class="actions">
            <button type="submit" name="decision" value="deny">Deny</button>
            <button type="submit" name="decision" value="allow" class="primary">Allow</button>
            </div>
            </form>
            </main>
            </div>
            </body>
            </html>
            """;
    }

    // The name comes from an untrusted CIMD document, so a very long one must not push the verified host off the page.
    private static string DisplayName(string name, HtmlEncoder e)
    {
        if (name.Length <= MaxDisplayNameLength)
        {
            return e.Encode(name);
        }

        var cut = MaxDisplayNameLength - 1;
        if (char.IsHighSurrogate(name[cut - 1]))
        {
            cut--;
        }

        return e.Encode(name[..cut]) + "…";
    }

    // White text on light shades (e.g. teal 5 in dark mode) is hard to read on the one button that matters.
    // One text color serves both the resting and hover shade, so it never flips mid-hover.

    private static (string Destination, string Kind) Describe(string redirectUri)
    {
        var uri = new Uri(redirectUri);
        // IdnHost shows punycode, so a homograph can't render as the host it imitates.
        if (McpRedirectUriPolicy.IsLoopback(redirectUri))
        {
            return ($"{uri.IdnHost}:{uri.Port}", "an app running on this computer");
        }

        return uri.Scheme == Uri.UriSchemeHttps
            ? (uri.IdnHost, "this website")
            : ($"{uri.Scheme}://{uri.IdnHost}", "this desktop app");
    }
}
