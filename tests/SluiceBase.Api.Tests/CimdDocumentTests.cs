using System.Text.Json;
using SluiceBase.Api.Mcp.Clients;

namespace SluiceBase.Api.Tests;

public class CimdDocumentTests
{
    private const string ChatGptUrl = "https://chatgpt.com/oauth/client.json";

    [Theory]
    [InlineData("https://claude.ai/oauth/claude-code-client-metadata")]
    [InlineData(ChatGptUrl)]
    [InlineData("https://x.example:8443/c.json")]
    [InlineData("https://xn--chtgpt-4nf.com/oauth/client.json")]
    public void IsAcceptableClientIdUrl_Accepts(string url)
    {
        Assert.True(CimdDocument.IsAcceptableClientIdUrl(url, out var parsed));
        Assert.NotNull(parsed);
    }

    [Theory]
    [InlineData("https://x.example")]
    [InlineData("https://x.example/")]
    [InlineData("https://u@x.example/c")]
    [InlineData("https://x.example/c#f")]
    [InlineData("https://x.example/a/../c")]
    [InlineData("https://x.example/./c")]
    [InlineData("https://x.example/a/%2e%2e/c")]
    [InlineData("http://x.example/c")]
    [InlineData("https://x.example/a/.%2e/c")]
    [InlineData("https://x.example/a/%2e./c")]
    [InlineData("https://x.example/a\\..\\c")]
    [InlineData(" https://x.example/c")]
    [InlineData("https://x.example/c\n")]
    [InlineData("HTTPS://x.example/c")]
    [InlineData("not a url")]
    [InlineData("https://ch\u0430tgpt.com/oauth/client.json")]
    [InlineData("https://x.example/caf\u00e9")]
    public void IsAcceptableClientIdUrl_Rejects(string url)
    {
        Assert.False(CimdDocument.IsAcceptableClientIdUrl(url, out _));
    }

    [Fact]
    public void Validate_HappyPath()
    {
        var doc = Parse($$"""
            {"client_id":"{{ChatGptUrl}}","client_name":"ChatGPT",
             "redirect_uris":["https://chatgpt.com/connector_platform_oauth_redirect","http://evil.example/cb"]}
            """);

        var client = CimdDocument.Validate(new Uri(ChatGptUrl), doc);

        Assert.NotNull(client);
        Assert.Equal(ChatGptUrl, client.ClientId);
        Assert.Equal("ChatGPT", client.DisplayName);
        Assert.Equal("chatgpt.com", client.VerifiedHost);
        Assert.Equal(["https://chatgpt.com/connector_platform_oauth_redirect"], client.RedirectUris);
        Assert.Equal(ClientKind.Cimd, client.Kind);
    }

    [Theory]
    [InlineData("https://chatgpt.com/oauth/client.json/")]
    [InlineData("https://ChatGPT.com/oauth/client.json")]
    [InlineData("https://chatgpt.com:443/oauth/client.json")]
    public void Validate_ClientIdMismatch_Null(string documentClientId)
    {
        var doc = Parse($$"""{"client_id":"{{documentClientId}}","redirect_uris":["https://chatgpt.com/cb"]}""");

        Assert.Null(CimdDocument.Validate(new Uri(ChatGptUrl), doc));
    }

    [Theory]
    [InlineData("""{"redirect_uris":["https://chatgpt.com/cb"]}""")]
    [InlineData("""{"client_id":5,"redirect_uris":["https://chatgpt.com/cb"]}""")]
    [InlineData("""[]""")]
    [InlineData("""{"client_id":"https://chatgpt.com/oauth/client.json"}""")]
    [InlineData("""{"client_id":"https://chatgpt.com/oauth/client.json","redirect_uris":"https://chatgpt.com/cb"}""")]
    public void Validate_MalformedShape_Null(string json)
    {
        Assert.Null(CimdDocument.Validate(new Uri(ChatGptUrl), Parse(json)));
    }

    [Fact]
    public void Validate_NoUsableRedirects_Null()
    {
        var doc = Parse($$"""{"client_id":"{{ChatGptUrl}}","redirect_uris":["http://evil.example/cb",7]}""");

        Assert.Null(CimdDocument.Validate(new Uri(ChatGptUrl), doc));
    }

    [Theory]
    [InlineData("client_secret", "\"s\"")]
    [InlineData("client_secret_expires_at", "0")]
    public void Validate_SecretPresent_Null(string property, string value)
    {
        var doc = Parse($$"""{"client_id":"{{ChatGptUrl}}","redirect_uris":["https://chatgpt.com/cb"],"{{property}}":{{value}}}""");

        Assert.Null(CimdDocument.Validate(new Uri(ChatGptUrl), doc));
    }

    [Fact]
    public void Validate_AuthMethodNotNone_Null()
    {
        var doc = Parse($$"""
            {"client_id":"{{ChatGptUrl}}","redirect_uris":["https://chatgpt.com/cb"],
             "token_endpoint_auth_method":"private_key_jwt",
             "token_endpoint_auth_methods_supported":["private_key_jwt"]}
            """);

        Assert.Null(CimdDocument.Validate(new Uri(ChatGptUrl), doc));
    }

    [Fact]
    public void Validate_SingularNotNonePluralNone_Ok()
    {
        var doc = Parse($$"""
            {"client_id":"{{ChatGptUrl}}","redirect_uris":["https://chatgpt.com/cb"],
             "token_endpoint_auth_method":"private_key_jwt",
             "token_endpoint_auth_methods_supported":["none"]}
            """);

        Assert.NotNull(CimdDocument.Validate(new Uri(ChatGptUrl), doc));
    }

