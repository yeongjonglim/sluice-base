using Microsoft.AspNetCore.Authorization;

namespace SluiceBase.Api.Auth;

internal sealed class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}