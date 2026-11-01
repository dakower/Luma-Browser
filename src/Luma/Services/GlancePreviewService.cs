using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Luma.Services;

public sealed class GlanceCardData
{
    public string RawUrl { get; set; } = string.Empty;
    public string CleanUrl { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public string Protocol { get; set; } = string.Empty;
    public bool IsSecure { get; set; }
    public string Category { get; set; } = "Web Page";
    public string FaviconUrl { get; set; } = string.Empty;
    public int RemovedTrackingParamsCount { get; set; }
}

/// <summary>
/// Service providing instant smart glances, tracking cleaner, and domain insights for URLs.
/// </summary>
public sealed class GlancePreviewService
{
    private static readonly HashSet<string> TrackingParamNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "utm_source", "utm_medium", "utm_campaign", "utm_term", "utm_content",
        "fbclid", "gclid", "dclid", "msclkid", "mc_eid", "igshid", "_hsenc", "_hsmi"
    };

    public GlanceCardData AnalyzeUrl(string url)
    {
        var result = new GlanceCardData { RawUrl = url };

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            result.CleanUrl = url;
            result.Host = url;
            return result;
        }

        result.Host = uri.Host;
        result.Protocol = uri.Scheme.ToUpperInvariant();
        result.IsSecure = uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase);
        result.FaviconUrl = $"https://icons.duckduckgo.com/ip3/{uri.Host}.ico";

        // Clean tracking parameters
        var (cleanUrl, removedCount) = CleanTrackingParameters(uri);
        result.CleanUrl = cleanUrl;
        result.RemovedTrackingParamsCount = removedCount;

        // Categorize destination
        result.Category = DetectCategory(uri);

        return result;
    }

    private static (string CleanUrl, int RemovedCount) CleanTrackingParameters(Uri uri)
    {
        if (string.IsNullOrEmpty(uri.Query)) return (uri.ToString(), 0);

        var query = uri.Query.TrimStart('?');
        var pairs = query.Split('&', StringSplitOptions.RemoveEmptyEntries);
        var cleanPairs = new List<string>();
        int removed = 0;

        foreach (var pair in pairs)
        {
            var parts = pair.Split('=', 2);
            var key = parts[0];
            if (TrackingParamNames.Contains(key))
            {
                removed++;
            }
            else
            {
                cleanPairs.Add(pair);
            }
        }

        var builder = new UriBuilder(uri);
        builder.Query = cleanPairs.Count > 0 ? string.Join("&", cleanPairs) : string.Empty;
        return (builder.Uri.ToString(), removed);
    }

    private static string DetectCategory(Uri uri)
    {
        var host = uri.Host.ToLowerInvariant();
        var path = uri.AbsolutePath.ToLowerInvariant();

        if (host.Contains("github.com") || host.Contains("gitlab.com") || host.Contains("bitbucket.org"))
            return "Code Repository";
        if (host.Contains("youtube.com") || host.Contains("youtu.be") || host.Contains("vimeo.com") || host.Contains("twitch.tv"))
            return "Video & Stream";
        if (host.Contains("reddit.com") || host.Contains("twitter.com") || host.Contains("x.com") || host.Contains("instagram.com"))
            return "Social Media";
        if (path.EndsWith(".pdf"))
            return "PDF Document";
        if (host.Contains("amazon.") || host.Contains("ebay.") || host.Contains("aliexpress."))
            return "E-Commerce";
        if (host.Contains("docs.") || host.Contains("learn.microsoft") || path.Contains("/doc") || path.Contains("/wiki"))
            return "Documentation";

        return "Web Page";
    }
}