    [Fact]
    public void Validate_SingularNotNoneWithoutPlural_Null()
    {
        var doc = Parse($$"""
            {"client_id":"{{ChatGptUrl}}","redirect_uris":["https://chatgpt.com/cb"],
             "token_endpoint_auth_method":"private_key_jwt"}
            """);

        Assert.Null(CimdDocument.Validate(new Uri(ChatGptUrl), doc));
    }

    [Fact]
    public void Validate_PluralNotArray_Null()
    {
        var doc = Parse($$"""
            {"client_id":"{{ChatGptUrl}}","redirect_uris":["https://chatgpt.com/cb"],
             "token_endpoint_auth_method":"none",
             "token_endpoint_auth_methods_supported":"none"}
            """);

        Assert.Null(CimdDocument.Validate(new Uri(ChatGptUrl), doc));
    }

    // Verbatim copies of the live documents, fetched 2026-10-19.
    public static TheoryData<string, string> RealWorldDocuments => new()
    {
        {
            "https://chatgpt.com/oauth/client.json",
            """{"client_id":"https://chatgpt.com/oauth/client.json","client_uri":"https://chatgpt.com/","redirect_uris":["https://chatgpt.com/connector_platform_oauth_redirect"],"token_endpoint_auth_method":"private_key_jwt","token_endpoint_auth_methods_supported":["none","private_key_jwt"],"grant_types":["authorization_code","refresh_token"],"response_types":["code"],"client_name":"ChatGPT","logo_uri":"https://persistent.oaistatic.com/sonic/misc/openai-logo.png","token_endpoint_auth_signing_alg":"RS256","jwks_uri":"https://chatgpt.com/oauth/jwks.json"}"""
        },
        {
            "https://claude.ai/oauth/claude-code-client-metadata",
            """{"client_id":"https://claude.ai/oauth/claude-code-client-metadata","client_name":"Claude Code","client_uri":"https://claude.ai","redirect_uris":["http://localhost/callback","http://127.0.0.1/callback"],"grant_types":["authorization_code","refresh_token"],"response_types":["code"],"token_endpoint_auth_method":"none"}"""
        },
        {
            "https://chatgpt.com/oauth/codex/client.json",
            """{"client_id":"https://chatgpt.com/oauth/codex/client.json","client_uri":"https://chatgpt.com/codex","application_type":"native","redirect_uris":["http://127.0.0.1/callback","http://localhost/callback"],"token_endpoint_auth_method":"none","token_endpoint_auth_methods_supported":["none"],"grant_types":["authorization_code","refresh_token"],"response_types":["code"],"client_name":"Codex","logo_uri":"https://persistent.oaistatic.com/sonic/misc/openai-logo.png"}"""
        },
        {
            "https://vscode.dev/oauth/client-metadata.json",
            """{"client_name":"Visual Studio Code","logo_uri":"https://code.visualstudio.com/assets/branding/code-stable.png","grant_types":["authorization_code","refresh_token","urn:ietf:params:oauth:grant-type:device_code"],"response_types":["code"],"token_endpoint_auth_method":"none","application_type":"native","client_id":"https://vscode.dev/oauth/client-metadata.json","client_uri":"https://vscode.dev/product","redirect_uris":["http://127.0.0.1:33418/","https://vscode.dev/redirect"]}"""
        },
    };

    [Theory]
    [MemberData(nameof(RealWorldDocuments))]
    public void Validate_RealWorldDocuments_Accepted(string clientId, string json)
    {
        Assert.True(CimdDocument.IsAcceptableClientIdUrl(clientId, out var url));

        var client = CimdDocument.Validate(url, Parse(json));

        Assert.NotNull(client);
        Assert.Equal(clientId, client.ClientId);
        Assert.NotEmpty(client.RedirectUris);
    }

    [Theory]
    [InlineData("\"token_endpoint_auth_method\":\"none\"")]
    [InlineData("\"token_endpoint_auth_methods_supported\":[\"none\"]")]
    [InlineData("\"x\":1")]
    public void Validate_AuthMethodValidForms_Ok(string extra)
    {
        var doc = Parse($$"""{"client_id":"{{ChatGptUrl}}","redirect_uris":["https://chatgpt.com/cb"],{{extra}}}""");

        Assert.NotNull(CimdDocument.Validate(new Uri(ChatGptUrl), doc));
    }

    [Fact]
    public void Validate_NonStringClientName_DisplaysHost()
    {
        var doc = Parse($$"""{"client_id":"{{ChatGptUrl}}","client_name":5,"redirect_uris":["https://chatgpt.com/cb"]}""");

        Assert.Equal("chatgpt.com", CimdDocument.Validate(new Uri(ChatGptUrl), doc)?.DisplayName);
    }

    [Fact]
    public void Validate_AuthMethodsPluralIncludesNone_Ok()
    {
        var doc = Parse($$"""
            {"client_id":"{{ChatGptUrl}}","redirect_uris":["https://chatgpt.com/cb"],
             "token_endpoint_auth_methods_supported":["private_key_jwt","none"]}
            """);

        Assert.NotNull(CimdDocument.Validate(new Uri(ChatGptUrl), doc));
    }

    [Fact]
    public void Validate_NoClientName_DisplaysHost()
    {
        var doc = Parse($$"""{"client_id":"{{ChatGptUrl}}","redirect_uris":["https://chatgpt.com/cb"]}""");

        var client = CimdDocument.Validate(new Uri(ChatGptUrl), doc);

        Assert.Equal("chatgpt.com", client?.DisplayName);
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();
}
