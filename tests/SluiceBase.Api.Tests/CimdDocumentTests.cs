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
    [InlineData("not a url")]
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
