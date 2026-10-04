namespace Luma.Core;

public static class UrlTools
{
    private static readonly char[] WordSeparators = [' ', '-', '–', '—', '|', '/', '.', ':', ',', '_'];
    private static readonly string[] TrackingParameters = ["fbclid", "gclid", "yclid", "igshid", "mc_eid", "mc_cid", "ref_src", "ref_url", "_ga", "si", "spm", "scid"];

    public static string NormalizeInput(string input, string searchEngine)
    {
        input = (input ?? string.Empty).Replace("\r", "").Replace("\n", "").Trim();
        if (string.IsNullOrWhiteSpace(input)) return HomeUrl(searchEngine);

        // Absolute URIs with recognized schemes
        if (Uri.TryCreate(input, UriKind.Absolute, out var uri))
        {
            if (uri.Scheme is "http" or "https" or "view-source" or "luma" or "file" or "blob" or "data" or "about" or "chrome" or "edge" or "chrome-extension")
                return uri.ToString();
        }

        // Local filesystem paths (e.g. C:\path\file.html or \\server\share)
        if (input.Length >= 3 && char.IsLetter(input[0]) && input[1] == ':' && (input[2] == '\\' || input[2] == '/'))
            return new Uri(input).AbsoluteUri;

        // Localhost and bare IPs with or without port (e.g. localhost:3000, 127.0.0.1:8080)
        var isLocal = input.StartsWith("localhost", StringComparison.OrdinalIgnoreCase)
                   || input.StartsWith("127.0.0.1", StringComparison.OrdinalIgnoreCase)
                   || input.StartsWith("0.0.0.0", StringComparison.OrdinalIgnoreCase);
        if (isLocal && !input.Contains(' '))
            return "http://" + input;

        // Domain with port or IP:port without spaces (e.g. 192.168.1.5:8000, myserver:8080)
        if (!input.Contains(' ') && input.Contains(':'))
        {
            var parts = input.Split(':', 2);
            if (parts.Length == 2 && int.TryParse(parts[1].Split('/')[0], out var port) && port > 0 && port <= 65535)
                return "http://" + input;
        }

        // Standard domain or IP without scheme
        if (input.Contains('.') && !input.Contains(' '))
        {
            var hostPart = input.Split('/')[0];
            if (System.Net.IPAddress.TryParse(hostPart, out _))
                return "http://" + input;
            return "https://" + input;
        }

        var query = Uri.EscapeDataString(input);
        return searchEngine switch
        {
            "google" => "https://www.google.com/search?q=" + query,
            "duckduckgo" => "https://duckduckgo.com/?q=" + query,
            "bing" => "https://www.bing.com/search?q=" + query,
            _ => "luma://search?q=" + query + "&mode=all",
        };
    }

    public static string HomeUrl(string searchEngine) => searchEngine switch
    {
        "google" => "https://www.google.com",
        "duckduckgo" => "https://duckduckgo.com/",
        "bing" => "https://www.bing.com/",
        _ => "luma://home",
    };

    public static string HostKey(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || string.IsNullOrWhiteSpace(uri.Host)) return string.Empty;
        return StripWww(uri.Host).ToLowerInvariant();
    }

    public static string DomainKey(string url) => HostKey(url);

    public static bool SameSite(string first, string second)
    {
        var a = HostKey(first); var b = HostKey(second);
        return a.Length > 0 && b.Length > 0 && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
    }

    public static double MatchScore(string query, string? title, string? url)
    {
        query = (query ?? string.Empty).ToLowerInvariant();
        if (query.Length == 0) return 0;
        var host = HostKey(url ?? string.Empty);
        var name = (title ?? string.Empty).ToLowerInvariant();
        var full = (url ?? string.Empty).ToLowerInvariant();
        if (host.StartsWith(query, StringComparison.Ordinal)) return 140 - Math.Min(35, host.Length - query.Length);
        if (host.Split('.').Any(part => part.StartsWith(query, StringComparison.Ordinal))) return 100;
        if (name.StartsWith(query, StringComparison.Ordinal)) return 95;
        if (name.Split(WordSeparators, StringSplitOptions.RemoveEmptyEntries).Any(word => word.StartsWith(query, StringComparison.Ordinal))) return 75;
        if (host.Contains(query, StringComparison.Ordinal)) return 60;
        if (name.Contains(query, StringComparison.Ordinal)) return 45;
        if (full.Contains(query, StringComparison.Ordinal)) return 28;
        return -1;
    }

    public static string CleanTrackingLink(string text)
    {
        var trimmed = (text ?? string.Empty).Trim();
        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")) return trimmed;
        var keep = new List<string>();
        foreach (var part in uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var name = part.Split('=')[0];
            if (name.StartsWith("utm_", StringComparison.OrdinalIgnoreCase) || TrackingParameters.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
            keep.Add(part);
        }
        var builder = new UriBuilder(uri) { Query = string.Join('&', keep) };
        var result = builder.Uri.ToString();
        return result.EndsWith('?') ? result[..^1] : result;
    }

    private static string StripWww(string host) => host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
}
