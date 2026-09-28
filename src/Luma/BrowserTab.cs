using Microsoft.Web.WebView2.Wpf;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Luma;

public sealed class BrowserTab : INotifyPropertyChanged, IDisposable
{
    private string _title = "Новая вкладка";
    private string _fullUrl = "";
    private string _secondaryTitle = "Вторая область";
    private string _secondaryUrl = "";
    private string _activePane = "primary";
    private double _splitRatio = .5;

    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string? FolderId { get; set; }
    private bool _isPinned;
    /// <summary>Pinned tabs live in the sidebar PINNED section and are hidden from the TABS list.</summary>
    public bool IsPinned { get => _isPinned; set { if (_isPinned == value) return; _isPinned = value; OnChanged(); } }
    public bool IsInternal { get; set; }
    public string InternalPageKind { get; set; } = "";
    public string BackFallbackInternalUrl { get; set; } = "";
    public bool IsHome => IsInternal && InternalPageKind == "luma://home";
    private bool _isAsleep;
    /// <summary>Last time this tab was in front; drives the quiet background sleep.</summary>
    public DateTime LastActiveAt { get; set; } = DateTime.UtcNow;
    /// <summary>Suspended in the background: the row only dims a little, nothing else changes.</summary>
    public bool IsAsleep { get => _isAsleep; set { if (_isAsleep == value) return; _isAsleep = value; OnChanged(); } }

