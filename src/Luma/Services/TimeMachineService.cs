using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Luma.Services;

public enum DiffLineType
{
    Unchanged,
    Added,
    Deleted
}

public sealed class DiffLine
{
    public DiffLineType Type { get; set; }
    public int? OldLineNumber { get; set; }
    public int? NewLineNumber { get; set; }
    public string Text { get; set; } = string.Empty;
}

public sealed class DiffResult
{
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public DateTimeOffset BaselineTimestamp { get; set; }
    public DateTimeOffset TargetTimestamp { get; set; }
    public int AddedCount { get; set; }
    public int DeletedCount { get; set; }
    public int UnchangedCount { get; set; }
    public List<DiffLine> Lines { get; set; } = new();
    public string Summary => $"+{AddedCount} additions, -{DeletedCount} deletions across {Lines.Count} total lines";
}

public sealed class TimeSnapshotMetadata
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;
    public string Url { get; set; } = string.Empty;
    public string Host { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public long HtmlLength { get; set; }
    public long CompressedBytes { get; set; }
    public string ContentHash { get; set; } = string.Empty;
    public string RelativeFilePath { get; set; } = string.Empty;

    public string DisplayTime => Timestamp.ToLocalTime().ToString("g");
    public string DisplaySize => $"{HtmlLength / 1024.0:F1} KB ({CompressedBytes / 1024.0:F1} KB compressed)";
}

/// <summary>
/// Offline DOM Time Machine & Delta Engine.
/// Stores compressed snapshots locally in %LocalAppData%\Luma\TimeMachine and calculates line-level diffs.
/// </summary>
public sealed class TimeMachineService
{
    private readonly string _baseStorageDir;
    private readonly string _metadataFilePath;
    private readonly List<TimeSnapshotMetadata> _index = new();
    private readonly object _indexLock = new();

    public TimeMachineService()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _baseStorageDir = Path.Combine(localAppData, "Luma", "TimeMachine");
        _metadataFilePath = Path.Combine(_baseStorageDir, "snapshots_index.json");

