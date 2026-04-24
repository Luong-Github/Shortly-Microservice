using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System;
using System.Security.Claims;
using UrlService.Events;
using UrlService.Models.Requests;
using UrlService.Repositories;
using UrlService.Services;
using UrlService.Services.UrlShortening.Commands;
using UrlService.Services.UrlShortening.Queries;

namespace UrlService.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UrlController : ControllerBase
    {
        private readonly IUrlRepository _urlRepository;
        private readonly UrlShorteningService _urlShorteningService;
        private readonly IMediator _mediator;
        private readonly IClickEventPublisher _clickEventPublisher;

        public UrlController(IUrlRepository urlRepository, UrlShorteningService urlShorteningService, IMediator mediator, IClickEventPublisher clickEventPublisher)
        {
            _urlRepository = urlRepository;
            _urlShorteningService = urlShorteningService;
            _mediator = mediator;
            _clickEventPublisher = clickEventPublisher;
        }

        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        [HttpPost("shorten")]
        public async Task<IActionResult> ShortenUrl([FromBody] ShortenUrlCommand command)
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            string shortCode = await _mediator.Send(command);
            return Ok(new {shortCode});
        }

        [HttpGet("{shortCode}")]
        public async Task<IActionResult> GetOriginalUrl(string shortCode)
        {
            var originalUrl = await _mediator.Send(new GetOriginalUrlQuery(shortCode));

            if (originalUrl == null) return NotFound();

            // Track click event asynchronously (fire-and-forget to not block HTTP redirect)
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var ipAddress = Request.HttpContext.Connection.RemoteIpAddress?.ToString();
            var userAgent = Request.Headers["User-Agent"].ToString();
            
            _ = _clickEventPublisher.PublishClickEventAsync(userId, shortCode, ipAddress, userAgent);

            return Redirect(originalUrl);
        }

        [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
        [HttpGet("my-urls")]
        public async Task<IActionResult> GetUserUrls()
        {
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var shortUrls = await _urlRepository.GetAllByUserId(Guid.Parse(userId));
            return Ok(shortUrls);
        }

        [HttpGet("count-by-tenant")]
        public async Task<IActionResult> GetTotalUrlByTenantId(string tenantId)
        {
            ///// Todo:
            return Ok(10);
        }
    }
}
