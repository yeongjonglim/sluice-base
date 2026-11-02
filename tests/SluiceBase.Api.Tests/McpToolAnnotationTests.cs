using System.Reflection;
using ModelContextProtocol.Server;
using SluiceBase.Api.Mcp.Tools;

namespace SluiceBase.Api.Tests;

public class McpToolAnnotationTests
{
    private static readonly MethodInfo[] ToolMethods = typeof(DatabaseTools).Assembly.GetTypes()
        .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
        .SelectMany(t => t.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance))
        .Where(m => m.GetCustomAttribute<McpServerToolAttribute>() is not null)
        .ToArray();

    // Read-only from the client's point of view: submitting or cancelling only files or withdraws a
    // request, and a human still decides what touches a database.
    public static TheoryData<string> Expected => new()
    {
        "list_databases",
        "get_schema",
        "run_query",
        "list_update_requests",
        "get_update_request",
        "submit_update_request",
        "cancel_update_request",
    };

    [Theory]
    [MemberData(nameof(Expected))]
    public void Tool_IsReadOnlyAndClosedWorld(string name)
    {
        var method = ToolMethods.Single(m => m.GetCustomAttribute<McpServerToolAttribute>()!.Name == name);
        var annotations = McpServerTool.Create(method).ProtocolTool.Annotations;

        Assert.NotNull(annotations);
        Assert.False(string.IsNullOrWhiteSpace(annotations.Title));
        Assert.Equal(true, annotations.ReadOnlyHint);
        Assert.Equal(false, annotations.OpenWorldHint);
    }

    // Preview runs the request's write SQL before rolling it back, so clients must treat it as a write.
    [Fact]
    public void PreviewTool_IsMarkedAsWritingButNotDestructive()
    {
        var method = ToolMethods.Single(m => m.GetCustomAttribute<McpServerToolAttribute>()!.Name == "preview_update_request");
        var annotations = McpServerTool.Create(method).ProtocolTool.Annotations;

        Assert.NotNull(annotations);
        Assert.False(string.IsNullOrWhiteSpace(annotations.Title));
        Assert.Equal(false, annotations.ReadOnlyHint);
        Assert.Equal(false, annotations.DestructiveHint);
        Assert.Equal(false, annotations.OpenWorldHint);
    }

    [Fact]
    public void EveryTool_IsCovered()
    {
        var names = ToolMethods.Select(m => m.GetCustomAttribute<McpServerToolAttribute>()!.Name).Order();
        Assert.Equal(Expected.Select(row => row.Data).Append("preview_update_request").Order(), names);
    }
}
