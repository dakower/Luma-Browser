using Luma.Authentication;
using Luma.Core;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Luma.Updates;

public sealed class UpdateOptions
{
    [JsonPropertyName("endpointUrl")] public string EndpointUrl { get; set; } = "";
    [JsonPropertyName("channel")] public string Channel { get; set; } = "stable";
    [JsonPropertyName("publicKeyFile")] public string PublicKeyFile { get; set; } = "update-public.pem";

    public bool IsConfigured =>
        Uri.TryCreate(EndpointUrl, UriKind.Absolute, out var uri) &&
        uri.Scheme == Uri.UriSchemeHttps &&
        File.Exists(Path.Combine(AppContext.BaseDirectory, PublicKeyFile));

    public static UpdateOptions Load()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "updates.json");
            return File.Exists(path)
                ? JsonSerializer.Deserialize<UpdateOptions>(File.ReadAllText(path)) ?? new()
                : new();
        }
        catch (Exception ex)
        {
            App.Log(ex);
            return new();
        }
    }
}

public sealed class SignedUpdateManifest
{
    [JsonPropertyName("version")] public string Version { get; set; } = "";
    [JsonPropertyName("channel")] public string Channel { get; set; } = "stable";
    [JsonPropertyName("packageKey")] public string PackageKey { get; set; } = "";
    [JsonPropertyName("sha256")] public string Sha256 { get; set; } = "";
    [JsonPropertyName("packageSize")] public long PackageSize { get; set; }
    [JsonPropertyName("mandatory")] public bool Mandatory { get; set; }
    [JsonPropertyName("minimumVersion")] public string MinimumVersion { get; set; } = "";
    [JsonPropertyName("title")] public string Title { get; set; } = "Обновление Luma";
    [JsonPropertyName("notes")] public string Notes { get; set; } = "Исправления и улучшения стабильности.";
    [JsonPropertyName("signature")] public string Signature { get; set; } = "";
}

internal sealed class UpdateApiResponse
{
    [JsonPropertyName("noUpdate")] public bool NoUpdate { get; set; }
    [JsonPropertyName("manifest")] public SignedUpdateManifest? Manifest { get; set; }
    [JsonPropertyName("packageUrl")] public string PackageUrl { get; set; } = "";
}

public enum UpdateCheckStatus { Disabled, NotAuthenticated, Current, Downloaded, Failed }
public sealed record UpdateCheckResult(UpdateCheckStatus Status, string Message, string? PackagePath = null, SignedUpdateManifest? Manifest = null);
public interface IUpdateService
{
    bool IsConfigured { get; }
    event Action<SignedUpdateManifest>? UpdateAvailable;
    Task<UpdateCheckResult> CheckAsync(Version currentVersion, string? accessToken, CancellationToken cancellationToken = default);
}

