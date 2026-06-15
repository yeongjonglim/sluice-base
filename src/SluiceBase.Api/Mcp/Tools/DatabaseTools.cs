using System.ComponentModel;
using ModelContextProtocol.Server;
using SluiceBase.Api.Auth;
using SluiceBase.Api.Services;

namespace SluiceBase.Api.Mcp.Tools;

[McpServerToolType]
internal sealed class DatabaseTools
{
    [McpServerTool(Name = "list_databases")]
    [Description("List databases the authenticated user can query, grouped by server.")]
    public static async Task<object> ListDatabases(
        ICatalogService catalog, ICurrentUserAccessor currentUser, CancellationToken ct)
    {
        var user = await currentUser.GetAsync(ct)
            ?? throw new InvalidOperationException("No authenticated user.");
        var result = await catalog.ListAccessibleAsync(user, ct);
        return result;
    }
}
