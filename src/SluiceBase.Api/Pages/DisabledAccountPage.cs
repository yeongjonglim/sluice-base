using System.Text.Encodings.Web;
using SluiceBase.Core.Branding;

namespace SluiceBase.Api.Pages;

// Shown when a disabled user finishes signing in at the IdP. The SPA can't show it: every SPA route
// first needs a signed-in user, which is exactly what this person can't become.
internal static class DisabledAccountPage
{
    // Tabler "user-off", the icon the Permissions page uses for disabling an account.
    private const string UserOffIcon =
        """<svg xmlns="http://www.w3.org/2000/svg" width="18" height="18" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M8.18 8.189a4.01 4.01 0 0 0 2.616 2.627m3.507 -.545a4 4 0 1 0 -5.59 -5.552"/><path d="M6 21v-2a4 4 0 0 1 4 -4h4c.412 0 .81 .062 1.183 .178m2.633 2.618c.12 .38 .184 .785 .184 1.204v2"/><path d="M3 3l18 18"/></svg>""";

    // signOutUrl ends the IdP session so the person can sign in as someone else; without it the IdP would
    // sign the same disabled account straight back in. It is null when the IdP gave no way to do that.
    public static IResult Render(
        HttpContext ctx, string? email, string tryAgainUrl, string? signOutUrl, BrandingOptions branding)
    {
        var nonce = BrandedPage.SecureResponse(ctx.Response);
        return Results.Content(
            Html(nonce, email, tryAgainUrl, signOutUrl, branding),
            "text/html; charset=utf-8",
            statusCode: StatusCodes.Status403Forbidden);
    }

    private static string Html(string nonce, string? email, string tryAgainUrl, string? signOutUrl, BrandingOptions branding)
    {
        var e = HtmlEncoder.Default;
        var appName = e.Encode(branding.AppName);
        var who = email is null ? "You signed in" : $"You signed in as <strong>{e.Encode(email)}</strong>";
        var hint = signOutUrl is null
            ? "Ask an administrator to enable it, then try again."
            : "Ask an administrator to enable it, then try again. Signed in with the wrong account? Use a different one.";
        var switchAccount = signOutUrl is null
            ? ""
            : $"""<a class="button" href="{e.Encode(signOutUrl)}">Use a different account</a>""";

        return $$"""
            <!doctype html>
            <html lang="en">
            <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <meta name="robots" content="noindex">
            <title>Account disabled · {{appName}}</title>
            <style nonce="{{nonce}}">
            {{BrandedPage.Styles(branding)}}
            </style>
            </head>
            <body>
            <div class="page">
            <p class="brand">{{appName}}</p>
            <main>
            <div class="heading">
            <span class="heading-icon">{{UserOffIcon}}</span>
            <h1>Your account is disabled</h1>
            </div>
            <p>{{who}}, but an administrator has disabled this account in {{appName}}, so it can't be used right now.</p>
            <p class="dimmed">{{hint}}</p>
            <div class="actions">
            {{switchAccount}}
            <a class="button primary" href="{{e.Encode(tryAgainUrl)}}">Try again</a>
            </div>
            </main>
            </div>
            </body>
            </html>
            """;
    }
}
