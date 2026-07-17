using SluiceBase.Api.Queries;

namespace SluiceBase.Api.Tests;

public sealed class PostgresPlanParserTests
{
    private const string EstimateJson = """
        [{"Plan":{"Node Type":"Seq Scan","Relation Name":"users","Total Cost":123.45,"Plan Rows":1000}}]
        """;

    private const string NestedNoSeqScanJson = """
        [{"Plan":{"Node Type":"Aggregate","Total Cost":50.0,"Plan Rows":1,
          "Plans":[{"Node Type":"Index Scan","Total Cost":40.0,"Plan Rows":10}]}}]
        """;

    private const string AnalyzeJson = """
        [{"Plan":{"Node Type":"Index Scan","Total Cost":8.3,"Plan Rows":1,"Actual Total Time":0.5},
          "Planning Time":0.2,"Execution Time":1.75}]
        """;

    [Fact]
    public void Parse_Estimate_ExtractsCostRowsRootAndSeqScan()
    {
        var summary = PostgresPlanParser.Parse(EstimateJson);

        Assert.Equal(123.45, summary.TotalCost);
        Assert.Equal(1000, summary.EstimatedRows);
        Assert.Equal("Seq Scan", summary.RootNode);
        Assert.True(summary.HasSeqScan);
        Assert.Null(summary.ActualTotalMs);
    }

    [Fact]
    public void Parse_NestedPlanWithoutSeqScan_HasSeqScanFalse()
    {
        var summary = PostgresPlanParser.Parse(NestedNoSeqScanJson);

        Assert.False(summary.HasSeqScan);
        Assert.Equal("Aggregate", summary.RootNode);
    }

    [Fact]
    public void Parse_Analyze_PopulatesActualTotalMs()
    {
        var summary = PostgresPlanParser.Parse(AnalyzeJson);

        Assert.Equal(1.75, summary.ActualTotalMs);
    }
}
