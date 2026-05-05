namespace SluiceBase.Core.Permissions;

public static class Permissions
{
    public const string PermissionManage = "permission:manage";
    public const string ServerManage = "server:manage";
    public const string QueryExecute = "query:execute";
    public const string UpdateSubmit = "update:submit";
    public const string UpdateApprove = "update:approve";
    public const string UpdateExecute = "update:execute";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        PermissionManage,
        ServerManage,
        QueryExecute,
        UpdateSubmit,
        UpdateApprove,
        UpdateExecute,
    };
}