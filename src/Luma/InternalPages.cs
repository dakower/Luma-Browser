using Microsoft.Win32;
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace Luma;

public static class InternalPages
{
    private const string SettingsResourceName = "Luma.Assets.settings.html";
    private const string ImportResourceName = "Luma.Assets.import.html";
    private const string AssistantResourceName = "Luma.Assets.assistant.html";
    private const string SearchResourceName = "Luma.Assets.search.html";

    public static string Assistant()
    {
        using var stream = typeof(InternalPages).Assembly.GetManifestResourceStream(AssistantResourceName)
            ?? throw new InvalidOperationException("Встроенная панель ассистента Luma не найдена.");
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        var html = reader.ReadToEnd();
        try
        {
            var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/luma-assistant.png"));
            if (resource?.Stream is not null)
            {
                using var icon = new MemoryStream();
                resource.Stream.CopyTo(icon);
                html = html.Replace("__LUMA_AI_ICON__", Convert.ToBase64String(icon.ToArray()), StringComparison.Ordinal);
            }
        }
        catch { html = html.Replace("__LUMA_AI_ICON__", "", StringComparison.Ordinal); }
        return html;
    }

    internal static string LumaMarkDataUri()
    {
        try
        {
            var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/luma-mark.png"));
            if (resource?.Stream is null) return "";
            using var image = new MemoryStream();
            resource.Stream.CopyTo(image);
            return "data:image/png;base64," + Convert.ToBase64String(image.ToArray());
        }
        catch { return ""; }
    }

    public static string Search(string query, string mode, bool tester)
    {
        var theme = Registry.CurrentUser.OpenSubKey(@"Software\Luma")?.GetValue("Theme") as string ?? "dark";
        var accent = theme switch { "dark" => "#8B8F99", "graphite" => "#607D8B", "blue" => "#6687C8", "purple" => "#756BB7", "sand" => "#A77C64", "mint" => "#4F9E94", _ => "#8B8F99" };
        var colors = theme switch
        {
            "blue" => new[] { "#0B1119", "#111B28", "#172334", "#1D2C40", "#0B1420", "#2A3C53", "#405873", "#F2F6FB", "#CFD8E6", "#8090A5" },
            "purple" => new[] { "#111016", "#1A1821", "#211E29", "#292531", "#17141C", "#37323F", "#4A4355", "#F7F4FA", "#D7D2DC", "#8D8797" },
            "sand" => new[] { "#151210", "#201A17", "#28211D", "#302722", "#191411", "#40352F", "#594940", "#FAF5F1", "#E2D7D0", "#A08D82" },
            "mint" => new[] { "#0C1413", "#12201E", "#172724", "#1D302C", "#0C1715", "#29413C", "#3B5B54", "#F1F8F6", "#CEE0DD", "#7F9E99" },
            "graphite" => new[] { "#121416", "#1B1E21", "#22262A", "#2A2F34", "#171A1D", "#363C42", "#4B545C", "#F3F5F6", "#D2D8DC", "#8D969D" },
            _ => new[] { "#0C0D0F", "#141518", "#1A1B1F", "#222329", "#111216", "#2D2F36", "#41444D", "#F4F5F7", "#D6D8DE", "#858891" }
        };
        var state = JsonSerializer.Serialize(new { query = query ?? "", mode = string.IsNullOrWhiteSpace(mode) ? "all" : mode, tester }, new JsonSerializerOptions { Encoder = JavaScriptEncoder.Default });
        using var stream = typeof(InternalPages).Assembly.GetManifestResourceStream(SearchResourceName)
            ?? throw new InvalidOperationException("Встроенная страница Luma Search не найдена.");
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        return reader.ReadToEnd()
            .Replace("__LUMA_ACCENT__", accent, StringComparison.Ordinal)
            .Replace("__LUMA_BG__", colors[0], StringComparison.Ordinal).Replace("__LUMA_PANEL__", colors[1], StringComparison.Ordinal)
            .Replace("__LUMA_SURFACE__", colors[2], StringComparison.Ordinal).Replace("__LUMA_RAISED__", colors[3], StringComparison.Ordinal)
            .Replace("__LUMA_INSET__", colors[4], StringComparison.Ordinal).Replace("__LUMA_BORDER__", colors[5], StringComparison.Ordinal)
            .Replace("__LUMA_STRONG__", colors[6], StringComparison.Ordinal).Replace("__LUMA_TEXT__", colors[7], StringComparison.Ordinal)
            .Replace("__LUMA_SECONDARY__", colors[8], StringComparison.Ordinal).Replace("__LUMA_MUTED__", colors[9], StringComparison.Ordinal)
            .Replace("__LUMA_LOGO__", LumaMarkDataUri(), StringComparison.Ordinal)
            .Replace("__LUMA_SEARCH_STATE__", state, StringComparison.Ordinal);
    }

