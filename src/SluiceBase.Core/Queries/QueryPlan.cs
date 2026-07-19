namespace SluiceBase.Core.Queries;

public sealed record QueryPlan(string PlanJson, QueryPlanSummary Summary);

public sealed record QueryPlanSummary(
    double TotalCost,
    double EstimatedRows,
    string RootNode,
    bool HasSeqScan,
    double? ActualTotalMs);
