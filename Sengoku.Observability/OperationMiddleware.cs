using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace Sengoku.Observability;

public sealed class OperationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, OperationLogger logger)
    {
        var action = context.GetEndpoint()?.Metadata.GetMetadata<ControllerActionDescriptor>();
        // Use server-owned endpoint metadata, never URL paths/query strings or client correlation labels.
        var title = action == null ? "Http.Request" : action.ControllerName + "." + action.ActionName;
        using var operation = logger.Begin(title, forceNew: true);
        int? failureStatus = null;
        try
        {
            await next(context);
            if (context.Response.StatusCode >= 400) operation.Reject("HttpRequestRejected");
            else operation.Complete();
        }
        catch (Exception ex) { failureStatus = ex is OperationCanceledException ? 499 : 500; operation.Fail(ex); throw; }
        finally
        {
            OperationLog.Identity(("UserId", context.User.FindFirstValue(ClaimTypes.NameIdentifier)),
                ("Username", context.User.Identity?.IsAuthenticated == true ? context.User.Identity.Name : null),
                ("SessionActive", context.User.Identity?.IsAuthenticated == true),
                ("SessionFingerprint", context.User.Identity?.IsAuthenticated == true
                    ? OperationDataPolicy.SessionFingerprint(context.Request.Cookies["__Host-sengoku-session"]) : null),
                ("HttpStatusCode", failureStatus ?? context.Response.StatusCode));
        }
    }
}
