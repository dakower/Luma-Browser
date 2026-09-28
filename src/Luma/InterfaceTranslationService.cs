using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Luma;

/// <summary>Translates browser-owned UI strings and stores a local per-language cache.</summary>
internal static class InterfaceTranslationService
{
    private static readonly HttpClient Http = CreateClient();
    private static readonly SemaphoreSlim NetworkGate = new(6, 6);
    private static readonly ConcurrentDictionary<string, ConcurrentDictionary<string, string>> Caches = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Regex Cyrillic = new("[А-Яа-яЁёІіЇїЄє]", RegexOptions.Compiled);
    private static readonly SemaphoreSlim SaveGate = new(1, 1);

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/140.0 Safari/537.36");
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ru-RU,ru;q=0.9,en;q=0.8");
        return client;
    }

    public static bool NeedsTranslation(string? text) => !string.IsNullOrWhiteSpace(text) && Cyrillic.IsMatch(text);

    public static string ResolveSourceText(string displayed, string language)
    {
        if (string.IsNullOrWhiteSpace(displayed)) return displayed;
        language = Normalize(language);
        if (language == "ru") return displayed;
        var cache = Caches.GetOrAdd(language, LoadCache);
        foreach (var pair in cache)
            if (string.Equals(pair.Value, displayed, StringComparison.Ordinal)) return pair.Key;
        return displayed;
    }

    public static Dictionary<string, string> GetKnownTranslations(IEnumerable<string> source, string language)
    {
        language = Normalize(language);
        var values = source.Where(NeedsTranslation).Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        if (language == "ru") return values.ToDictionary(x => x, x => x, StringComparer.Ordinal);
        var cache = Caches.GetOrAdd(language, LoadCache);
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var value in values)
        {
            if (cache.TryGetValue(value, out var exact)) result[value] = exact;
            else if (TryTranslateTemplate(value, cache, out var templated)) result[value] = templated;
        }
        return result;
    }

    public static async Task<Dictionary<string, string>> TranslateManyAsync(IEnumerable<string> source, string language, CancellationToken token = default)
    {
        language = Normalize(language);
        var values = source.Where(NeedsTranslation).Select(x => x.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        if (language == "ru") return values.ToDictionary(x => x, x => x, StringComparer.Ordinal);
        var cache = Caches.GetOrAdd(language, LoadCache);
        foreach (var value in values)
            if (!cache.ContainsKey(value) && TryTranslateTemplate(value, cache, out var templated)) cache[value] = templated;
        var missing = values.Where(x => !cache.ContainsKey(x)).ToArray();
        if (missing.Length > 0)
        {
            var translatedBatches = await Task.WhenAll(BuildBatches(missing).Select(batch => TranslateBatchAsync(batch, language, token)));
            foreach (var translatedBatch in translatedBatches)
            {
                foreach (var pair in translatedBatch)
                    if (!string.IsNullOrWhiteSpace(pair.Value) && !string.Equals(pair.Key, pair.Value, StringComparison.Ordinal)) cache[pair.Key] = pair.Value;
            }
            await SaveCacheAsync(language, cache);
        }
        return values.ToDictionary(x => x, x => cache.TryGetValue(x, out var translated) ? translated : x, StringComparer.Ordinal);
    }

    private static bool TryTranslateTemplate(string source, ConcurrentDictionary<string, string> cache, out string translated)
    {
        translated = string.Empty;
        foreach (var pair in cache)
        {
            if (!pair.Key.Contains('{') || !pair.Value.Contains('{')) continue;
            var placeholders = Regex.Matches(pair.Key, @"\{[^{}]+\}");
            if (placeholders.Count == 0) continue;
            var pattern = new System.Text.StringBuilder("^");
            var cursor = 0;
            foreach (Match placeholder in placeholders)
            {
                pattern.Append(Regex.Escape(pair.Key[cursor..placeholder.Index]));
                pattern.Append("(.+?)");
                cursor = placeholder.Index + placeholder.Length;
            }
            pattern.Append(Regex.Escape(pair.Key[cursor..]));
            pattern.Append('$');
            var match = Regex.Match(source, pattern.ToString(), RegexOptions.Singleline);
            if (!match.Success) continue;
            var index = 0;
            var candidate = Regex.Replace(pair.Value, @"\{[^{}]+\}", _ => index < placeholders.Count ? match.Groups[++index].Value : string.Empty);
            if (index != placeholders.Count) continue;
            translated = candidate;
            return true;
        }
        return false;
    }

    public static Task WarmSupportedPacksAsync()
    {
        var source = LoadBundledPack("ru").Keys.ToArray();
        return Task.WhenAll(TranslateManyAsync(source, "en"), TranslateManyAsync(source, "uk"));
    }

    private static IEnumerable<string[]> BuildBatches(IEnumerable<string> values)
    {
        var batch = new List<string>();
        var length = 0;
        foreach (var value in values)
        {
            if (batch.Count >= 20 || length + value.Length > 2200)
            {
                yield return batch.ToArray();
                batch.Clear();
                length = 0;
            }
            batch.Add(value);
            length += value.Length + 24;
        }
        if (batch.Count > 0) yield return batch.ToArray();
    }

    private static async Task<Dictionary<string, string>> TranslateBatchAsync(string[] batch, string language, CancellationToken token)
    {
        if (batch.Length == 1)
        {
            var translated = await TranslateOneAsync(batch[0], language, token);
            return new(StringComparer.Ordinal) { [batch[0]] = translated };
        }
        var combined = string.Join("\n", batch.Select((text, index) => $"[[[LUMA_{index:000}]]]\n{text}"));
        var translatedCombined = await TranslateOneAsync(combined, language, token);
        var markers = Regex.Matches(translatedCombined, @"\[\[\[LUMA_(\d{3})\]\]\]");
        if (markers.Count == batch.Length)
        {
            var result = new Dictionary<string, string>(StringComparer.Ordinal);
            for (var i = 0; i < markers.Count; i++)
            {
                var start = markers[i].Index + markers[i].Length;
                var end = i + 1 < markers.Count ? markers[i + 1].Index : translatedCombined.Length;
                result[batch[i]] = translatedCombined[start..end].Trim();
            }
            return result;
        }
        var fallback = await Task.WhenAll(batch.Select(async text => (Source: text, Value: await TranslateOneAsync(text, language, token))));
        return fallback.ToDictionary(x => x.Source, x => x.Value, StringComparer.Ordinal);
    }

    private static async Task<string> TranslateOneAsync(string text, string language, CancellationToken token)
    {
        await NetworkGate.WaitAsync(token);
        try
        {
            foreach (var host in new[] { "https://translate.googleapis.com", "https://translate.google.com" })
            {
                var translated = await TryGoogleAsync(host, text, language, token);
                if (!string.IsNullOrWhiteSpace(translated) && !string.Equals(translated, text, StringComparison.Ordinal)) return translated;
            }
            var fallback = await TryMyMemoryAsync(text, language, token);
            return string.IsNullOrWhiteSpace(fallback) ? text : fallback;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { return text; }
        catch (Exception ex) { App.Log(ex); return text; }
        finally { NetworkGate.Release(); }
    }

    private static async Task<string?> TryGoogleAsync(string host, string text, string language, CancellationToken token)
    {
        try
        {
            var url = host + "/translate_a/single?client=gtx&sl=ru&tl=" + Uri.EscapeDataString(language) + "&dt=t";
            using var body = new FormUrlEncodedContent(new Dictionary<string, string> { ["q"] = text });
            using var response = await Http.PostAsync(url, body, token);
            if (!response.IsSuccessStatusCode) return null;
            await using var stream = await response.Content.ReadAsStreamAsync(token);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: token);
            if (json.RootElement.ValueKind != JsonValueKind.Array || json.RootElement.GetArrayLength() == 0) return null;
            var result = new System.Text.StringBuilder();
            foreach (var segment in json.RootElement[0].EnumerateArray())
                if (segment.ValueKind == JsonValueKind.Array && segment.GetArrayLength() > 0 && segment[0].ValueKind == JsonValueKind.String)
                    result.Append(segment[0].GetString());
            return result.Length == 0 ? null : result.ToString();
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex) { App.Log(ex); return null; }
    }

    private static async Task<string?> TryMyMemoryAsync(string text, string language, CancellationToken token)
    {
        try
        {
            var url = "https://api.mymemory.translated.net/get?langpair=ru%7C" + Uri.EscapeDataString(language) + "&q=" + Uri.EscapeDataString(text);
            using var response = await Http.GetAsync(url, token);
            if (!response.IsSuccessStatusCode) return null;
            await using var stream = await response.Content.ReadAsStreamAsync(token);
            using var json = await JsonDocument.ParseAsync(stream, cancellationToken: token);
            return json.RootElement.TryGetProperty("responseData", out var data) && data.TryGetProperty("translatedText", out var value) ? value.GetString() : null;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception ex) { App.Log(ex); return null; }
    }

    private static string Normalize(string language)
    {
        if (string.IsNullOrWhiteSpace(language) || language == "auto") language = System.Globalization.CultureInfo.CurrentUICulture.Name;
        var dash = language.IndexOf('-');
        language = (dash > 0 ? language[..dash] : language).ToLowerInvariant();
        return language is "ru" or "en" or "uk" ? language : "ru";
    }

    private static string CachePath(string language) => Path.Combine(LumaState.DirectoryPath, "locales-v4", language + ".json");
    private static ConcurrentDictionary<string, string> LoadCache(string language)
    {
        try
        {
            var merged = LoadBundledPack(language);
            var path = CachePath(language);
            if (File.Exists(path))
            {
                var values = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path)) ?? [];
                foreach (var pair in values.Where(pair => !string.IsNullOrWhiteSpace(pair.Value) && !string.Equals(pair.Key, pair.Value, StringComparison.Ordinal)))
                    if (!merged.ContainsKey(pair.Key)) merged[pair.Key] = pair.Value;
            }
            return new(merged, StringComparer.Ordinal);
        }
        catch { return new(StringComparer.Ordinal); }
    }

    private static Dictionary<string, string> LoadBundledPack(string language)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"Luma.Assets.Locales.{language}.json");
        if (stream is null) return result;
        var values = JsonSerializer.Deserialize<Dictionary<string, string>>(stream) ?? [];
        foreach (var pair in values.Where(pair => !string.IsNullOrWhiteSpace(pair.Value))) result[pair.Key] = pair.Value;
        return result;
    }
    private static async Task SaveCacheAsync(string language, ConcurrentDictionary<string, string> cache)
    {
        await SaveGate.WaitAsync();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(CachePath(language))!);
            await File.WriteAllTextAsync(CachePath(language), JsonSerializer.Serialize(cache.OrderBy(x => x.Key).ToDictionary(x => x.Key, x => x.Value)));
        }
        catch (Exception ex) { App.Log(ex); }
        finally { SaveGate.Release(); }
    }
}
