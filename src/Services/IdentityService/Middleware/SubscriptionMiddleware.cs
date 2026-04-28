using IdentityService.Entities;
using IdentityService.Services.Subscription;
using MediatR;
using Stripe;

namespace IdentityService.Middleware
{
    public class SubscriptionMiddleware
    {
        private readonly RequestDelegate _next;

        public SubscriptionMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext context, IMediator mediator)
        {
            var tenant = context.Items["Tenant"] as Tenant;
            if (tenant != null)
            {
                var subscription = await mediator.Send(new GetActiveSubscriptionQuery() { TenantId = tenant.Id});
                if (subscription == null || subscription.PaymentStatus != "Paid")
                {
                    context.Response.StatusCode = 402; // Payment Required
                    await context.Response.WriteAsync("Subscription expired or missing!");
                    return;
                }
            }

            await _next(context);
        }
    }
}
