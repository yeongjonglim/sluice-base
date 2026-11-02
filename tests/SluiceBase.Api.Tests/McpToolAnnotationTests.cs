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

    // Every tool is read-only from the client's point of view: submitting only files a request, and a
    // human still decides what touches a database.
    public static TheoryData<string> Expected => new()
    {
        "list_databases",
        "get_schema",
        "run_query",
        "list_update_requests",
        "get_update_request",
        "submit_update_request",
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

    [Fact]
    public void EveryTool_IsCovered()
    {
        var names = ToolMethods.Select(m => m.GetCustomAttribute<McpServerToolAttribute>()!.Name).Order();
        Assert.Equal(Expected.Select(row => row.Data).Order(), names);
    }
}
