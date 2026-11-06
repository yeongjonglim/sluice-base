using System.Security.Cryptography;
using SluiceBase.Core.Branding;

namespace SluiceBase.Api.Pages;

// Shared look for the few pages the server renders outside the SPA (the MCP consent page, the disabled-account
// sign-in page), so they read as the same app as the frontend.
internal static class BrandedPage
{
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

    // Sets the headers every such page needs and returns the nonce its inline <style> must carry.
    public static string SecureResponse(HttpResponse response)
    {
        var nonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        var headers = response.Headers;
        // No form-action: Chrome applies it to the post-submit redirect, which goes to the client's
        // loopback or app-scheme callback rather than to 'self'.
        headers.ContentSecurityPolicy =
            $"default-src 'none'; style-src 'nonce-{nonce}'; frame-ancestors 'none'; base-uri 'none'";
        headers.XFrameOptions = "DENY";
        headers.CacheControl = "no-store";
        headers["Referrer-Policy"] = "no-referrer";
        return nonce;
    }

    public static string Styles(BrandingOptions branding)
    {
        var (primaryLight, primaryLightHover, primaryDark, primaryDarkHover) = PrimaryShades[branding.GetValidatedPrimaryColor()];
        return $$"""
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
            .actions { display: flex; gap: 8px; justify-content: flex-end; margin-top: 20px; }
            button, .button { font: inherit; font-weight: 600; height: 36px; padding: 0 18px; border-radius: 4px; cursor: pointer;
              border: 1px solid var(--control-border); background: var(--default); color: var(--text); }
            .button { display: inline-flex; align-items: center; text-decoration: none; }
            button:hover, .button:hover { background: var(--default-hover); }
            button:active, .button:active { transform: translateY(1px); }
            button.primary, .button.primary { background: var(--primary); border-color: transparent; color: var(--on-primary); }
            button.primary:hover, .button.primary:hover { background: var(--primary-hover); }
            button:focus-visible, .button:focus-visible { outline: 2px solid var(--primary); outline-offset: 2px; }
            @media (max-width: 480px) { main { padding: 16px; } }
            """;
    }

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
}