    private string _mediaTitle = "";
    private string _mediaArtist = "";
    private string _mediaArtwork = "";
    private bool _mediaPlaying;
    private double _mediaPosition;
    private double _mediaDuration;
    private double _mediaVolume = 1;
    private bool _mediaIsVideo;
    /// <summary>Live "what is this tab playing" snapshot reported by the media-watch script; only
    /// meaningful while <see cref="MediaPlaying"/> is true (see MainWindow's now-playing widget).</summary>
    public string MediaTitle { get => _mediaTitle; set { if (_mediaTitle == value) return; _mediaTitle = value; OnChanged(); } }
    public string MediaArtist { get => _mediaArtist; set { if (_mediaArtist == value) return; _mediaArtist = value; OnChanged(); } }
    public string MediaArtwork { get => _mediaArtwork; set { if (_mediaArtwork == value) return; _mediaArtwork = value; OnChanged(); } }
    public bool MediaPlaying { get => _mediaPlaying; set { if (_mediaPlaying == value) return; _mediaPlaying = value; OnChanged(); } }
    public double MediaPosition { get => _mediaPosition; set { if (Math.Abs(_mediaPosition - value) < .01) return; _mediaPosition = value; OnChanged(); OnChanged(nameof(MediaPositionText)); OnChanged(nameof(MediaProgress)); } }
    public double MediaDuration { get => _mediaDuration; set { if (Math.Abs(_mediaDuration - value) < .01) return; _mediaDuration = value; OnChanged(); OnChanged(nameof(MediaDurationText)); OnChanged(nameof(MediaProgress)); } }
    public double MediaVolume { get => _mediaVolume; set { if (Math.Abs(_mediaVolume - value) < .01) return; _mediaVolume = value; OnChanged(); } }
    public bool MediaIsVideo { get => _mediaIsVideo; set { if (_mediaIsVideo == value) return; _mediaIsVideo = value; OnChanged(); } }
    public string MediaPositionText => FormatMediaTime(MediaPosition);
    public string MediaDurationText => FormatMediaTime(MediaDuration);
    public double MediaProgress => MediaDuration > 0 ? Math.Clamp(MediaPosition / MediaDuration, 0, 1) : 0;
    private static string FormatMediaTime(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds) || seconds < 0) seconds = 0;
        var t = TimeSpan.FromSeconds(seconds);
        return t.Hours > 0 ? $"{t.Hours}:{t.Minutes:00}:{t.Seconds:00}" : $"{t.Minutes}:{t.Seconds:00}";
    }

    public WebView2 View { get; private set; } = new();
    public WebView2? SecondaryView { get; set; }

    public string Title { get => _title; set { if (_title == value) return; _title = value; OnChanged(); OnChanged(nameof(DisplayTitle)); } }
    public string FullUrl { get => _fullUrl; set { if (_fullUrl == value) return; _fullUrl = value; OnChanged(); OnChanged(nameof(ActiveUrl)); OnChanged(nameof(Domain)); OnChanged(nameof(FaviconUrl)); } }
    public string SecondaryTitle { get => _secondaryTitle; set { if (_secondaryTitle == value) return; _secondaryTitle = value; OnChanged(); OnChanged(nameof(DisplayTitle)); } }
    public string SecondaryUrl { get => _secondaryUrl; set { if (_secondaryUrl == value) return; _secondaryUrl = value; OnChanged(); OnChanged(nameof(ActiveUrl)); OnChanged(nameof(Domain)); OnChanged(nameof(FaviconUrl)); OnChanged(nameof(IsSplit)); } }
    public string ActivePane { get => _activePane; set { var next = value == "secondary" && SecondaryView is not null ? "secondary" : "primary"; if (_activePane == next) return; _activePane = next; OnChanged(); OnChanged(nameof(ActiveUrl)); OnChanged(nameof(Domain)); OnChanged(nameof(FaviconUrl)); OnChanged(nameof(DisplayTitle)); } }
    public double SplitRatio { get => _splitRatio; set { var next = Math.Clamp(value, .25, .75); if (Math.Abs(_splitRatio - next) < .001) return; _splitRatio = next; OnChanged(); } }

    public bool IsSplit => SecondaryView is not null && !string.IsNullOrWhiteSpace(SecondaryUrl);
    public WebView2 ActiveView => ActivePane == "secondary" && SecondaryView is not null ? SecondaryView : View;
    public string ActiveUrl => ActivePane == "secondary" && IsSplit ? SecondaryUrl : FullUrl;
    public string DisplayTitle => ActivePane == "secondary" && IsSplit ? SecondaryTitle : Title;
    private string _favicon = "";
    private string _secondaryFavicon = "";
    /// <summary>Icon reported by the page itself (WebView2 FaviconChanged); far more reliable than /favicon.ico.</summary>
    public string Favicon { get => _favicon; set { if (_favicon == value) return; _favicon = value ?? ""; OnChanged(); OnChanged(nameof(FaviconUrl)); } }
    public string SecondaryFavicon { get => _secondaryFavicon; set { if (_secondaryFavicon == value) return; _secondaryFavicon = value ?? ""; OnChanged(); OnChanged(nameof(FaviconUrl)); } }
    public string Domain => IsInternal && InternalPageKind.StartsWith("luma://search", StringComparison.Ordinal) ? "Luma Search" : DomainOf(ActiveUrl, IsInternal);
    public string FaviconUrl
    {
        get
        {
            var reported = ActivePane == "secondary" && IsSplit ? _secondaryFavicon : _favicon;
            return string.IsNullOrWhiteSpace(reported) ? FaviconOf(ActiveUrl, IsInternal) : reported;
        }
    }

    public void NotifySplitChanged() { OnChanged(nameof(IsSplit)); OnChanged(nameof(ActiveUrl)); OnChanged(nameof(Domain)); OnChanged(nameof(FaviconUrl)); OnChanged(nameof(DisplayTitle)); }
    public WebView2 ReplacePrimaryView()
    {
        var old = View;
        View = new WebView2();
        old.Dispose();
        OnChanged(nameof(ActiveView));
        return View;
    }
    public void PromoteSecondary() { if (SecondaryView is null) return; View.Dispose(); View = SecondaryView; SecondaryView = null; FullUrl = SecondaryUrl; Title = SecondaryTitle; SecondaryUrl = ""; SecondaryTitle = "Вторая область"; ActivePane = "primary"; NotifySplitChanged(); }
    public void CloseSecondary() { SecondaryView?.Dispose(); SecondaryView = null; SecondaryFavicon = ""; SecondaryUrl = ""; SecondaryTitle = "Вторая область"; ActivePane = "primary"; NotifySplitChanged(); }
    public TabSessionState ToSession() => new() { Id = Id, Url = FullUrl, Title = Title, FolderId = FolderId, IsPinned = IsPinned, IsInternal = IsInternal, InternalPageKind = InternalPageKind, IsSplit = IsSplit, SecondaryUrl = IsSplit ? SecondaryUrl : "", SecondaryTitle = SecondaryTitle, ActivePane = ActivePane, SplitRatio = SplitRatio };

    public static string DomainOf(string url, bool internalPage = false) { if (internalPage || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme == "luma") return "Страница Luma"; var host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host; return string.IsNullOrWhiteSpace(host) ? "Поиск или введите адрес…" : host; }
    public const string LumaIconUri = "pack://application:,,,/Assets/luma-mark.png";
    public static string FaviconOf(string url, bool internalPage = false)
    {
        if (internalPage || url.StartsWith("luma://", StringComparison.OrdinalIgnoreCase)
            || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host)) return LumaIconUri;
        return "https://www.google.com/s2/favicons?domain_url=" + Uri.EscapeDataString(uri.GetLeftPart(UriPartial.Authority)) + "&sz=64";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    public void Dispose() { View.Dispose(); SecondaryView?.Dispose(); SecondaryView = null; }
}

public sealed class SearchSuggestion
{
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string Url { get; set; } = "";
    public BrowserTab? ExistingTab { get; set; }
    public string FaviconUrl => ExistingTab?.FaviconUrl ?? BrowserTab.FaviconOf(Url);
    /// <summary>tab | site | history | address | search</summary>
    public string Kind { get; set; } = "history";
    public string Action => Kind switch
    {
        "tab" => "Перейти",
        "closed" => "Вернуть",
        "site" => "Сохранённое",
        "search" => "Искать",
        "address" => "Адрес",
        _ => "Открыть",
    };
}
