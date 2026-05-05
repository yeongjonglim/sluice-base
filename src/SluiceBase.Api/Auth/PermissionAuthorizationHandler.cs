using Microsoft.AspNetCore.Authorization;

namespace SluiceBase.Api.Auth;

internal sealed class PermissionAuthorizationHandler(
    ICurrentUserAccessor currentUser) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext ctx, PermissionRequirement req)
    {
        var user = await currentUser.GetAsync(CancellationToken.None);
        if (user?.HasPermission(req.Permission) is true)
        {
            ctx.Succeed(req);
        }
    }
}