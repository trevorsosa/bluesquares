using BlueSquares.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BlueSquares.Filters;

/// <summary>
/// Rejects the request with HTTP 402 if the authenticated merchant's subscription
/// is expired. Apply to any action that should be gated behind an active subscription.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class RequireActiveSubscriptionAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var merchantIdClaim = context.HttpContext.User.Claims
            .FirstOrDefault(c => c.Type == "merchant_id")?.Value;

        if (string.IsNullOrEmpty(merchantIdClaim) || !Guid.TryParse(merchantIdClaim, out var merchantId))
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        var subscriptionService = context.HttpContext.RequestServices
            .GetRequiredService<ISubscriptionService>();

        var isActive = await subscriptionService.IsSubscriptionActive(merchantId);

        if (!isActive)
        {
            context.Result = new ObjectResult(new
            {
                error = "Your subscription has expired. Please upgrade to continue.",
                code = "SUBSCRIPTION_EXPIRED"
            })
            { StatusCode = 402 };
            return;
        }

        await next();
    }
}
