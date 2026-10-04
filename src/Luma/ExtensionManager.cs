using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using Microsoft.Web.WebView2.Core;

namespace Luma;

public class ExtensionMetadata
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Version { get; set; } = "";
    public string Description { get; set; } = "";
    public string Icon { get; set; } = "";
    public bool IsEnabled { get; set; } = true;
    public string Path { get; set; } = "";
    public DateTime InstalledAt { get; set; } = DateTime.UtcNow;
}

public static class ExtensionManager
{
    private static readonly string ExtensionsFolder = System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Luma", "Extensions");
    private static readonly string MetadataFile = System.IO.Path.Combine(ExtensionsFolder, "extensions.json");

    private static readonly List<ExtensionMetadata> _extensions = new();
    private static readonly Dictionary<string, CoreWebView2BrowserExtension> _activeExtensions = new();

    public static IReadOnlyList<ExtensionMetadata> Extensions => _extensions.AsReadOnly();

    static ExtensionManager()
    {
        try
        {
            Directory.CreateDirectory(ExtensionsFolder);
            LoadMetadata();
        }
        catch { }
    }

    private static void LoadMetadata()
    {
        try
        {
            if (File.Exists(MetadataFile))
            {
                var json = File.ReadAllText(MetadataFile);
                var list = JsonSerializer.Deserialize<List<ExtensionMetadata>>(json);
                if (list != null)
                {
                    _extensions.Clear();
                    _extensions.AddRange(list);
                }
            }
        }
        catch { }
    }

    public static void SaveMetadata()
    {
        try
        {
            Directory.CreateDirectory(ExtensionsFolder);
            var json = JsonSerializer.Serialize(_extensions, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(MetadataFile, json);
        }
        catch { }
    }

    private static readonly SemaphoreSlim InitGate = new(1, 1);
    private static string? _initializedProfileKey;

    /// <summary>The profile extensions were registered with (used by the toolbar menu).</summary>
    public static CoreWebView2Profile? Profile { get; private set; }

    /// <summary>Raised on the calling thread whenever the installed set or a state changes.</summary>
    public static event Action? Changed;
    private static void RaiseChanged() { try { Changed?.Invoke(); } catch { } }

    public sealed record ManifestInfo(string? IconPath, string? PopupPage, string? OptionsPage, string? TargetUrl);

    /// <summary>Reads icon, action popup, options page and target landing URL from the extension's manifest.</summary>
    public static ManifestInfo ReadManifestInfo(ExtensionMetadata meta)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(meta.Path) || !Directory.Exists(meta.Path))
                return new(null, null, null, null);

