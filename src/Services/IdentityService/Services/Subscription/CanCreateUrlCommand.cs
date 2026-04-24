using IdentityService.Data;
using IdentityService.Entities;
using IdentityService.Services.UsageTracking;
using MediatR;
using Microsoft.EntityFrameworkCore;

namespace IdentityService.Services.Subscription
{
    public class CanCreateUrlCommand : IRequest<bool>
    {
        public Guid TenantId { get; set; }
    }

    public class CanCreateUrlCommandHandler : IRequestHandler<CanCreateUrlCommand, bool>
    {
        private readonly AppIdentityDbContext _context;
        private readonly IMediator _mediator;
        public CanCreateUrlCommandHandler(AppIdentityDbContext context, IMediator mediator)
        {
            _context = context;
            _mediator = mediator;
        }
        public async Task<bool> Handle(CanCreateUrlCommand request, CancellationToken cancellationToken)
        {
            var subscription = await _mediator.Send(new GetActiveSubscriptionQuery() { TenantId = request.TenantId});
            if (subscription == null) return false;

            var plan = await _context.SubscriptionPlans.FindAsync(subscription.PlanId);
            // can to url service to get count var createdUrls = await _context.Set<ShortenedUrl>().CountAsync(u => u.TenantId == tenantId);
            var client = new HttpClient();
            int createdUrls = await client.GetFromJsonAsync<int>($"http://localhost:5000/api/url/count-by-tenant?{request.TenantId}");

            if (createdUrls >= plan.UrlLimit)
            {
                // Charge for extra URLs
                await _mediator.Send(new RecordUsageCommand() { TenantId = request.TenantId, Feature = "ExtraURLs", Quantity = 1, CostPerUnit = 0.05m });
            }

            return true;
        }
    }
}
