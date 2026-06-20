using Microsoft.AspNetCore.Authorization;

namespace SluiceBase.Api.Auth;

internal sealed class PermissionAuthorizationHandler(
    ICurrentUserAccessor currentUser,
    IAccessResolver resolver) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext ctx, PermissionRequirement req)
    {
        var user = await currentUser.GetAsync(CancellationToken.None);
        if (user is null)
        {
            return;
        }
        if (await resolver.HasGlobalPermissionAsync(user.Id, req.Permission, CancellationToken.None))
        {
            ctx.Succeed(req);
        }
    }
}