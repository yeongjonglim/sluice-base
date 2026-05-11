using System.Net;
using System.Text.RegularExpressions;
using System.Web;
using Aspire.Hosting;
using Aspire.Hosting.Testing;

namespace IntegrationTests.Supports;

public sealed class AuthenticatedSession(HttpClient client, CookieContainer cookies) : IDisposable
{
    public HttpClient Client => client;

    public async Task<string> FetchXsrfTokenAsync(CancellationToken ct = default)
    {
        using var response = await client.GetAsync("/api/antiforgery-token", ct);
        response.EnsureSuccessStatusCode();
        var cookie = cookies.GetCookies(client.BaseAddress!)["XSRF-TOKEN"];
        return Uri.UnescapeDataString(cookie?.Value ??
            throw new InvalidOperationException("XSRF-TOKEN cookie not set after /api/antiforgery-token"));
    }

    public void Dispose() => client.Dispose();
}

public sealed partial class KeycloakLoginHelper(DistributedApplication app)
{
    private static readonly Regex FormActionRegex = MyRegex();

    public async Task<AuthenticatedSession> SignInAsync(
        string username,
        string password,
        CancellationToken ct = default)
    {
        var cookies = new CookieContainer();
        using var loginHandler = new HttpClientHandler
        {
            CookieContainer = cookies,
            AllowAutoRedirect = true,
            UseCookies = true,
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        };
        using var loginClient = new HttpClient(loginHandler)
        {
            BaseAddress = app.GetEndpoint("api", "https")
        };

        var loginPage = await loginClient.GetAsync("/login", ct);
        loginPage.EnsureSuccessStatusCode();

        var html = await loginPage.Content.ReadAsStringAsync(ct);
        var match = FormActionRegex.Match(html);
        if (!match.Success)
        {
            throw new InvalidOperationException(
                "Could not locate Keycloak login form (id=kc-form-login). " +
                "Realm theme or Keycloak version may have changed.");
        }
        var actionUrl = HttpUtility.HtmlDecode(match.Groups["action"].Value);

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["username"] = username,
            ["password"] = password,
            ["credentialId"] = string.Empty,
        });
        var submitResponse = await loginClient.PostAsync(actionUrl, form, ct);
        submitResponse.EnsureSuccessStatusCode();

        await FollowAutoPostFormAsync(loginClient, submitResponse, ct);

        var testHandler = new HttpClientHandler
        {
            CookieContainer = cookies,
            AllowAutoRedirect = false,
            UseCookies = true,
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator,
        };
        var testClient = new HttpClient(testHandler)
        {
            BaseAddress = app.GetEndpoint("api", "https"),
        };
        return new AuthenticatedSession(testClient, cookies);
    }

    private static readonly Regex AutoPostFormRegex = AutoPostFormMyRegex();
    private static readonly Regex AutoPostInputRegex = AutoPostInputMyRegex();

    private static async Task FollowAutoPostFormAsync(HttpClient client,
        HttpResponseMessage response,
        CancellationToken ct)
    {
        var html = await response.Content.ReadAsStringAsync(ct);

        // Not an auto-post page, return as-is
        if (!html.Contains("document.forms[0].submit()", StringComparison.OrdinalIgnoreCase)
            && !html.Contains("Onload", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // Extract form action URL
        var actionMatch = AutoPostFormRegex.Match(html);
        if (!actionMatch.Success)
        {
            throw new InvalidOperationException(
                "Found auto-post page but could not extract form action.\n\n" +
                html[..Math.Min(html.Length, 2000)]);
        }

        var actionUrl = HttpUtility.HtmlDecode(actionMatch.Groups["action"].Value);

        // Extract all hidden input fields
        var fields = AutoPostInputRegex
            .Matches(html)
            .ToDictionary(
                m => m.Groups["name"].Value,
                m => HttpUtility.HtmlDecode(m.Groups["value"].Value));

        var form = new FormUrlEncodedContent(fields);
        await client.PostAsync(actionUrl, form, ct);
    }

    [GeneratedRegex(
        """<form[^>]+action="(?<action>[^"]+)""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        "en-SG")]
    private static partial Regex AutoPostFormMyRegex();

    [GeneratedRegex(
        """<input[^>]+name="(?<name>[^"]+)"[^>]+value="(?<value>[^"]*)" """,
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        "en-SG")]
    private static partial Regex AutoPostInputMyRegex();

    [GeneratedRegex(
        """<form[^>]+id="kc-form-login"[^>]+action="(?<action>[^"]+)""",
        RegexOptions.IgnoreCase | RegexOptions.Compiled,
        "en-SG")]
    private static partial Regex MyRegex();
}