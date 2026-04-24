using Shared.Domain.Abstractions;
using Shared.Domain.Abstractions.IEntites;
using System;
using System.ComponentModel.DataAnnotations;

namespace UrlService.Models
{
    public class ShortUrl : EntityAuditBase<Guid>
    {
        [Required]
        public string OriginalUrl { get; set; } = string.Empty;
        [Required]
        public string ShortCode { get; set; } = string.Empty;

        public DateTimeOffset ExpirationDate { get; set; }
    }
}
