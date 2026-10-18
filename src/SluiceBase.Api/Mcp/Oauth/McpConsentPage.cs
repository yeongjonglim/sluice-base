using System.Security.Cryptography;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Antiforgery;
using SluiceBase.Api.Mcp.Clients;
using SluiceBase.Core.Branding;

namespace SluiceBase.Api.Mcp.Oauth;

internal static class McpConsentPage
{
    private const int MaxDisplayNameLength = 80;

    // Mantine shades matching the frontend theme's primaryShade (light 7, dark 5).
    private static readonly Dictionary<string, (string Light, string Dark)> PrimaryShades = new()
    {
        ["dark"] = ("#242424", "#3b3b3b"),
        ["gray"] = ("#495057", "#adb5bd"),
        ["red"] = ("#f03e3e", "#ff6b6b"),
        ["pink"] = ("#d6336c", "#f06595"),
        ["grape"] = ("#ae3ec9", "#cc5de8"),
        ["violet"] = ("#7048e8", "#845ef7"),
        ["indigo"] = ("#4263eb", "#5c7cfa"),
        ["blue"] = ("#1c7ed6", "#339af0"),
        ["cyan"] = ("#1098ad", "#22b8cf"),
        ["green"] = ("#37b24d", "#51cf66"),
        ["lime"] = ("#74b816", "#94d82d"),
        ["yellow"] = ("#f59f00", "#fcc419"),
        ["orange"] = ("#f76707", "#ff922b"),
        ["teal"] = ("#0ca678", "#20c997"),
    };

    public static IResult Render(
        HttpContext ctx,
        AuthorizeRequest request,
        ResolvedClient client,
        string? userLabel,
        AntiforgeryTokenSet antiforgery,
        BrandingOptions branding)
    {
        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        var headers = ctx.Response.Headers;
        // No form-action: Chrome applies it to the post-submit redirect, which goes to the client's
        // loopback or app-scheme callback rather than to 'self'.
        headers.ContentSecurityPolicy =
            $"default-src 'none'; style-src 'nonce-{nonce}'; frame-ancestors 'none'; base-uri 'none'";
        headers.XFrameOptions = "DENY";
        headers.CacheControl = "no-store";
        headers["Referrer-Policy"] = "no-referrer";

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
        var (primaryLight, primaryDark) = PrimaryShades[branding.GetValidatedPrimaryColor()];
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
            :root { --bg: #f8f9fa; --card: #fff; --text: #212529; --dimmed: #868e96; --border: #dee2e6;
              --subtle: #f1f3f5; --primary: {{primaryLight}}; --on-primary: {{OnPrimary(primaryLight)}}; }
            @media (prefers-color-scheme: dark) {
              :root { --bg: #1a1b1e; --card: #25262b; --text: #c1c2c5; --dimmed: #909296; --border: #373a40;
                --subtle: #2c2e33; --primary: {{primaryDark}}; --on-primary: {{OnPrimary(primaryDark)}}; }
            }
            * { box-sizing: border-box; }
            body { margin: 0; min-height: 100vh; display: grid; place-items: center; padding: 16px;
              background: var(--bg); color: var(--text);
              font: 14px/1.55 system-ui, -apple-system, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif; }
            main { width: 100%; max-width: 440px; background: var(--card); border: 1px solid var(--border);
              border-radius: 8px; padding: 28px; }
            .app { font-size: 12px; font-weight: 600; letter-spacing: .04em; text-transform: uppercase; color: var(--dimmed); margin: 0 0 12px; }
            h1 { font-size: 20px; line-height: 1.3; margin: 0 0 8px; }
            p { margin: 0 0 16px; }
            .dimmed { display: block; margin-top: 2px; color: var(--dimmed); font-size: 12px; }
            .destination { background: var(--subtle); border: 1px solid var(--border); border-radius: 4px; padding: 12px 14px; margin: 0 0 16px; }
            .destination .label { font-size: 12px; color: var(--dimmed); margin: 0 0 2px; }
            .destination .host { font: 600 16px/1.4 ui-monospace, SFMono-Regular, Menlo, Consolas, monospace; overflow-wrap: anywhere; margin: 0; }
            ul { margin: 0 0 16px; padding-left: 20px; }
            li { margin: 2px 0; }
            .warn { border-left: 3px solid var(--primary); padding-left: 10px; }
            .actions { display: flex; gap: 8px; justify-content: flex-end; margin-top: 24px; }
            button { font: inherit; font-weight: 600; border-radius: 4px; padding: 8px 18px; cursor: pointer; border: 1px solid var(--border); background: transparent; color: var(--text); }
            button.primary { background: var(--primary); border-color: var(--primary); color: var(--on-primary); }
            button:focus-visible { outline: 2px solid var(--primary); outline-offset: 2px; }
            </style>
            </head>
            <body>
            <main>
            <p class="app">{{appName}}</p>
            <h1>Allow this AI tool to access {{appName}}?</h1>
            <p><strong>{{host}}</strong> wants to use {{appName}}{{asUser}}.
            <span class="dimmed">It calls itself “{{displayName}}”. The name comes from the tool; the domain {{host}} is verified.</span></p>
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
            <p class="warn">Only allow this if you just started connecting an AI tool yourself. If you didn't, choose Deny.</p>
            <form method="post" action="/mcp/oauth/authorize">
            {{hidden}}
            <div class="actions">
            <button type="submit" name="decision" value="deny">Deny</button>
            <button type="submit" name="decision" value="allow" class="primary">Allow</button>
            </div>
            </form>
            </main>
            </body>
            </html>
            """;
    }

    // The name comes from an untrusted CIMD document, so a very long one must not push the verified host off the page.
    private static string DisplayName(string name, HtmlEncoder e) =>
        name.Length <= MaxDisplayNameLength
            ? e.Encode(name)
            : e.Encode(name[..(MaxDisplayNameLength - 1)]) + "…";

    // White text on light shades (e.g. teal 5 in dark mode) is hard to read on the one button that matters.
    private static string OnPrimary(string hex)
    {
        static double Channel(string hex, int offset)
        {
            var c = Convert.ToInt32(hex.Substring(offset, 2), 16) / 255.0;
            return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
        }

        var luminance = 0.2126 * Channel(hex, 1) + 0.7152 * Channel(hex, 3) + 0.0722 * Channel(hex, 5);
        return luminance > 0.4 ? "#1a1b1e" : "#fff";
    }

    private static (string Destination, string Kind) Describe(string redirectUri)
    {
        var uri = new Uri(redirectUri);
        if (uri.IsLoopback)
        {
            return ($"{uri.Host}:{uri.Port}", "an app running on this computer");
        }

        return uri.Scheme == Uri.UriSchemeHttps
            ? (uri.Host, "this website")
            : ($"{uri.Scheme}://{uri.Host}", "this desktop app");
    }
}
