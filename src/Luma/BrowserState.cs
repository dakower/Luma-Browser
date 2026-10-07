using Microsoft.Win32;
using System.IO;
using System.Text.Json;

namespace Luma;

public sealed class LumaState
{
    public int SchemaVersion { get; set; } = 15;
    public long TotalUsageSeconds { get; set; }
    /// <summary>Set once the onboarding screen has been completed. Never shown again after that.</summary>
    public bool HasSeenWelcome { get; set; }
    public bool SidebarVisible { get; set; } = true;
    public string StartupBehavior { get; set; } = "home";
    public string SearchEngine { get; set; } = "google";
    public string Language { get; set; } = "en";
    public bool RestoreSession { get; set; }
    public bool ConfirmManyTabs { get; set; } = true;
    public bool BlockThirdPartyCookies { get; set; }
    public bool DoNotTrack { get; set; }
    public string AppearanceMode { get; set; } = "system";
    public double Transparency { get; set; } = .72;
    public double BlurStrength { get; set; } = .68;
    public bool AnimationsEnabled { get; set; } = true;
    public bool FloatingMusicEnabled { get; set; } = true;
    public bool FloatingVideoEnabled { get; set; } = true;
    public bool FloatingMediaAlwaysOnTop { get; set; } = true;
    public int FloatingVideoQuality { get; set; } = 82;
    public string PerformanceProfile { get; set; } = "balanced";
    public int PageScale { get; set; } = 100;
    public string DownloadPath { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
    public int ActiveSpace { get; set; }
    public Dictionary<string, bool> AutoPictureInPicture { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, bool> AmbientLight { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> AlwaysTranslateDomains { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public string AssistantModel { get; set; } = "fast";
    public bool AssistantScreenshot { get; set; }
    public bool VoiceAssistantMuted { get; set; }
    public List<AssistantConversationState> AssistantConversations { get; set; } = [];
    public List<string> AssistantCustomCommands { get; set; } = [];
    public AssistantQuotaState AssistantQuota { get; set; } = new();
    public List<HistoryEntry> History { get; set; } = [];
    public List<DownloadEntry> Downloads { get; set; } = [];
    /// <summary>Remembered page zoom per site, so a site keeps the size you gave it.</summary>
    public Dictionary<string, double> ZoomByDomain { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Sites forced into a dark palette even when they ship no dark theme.</summary>
    public HashSet<string> ForceDarkDomains { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>Sites whose cookies and storage are wiped on the next start.</summary>
    public HashSet<string> ForgetOnExitDomains { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public List<HistoryEntry> RecentlyClosed { get; set; } = [];
    public int SleepAfterMinutes { get; set; } = 5;

    // Home Dashboard Customization
    public string HomeSearchShape { get; set; } = "pill";
    public string HomeSearchStyle { get; set; } = "solid";
    public string HomeFontFamily { get; set; } = "Segoe UI Variable Display, Segoe UI";
    public bool HomeShowLogo { get; set; } = true;
    public bool HomeShowClock { get; set; } = true;
    public bool HomeShowHeadline { get; set; } = true;
    public bool HomeShowSearch { get; set; } = true;
    public bool HomeShowQuickPills { get; set; } = true;
    public bool HomeShowMedia { get; set; } = true;
    public bool HomeShowRecent { get; set; } = true;
    public string? HomeWallpaperPath { get; set; }
    public double HomeWallpaperDim { get; set; } = 0.35;
    public string? SupportGuestId { get; set; }
    public string? SupportThreadId { get; set; }

    public List<SpaceState> Spaces { get; set; } = [new SpaceState()];
    public static string DirectoryPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Luma");
    public static string FilePath => Path.Combine(DirectoryPath, "state.json");
    /// <summary>Language chosen in the installer (HKCU\Software\Luma\Language). English when the user never changed it.</summary>
    private static string InstalledLanguage()
    {
        try
        {
            var chosen = Registry.CurrentUser.OpenSubKey(@"Software\Luma")?.GetValue("Language") as string;
            return chosen is "ru" or "uk" ? chosen : "en";
        }
        catch { return "en"; }
    }

    public static LumaState Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return new() { Language = InstalledLanguage() };
            var state = JsonSerializer.Deserialize<LumaState>(File.ReadAllText(FilePath)) ?? new();
            state.AutoPictureInPicture = new(state.AutoPictureInPicture ?? [], StringComparer.OrdinalIgnoreCase);
            state.AlwaysTranslateDomains = new(state.AlwaysTranslateDomains ?? [], StringComparer.OrdinalIgnoreCase);
            state.History ??= [];
            state.Downloads ??= [];
            state.AssistantConversations ??= [];
            state.AssistantCustomCommands ??= [];
            state.AssistantQuota ??= new();
            state.ZoomByDomain = new(state.ZoomByDomain ?? [], StringComparer.OrdinalIgnoreCase);
            state.ForceDarkDomains = new(state.ForceDarkDomains ?? [], StringComparer.OrdinalIgnoreCase);
            state.ForgetOnExitDomains = new(state.ForgetOnExitDomains ?? [], StringComparer.OrdinalIgnoreCase);
            state.RecentlyClosed ??= [];
            state.FloatingVideoQuality = Math.Clamp(state.FloatingVideoQuality, 45, 95);
            if (state.SleepAfterMinutes is < 0 or > 240 || state.SleepAfterMinutes is > 0 and < 2) state.SleepAfterMinutes = 5;
            // Older builds stored a raw provider model id here.
            if (state.AssistantModel is not "fast" and not "pro") state.AssistantModel = "fast";
            if (string.IsNullOrWhiteSpace(state.HomeSearchShape)) state.HomeSearchShape = "pill";
            if (string.IsNullOrWhiteSpace(state.HomeSearchStyle)) state.HomeSearchStyle = "solid";
            if (string.IsNullOrWhiteSpace(state.HomeFontFamily)) state.HomeFontFamily = "Segoe UI Variable Display, Segoe UI";
            state.Spaces ??= [];
            if (state.SchemaVersion < 3) state.SchemaVersion = 3;
            if (state.SchemaVersion < 4) state.SchemaVersion = 4;
            if (state.SchemaVersion < 5) state.SchemaVersion = 5;
            if (state.SchemaVersion < 6) state.SchemaVersion = 6;
            if (state.SchemaVersion < 7)
            {
                state.StartupBehavior = state.RestoreSession ? "restore" : state.StartupBehavior == "clean" ? "clean" : "home";
                state.SchemaVersion = 7;
            }
            if (state.SchemaVersion < 8)
            {
                // Existing installs already "know" Luma, so an update must never replay onboarding.
                state.HasSeenWelcome = true;
                state.SchemaVersion = 8;
            }
            if (state.SchemaVersion < 9) state.SchemaVersion = 9;
            state.Spaces ??= [];
            if (state.SchemaVersion < 10)
            {
                // Preserve every old session entry and assign stable identifiers instead of
                // rebuilding or clearing it. Future versions can migrate by id without data loss.
                foreach (var space in state.Spaces)
                {
                    space.SessionTabs ??= [];
                    space.Folders ??= [];
                    space.LooseItems ??= [];
                    foreach (var tab in space.SessionTabs) if (string.IsNullOrWhiteSpace(tab.Id)) tab.Id = Guid.NewGuid().ToString("N");
                    if (string.IsNullOrWhiteSpace(space.ActiveTabId)) space.ActiveTabId = space.SessionTabs.FirstOrDefault()?.Id ?? "";
                }
                state.SchemaVersion = 10;
            }
            if (state.SchemaVersion < 11) state.SchemaVersion = 11;
            if (state.SchemaVersion < 12) state.SchemaVersion = 12;
            if (state.SchemaVersion < 13) { state.SearchEngine = "luma"; state.SchemaVersion = 13; }
            if (state.SchemaVersion < 14) state.SchemaVersion = 14;
            if (state.SchemaVersion < 15) { if (state.SearchEngine == "luma") state.SearchEngine = "google"; state.SchemaVersion = 15; }
            if (state.SchemaVersion < 16) { if (state.SleepAfterMinutes == 30) state.SleepAfterMinutes = 5; state.SchemaVersion = 16; }
            if (state.SearchEngine is not ("luma" or "google" or "bing" or "duckduckgo")) state.SearchEngine = "google";
            state.AssistantCustomCommands = state.AssistantCustomCommands
                .Where(command => !string.IsNullOrWhiteSpace(command))
                .Select(command => command.Trim()[..Math.Min(command.Trim().Length, 240)])
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToList();
            state.Downloads = state.Downloads
                .Where(item => !string.IsNullOrWhiteSpace(item.Id) && !string.IsNullOrWhiteSpace(item.FilePath))
                .OrderByDescending(item => item.StartedAt).Take(300).ToList();
            state.AssistantConversations = state.AssistantConversations
                .Where(item => !string.IsNullOrWhiteSpace(item.Id) && !string.IsNullOrWhiteSpace(item.Title))
                .OrderByDescending(item => item.UpdatedAt).Take(20).ToList();
            foreach (var space in state.Spaces) { space.Folders ??= []; space.LooseItems ??= []; space.SessionTabs ??= []; }
            if (state.Spaces.Count == 0) state.Spaces.Add(new SpaceState());
            state.Save(); return state;
        }
        catch
        {
            // A damaged state file must not silently wipe the profile: keep a copy for recovery.
            try { if (File.Exists(FilePath)) File.Copy(FilePath, Path.Combine(DirectoryPath, $"state.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}.json"), true); } catch { }
            return new() { Language = InstalledLanguage() };
        }
    }
    private static readonly object SaveLock = new();
    public void Save()
    {
        // Saves come from timers, download callbacks and the UI at once; serialise them so the shared
        // temp file is never written by two threads (that used to throw IOException and lose the save).
        lock (SaveLock)
        {
            try
            {
                SchemaVersion = 15;
                Directory.CreateDirectory(DirectoryPath);
                var temp = FilePath + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
                File.Move(temp, FilePath, true);
            }
            catch (Exception ex)
            {
                // Prevent crash if state.json is temporarily locked by antivirus, cloud sync, or external processes
                System.Diagnostics.Debug.WriteLine($"[LumaState.Save Error]: {ex.Message}");
            }
        }
    }
}

public sealed class SpaceState
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "Личное";
    public string Accent { get; set; } = "#7C5CE4";
    public string ActiveTabId { get; set; } = "";
    public List<string> OpenUrls { get; set; } = [];
    public List<TabSessionState> SessionTabs { get; set; } = [];
    public List<FolderState> Folders { get; set; } = [];
    public List<SavedSite> LooseItems { get; set; } = [];
}
public sealed class TabSessionState { public string Id { get; set; } = Guid.NewGuid().ToString("N"); public string Url { get; set; } = ""; public string Title { get; set; } = ""; public string? FolderId { get; set; } public bool IsPinned { get; set; } public bool IsInternal { get; set; } public string InternalPageKind { get; set; } = ""; public bool IsSplit { get; set; } public string SecondaryUrl { get; set; } = ""; public string SecondaryTitle { get; set; } = ""; public string ActivePane { get; set; } = "primary"; public double SplitRatio { get; set; } = .5; }
public sealed class FolderState { public string Id { get; set; } = Guid.NewGuid().ToString("N"); public string Name { get; set; } = "Новая папка"; public bool Expanded { get; set; } public List<SavedSite> Items { get; set; } = []; }
public sealed class SavedSite {
    /// <summary>Runtime id of the live tab this pinned item owns (not persisted across restarts).</summary>
    public string? TabId { get; set; } public string Id { get; set; } = Guid.NewGuid().ToString("N"); public string Title { get; set; } = "Сайт"; public string Url { get; set; } = ""; }

public sealed class HistoryEntry { public string Id { get; set; } = Guid.NewGuid().ToString("N"); public string Url { get; set; } = ""; public string Title { get; set; } = ""; public DateTime VisitedAt { get; set; } = DateTime.UtcNow; public string Domain => BrowserTab.DomainOf(Url); public string FaviconUrl => BrowserTab.FaviconOf(Url); }

public sealed class DownloadEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string FilePath { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public string MimeType { get; set; } = "";
    public long TotalBytes { get; set; }
    public long BytesReceived { get; set; }
    public string Status { get; set; } = "downloading";
    public DateTime StartedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
}

public sealed class AssistantConversationState
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "Диалог";
    public int Turns { get; set; }
    public string Html { get; set; } = "";
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class AssistantQuotaState
{
    public int Limit { get; set; } = 15;
    public int Used { get; set; }
    public int Remaining { get; set; } = 15;
    public bool Unlimited { get; set; }
    public int AgentLimit { get; set; } = 3;
    public int AgentUsed { get; set; }
    public string AgentQuotaDate { get; set; } = "";
    public DateTime UpdatedAt { get; set; }
}
