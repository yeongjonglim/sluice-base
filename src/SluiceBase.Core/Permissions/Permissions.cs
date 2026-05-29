namespace SluiceBase.Core.Permissions;

public static class Permissions
{
    public const string PermissionManage = "permission:manage";
    public const string ServerManage = "server:manage";
    public const string GroupManage = "group:manage";
    public const string QueryExecute = "query:execute";
    public const string QueryAudit = "query:audit";
    public const string UpdateSubmit = "update:submit";
    public const string UpdateApprove = "update:approve";
    public const string UpdateExecute = "update:execute";

    // Global permissions managed in user_permission — grantable from the Permissions admin page.
    public static readonly IReadOnlySet<string> Global = new HashSet<string>
    {
        PermissionManage,
        ServerManage,
        GroupManage,
    };

    // Operational permissions managed in user_database_role — grantable per database from the Access admin page.
    public static readonly IReadOnlySet<string> Scopeable = new HashSet<string>
    {
        QueryExecute,
        QueryAudit,
        UpdateSubmit,
        UpdateApprove,
        UpdateExecute,
    };
}
