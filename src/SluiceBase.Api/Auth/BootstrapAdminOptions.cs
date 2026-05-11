namespace SluiceBase.Api.Auth;

public sealed class BootstrapAdminOptions
{
    public const string SectionName = "Permissions:Bootstrap";
    public IList<string> Admins { get; set; } = [];
}