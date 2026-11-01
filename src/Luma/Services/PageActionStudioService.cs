using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Luma.Services;

public sealed class ExtractedTable
{
    public int Index { get; set; }
    public string Caption { get; set; } = string.Empty;
    public List<string> Headers { get; set; } = new();
    public List<List<string>> Rows { get; set; } = new();

    public int RowCount => Rows.Count;
    public int ColumnCount => Headers.Count > 0 ? Headers.Count : (Rows.Count > 0 ? Rows[0].Count : 0);
}

public sealed class TableExtractionResult
{
    public string PageUrl { get; set; } = string.Empty;
    public string PageTitle { get; set; } = string.Empty;
    public List<ExtractedTable> Tables { get; set; } = new();
}

public sealed class ReaderModeData
{
    public string Title { get; set; } = string.Empty;
    public string Byline { get; set; } = string.Empty;
    public string ContentHtml { get; set; } = string.Empty;
    public int WordCount { get; set; }
    public int EstimatedReadingMinutes { get; set; }
}

/// <summary>
/// Service for live DOM manipulation, Table/Grid Data Extraction to CSV/Markdown,
/// Pure Reader Mode synthesis, and Domain Boosts (CSS/JS injection).
/// </summary>
public sealed class PageActionStudioService
{
    private readonly string _boostsDirectory;

