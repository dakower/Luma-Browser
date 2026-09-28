using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Luma.Authentication;

public interface IAuthService
{
    bool IsConfigured { get; }
    AuthUser? CurrentUser { get; }
    AccountAccess? Access { get; }
    bool IsAuthenticated { get; }
    event EventHandler? SessionChanged;
    Task RestoreSessionAsync(bool allow, CancellationToken ct = default);
    Task SignInAsync(string email, string password, CancellationToken ct = default);
    Task<SignUpResult> SignUpAsync(string email, string password, string name, CancellationToken ct = default);
    Task SendPasswordResetAsync(string email, CancellationToken ct = default);
    Task<string> CreateBetaCodeAsync(string label, int maxUses, int validDays, CancellationToken ct = default);
    Task<string?> GetAccessTokenAsync(CancellationToken ct = default);
    Task<string> UploadAvatarAsync(byte[] pngBytes, CancellationToken ct = default);
    Task RemoveAvatarAsync(CancellationToken ct = default);
    Task SignOutAsync(CancellationToken ct = default);
}

public sealed class SupabaseAuthService : IAuthService
{
    private readonly SupabaseOptions options;
    private readonly IAuthSessionStore store;
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly SemaphoreSlim gate = new(1, 1);
    private AuthSession? session;
    private AccountAccess? access;
    private bool restored;

    public SupabaseAuthService(SupabaseOptions options, IAuthSessionStore store)
    {
        this.options = options; this.store = store;
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Luma-Browser/2.0.9");
    }
    public bool IsConfigured => options.IsConfigured;
    public AuthUser? CurrentUser => session?.User;
    public AccountAccess? Access => access;
    public bool IsAuthenticated => CurrentUser is not null && !string.IsNullOrWhiteSpace(session?.RefreshToken);
    public event EventHandler? SessionChanged;

    public async Task RestoreSessionAsync(bool allow, CancellationToken ct = default)
    {
        if (restored) return;
        await gate.WaitAsync(ct);
        try
        {
            if (restored) return; restored = true; if (!allow) return;
            session = store.Load(); if (session is null) return;
            // Render the cached account immediately. Refreshing an expiring token or loading
            // tester/admin access can take several seconds; the sidebar must not look signed out
            // while a valid local session is already available.
            Changed();
            if (session.ExpiresSoon)
            {
                try { await Refresh(ct); }
                catch (AuthException ex) when (ex.StatusCode is 400 or 401 or 403) { Clear(); return; }
                catch (AuthException ex) { App.Log(ex); }
            }
            // Pull the current Auth metadata instead of trusting the locally cached session.
            // This restores the permanent Supabase avatar after a reinstall and also picks up
            // a change made by another device or by the website.
            try
            {
                if (!string.IsNullOrWhiteSpace(session?.AccessToken))
                {
                    session.User = await Send<AuthUser>(HttpMethod.Get, "/auth/v1/user", null, session.AccessToken, ct);
                    store.Save(session);
                }
            }
            catch (AuthException ex) { App.Log(ex); }
            await LoadAccessSafe(ct); Changed();
        }
        finally { gate.Release(); }
    }

    public async Task SignInAsync(string email, string password, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try { Set(Need(await Send<AuthResponse>(HttpMethod.Post, "/auth/v1/token?grant_type=password", new { email = NormalizeEmail(email), password }, null, ct))); await LoadAccessSafe(ct); Changed(); }
        finally { gate.Release(); }
    }