            var manifestPath = System.IO.Path.Combine(meta.Path, "manifest.json");
            if (!File.Exists(manifestPath)) return new(null, null, null, null);
            using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath), new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            var root = doc.RootElement;
            string? icon = null; var best = -1;
            void PickIcons(JsonElement el)
            {
                if (el.ValueKind != JsonValueKind.Object) return;
                foreach (var p in el.EnumerateObject())
                    if (int.TryParse(p.Name, out var size) && size > best && p.Value.ValueKind == JsonValueKind.String) { best = size; icon = p.Value.GetString(); }
            }
            if (root.TryGetProperty("icons", out var icons)) PickIcons(icons);
            string? popup = null;
            foreach (var key in new[] { "action", "browser_action", "page_action" })
                if (root.TryGetProperty(key, out var action) && action.ValueKind == JsonValueKind.Object)
                {
                    if (icon is null && action.TryGetProperty("default_icon", out var di))
                    {
                        if (di.ValueKind == JsonValueKind.String) icon = di.GetString(); else PickIcons(di);
                    }
                    if (popup is null && action.TryGetProperty("default_popup", out var dp) && dp.ValueKind == JsonValueKind.String) popup = dp.GetString();
                }
            string? options = null;
            if (root.TryGetProperty("options_ui", out var ui) && ui.TryGetProperty("page", out var page) && page.ValueKind == JsonValueKind.String) options = page.GetString();
            else if (root.TryGetProperty("options_page", out var op) && op.ValueKind == JsonValueKind.String) options = op.GetString();

            // Check if options / popup files exist in folder as fallback
            if (string.IsNullOrWhiteSpace(options))
            {
                if (File.Exists(System.IO.Path.Combine(meta.Path, "options.html"))) options = "options.html";
                else if (File.Exists(System.IO.Path.Combine(meta.Path, "settings.html"))) options = "settings.html";
            }
            if (string.IsNullOrWhiteSpace(popup))
            {
                if (File.Exists(System.IO.Path.Combine(meta.Path, "popup.html"))) popup = "popup.html";
            }

            string? iconPath = null;
            if (!string.IsNullOrWhiteSpace(icon))
            {
                var full = System.IO.Path.GetFullPath(System.IO.Path.Combine(meta.Path, icon.TrimStart('/', '\\')));
                if (File.Exists(full)) iconPath = full;
            }

            // Determine TargetUrl (where clicking the extension row navigates to)
            string? targetUrl = null;
            if (!string.IsNullOrWhiteSpace(options))
            {
                targetUrl = $"chrome-extension://{meta.Id}/{options.TrimStart('/')}";
            }
            else if (!string.IsNullOrWhiteSpace(popup))
            {
                targetUrl = $"chrome-extension://{meta.Id}/{popup.TrimStart('/')}";
            }
            else
            {
                // Inspect content scripts for domain landings (e.g. BTRoblox -> roblox.com)
                if (root.TryGetProperty("content_scripts", out var cs) && cs.ValueKind == JsonValueKind.Array)
                {
                    foreach (var c in cs.EnumerateArray())
                    {
                        if (c.TryGetProperty("matches", out var matches) && matches.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var m in matches.EnumerateArray())
                            {
                                var pattern = m.GetString() ?? "";
                                if (pattern.Contains("roblox.com", StringComparison.OrdinalIgnoreCase))
                                {
                                    targetUrl = "https://www.roblox.com/home?btr_settings_open=true";
                                    break;
                                }
                                if (pattern.Contains("://"))
                                {
                                    var cleanMatch = pattern.Replace("*://", "https://").Replace("*.", "www.").Split('*')[0];
                                    if (Uri.TryCreate(cleanMatch, UriKind.Absolute, out var uri))
                                    {
                                        targetUrl = uri.AbsoluteUri;
                                        break;
                                    }
                                }
                            }
                        }
                        if (targetUrl != null) break;
                    }
                }
            }

            return new(iconPath, string.IsNullOrWhiteSpace(popup) ? null : popup.TrimStart('/'), string.IsNullOrWhiteSpace(options) ? null : options.TrimStart('/'), targetUrl);
        }
        catch { return new(null, null, null, null); }
    }

    public static async Task InitializeExtensionsAsync(CoreWebView2Profile profile)
    {
        if (profile == null) return;
        await InitGate.WaitAsync();
        try
        {
            var key = SafeProfileKey(profile);
            Profile = profile;
            if (key == _initializedProfileKey) return;
            _initializedProfileKey = key;
            await InitializeCoreAsync(profile);
        }
        finally { InitGate.Release(); RaiseChanged(); }
    }

    private static string SafeProfileKey(CoreWebView2Profile profile)
    {
        try { return profile.ProfilePath ?? ""; } catch { return ""; }
    }

    private static async Task InitializeCoreAsync(CoreWebView2Profile profile)
    {
        try
        {
            var existing = await profile.GetBrowserExtensionsAsync();
            foreach (var ext in existing)
            {
                _activeExtensions[ext.Id] = ext;
            }

            foreach (var meta in _extensions.ToList())
            {
                if (!Directory.Exists(meta.Path))
                {
                    _extensions.Remove(meta);
                    continue;
                }

                // Clean up _metadata directory if present (from CRX extraction)
                var metadataDir = System.IO.Path.Combine(meta.Path, "_metadata");
                if (Directory.Exists(metadataDir))
                {
                    try { Directory.Delete(metadataDir, true); } catch { }
                }

                if (!_activeExtensions.ContainsKey(meta.Id))
                {
                    try
                    {
                        var ext = await profile.AddBrowserExtensionAsync(meta.Path);
                        if (ext != null)
                        {
                            _activeExtensions[ext.Id] = ext;
                            if (!string.Equals(ext.Id, meta.Id, StringComparison.OrdinalIgnoreCase))
                            {
                                meta.Id = ext.Id;
                            }
                            if (!meta.IsEnabled)
                            {
                                await ext.EnableAsync(false);
                            }
                        }
                    }
                    catch (Exception ex) { App.Log(ex); }
                }
                else
                {
                    var ext = _activeExtensions[meta.Id];
                    if (ext.IsEnabled != meta.IsEnabled)
                    {
                        await ext.EnableAsync(meta.IsEnabled);
                    }
                }
            }

            // Sync any existing profile extensions back into metadata
            foreach (var ext in existing)
            {
                if (!_extensions.Any(x => string.Equals(x.Id, ext.Id, StringComparison.OrdinalIgnoreCase)))
                {
                    _extensions.Add(new ExtensionMetadata
                    {
                        Id = ext.Id,
                        Name = ext.Name ?? "Расширение",
                        IsEnabled = ext.IsEnabled,
                        Path = "",
                        InstalledAt = DateTime.UtcNow
                    });
                }
            }

            SaveMetadata();
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }
    }

    public static int FindZipOffset(byte[] crxBytes)
    {
        if (crxBytes == null || crxBytes.Length < 4) return -1;
        if (crxBytes[0] == 'P' && crxBytes[1] == 'K' && crxBytes[2] == 3 && crxBytes[3] == 4) return 0;
        for (int i = 0; i < crxBytes.Length - 4; i++)
        {
            if (crxBytes[i] == 'P' && crxBytes[i + 1] == 'K' && crxBytes[i + 2] == 3 && crxBytes[i + 3] == 4)
            {
                return i;
            }
        }
        return -1;
    }

    public static List<string> ParsePermissions(string manifestJson)
    {
        var result = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(manifestJson, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            var root = doc.RootElement;
            var perms = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddArray(string propName)
            {
                if (root.TryGetProperty(propName, out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in arr.EnumerateArray())
                    {
                        var s = item.GetString();
                        if (!string.IsNullOrWhiteSpace(s)) perms.Add(s);
                    }
                }
            }

            AddArray("permissions");
            AddArray("host_permissions");
            AddArray("optional_permissions");

            bool hasAllUrls = perms.Any(p => p.Contains("<all_urls>") || p.Contains("*://*/*") || p.Contains("http://*/*") || p.Contains("https://*/*"));
            if (hasAllUrls)
            {
                result.Add("Чтение и изменение всех данных на всех веб-сайтах");
            }
            else
            {
                var hosts = perms.Where(p => p.Contains("://")).Take(3).ToList();
                if (hosts.Count > 0)
                    result.Add($"Чтение и изменение данных на: {string.Join(", ", hosts)}");
            }

            if (perms.Contains("tabs")) result.Add("Доступ к открытым вкладкам браузера");
            if (perms.Contains("storage")) result.Add("Хранение данных и настроек на устройстве");
            if (perms.Contains("cookies")) result.Add("Чтение и изменение файлов cookie");
            if (perms.Contains("webNavigation")) result.Add("Отслеживание переходов по страницам");
            if (perms.Contains("declarativeNetRequest") || perms.Contains("webRequest")) result.Add("Блокировка и фильтрация сетевого содержимого");
            if (perms.Contains("clipboardRead") || perms.Contains("clipboardWrite")) result.Add("Доступ к буферу обмена");
            if (perms.Contains("notifications")) result.Add("Отображение всплывающих уведомлений");
            if (perms.Contains("geolocation")) result.Add("Доступ к вашему местоположению");
        }
        catch { }

        if (result.Count == 0)
        {
            result.Add("Чтение и изменение данных на посещаемых веб-сайтах");
        }

        return result;
    }

    public sealed record CrxManifestInfo(string Name, string Version, List<string> Permissions, byte[]? IconBytes);

    public static CrxManifestInfo ReadManifestFromCrxBytes(byte[] crxBytes)
    {
        try
        {
            var zipOffset = FindZipOffset(crxBytes);
            if (zipOffset < 0) return new("Расширение", "1.0", new List<string> { "Чтение и изменение данных на посещаемых сайтах" }, null);

            using var ms = new MemoryStream(crxBytes, zipOffset, crxBytes.Length - zipOffset);
            using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
            var manifestEntry = archive.GetEntry("manifest.json");
            if (manifestEntry == null) return new("Расширение", "1.0", new List<string> { "Чтение и изменение данных на посещаемых сайтах" }, null);

            using var reader = new StreamReader(manifestEntry.Open(), System.Text.Encoding.UTF8);
            var json = reader.ReadToEnd();
            var perms = ParsePermissions(json);

            string name = "Расширение";
            string version = "1.0";
            string? iconPath = null;

            using (var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }))
            {
                var root = doc.RootElement;
                if (root.TryGetProperty("name", out var nEl)) name = nEl.GetString() ?? name;
                if (root.TryGetProperty("version", out var vEl)) version = vEl.GetString() ?? version;
                if (root.TryGetProperty("icons", out var iconsEl) && iconsEl.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in iconsEl.EnumerateObject().Reverse())
                    {
                        iconPath = prop.Value.GetString();
                        if (!string.IsNullOrWhiteSpace(iconPath)) break;
                    }
                }
            }

            byte[]? iconBytes = null;
            if (!string.IsNullOrWhiteSpace(iconPath))
            {
                var iconEntry = archive.GetEntry(iconPath.TrimStart('/', '\\'));
                if (iconEntry != null)
                {
                    using var iconStream = iconEntry.Open();
                    using var iconMs = new MemoryStream();
                    iconStream.CopyTo(iconMs);
                    iconBytes = iconMs.ToArray();
                }
            }

            return new(name, version, perms, iconBytes);
        }
        catch
        {
            return new("Расширение", "1.0", new List<string> { "Чтение и изменение данных на посещаемых сайтах" }, null);
        }
    }

    public static async Task<byte[]?> DownloadCrxBytesAsync(string extensionId, string? preferredSource = null)
    {
        var edgeUrl = $"https://edge.microsoft.com/extensionwebstorebase/v1/crx?x=id%3D{extensionId}%26installsource%3Dondemand&response=redirect";
        var chromeUrl = $"https://clients2.google.com/service/update2/crx?response=redirect&os=win&arch=x86-64&os_arch=x86-64&nacl_arch=x86-64&prod=chromecrx&prodchannel=&prodversion=130.0.6723.117&lang=en-US&acceptformat=crx3,crx2&x=id%3D{extensionId}%26installsource%3Dondemand%26uc";

        var isEdge = string.Equals(preferredSource, "edge", StringComparison.OrdinalIgnoreCase);
        var urls = isEdge ? new[] { edgeUrl, chromeUrl } : new[] { chromeUrl, edgeUrl };

        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36 Edg/130.0.0.0");
        http.Timeout = TimeSpan.FromSeconds(35);

        foreach (var url in urls)
        {
            try
            {
                var bytes = await http.GetByteArrayAsync(url);
                // Valid CRX / ZIP has length and signatures ('Cr24' or 'PK\x03\x04')
                if (bytes.Length > 1000 && (bytes[0] == 'C' || bytes[0] == 'P' || FindZipOffset(bytes) >= 0))
                {
                    return bytes;
                }
            }
            catch { }
        }

        return null;
    }

    public static void ExtractZipClean(ZipArchive archive, string destinationDirectory)
    {
        Directory.CreateDirectory(destinationDirectory);
        var destFullPath = Path.GetFullPath(destinationDirectory);
        foreach (var entry in archive.Entries)
        {
            var clean = entry.FullName.Replace('\\', '/').TrimStart('/');
            if (string.IsNullOrWhiteSpace(clean)) continue;
            // Crucial: Chromium strictly forbids any _metadata directory in unpacked extensions!
            if (clean.StartsWith("_metadata/", StringComparison.OrdinalIgnoreCase) || clean.Equals("_metadata", StringComparison.OrdinalIgnoreCase))
                continue;

            var targetPath = Path.GetFullPath(Path.Combine(destFullPath, clean.Replace('/', Path.DirectorySeparatorChar)));
            if (!targetPath.StartsWith(destFullPath, StringComparison.OrdinalIgnoreCase))
                continue; // Zip Slip protection

            if (clean.EndsWith('/'))
            {
                Directory.CreateDirectory(targetPath);
                continue;
            }

            var dir = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            entry.ExtractToFile(targetPath, true);
        }
    }

    public static async Task<ExtensionMetadata?> InstallFromStoreAsync(CoreWebView2Profile? profile, string extensionId, string? preferredSource = null)
    {
        if (string.IsNullOrWhiteSpace(extensionId)) return null;

        var targetFolder = System.IO.Path.Combine(ExtensionsFolder, extensionId);

        try
        {
            var crxBytes = await DownloadCrxBytesAsync(extensionId, preferredSource);
            if (crxBytes == null || crxBytes.Length < 100) return null;

            int zipOffset = FindZipOffset(crxBytes);
            if (zipOffset < 0) return null;

            if (Directory.Exists(targetFolder))
            {
                try { Directory.Delete(targetFolder, true); } catch { }
            }
            Directory.CreateDirectory(targetFolder);

            using (var ms = new MemoryStream(crxBytes, zipOffset, crxBytes.Length - zipOffset))
            using (var archive = new ZipArchive(ms, ZipArchiveMode.Read))
            {
                ExtractZipClean(archive, targetFolder);
            }

            return await RegisterExtensionFromFolderAsync(profile, targetFolder, extensionId);
        }
        catch (Exception ex)
        {
            App.Log(ex);
            return null;
        }
    }

    public static async Task<ExtensionMetadata?> InstallFromWebStoreAsync(CoreWebView2Profile? profile, string extensionId)
    {
        return await InstallFromStoreAsync(profile, extensionId, "chrome");
    }

    public static async Task<ExtensionMetadata?> InstallFromLocalFileAsync(CoreWebView2Profile? profile, string filePath)
    {
        if (!File.Exists(filePath)) return null;

        try
        {
            var crxBytes = await File.ReadAllBytesAsync(filePath);
            int zipOffset = FindZipOffset(crxBytes);

            string folderName = Guid.NewGuid().ToString("N")[..16];
            if (zipOffset >= 0)
            {
                try
                {
                    using var msPeek = new MemoryStream(crxBytes, zipOffset, crxBytes.Length - zipOffset);
                    using var archivePeek = new ZipArchive(msPeek, ZipArchiveMode.Read);
                    var manifestEntry = archivePeek.GetEntry("manifest.json");
                    if (manifestEntry != null)
                    {
                        using var reader = new StreamReader(manifestEntry.Open(), System.Text.Encoding.UTF8);
                        var manifestJson = reader.ReadToEnd();
                        using var doc = JsonDocument.Parse(manifestJson, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
                        if (doc.RootElement.TryGetProperty("name", out var nEl))
                        {
                            var cleanName = string.Concat(nEl.GetString()?.Split(Path.GetInvalidFileNameChars()) ?? Array.Empty<string>()).Trim();
                            if (!string.IsNullOrWhiteSpace(cleanName)) folderName = cleanName;
                        }
                    }
                }
                catch { }
            }

            var targetFolder = System.IO.Path.Combine(ExtensionsFolder, folderName);
            if (Directory.Exists(targetFolder))
            {
                try { Directory.Delete(targetFolder, true); } catch { }
            }
            Directory.CreateDirectory(targetFolder);

            if (zipOffset >= 0)
            {
                using var ms = new MemoryStream(crxBytes, zipOffset, crxBytes.Length - zipOffset);
                using var archive = new ZipArchive(ms, ZipArchiveMode.Read);
                ExtractZipClean(archive, targetFolder);
            }
            else
            {
                using var archive = ZipFile.OpenRead(filePath);
                ExtractZipClean(archive, targetFolder);
            }

            return await RegisterExtensionFromFolderAsync(profile, targetFolder);
        }
        catch (Exception ex)
        {
            App.Log(ex);
            return null;
        }
    }

    public static async Task<ExtensionMetadata?> RegisterExtensionFromFolderAsync(CoreWebView2Profile? profile, string folderPath, string? customId = null)
    {
        if (!Directory.Exists(folderPath)) return null;

        // Clean up _metadata directory if present (from CRX extraction)
        var metadataDir = System.IO.Path.Combine(folderPath, "_metadata");
        if (Directory.Exists(metadataDir))
        {
            try { Directory.Delete(metadataDir, true); } catch { }
        }

        var manifestPath = System.IO.Path.Combine(folderPath, "manifest.json");
        if (!File.Exists(manifestPath)) return null;

        string name = System.IO.Path.GetFileName(folderPath);
        string version = "1.0";
        string description = "";
        string icon = "";

        try
        {
            var manifestJson = File.ReadAllText(manifestPath);
            using var doc = JsonDocument.Parse(manifestJson, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            var root = doc.RootElement;

            if (root.TryGetProperty("name", out var nEl)) name = nEl.GetString() ?? name;
            if (root.TryGetProperty("version", out var vEl)) version = vEl.GetString() ?? version;
            if (root.TryGetProperty("description", out var dEl)) description = dEl.GetString() ?? description;

            if (root.TryGetProperty("icons", out var iconsEl) && iconsEl.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in iconsEl.EnumerateObject().Reverse())
                {
                    icon = prop.Value.GetString() ?? "";
                    if (!string.IsNullOrWhiteSpace(icon)) break;
                }
            }
        }
        catch { }

        try
        {
            profile ??= Profile;
            if (profile == null) return null;

            var extension = await profile.AddBrowserExtensionAsync(folderPath);
            if (extension != null)
            {
                var meta = new ExtensionMetadata
                {
                    Id = extension.Id,
                    Name = extension.Name ?? name,
                    Version = version,
                    Description = description,
                    Icon = icon,
                    IsEnabled = extension.IsEnabled,
                    Path = folderPath,
                    InstalledAt = DateTime.UtcNow
                };

                _activeExtensions[extension.Id] = extension;
                _extensions.RemoveAll(x => x.Id == extension.Id || x.Path.Equals(folderPath, StringComparison.OrdinalIgnoreCase));
                _extensions.Add(meta);
                SaveMetadata();
                RaiseChanged();
                return meta;
            }
        }
        catch (Exception ex)
        {
            App.Log(ex);
        }

        return null;
    }

    public static async Task<bool> ToggleExtensionAsync(CoreWebView2Profile? profile, string extensionId, bool enable)
    {
        var meta = _extensions.FirstOrDefault(x => string.Equals(x.Id, extensionId, StringComparison.OrdinalIgnoreCase));
        if (meta != null)
        {
            meta.IsEnabled = enable;
            SaveMetadata();
            RaiseChanged();
        }

        profile ??= Profile;
        if (profile != null)
        {
            try
            {
                var installed = await profile.GetBrowserExtensionsAsync();
                var target = installed.FirstOrDefault(x => string.Equals(x.Id, extensionId, StringComparison.OrdinalIgnoreCase));
                if (target != null)
                {
                    await target.EnableAsync(enable);
                    _activeExtensions[target.Id] = target;
                    return true;
                }
            }
            catch (Exception ex) { App.Log(ex); }
        }

        if (_activeExtensions.TryGetValue(extensionId, out var ext))
        {
            try
            {
                await ext.EnableAsync(enable);
                return true;
            }
            catch (Exception ex) { App.Log(ex); }
        }

        return false;
    }

    public static async Task<bool> RemoveExtensionAsync(CoreWebView2Profile? profile, string extensionId)
    {
        var meta = _extensions.FirstOrDefault(x => string.Equals(x.Id, extensionId, StringComparison.OrdinalIgnoreCase));
        if (meta != null)
        {
            _extensions.Remove(meta);
            SaveMetadata();
            RaiseChanged();
        }

        profile ??= Profile;
        if (profile != null)
        {
            try
            {
                var installed = await profile.GetBrowserExtensionsAsync();
                var target = installed.FirstOrDefault(x => string.Equals(x.Id, extensionId, StringComparison.OrdinalIgnoreCase));
                if (target != null)
                {
                    await target.RemoveAsync();
                }
            }
            catch (Exception ex) { App.Log(ex); }
        }

        if (_activeExtensions.TryGetValue(extensionId, out var ext))
        {
            try { await ext.RemoveAsync(); } catch { }
            _activeExtensions.Remove(extensionId);
        }

        if (meta != null && !string.IsNullOrWhiteSpace(meta.Path) && Directory.Exists(meta.Path))
        {
            try { Directory.Delete(meta.Path, true); } catch { }
        }

        return true;
    }
}
