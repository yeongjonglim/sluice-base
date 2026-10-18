namespace SluiceBase.Api.Mcp.Clients;

internal enum ClientKind
{
    Configured,
    Cimd,
}

internal sealed record ResolvedClient(
    string ClientId,
    string DisplayName,
    string? VerifiedHost,
    IReadOnlyList<string> RedirectUris,
    ClientKind Kind);

internal abstract record ClientResolution
{
    private ClientResolution()
    {
    }

    public sealed record Found(ResolvedClient Client) : ClientResolution;

    public sealed record NotFound : ClientResolution
    {
        public static NotFound Instance { get; } = new();
    }

    public sealed record TemporarilyUnavailable : ClientResolution
    {
        public static TemporarilyUnavailable Instance { get; } = new();
    }
}