public sealed class BackgroundUpdateService : IUpdateService
{
    private const long MaxPackageBytes = 1024L * 1024 * 1024;
    private readonly UpdateOptions _options;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(20) };
    private readonly SemaphoreSlim _gate = new(1, 1);

    public BackgroundUpdateService(UpdateOptions options)
    {
        _options = options;
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("Luma-Browser/2.1.0");
    }

    public bool IsConfigured => _options.IsConfigured;
    public event Action<SignedUpdateManifest>? UpdateAvailable;

    public async Task<UpdateCheckResult> CheckAsync(Version currentVersion, string? accessToken, CancellationToken cancellationToken = default)
    {
        if (!IsConfigured) return new(UpdateCheckStatus.Disabled, "Сервер обновлений пока не настроен.");
        if (!await _gate.WaitAsync(0, cancellationToken)) return new(UpdateCheckStatus.Current, "Проверка уже выполняется.");

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, _options.EndpointUrl);
            // Security updates must reach signed-out users too. Authentication is optional;
            // authenticity is enforced by the embedded ECDSA public key and package SHA-256.
            if (!string.IsNullOrWhiteSpace(accessToken))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Headers.TryAddWithoutValidation("apikey", SupabaseOptions.PublishableKey);
            request.Content = new StringContent(JsonSerializer.Serialize(new { channel = _options.Channel }), Encoding.UTF8, "application/json");

            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
            var envelope = await JsonSerializer.DeserializeAsync<UpdateApiResponse>(responseStream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }, cancellationToken)
                ?? throw new InvalidDataException("Пустой ответ сервера обновлений.");
            if (envelope.NoUpdate) return new(UpdateCheckStatus.Current, "Установлена актуальная версия.");

            var manifest = envelope.Manifest ?? throw new InvalidDataException("Сервер не вернул манифест.");
            if (!string.Equals(manifest.Channel, _options.Channel, StringComparison.OrdinalIgnoreCase)) return new(UpdateCheckStatus.Current, "Обновлений в этом канале нет.");
            if (!Version.TryParse(manifest.Version, out var target)) throw new InvalidDataException("Некорректная версия обновления.");
            if (target <= currentVersion) return new(UpdateCheckStatus.Current, "Установлена актуальная версия.");
            if (string.IsNullOrWhiteSpace(manifest.PackageKey) || !manifest.PackageKey.StartsWith("updates/", StringComparison.Ordinal) || manifest.PackageKey.Contains("..", StringComparison.Ordinal) || manifest.PackageKey.Contains('\\')) throw new InvalidDataException("Некорректный путь пакета.");
            if (manifest.PackageSize <= 0 || manifest.PackageSize > MaxPackageBytes) throw new InvalidDataException("Некорректный размер пакета.");
            if (manifest.Sha256.Length != 64 || manifest.Sha256.Any(c => !Uri.IsHexDigit(c))) throw new InvalidDataException("Некорректная SHA-256.");
            if (!Uri.TryCreate(envelope.PackageUrl, UriKind.Absolute, out var packageUri) || packageUri.Scheme != Uri.UriSchemeHttps) throw new InvalidDataException("Пакет должен использовать HTTPS.");

            var publicKey = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, _options.PublicKeyFile), cancellationToken);
            if (!SignedUpdatePolicy.Verify(publicKey, manifest.Signature, manifest.Version, manifest.Channel, manifest.PackageKey, manifest.Sha256, manifest.PackageSize, manifest.Mandatory, manifest.MinimumVersion, manifest.Title, manifest.Notes)) throw new InvalidDataException("Подпись обновления недействительна.");
            try { UpdateAvailable?.Invoke(manifest); }
            catch (Exception ex) { App.Log(ex); }

            var directory = Path.Combine(LumaState.DirectoryPath, "Updates");
            Directory.CreateDirectory(directory);
            var finalPath = Path.Combine(directory, $"Luma-{target}-x64.zip");
            var partialPath = finalPath + ".part";
            if (File.Exists(finalPath) && await HashMatchesAsync(finalPath, manifest.Sha256, cancellationToken)) return new(UpdateCheckStatus.Downloaded, "Обновление готово к установке.", finalPath, manifest);
            File.Delete(finalPath);
            File.Delete(partialPath);

            using var packageResponse = await _http.GetAsync(packageUri, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            packageResponse.EnsureSuccessStatusCode();
            if (packageResponse.Content.Headers.ContentLength is long length && (length > MaxPackageBytes || length != manifest.PackageSize)) throw new InvalidDataException("Размер пакета не совпадает.");

            await using (var input = await packageResponse.Content.ReadAsStreamAsync(cancellationToken))
            await using (var output = new FileStream(partialPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true))
            {
                var buffer = new byte[81920];
                long total = 0;
                int read;
                while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    total += read;
                    if (total > MaxPackageBytes || total > manifest.PackageSize) throw new InvalidDataException("Пакет слишком большой.");
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }
                if (total != manifest.PackageSize) throw new InvalidDataException("Пакет загружен не полностью.");
            }

            if (!await HashMatchesAsync(partialPath, manifest.Sha256, cancellationToken))
            {
                File.Delete(partialPath);
                throw new InvalidDataException("SHA-256 пакета не совпадает.");
            }
            File.Move(partialPath, finalPath, true);
            return new(UpdateCheckStatus.Downloaded, "Обновление загружено и проверено.", finalPath, manifest);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            App.Log(ex);
            return new(UpdateCheckStatus.Failed, "Не удалось проверить или загрузить обновление.");
        }
        finally { _gate.Release(); }
    }

    private static async Task<bool> HashMatchesAsync(string path, string expected, CancellationToken cancellationToken)
    {
        try
        {
            await using var package = File.OpenRead(path);
            var actual = Convert.ToHexString(await SHA256.HashDataAsync(package, cancellationToken));
            return CryptographicOperations.FixedTimeEquals(Convert.FromHexString(actual), Convert.FromHexString(expected));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException) { return false; }
    }
}

public static class UpdateInstaller
{
    public static void Start(string packagePath, SignedUpdateManifest manifest)
    {
        var script = Path.Combine(AppContext.BaseDirectory, "Updates", "apply-update.ps1");
        if (!File.Exists(script)) throw new FileNotFoundException("Помощник обновления не найден.", script);
        if (!File.Exists(packagePath)) throw new FileNotFoundException("Пакет обновления не найден.", packagePath);
        var applicationPath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName ?? throw new InvalidOperationException("Не найден Luma.exe.");
        var installDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var transaction = Path.Combine(LumaState.DirectoryPath, "Updates", "Transactions", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(transaction);

        var startInfo = new ProcessStartInfo("powershell.exe")
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        foreach (var argument in new[]
        {
            "-NoLogo", "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", script,
            "-ParentProcessId", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-PackagePath", Path.GetFullPath(packagePath), "-InstallDir", installDirectory,
            "-ApplicationPath", applicationPath, "-SuccessMarker", Path.Combine(transaction, "success.flag"),
            "-TargetVersion", manifest.Version
        }) startInfo.ArgumentList.Add(argument);

        if (!CanWrite(installDirectory))
        {
            startInfo.UseShellExecute = true;
            startInfo.CreateNoWindow = false;
            startInfo.Verb = "runas";
            startInfo.WindowStyle = ProcessWindowStyle.Normal;
        }

        _ = Process.Start(startInfo) ?? throw new InvalidOperationException("Не удалось запустить обновление.");
    }

    private static bool CanWrite(string directory)
    {
        try
        {
            var path = Path.Combine(directory, $".luma-update-{Guid.NewGuid():N}.tmp");
            File.WriteAllText(path, "test");
            File.Delete(path);
            return true;
        }
        catch { return false; }
    }
}
