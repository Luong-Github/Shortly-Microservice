using IdentityService.Services.Tenant;
using MediatR;

namespace IdentityService.Middleware
{
    public class TenantMiddleware
    {
        private readonly RequestDelegate _next;

        public TenantMiddleware(RequestDelegate next)
        {
            _next = next;
        }

        public async Task Invoke(HttpContext context, IMediator mediator)
        {
            string host = context.Request.Host.Value;
            var tenant = await mediator.Send(new GetTenantAsyncQuery() { Domain = host });

            if (tenant != null)
            {
                context.Items["Tenant"] = tenant;
            }

            await _next(context);
        }
    }
}
