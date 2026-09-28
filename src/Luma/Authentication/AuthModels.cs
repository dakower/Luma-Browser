using System.Text.Json.Serialization;
namespace Luma.Authentication;

public sealed class AuthUser
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("email")] public string Email { get; set; } = "";
    [JsonPropertyName("user_metadata")] public Dictionary<string, object?> UserMetadata { get; set; } = new();
    [JsonIgnore] public string DisplayName => UserMetadata.TryGetValue("display_name", out var value) && value is not null ? value.ToString() ?? Email : Email;
    [JsonIgnore] public string? AvatarUrl => UserMetadata.TryGetValue("avatar_url", out var value) && value is not null ? value.ToString() : null;
}
public sealed class AuthSession
{
    [JsonPropertyName("access_token")] public string AccessToken { get; set; } = "";
    [JsonPropertyName("refresh_token")] public string RefreshToken { get; set; } = "";
    [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
    [JsonPropertyName("expires_at")] public long ExpiresAt { get; set; }
    [JsonPropertyName("user")] public AuthUser? User { get; set; }
    [JsonIgnore] public bool ExpiresSoon => ExpiresAt <= DateTimeOffset.UtcNow.AddMinutes(2).ToUnixTimeSeconds();
}
internal sealed class AuthResponse
{
    [JsonPropertyName("access_token")] public string? AccessToken { get; set; }
    [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
    [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
    [JsonPropertyName("expires_at")] public long ExpiresAt { get; set; }
    [JsonPropertyName("user")] public AuthUser? User { get; set; }
    public AuthSession ToSession() => new() { AccessToken = AccessToken ?? "", RefreshToken = RefreshToken ?? "", ExpiresIn = ExpiresIn, ExpiresAt = ExpiresAt > 0 ? ExpiresAt : DateTimeOffset.UtcNow.AddSeconds(Math.Max(60, ExpiresIn)).ToUnixTimeSeconds(), User = User };
}
public sealed class AccountAccess
{
    [JsonPropertyName("role")] public string Role { get; set; } = "user";
    [JsonPropertyName("is_admin")] public bool IsAdmin { get; set; }
    [JsonPropertyName("has_beta")] public bool HasBeta { get; set; }
    [JsonPropertyName("beta_status")] public string BetaStatus { get; set; } = "none";
}
public sealed class SignUpResult { public required AuthUser User { get; init; } public bool RequiresEmailConfirmation { get; init; } }
public sealed class AuthException : Exception { public int? StatusCode { get; } public AuthException(string message, int? statusCode = null, Exception? inner = null) : base(message, inner) { StatusCode = statusCode; } }
