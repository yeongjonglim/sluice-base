using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using SluiceBase.Core.Mcp;

namespace SluiceBase.Api.Mcp.Oauth;

// One subclass per supported grant_type, so adding a grant means implementing every step it needs.
internal abstract record TokenGrant(string ClientId)
{
    public static bool TryFromForm(IFormCollection form, [NotNullWhen(true)] out TokenGrant? grant)
    {
        if (!GrantType.TryFrom(form["grant_type"].ToString(), out var type))
        {
            grant = null;
            return false;
        }

        var clientId = form["client_id"].ToString();
        if (type == GrantType.AuthorizationCode)
        {
            grant = new AuthorizationCodeGrant(
                clientId, form["code"].ToString(), form["redirect_uri"].ToString(), form["code_verifier"].ToString());
        }
        else if (type == GrantType.RefreshToken)
        {
            grant = new RefreshTokenGrant(clientId, form["refresh_token"].ToString());
        }
        else
        {
            throw new UnreachableException($"No token grant for grant_type '{type}'.");
        }

        return true;
    }

    public abstract Task<LiveGrant?> FindLiveAsync(IMcpTokenService tokens, CancellationToken ct);

    public abstract Task<IssuedTokens?> RedeemAsync(IMcpTokenService tokens, CancellationToken ct);
}

internal sealed record AuthorizationCodeGrant(string ClientId, string Code, string RedirectUri, string CodeVerifier)
    : TokenGrant(ClientId)
{
    public override Task<LiveGrant?> FindLiveAsync(IMcpTokenService tokens, CancellationToken ct) =>
        tokens.FindLiveAuthCodeAsync(ClientId, Code, ct);

    public override Task<IssuedTokens?> RedeemAsync(IMcpTokenService tokens, CancellationToken ct) =>
        tokens.RedeemAuthCodeAsync(ClientId, Code, RedirectUri, CodeVerifier, ct);
}

internal sealed record RefreshTokenGrant(string ClientId, string RefreshToken) : TokenGrant(ClientId)
{
    public override Task<LiveGrant?> FindLiveAsync(IMcpTokenService tokens, CancellationToken ct) =>
        tokens.FindLiveRefreshTokenAsync(ClientId, RefreshToken, ct);

    public override Task<IssuedTokens?> RedeemAsync(IMcpTokenService tokens, CancellationToken ct) =>
        tokens.RefreshAsync(ClientId, RefreshToken, ct);
}