    public PageActionStudioService()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _boostsDirectory = Path.Combine(localAppData, "Luma", "Boosts");
        try
        {
            if (!Directory.Exists(_boostsDirectory))
            {
                Directory.CreateDirectory(_boostsDirectory);
            }
        }
        catch { }
    }

    #region JS Extraction Scripts

    public const string TableScraperScript = """
    (() => {
        try {
            const tables = Array.from(document.querySelectorAll('table, [role="grid"], [role="table"]'));
            const results = [];

            tables.forEach((tbl, idx) => {
                const captionEl = tbl.querySelector('caption, h1, h2, h3, h4, [role="heading"]');
                const caption = captionEl ? captionEl.innerText.trim() : ('Table ' + (idx + 1));

                // Extract Headers
                let headers = [];
                const headerCells = tbl.querySelectorAll('thead th, thead td, tr:first-child th');
                if (headerCells.length > 0) {
                    headers = Array.from(headerCells).map(c => c.innerText.trim().replace(/\s+/g, ' '));
                }

                // Extract Rows
                const rows = [];
                const rowElements = tbl.querySelectorAll('tbody tr, tr');
                rowElements.forEach((tr, rIdx) => {
                    const cells = Array.from(tr.querySelectorAll('td, th')).map(c => c.innerText.trim().replace(/\s+/g, ' '));
                    if (cells.length > 0) {
                        // If we didn't find headers in thead, and this is row 0, use it as headers
                        if (headers.length === 0 && rIdx === 0) {
                            headers = cells;
                        } else {
                            rows.push(cells);
                        }
                    }
                });

                if (headers.length > 0 || rows.length > 0) {
                    results.push({
                        Index: idx + 1,
                        Caption: caption,
                        Headers: headers,
                        Rows: rows
                    });
                }
            });

            return JSON.stringify({
                PageUrl: window.location.href,
                PageTitle: document.title,
                Tables: results
            });
        } catch (e) {
            return JSON.stringify({ PageUrl: window.location.href, PageTitle: document.title, Tables: [] });
        }
    })()
    """;

    public const string ReaderModeExtractScript = """
    (() => {
        try {
            // Find best content container
            const candidates = Array.from(document.querySelectorAll('article, [role="main"], main, .post-content, .entry-content, .article-body, #content'));
            let bestEl = candidates[0];

            if (!bestEl) {
                // Heuristic: element with largest text length
                let maxLen = 0;
                document.querySelectorAll('div, section').forEach(el => {
                    const textLen = el.innerText ? el.innerText.trim().length : 0;
                    if (textLen > maxLen && !el.querySelector('article, main')) {
                        maxLen = textLen;
                        bestEl = el;
                    }
                });
            }

            if (!bestEl) bestEl = document.body;

            // Clone to sanitize
            const clone = bestEl.cloneNode(true);
            const removeSelectors = 'script, style, iframe, nav, footer, header, aside, .ad, .ads, .social-share, .comments';
            clone.querySelectorAll(removeSelectors).forEach(n => n.remove());

            const text = clone.innerText || '';
            const words = text.split(/\s+/).filter(w => w.length > 0).length;
            const readingTime = Math.max(1, Math.round(words / 220));

            return JSON.stringify({
                Title: document.title,
                Byline: (document.querySelector('meta[name="author"]') || {}).content || '',
                ContentHtml: clone.innerHTML,
                WordCount: words,
                EstimatedReadingMinutes: readingTime
            });
        } catch (err) {
            return JSON.stringify({ Title: document.title, Byline: '', ContentHtml: '', WordCount: 0, EstimatedReadingMinutes: 0 });
        }
    })()
    """;

    #endregion

    #region Export Formatters

    public static string TableToCsv(ExtractedTable table)
    {
        var sb = new StringBuilder();

        if (table.Headers.Count > 0)
        {
            sb.AppendLine(string.Join(",", table.Headers.Select(EscapeCsvField)));
        }

        foreach (var row in table.Rows)
        {
            sb.AppendLine(string.Join(",", row.Select(EscapeCsvField)));
        }

        return sb.ToString();
    }

    public static string TableToMarkdown(ExtractedTable table)
    {
        var sb = new StringBuilder();
        int cols = table.ColumnCount;
        if (cols == 0) return string.Empty;

        var headers = table.Headers.Count > 0
            ? table.Headers
            : Enumerable.Range(1, cols).Select(i => $"Col {i}").ToList();

        // Header row
        sb.Append("| ");
        sb.Append(string.Join(" | ", headers.Select(h => h.Replace("|", "\\|"))));
        sb.AppendLine(" |");

        // Separator row
        sb.Append("|");
        for (int i = 0; i < cols; i++)
        {
            sb.Append(" --- |");
        }
        sb.AppendLine();

        // Data rows
        foreach (var row in table.Rows)
        {
            sb.Append("| ");
            var padded = new List<string>(row);
            while (padded.Count < cols) padded.Add("");
            sb.Append(string.Join(" | ", padded.Take(cols).Select(c => c.Replace("|", "\\|"))));
            sb.AppendLine(" |");
        }

        return sb.ToString();
    }

    public static string TableToJson(ExtractedTable table)
    {
        var list = new List<Dictionary<string, string>>();
        var headers = table.Headers.Count > 0
            ? table.Headers
            : Enumerable.Range(1, table.ColumnCount).Select(i => $"col_{i}").ToList();

        foreach (var row in table.Rows)
        {
            var dict = new Dictionary<string, string>();
            for (int i = 0; i < headers.Count; i++)
            {
                var val = i < row.Count ? row[i] : "";
                dict[headers[i]] = val;
            }
            list.Add(dict);
        }

        return JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string EscapeCsvField(string field)
    {
        if (string.IsNullOrEmpty(field)) return "\"\"";
        if (field.Contains(',') || field.Contains('"') || field.Contains('\n') || field.Contains('\r'))
        {
            return $"\"{field.Replace("\"", "\"\"")}\"";
        }
        return $"\"{field}\"";
    }

    #endregion

    #region Reader Mode HTML Template Generator

    public static string GenerateReaderDocumentHtml(ReaderModeData data)
    {
        var title = System.Net.WebUtility.HtmlEncode(data.Title);
        var bylineSpan = string.IsNullOrWhiteSpace(data.Byline) ? "" : $"<span>• By {System.Net.WebUtility.HtmlEncode(data.Byline)}</span>";
        return $$"""
        <!DOCTYPE html>
        <html lang="en">
        <head>
            <meta charset="utf-8">
            <title>{{title}} - Luma Pure Reader</title>
            <style>
                :root {
                    --bg-color: #121214;
                    --text-color: #E2E2E6;
                    --accent-color: #6366F1;
                    --muted-color: #8E8E93;
                    --border-color: #27272A;
                }
                body {
                    margin: 0;
                    padding: 40px 20px;
                    background-color: var(--bg-color);
                    color: var(--text-color);
                    font-family: -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif;
                    line-height: 1.75;
                    font-size: 19px;
                    display: flex;
                    justify-content: center;
                }
                .reader-container {
                    max-width: 720px;
                    width: 100%;
                }
                .reader-header {
                    margin-bottom: 36px;
                    border-bottom: 1px solid var(--border-color);
                    padding-bottom: 24px;
                }
                h1.reader-title {
                    font-size: 34px;
                    line-height: 1.25;
                    font-weight: 700;
                    letter-spacing: -0.02em;
                    margin: 0 0 16px 0;
                    color: #FFFFFF;
                }
                .reader-meta {
                    font-size: 14px;
                    color: var(--muted-color);
                    display: flex;
                    gap: 16px;
                    align-items: center;
                }
                .reader-badge {
                    background: #27272A;
                    padding: 3px 10px;
                    border-radius: 12px;
                    font-size: 12px;
                    font-weight: 600;
                    color: #A1A1AA;
                }
                .reader-content p {
                    margin: 0 0 24px 0;
                }
                .reader-content img {
                    max-width: 100%;
                    height: auto;
                    border-radius: 8px;
                    margin: 20px 0;
                }
                .reader-content blockquote {
                    margin: 24px 0;
                    padding: 12px 20px;
                    border-left: 4px solid var(--accent-color);
                    background: #18181B;
                    border-radius: 0 8px 8px 0;
                    color: #D4D4D8;
                    font-style: italic;
                }
                .reader-content pre, .reader-content code {
                    font-family: Consolas, monospace;
                    background: #18181B;
                    padding: 2px 6px;
                    border-radius: 4px;
                    font-size: 16px;
                }
            </style>
        </head>
        <body>
            <div class="reader-container">
                <header class="reader-header">
                    <h1 class="reader-title">{{title}}</h1>
                    <div class="reader-meta">
                        <span class="reader-badge">PURE READER</span>
                        <span>{{data.WordCount}} words</span>
                        <span>•</span>
                        <span>~{{data.EstimatedReadingMinutes}} min read</span>
                        {{bylineSpan}}
                    </div>
                </header>
                <article class="reader-content">
                    {{data.ContentHtml}}
                </article>
            </div>
        </body>
        </html>
        """;
    }

    #endregion

    #region Domain Boosts (CSS/JS)

    public static readonly Dictionary<string, string> BoostPresets = new()
    {
        ["OLED Pitch Black"] = """
            html, body { background-color: #000000 !important; color: #E4E4E7 !important; }
            div, section, article, nav, header, footer { background-color: transparent !important; }
            p, span, h1, h2, h3, h4, h5, h6, li { color: #E4E4E7 !important; }
            a { color: #818CF8 !important; }
            img, video { filter: brightness(0.9) contrast(1.05); }
        """,
        ["Zen Minimalist"] = """
            aside, [role="complementary"], .ad, .ads, .sidebar, .social-share, .comments, iframe[id*="google"] {
                display: none !important;
            }
            body { max-width: 1100px !important; margin: 0 auto !important; }
        """,
        ["Cyberpunk Neon"] = """
            body { background-color: #0b0c16 !important; color: #00f0ff !important; font-family: 'Consolas', monospace !important; }
            a { color: #ff007f !important; text-shadow: 0 0 5px #ff007f; }
            h1, h2, h3 { color: #ffe600 !important; text-shadow: 0 0 8px #ffe600; }
            border, div { border-color: #00f0ff33 !important; }
        """
    };

    public string GetBoostForDomain(string domain)
    {
        try
        {
            var path = Path.Combine(_boostsDirectory, $"{domain.ToLowerInvariant()}.css");
            if (File.Exists(path)) return File.ReadAllText(path);
        }
        catch { }
        return string.Empty;
    }

    public void SaveBoostForDomain(string domain, string css)
    {
        try
        {
            var path = Path.Combine(_boostsDirectory, $"{domain.ToLowerInvariant()}.css");
            if (string.IsNullOrWhiteSpace(css))
            {
                if (File.Exists(path)) File.Delete(path);
            }
            else
            {
                File.WriteAllText(path, css);
            }
        }
        catch { }
    }

    public static string CreateInjectCssScript(string css)
    {
        var escaped = JsonSerializer.Serialize(css);
        return $$"""
        (() => {
            let style = document.getElementById('luma-custom-boost');
            if (!style) {
                style = document.createElement('style');
                style.id = 'luma-custom-boost';
                document.head.appendChild(style);
            }
            style.textContent = {{escaped}};
        })();
        """;
    }

    public static string CreateRemoveCssScript()
    {
        return """
        (() => {
            const style = document.getElementById('luma-custom-boost');
            if (style) style.remove();
        })();
        """;
    }

    #endregion
}
