using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace GameMatcher.Controllers;

public sealed class OrganizerWriteFilter : IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        if (HttpMethods.IsGet(context.HttpContext.Request.Method) || HttpMethods.IsHead(context.HttpContext.Request.Method) ||
            HttpMethods.IsOptions(context.HttpContext.Request.Method) ||
            context.ActionDescriptor.EndpointMetadata.OfType<IAllowAnonymous>().Any()) return;
        if (context.HttpContext.User.Identity?.IsAuthenticated != true)
            context.Result = new ObjectResult(new ProblemDetails
            {
                Status = 401, Title = "Organizer sign-in required.", Detail = "Only the organizer can change players and games."
            }) { StatusCode = 401 };
    }
}
