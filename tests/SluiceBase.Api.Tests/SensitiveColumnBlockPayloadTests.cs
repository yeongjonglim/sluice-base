using SluiceBase.Api.Mcp;
using SluiceBase.Api.Services;

namespace SluiceBase.Api.Tests;

public class SensitiveColumnBlockPayloadTests
{
    [Fact]
    public void From_SetsStableErrorDiscriminator()
    {
        var payload = SensitiveColumnBlockPayload.From(
            [new BlockedColumn("hr", "employees", "ssn")]);

        Assert.Equal("sensitive_columns_blocked", payload.Error);
    }

    [Fact]
    public void From_ProjectsColumnsToQualifiedIdentifiers()
    {
        var payload = SensitiveColumnBlockPayload.From(
        [
            new BlockedColumn("hr", "employees", "ssn"),
            new BlockedColumn("hr", "employees", "salary"),
        ]);

        string[] expected = ["hr.employees.ssn", "hr.employees.salary"];
        Assert.Equal(expected, payload.BlockedColumns);
    }

    [Fact]
    public void From_GuidanceDetersWorkaroundsAndStatesNoOverride()
    {
        var payload = SensitiveColumnBlockPayload.From(
            [new BlockedColumn("hr", "employees", "ssn")]);

        Assert.False(string.IsNullOrWhiteSpace(payload.Guidance));
        // The guidance must tell a cooperative agent to exclude and stop probing,
        // and must be honest that it cannot be overridden from the client side.
        Assert.Contains("without them", payload.Guidance, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cannot override", payload.Guidance, StringComparison.OrdinalIgnoreCase);
    }
}
