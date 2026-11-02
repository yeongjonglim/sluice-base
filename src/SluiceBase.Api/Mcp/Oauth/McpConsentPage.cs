using System.Security.Cryptography;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Antiforgery;
using SluiceBase.Api.Mcp.Clients;
using SluiceBase.Core.Branding;

namespace SluiceBase.Api.Mcp.Oauth;

internal static class McpConsentPage
{
    private const int MaxDisplayNameLength = 80;

    // Mantine shades matching the frontend theme's primaryShade (light 7, dark 5), each with the
    // next shade that Mantine's filled buttons use on hover.
    private static readonly Dictionary<string, (string Light, string LightHover, string Dark, string DarkHover)> PrimaryShades = new()
    {
        ["dark"] = ("#242424", "#1f1f1f", "#3b3b3b", "#2e2e2e"),
        ["gray"] = ("#495057", "#343a40", "#adb5bd", "#868e96"),
        ["red"] = ("#f03e3e", "#e03131", "#ff6b6b", "#fa5252"),
        ["pink"] = ("#d6336c", "#c2255c", "#f06595", "#e64980"),
        ["grape"] = ("#ae3ec9", "#9c36b5", "#cc5de8", "#be4bdb"),
        ["violet"] = ("#7048e8", "#6741d9", "#845ef7", "#7950f2"),
        ["indigo"] = ("#4263eb", "#3b5bdb", "#5c7cfa", "#4c6ef5"),
        ["blue"] = ("#1c7ed6", "#1971c2", "#339af0", "#228be6"),
        ["cyan"] = ("#1098ad", "#0c8599", "#22b8cf", "#15aabf"),
        ["green"] = ("#37b24d", "#2f9e44", "#51cf66", "#40c057"),
        ["lime"] = ("#74b816", "#66a80f", "#94d82d", "#82c91e"),
        ["yellow"] = ("#f59f00", "#f08c00", "#fcc419", "#fab005"),
        ["orange"] = ("#f76707", "#e8590c", "#ff922b", "#fd7e14"),
        ["teal"] = ("#0ca678", "#099268", "#20c997", "#12b886"),
    };

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
        var (primaryLight, primaryLightHover, primaryDark, primaryDarkHover) = PrimaryShades[branding.GetValidatedPrimaryColor()];
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
            /* Mantine 9 tokens as the frontend renders them, including its AA fixes for dimmed text. */
            :root { color-scheme: light; --body: #fff; --card: #fff; --text: #000; --dimmed: #5c636b;
              --border: #dee2e6; --control-border: #ced4da; --default: #fff; --default-hover: #f8f9fa;
              --inset: #f8f9fa; --icon-bg: #f1f3f5; --icon-fg: #212529; --alert-bg: #fff4e6; --alert-text: #000;
              --primary: {{primaryLight}}; --primary-hover: {{primaryLightHover}};
              --on-primary: {{OnPrimary(primaryLight, primaryLightHover)}}; }
            @media (prefers-color-scheme: dark) {
              :root { color-scheme: dark; --body: #242424; --card: #2e2e2e; --text: #c9c9c9; --dimmed: #a1a1a1;
                --border: #424242; --control-border: #424242; --default: #2e2e2e; --default-hover: #3b3b3b;
                --inset: #242424; --icon-bg: #242424; --icon-fg: #f8f9fa; --alert-bg: #6d2408; --alert-text: #fff;
                --primary: {{primaryDark}}; --primary-hover: {{primaryDarkHover}};
                --on-primary: {{OnPrimary(primaryDark, primaryDarkHover)}}; }
            }
            * { box-sizing: border-box; }
            body { margin: 0; min-height: 100vh; display: grid; place-items: center; padding: 16px;
              background: var(--body); color: var(--text); -webkit-font-smoothing: antialiased;
              font: 14px/1.4 system-ui, -apple-system, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif; }
            .page { width: 100%; max-width: 440px; }
            .brand { font-size: 18px; line-height: 1.45; font-weight: 700; margin: 0 0 12px; }
            main { background: var(--card); border: 1px solid var(--border); border-radius: 4px; padding: 20px; }
            .heading { display: flex; gap: 8px; align-items: flex-start; margin: 0 0 12px; }
            .heading-icon { flex: none; width: 28px; height: 28px; margin-top: 2px; border-radius: 4px; display: grid;
              place-items: center; background: var(--icon-bg); color: var(--icon-fg); }
            h1 { font-size: 22px; line-height: 1.4; font-weight: 700; margin: 0; text-wrap: balance; }
            p { margin: 0 0 12px; }
            .dimmed { display: block; margin-top: 4px; color: var(--dimmed); font-size: 12px; }
            .destination { background: var(--inset); border: 1px solid var(--border); border-radius: 4px; padding: 8px 12px; margin: 0 0 16px; }
            .destination .label { font-size: 12px; color: var(--dimmed); margin: 0 0 2px; }
            .destination .host { font: 600 16px/1.4 ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, "Liberation Mono", "Courier New", monospace;
              overflow-wrap: anywhere; margin: 0; }
            ul { margin: 0 0 16px; padding-left: 20px; }
            li { margin: 2px 0; }
            .alert { background: var(--alert-bg); color: var(--alert-text); border-radius: 4px; padding: 12px; margin: 0; }
            .actions { display: flex; gap: 8px; justify-content: flex-end; margin-top: 20px; }
            button { font: inherit; font-weight: 600; height: 36px; padding: 0 18px; border-radius: 4px; cursor: pointer;
              border: 1px solid var(--control-border); background: var(--default); color: var(--text); }
            button:hover { background: var(--default-hover); }
            button:active { transform: translateY(1px); }
            button.primary { background: var(--primary); border-color: transparent; color: var(--on-primary); }
            button.primary:hover { background: var(--primary-hover); }
            button:focus-visible { outline: 2px solid var(--primary); outline-offset: 2px; }
            @media (max-width: 480px) { main { padding: 16px; } }
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
    private static string OnPrimary(string hex, string hoverHex)
    {
        const string light = "#ffffff";
        const string dark = "#1a1b1e";

        static double Luminance(string hex)
        {
            static double Channel(string hex, int offset)
            {
                var c = Convert.ToInt32(hex.Substring(offset, 2), 16) / 255.0;
                return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
            }

            return 0.2126 * Channel(hex, 1) + 0.7152 * Channel(hex, 3) + 0.0722 * Channel(hex, 5);
        }

        static double Contrast(string a, string b)
        {
            var (la, lb) = (Luminance(a), Luminance(b));
            return (Math.Max(la, lb) + 0.05) / (Math.Min(la, lb) + 0.05);
        }

        var worstLight = Math.Min(Contrast(light, hex), Contrast(light, hoverHex));
        var worstDark = Math.Min(Contrast(dark, hex), Contrast(dark, hoverHex));
        return worstLight >= worstDark ? light : dark;
    }

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
