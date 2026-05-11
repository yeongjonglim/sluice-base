using Microsoft.AspNetCore.Authorization;

namespace SluiceBase.Api.Auth;

internal sealed class AnyPermissionAuthorizationHandler(
    ICurrentUserAccessor currentUser) : AuthorizationHandler<AnyPermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext ctx, AnyPermissionRequirement req)
    {
        var user = await currentUser.GetAsync(CancellationToken.None);
        if (user is not null && req.Permissions.Any(user.HasPermission))
        {
            ctx.Succeed(req);
        }
    }
}