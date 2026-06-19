using Microsoft.AspNetCore.Authorization;

namespace SluiceBase.Api.Auth;

internal sealed class AnyPermissionAuthorizationHandler(
    ICurrentUserAccessor currentUser,
    IAccessResolver resolver) : AuthorizationHandler<AnyPermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext ctx, AnyPermissionRequirement req)
    {
        var user = await currentUser.GetAsync(CancellationToken.None);
        if (user is null)
        {
            return;
        }
        foreach (var permission in req.Permissions)
        {
            if (await resolver.HasGlobalPermissionAsync(user.Id, permission, CancellationToken.None))
            {
                ctx.Succeed(req);
                return;
            }
        }
    }
}