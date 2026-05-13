namespace SluiceBase.Core.Permissions;

public static class Permissions
{
    public const string PermissionManage = "permission:manage";
    public const string ServerManage = "server:manage";
    public const string QueryExecute = "query:execute";
    public const string QueryAudit = "query:audit";
    public const string UpdateSubmit = "update:submit";
    public const string UpdateApprove = "update:approve";
    public const string UpdateExecute = "update:execute";

    // Virtual policy — never assigned to users; combines update:submit|approve|execute for read access
    public const string UpdateAny = "update:any";

    // Virtual policy — never assigned to users; any operational permission grants catalog read access
    public const string CatalogRead = "catalog:read";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        PermissionManage,
        ServerManage,
        QueryExecute,
        QueryAudit,
        UpdateSubmit,
        UpdateApprove,
        UpdateExecute,
    };
}