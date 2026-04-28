using MediatR;
using System.Security.Cryptography;
using System.Text;
using UrlService.Models;
using UrlService.Repositories;

namespace UrlService.Services.UrlShortening.Commands
{
    public class ShortenUrlCommand : IRequest<string>
    {
        public string OriginalUrl { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        
        public ShortenUrlCommand()
        {
        }

        public ShortenUrlCommand(string originalUrl, string userId)
        {
            OriginalUrl = originalUrl;
            UserId = userId;
        }
    }

    public class ShortenUrlCommandHandler : IRequestHandler<ShortenUrlCommand, string>
    {
        private readonly IUrlRepository _urlRepository;

        public ShortenUrlCommandHandler(IUrlRepository urlRepository)
        {
            _urlRepository = urlRepository;
        }

        public async Task<string> Handle(ShortenUrlCommand request, CancellationToken cancellationToken)
        {
            var shortCode = GenerateShortCode();
            var shortUrl = new ShortUrl
            {
                OriginalUrl = request.OriginalUrl,
                ShortCode = shortCode,
                CreatedBy =  Guid.Parse(request.UserId),
                CreatedDate = DateTimeOffset.UtcNow,
                IsDeleted = false
            };

            return (await _urlRepository.CreateAsync(shortUrl)).ShortCode;
        }

        #region Private Methods
        private string GenerateShortCode()
        {
            using var sha256 = SHA256.Create();
            byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(Guid.NewGuid().ToString()));
            return ConvertToBase62(hashBytes);
        }

        private string ConvertToBase62(byte[] hash)
        {
            const string base62 = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz";
            var sb = new StringBuilder();

            foreach (byte b in hash)
            {
                sb.Append(base62[b % base62.Length]);
            }
            return sb.ToString();
        }
        #endregion
    }
}