    public async Task<SignUpResult> SignUpAsync(string email, string password, string name, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            var response = await Send<AuthResponse>(HttpMethod.Post, "/auth/v1/signup", new { email = NormalizeEmail(email), password, data = new { display_name = name.Trim() } }, null, ct);
            var user = response.User ?? throw new AuthException("Сервер не вернул пользователя.");
            var confirm = string.IsNullOrWhiteSpace(response.AccessToken) || string.IsNullOrWhiteSpace(response.RefreshToken);
            if (!confirm) { Set(Need(response)); await LoadAccessSafe(ct); Changed(); }
            return new() { User = user, RequiresEmailConfirmation = confirm };
        }
        finally { gate.Release(); }
    }

    public Task SendPasswordResetAsync(string email, CancellationToken ct = default) => Send<JsonElement>(HttpMethod.Post, "/auth/v1/recover", new { email = NormalizeEmail(email) }, null, ct, true);

    public async Task<string> CreateBetaCodeAsync(string label, int maxUses, int validDays, CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (session is null || access?.IsAdmin != true) throw new AuthException("Недостаточно прав администратора.", 403);
            if (session.ExpiresSoon) await Refresh(ct);
            return await Send<string>(HttpMethod.Post, "/rest/v1/rpc/create_beta_code", new { p_label = label.Trim(), p_max_uses = Math.Clamp(maxUses, 1, 10000), p_valid_days = Math.Clamp(validDays, 1, 365) }, session.AccessToken, ct);
        }
        finally { gate.Release(); }
    }

    public async Task<string?> GetAccessTokenAsync(CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try { if (session is null) return null; if (session.ExpiresSoon) { try { await Refresh(ct); } catch (AuthException ex) when (ex.StatusCode is 400 or 401 or 403) { Clear(); return null; } } return session.AccessToken; }
        finally { gate.Release(); }
    }

    public async Task<string> UploadAvatarAsync(byte[] pngBytes, CancellationToken ct = default)
    {
        if (pngBytes is null || pngBytes.Length == 0) throw new ArgumentException("Изображение пустое.", nameof(pngBytes));
        await gate.WaitAsync(ct);
        try
        {
            var current = await RequireCurrentSessionAsync(ct);
            var objectName = $"{current.User!.Id}/avatar.png";
            await UploadStorageObjectAsync("avatars", objectName, pngBytes, current.AccessToken, ct);
            var publicUrl = $"{SupabaseOptions.ProjectUrl}/storage/v1/object/public/avatars/{current.User.Id}/avatar.png?v={DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}";
            await SetAvatarReferencesAsync(publicUrl, current, ct);
            Changed();
            return publicUrl;
        }
        finally { gate.Release(); }
    }

    public async Task RemoveAvatarAsync(CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            var current = await RequireCurrentSessionAsync(ct);
            await SetAvatarReferencesAsync(null, current, ct);
            await DeleteStorageObjectAsync("avatars", $"{current.User!.Id}/avatar.png", current.AccessToken, ct);
            Changed();
        }
        finally { gate.Release(); }
    }

    public async Task SignOutAsync(CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (!string.IsNullOrWhiteSpace(session?.AccessToken)) try { await Send<JsonElement>(HttpMethod.Post, "/auth/v1/logout", null, session.AccessToken, ct, true); } catch (AuthException ex) { App.Log(ex); }
            Clear();
        }
        finally { gate.Release(); }
    }

    private async Task<AuthSession> RequireCurrentSessionAsync(CancellationToken ct)
    {
        if (session?.User is null) throw new AuthException("Сначала войдите в аккаунт Luma.", 401);
        if (session.ExpiresSoon) await Refresh(ct);
        if (session?.User is null || string.IsNullOrWhiteSpace(session.AccessToken)) throw new AuthException("Сессия завершена. Войдите снова.", 401);
        return session;
    }

    private async Task SetAvatarReferencesAsync(string? avatarUrl, AuthSession current, CancellationToken ct)
    {
        var updatedUser = await Send<AuthUser>(HttpMethod.Put, "/auth/v1/user", new { data = new { avatar_url = avatarUrl } }, current.AccessToken, ct);
        current.User = updatedUser;
        store.Save(current);
        var id = Uri.EscapeDataString(updatedUser.Id);
        await Send<JsonElement>(HttpMethod.Patch, $"/rest/v1/profiles?id=eq.{id}", new { avatar_url = avatarUrl, updated_at = DateTimeOffset.UtcNow }, current.AccessToken, ct, true);
    }

    private async Task UploadStorageObjectAsync(string bucket, string objectName, byte[] bytes, string token, CancellationToken ct)
    {
        var path = string.Join("/", objectName.Split('/').Select(Uri.EscapeDataString));
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{SupabaseOptions.ProjectUrl}/storage/v1/object/{Uri.EscapeDataString(bucket)}/{path}");
        request.Headers.TryAddWithoutValidation("apikey", SupabaseOptions.PublishableKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.TryAddWithoutValidation("x-upsert", "true");
        request.Headers.TryAddWithoutValidation("cache-control", "3600");
        request.Content = new ByteArrayContent(bytes);
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        await SendStorageRequestAsync(request, ct);
    }

    private async Task DeleteStorageObjectAsync(string bucket, string objectName, string token, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, $"{SupabaseOptions.ProjectUrl}/storage/v1/object/{Uri.EscapeDataString(bucket)}");
        request.Headers.TryAddWithoutValidation("apikey", SupabaseOptions.PublishableKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new StringContent(JsonSerializer.Serialize(new { prefixes = new[] { objectName } }), Encoding.UTF8, "application/json");
        await SendStorageRequestAsync(request, ct);
    }

    private async Task SendStorageRequestAsync(HttpRequestMessage request, CancellationToken ct)
    {
        HttpResponseMessage response;
        try { response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct); }
        catch (HttpRequestException ex) { throw new AuthException("Нет соединения с сервером.", inner: ex); }
        using (response)
        {
            var content = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode) throw Error(response.StatusCode, content);
        }
    }

    private async Task Refresh(CancellationToken ct)
    {
        var token = session?.RefreshToken; if (string.IsNullOrWhiteSpace(token)) throw new AuthException("Сессия завершена.", 401);
        Set(Need(await Send<AuthResponse>(HttpMethod.Post, "/auth/v1/token?grant_type=refresh_token", new { refresh_token = token }, null, ct)));
    }
    private async Task LoadAccessSafe(CancellationToken ct)
    {
        access = null;
        if (string.IsNullOrWhiteSpace(session?.AccessToken)) return;
        try { access = await Send<AccountAccess>(HttpMethod.Post, "/rest/v1/rpc/get_my_beta_access", new { }, session.AccessToken, ct); }
        catch (AuthException ex) { App.Log(ex); }
    }
    private async Task<T> Send<T>(HttpMethod method, string path, object? body, string? token, CancellationToken ct, bool empty = false)
    {
        using var request = new HttpRequestMessage(method, SupabaseOptions.ProjectUrl + path);
        request.Headers.TryAddWithoutValidation("apikey", SupabaseOptions.PublishableKey);
        if (token is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null) request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        HttpResponseMessage response;
        try { response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct); }
        catch (HttpRequestException ex) { throw new AuthException("Нет соединения с сервером.", inner: ex); }
        using (response)
        {
            var content = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode) throw Error(response.StatusCode, content);
            if (empty && string.IsNullOrWhiteSpace(content)) return default!;
            return JsonSerializer.Deserialize<T>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        }
    }
    private static AuthException Error(HttpStatusCode status, string content)
    {
        string? message = null;
        try { using var document = JsonDocument.Parse(content); var root = document.RootElement; foreach (var name in new[] { "msg", "message", "error_description", "error", "hint" }) if (root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String) { message = value.GetString(); break; } } catch { }
        var lower = (message ?? "").ToLowerInvariant();
        if (lower.Contains("invalid login")) message = "Неверная почта или пароль.";
        else if (lower.Contains("already registered")) message = "Аккаунт уже существует.";
        else if (lower.Contains("rate")) message = "Слишком много попыток.";
        return new(message ?? "Ошибка авторизации.", (int)status);
    }
    private void Set(AuthSession value) { session = value; store.Save(value); }
    private void Clear() { session = null; access = null; store.Clear(); Changed(); }
    private void Changed() => SessionChanged?.Invoke(this, EventArgs.Empty);
    private static AuthSession Need(AuthResponse response) { var value = response.ToSession(); if (string.IsNullOrWhiteSpace(value.AccessToken) || string.IsNullOrWhiteSpace(value.RefreshToken)) throw new AuthException("Сессия не создана."); return value; }
    private static string NormalizeEmail(string value) => value.Trim().ToLowerInvariant();
}
