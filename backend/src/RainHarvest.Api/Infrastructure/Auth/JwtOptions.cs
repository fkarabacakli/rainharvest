using System.ComponentModel.DataAnnotations;
using System.Text;

namespace RainHarvest.Api.Infrastructure.Auth;

public sealed class JwtOptions : IValidatableObject
{
    public const string SectionName = "Jwt";

    [Required]
    public string Issuer { get; set; } = "rainharvest";

    [Required]
    public string Audience { get; set; } = "rainharvest-api";

    /// <summary>HMAC-SHA256 key, at least 32 bytes. Never commit a real one: use user-secrets or env vars.</summary>
    [Required]
    public string SigningKey { get; set; } = "";

    [Range(typeof(TimeSpan), "00:05:00", "7.00:00:00")]
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromHours(8);

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (Encoding.UTF8.GetByteCount(SigningKey) < 32)
        {
            yield return new ValidationResult(
                "Jwt:SigningKey must be at least 32 bytes (256 bits).", [nameof(SigningKey)]);
        }
    }
}
