using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Luma.Services;

public sealed class ScrapbookClip
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string FaviconUrl { get; set; } = string.Empty;
    public string TextContent { get; set; } = string.Empty;
    public string UserNote { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();

    public string DisplayTime => CreatedAt.ToLocalTime().ToString("g");
    public string TagString => Tags.Count > 0 ? string.Join(" ", Tags.Select(t => t.StartsWith('#') ? t : "#" + t)) : string.Empty;
    public string PreviewSnippet => TextContent.Length > 120 ? TextContent[..120] + "..." : TextContent;
}

/// <summary>
/// Omni-Scrapbook Knowledge Studio.
/// Captures quotes, web snippets, ideas, and exports full research dossiers to Markdown.
/// </summary>
public sealed class ScrapbookService
{
    private readonly string _storageFilePath;
    private readonly List<ScrapbookClip> _clips = new();
    private readonly object _lock = new();

    public event Action? ClipsChanged;

    public ScrapbookService()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var dir = Path.Combine(localAppData, "Luma");
        try { if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); } catch { }
        _storageFilePath = Path.Combine(dir, "scrapbook.json");

        Load();
    }

    private void Load()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(_storageFilePath))
                {
                    var json = File.ReadAllText(_storageFilePath);
                    var list = JsonSerializer.Deserialize<List<ScrapbookClip>>(json);
                    if (list is not null)
                    {
                        _clips.Clear();
                        _clips.AddRange(list);
                    }
                }
            }
            catch
            {
                _clips.Clear();
            }
        }
    }

    private void Save()
    {
        lock (_lock)
        {
            try
            {
                var json = JsonSerializer.Serialize(_clips, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_storageFilePath, json);
            }
            catch { }
        }
    }

    public ScrapbookClip AddClip(string title, string url, string textContent, string userNote = "", List<string>? tags = null)
    {
        string domain = string.Empty;
        try { domain = new Uri(url).Host; } catch { }

        var clip = new ScrapbookClip
        {
            Id = Guid.NewGuid().ToString("N"),
            CreatedAt = DateTimeOffset.UtcNow,
            Title = string.IsNullOrWhiteSpace(title) ? domain : title,
            Url = url,
            Domain = domain,
            FaviconUrl = string.IsNullOrEmpty(domain) ? "" : $"https://icons.duckduckgo.com/ip3/{domain}.ico",
            TextContent = textContent.Trim(),
            UserNote = userNote.Trim(),
            Tags = tags ?? new List<string>()
        };

        lock (_lock)
        {
            _clips.Insert(0, clip);
            Save();
        }

        ClipsChanged?.Invoke();
        return clip;
    }

    public void DeleteClip(string id)
    {
        lock (_lock)
        {
            _clips.RemoveAll(c => c.Id == id);
            Save();
        }
        ClipsChanged?.Invoke();
    }

    public List<ScrapbookClip> GetAllClips()
    {
        lock (_lock)
        {
            return _clips.OrderByDescending(c => c.CreatedAt).ToList();
        }
    }

    public List<ScrapbookClip> SearchClips(string query, string? tagFilter = null)
    {
        lock (_lock)
        {
            var q = _clips.AsEnumerable();

            if (!string.IsNullOrWhiteSpace(tagFilter))
            {
                var normalizedTag = tagFilter.TrimStart('#');
                q = q.Where(c => c.Tags.Any(t => t.TrimStart('#').Equals(normalizedTag, StringComparison.OrdinalIgnoreCase)));
            }

            if (!string.IsNullOrWhiteSpace(query))
            {
                q = q.Where(c => c.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                 c.TextContent.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                 c.UserNote.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                 c.Domain.Contains(query, StringComparison.OrdinalIgnoreCase));
            }

            return q.OrderByDescending(c => c.CreatedAt).ToList();
        }
    }

    public List<string> GetAllUniqueTags()
    {
        lock (_lock)
        {
            return _clips.SelectMany(c => c.Tags)
                         .Select(t => t.TrimStart('#'))
                         .Distinct(StringComparer.OrdinalIgnoreCase)
                         .OrderBy(t => t)
                         .ToList();
        }
    }

    /// <summary>
    /// Compiles all captured clips into an Obsidian / Notion ready Markdown Dossier.
    /// </summary>
    public string GenerateDossierMarkdown(string? tagFilter = null)
    {
        var clips = SearchClips("", tagFilter);
        var sb = new StringBuilder();

        var date = DateTime.Now.ToString("yyyy-MM-dd HH:mm");
        sb.AppendLine($"# Luma Research Dossier: {(string.IsNullOrEmpty(tagFilter) ? "All Clippings" : "#" + tagFilter)}");
        sb.AppendLine($"*Compiled on {date} with Luma Omni-Scrapbook Studio*");
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();

        sb.AppendLine("## Summary Table");
        sb.AppendLine("| # | Title | Domain | Date | Tags |");
        sb.AppendLine("|---|---|---|---|---|");
        for (int i = 0; i < clips.Count; i++)
        {
            var c = clips[i];
            sb.AppendLine($"| {i + 1} | [{c.Title}](#clip-{i + 1}) | `{c.Domain}` | {c.DisplayTime} | {c.TagString} |");
        }
        sb.AppendLine();
        sb.AppendLine("---");
        sb.AppendLine();

        sb.AppendLine("## Research Extracts & Notes");
        sb.AppendLine();

        for (int i = 0; i < clips.Count; i++)
        {
            var c = clips[i];
            sb.AppendLine($"<a id=\"clip-{i + 1}\"></a>");
            sb.AppendLine($"### {i + 1}. {c.Title}");
            sb.AppendLine($"- **Source URL**: [{c.Url}]({c.Url})");
            sb.AppendLine($"- **Domain**: `{c.Domain}`");
            sb.AppendLine($"- **Captured**: {c.DisplayTime}");
            if (c.Tags.Count > 0)
            {
                sb.AppendLine($"- **Tags**: {c.TagString}");
            }
            sb.AppendLine();

            if (!string.IsNullOrWhiteSpace(c.TextContent))
            {
                sb.AppendLine("> " + string.Join("\n> ", c.TextContent.Split('\n')));
                sb.AppendLine();
            }

            if (!string.IsNullOrWhiteSpace(c.UserNote))
            {
                sb.AppendLine($"💡 **Researcher Note**: {c.UserNote}");
                sb.AppendLine();
            }

            sb.AppendLine("---");
            sb.AppendLine();
        }

        return sb.ToString();
    }
}
