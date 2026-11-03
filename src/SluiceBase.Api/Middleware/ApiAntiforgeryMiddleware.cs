using Microsoft.AspNetCore.Antiforgery;

namespace SluiceBase.Api.Middleware;

// Browser calls to /api are authenticated by cookie, so a state-changing call must also carry the antiforgery request
// token the SPA reads from the XSRF-TOKEN cookie. SameSite=Lax alone still lets a sibling subdomain post to the API.
internal sealed class ApiAntiforgeryMiddleware(RequestDelegate next, IAntiforgery antiforgery)
{
    public const string FailureType = "antiforgery";

    public async Task InvokeAsync(HttpContext context)
    {
        if (RequiresValidation(context))
        {
            try
            {
                await antiforgery.ValidateRequestAsync(context);
            }
            catch (AntiforgeryValidationException)
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                await context.Response.WriteAsJsonAsync(new
                {
                    type = FailureType,
                    title = "The request's antiforgery token is missing or no longer valid.",
                });
                return;
            }
        }

        await next(context);
    }

    private static bool RequiresValidation(HttpContext context)
    {
        var method = context.Request.Method;
        if (HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method)
            || HttpMethods.IsTrace(method) || !context.Request.Path.StartsWithSegments("/api"))
        {
            return false;
        }

        return context.GetEndpoint()?.Metadata.GetMetadata<IAntiforgeryMetadata>() is not { RequiresValidation: false };
    }
}