        EnsureStorageDirectories();
        LoadIndex();
    }

    private void EnsureStorageDirectories()
    {
        try
        {
            if (!Directory.Exists(_baseStorageDir))
            {
                Directory.CreateDirectory(_baseStorageDir);
            }
        }
        catch { }
    }

    private void LoadIndex()
    {
        lock (_indexLock)
        {
            try
            {
                if (File.Exists(_metadataFilePath))
                {
                    var json = File.ReadAllText(_metadataFilePath);
                    var loaded = JsonSerializer.Deserialize<List<TimeSnapshotMetadata>>(json);
                    if (loaded is not null)
                    {
                        _index.Clear();
                        _index.AddRange(loaded);
                    }
                }
            }
            catch
            {
                _index.Clear();
            }
        }
    }

    private void SaveIndex()
    {
        lock (_indexLock)
        {
            try
            {
                EnsureStorageDirectories();
                var json = JsonSerializer.Serialize(_index, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_metadataFilePath, json);
            }
            catch { }
        }
    }

    /// <summary>
    /// Captures, GZips, and records a snapshot of the current page DOM.
    /// </summary>
    public async Task<TimeSnapshotMetadata?> CaptureSnapshotAsync(string url, string title, string html)
    {
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(html))
            return null;

        try
        {
            Uri uri;
            try { uri = new Uri(url); } catch { return null; }
            var host = uri.Host;
            if (string.IsNullOrEmpty(host)) host = "unknown";

            var cleanHost = SanitizeFileName(host);
            var hostDir = Path.Combine(_baseStorageDir, cleanHost);
            if (!Directory.Exists(hostDir)) Directory.CreateDirectory(hostDir);

            // Compute SHA256 of raw HTML
            byte[] rawBytes = Encoding.UTF8.GetBytes(html);
            string hash;
            using (var sha = SHA256.Create())
            {
                hash = Convert.ToHexString(sha.ComputeHash(rawBytes));
            }

            // Check if identical to last snapshot for this host
            lock (_indexLock)
            {
                var latest = _index.Where(s => s.Host.Equals(host, StringComparison.OrdinalIgnoreCase))
                                   .OrderByDescending(s => s.Timestamp)
                                   .FirstOrDefault();
                if (latest != null && latest.ContentHash == hash)
                {
                    // No changes detected since last snapshot
                    return latest;
                }
            }

            var timestamp = DateTimeOffset.UtcNow;
            var fileName = $"{timestamp:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..6]}.lts";
            var relativePath = Path.Combine(cleanHost, fileName);
            var fullPath = Path.Combine(_baseStorageDir, relativePath);

            // Compress HTML with GZipStream
            using (var fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var gzip = new GZipStream(fileStream, CompressionLevel.Optimal))
            {
                await gzip.WriteAsync(rawBytes);
            }

            var fileInfo = new FileInfo(fullPath);

            var meta = new TimeSnapshotMetadata
            {
                Id = Guid.NewGuid().ToString("N"),
                Timestamp = timestamp,
                Url = url,
                Host = host,
                Title = string.IsNullOrWhiteSpace(title) ? host : title,
                HtmlLength = rawBytes.Length,
                CompressedBytes = fileInfo.Length,
                ContentHash = hash,
                RelativeFilePath = relativePath
            };

            lock (_indexLock)
            {
                _index.Insert(0, meta);
                SaveIndex();
            }

            return meta;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"CaptureSnapshotAsync error: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Retrieves all snapshots matching a specific host or domain.
    /// </summary>
    public List<TimeSnapshotMetadata> GetSnapshotsForHost(string host)
    {
        lock (_indexLock)
        {
            return _index.Where(s => s.Host.Equals(host, StringComparison.OrdinalIgnoreCase) ||
                                     s.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase))
                         .OrderByDescending(s => s.Timestamp)
                         .ToList();
        }
    }

    public List<TimeSnapshotMetadata> GetAllSnapshots()
    {
        lock (_indexLock)
        {
            return _index.OrderByDescending(s => s.Timestamp).ToList();
        }
    }

    /// <summary>
    /// Reads and decompresses the raw HTML of a stored snapshot.
    /// </summary>
    public async Task<string> ReadSnapshotHtmlAsync(TimeSnapshotMetadata meta)
    {
        try
        {
            var fullPath = Path.Combine(_baseStorageDir, meta.RelativeFilePath);
            if (!File.Exists(fullPath)) return string.Empty;

            using var fileStream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var gzip = new GZipStream(fileStream, CompressionMode.Decompress);
            using var reader = new StreamReader(gzip, Encoding.UTF8);
            return await reader.ReadToEndAsync();
        }
        catch (Exception ex)
        {
            return $"<!-- Error loading snapshot: {ex.Message} -->";
        }
    }

    /// <summary>
    /// Calculates line-by-line diff between two snapshots.
    /// </summary>
    public async Task<DiffResult> CompareSnapshotsAsync(TimeSnapshotMetadata baseline, TimeSnapshotMetadata target)
    {
        var baselineHtml = await ReadSnapshotHtmlAsync(baseline);
        var targetHtml = await ReadSnapshotHtmlAsync(target);

        return ComputeDiff(baselineHtml, targetHtml, baseline.Title, baseline.Url, baseline.Timestamp, target.Timestamp);
    }

    /// <summary>
    /// Compares a stored historical snapshot with live HTML from the active webview.
    /// </summary>
    public async Task<DiffResult> CompareWithLiveAsync(TimeSnapshotMetadata baseline, string liveHtml)
    {
        var baselineHtml = await ReadSnapshotHtmlAsync(baseline);
        return ComputeDiff(baselineHtml, liveHtml, baseline.Title, baseline.Url, baseline.Timestamp, DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// High-performance line-by-line LCS diff generator.
    /// </summary>
    public static DiffResult ComputeDiff(string oldText, string newText, string title, string url, DateTimeOffset oldTime, DateTimeOffset newTime)
    {
        var oldLines = SplitLines(oldText);
        var newLines = SplitLines(newText);

        var diff = new DiffResult
        {
            Title = title,
            Url = url,
            BaselineTimestamp = oldTime,
            TargetTimestamp = newTime
        };

        // If files are huge, limit lines to first 1200 lines to preserve instant UI responsiveness
        int maxLines = 1500;
        bool truncated = false;
        if (oldLines.Count > maxLines || newLines.Count > maxLines)
        {
            oldLines = oldLines.Take(maxLines).ToList();
            newLines = newLines.Take(maxLines).ToList();
            truncated = true;
        }

        var lcs = ComputeLcsTable(oldLines, newLines);
        int i = oldLines.Count;
        int j = newLines.Count;

        var stack = new Stack<DiffLine>();

        while (i > 0 || j > 0)
        {
            if (i > 0 && j > 0 && oldLines[i - 1] == newLines[j - 1])
            {
                stack.Push(new DiffLine
                {
                    Type = DiffLineType.Unchanged,
                    OldLineNumber = i,
                    NewLineNumber = j,
                    Text = oldLines[i - 1]
                });
                i--;
                j--;
            }
            else if (j > 0 && (i == 0 || lcs[i, j - 1] >= lcs[i - 1, j]))
            {
                stack.Push(new DiffLine
                {
                    Type = DiffLineType.Added,
                    NewLineNumber = j,
                    Text = newLines[j - 1]
                });
                j--;
            }
            else if (i > 0 && (j == 0 || lcs[i, j - 1] < lcs[i - 1, j]))
            {
                stack.Push(new DiffLine
                {
                    Type = DiffLineType.Deleted,
                    OldLineNumber = i,
                    Text = oldLines[i - 1]
                });
                i--;
            }
        }

        diff.Lines = stack.ToList();
        diff.AddedCount = diff.Lines.Count(l => l.Type == DiffLineType.Added);
        diff.DeletedCount = diff.Lines.Count(l => l.Type == DiffLineType.Deleted);
        diff.UnchangedCount = diff.Lines.Count(l => l.Type == DiffLineType.Unchanged);

        if (truncated)
        {
            diff.Lines.Add(new DiffLine
            {
                Type = DiffLineType.Unchanged,
                Text = $"... [Diff capped at {maxLines} lines for instant preview performance] ..."
            });
        }

        return diff;
    }

    private static int[,] ComputeLcsTable(List<string> oldLines, List<string> newLines)
    {
        int m = oldLines.Count;
        int n = newLines.Count;
        int[,] table = new int[m + 1, n + 1];

        for (int i = 0; i < m; i++)
        {
            for (int j = 0; j < n; j++)
            {
                if (oldLines[i] == newLines[j])
                {
                    table[i + 1, j + 1] = table[i, j] + 1;
                }
                else
                {
                    table[i + 1, j + 1] = Math.Max(table[i + 1, j], table[i, j + 1]);
                }
            }
        }

        return table;
    }

    private static List<string> SplitLines(string text)
    {
        if (string.IsNullOrEmpty(text)) return new List<string>();
        return text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None).ToList();
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder();
        foreach (var c in name)
        {
            if (invalid.Contains(c) || c == ':') sb.Append('_');
            else sb.Append(c);
        }
        return sb.ToString();
    }

    public void DeleteSnapshot(TimeSnapshotMetadata meta)
    {
        lock (_indexLock)
        {
            try
            {
                var fullPath = Path.Combine(_baseStorageDir, meta.RelativeFilePath);
                if (File.Exists(fullPath)) File.Delete(fullPath);
                _index.RemoveAll(s => s.Id == meta.Id);
                SaveIndex();
            }
            catch { }
        }
    }
}