    public static string Settings(LumaState state)
    {
        var theme = Registry.CurrentUser.OpenSubKey(@"Software\Luma")?.GetValue("Theme") as string ?? "dark";
        var accent = theme switch
        {
            "blue" => "#6687C8",
            "purple" => "#756BB7",
            "sand" => "#A77C64",
            "mint" => "#4F9E94",
            "dark" => "#8B8F99",
            "graphite" => "#607D8B",
            _ => "#8B8F99"
        };

        var data = JsonSerializer.Serialize(
            new
            {
                state.StartupBehavior,
                state.RestoreSession,
                state.SidebarVisible,
                state.ConfirmManyTabs,
                state.SearchEngine,
                state.Language,
                state.BlockThirdPartyCookies,
                state.DoNotTrack,
                state.AppearanceMode,
                state.Transparency,
                state.BlurStrength,
                state.AnimationsEnabled,
                state.FloatingMusicEnabled,
                state.FloatingVideoEnabled,
                state.FloatingMediaAlwaysOnTop,
                state.FloatingVideoQuality,
                state.SleepAfterMinutes,
                state.PerformanceProfile,
                state.PageScale,
                state.DownloadPath,
                theme,
                History = state.History
                    .OrderByDescending(h => h.VisitedAt)
                    .Take(1000)
                    .Select(h => new { h.Id, h.Url, h.Title, VisitedAt = h.VisitedAt.ToString("o") }),
                Downloads = state.Downloads
                    .OrderByDescending(item => item.StartedAt)
                    .Take(300)
                    .Select(item => new { item.Id, item.FilePath, item.SourceUrl, item.MimeType, item.TotalBytes, item.BytesReceived, item.Status, StartedAt = item.StartedAt.ToString("o"), CompletedAt = item.CompletedAt?.ToString("o") })
            },
            new JsonSerializerOptions { Encoder = JavaScriptEncoder.Default });

        using var stream = typeof(InternalPages).Assembly.GetManifestResourceStream(SettingsResourceName)
            ?? throw new InvalidOperationException("Встроенная страница настроек Luma не найдена.");
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        return reader.ReadToEnd()
            .Replace("__LUMA_ACCENT__", accent, StringComparison.Ordinal)
            .Replace("__LUMA_VERSION__", MainWindow.AppVersion, StringComparison.Ordinal)
            .Replace("__LUMA_LOGO__", LumaMarkDataUri(), StringComparison.Ordinal)
            .Replace("__LUMA_STATE_JSON__", data, StringComparison.Ordinal);
    }

    public static string Import()
    {
        var theme = Registry.CurrentUser.OpenSubKey(@"Software\Luma")?.GetValue("Theme") as string ?? "dark";
        var accent = theme switch { "dark" => "#8B8F99", "graphite" => "#607D8B", "blue" => "#6687C8", "purple" => "#756BB7", "sand" => "#A77C64", "mint" => "#4F9E94", _ => "#8B8F99" };
        using var stream = typeof(InternalPages).Assembly.GetManifestResourceStream(ImportResourceName)
            ?? throw new InvalidOperationException("Встроенная страница импорта Luma не найдена.");
        using var reader = new StreamReader(stream, Encoding.UTF8, true);
        return reader.ReadToEnd().Replace("__LUMA_ACCENT__", accent, StringComparison.Ordinal);
    }
}
