using System.Linq;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Luma;

public sealed class AssistantImage
{
    public string Base64 { get; set; } = "";
    public string MimeType { get; set; } = "image/jpeg";
    public string Name { get; set; } = "image";
}

/// <summary>One conversation turn. Images are raw base64 without a data: prefix.</summary>
public sealed class AssistantMessage
{
    public string Role { get; set; } = "user";
    public string Text { get; set; } = "";
    public List<AssistantImage> Images { get; set; } = [];
}

public sealed class AssistantQuota
{
    public int Limit { get; init; }
    public int Used { get; init; }
    public int Remaining { get; init; }
    public bool Unlimited { get; init; }
}

/// <summary>
/// Authenticated streaming client for the Supabase relay. Provider credentials and quota logic
/// stay server-side; the desktop app only sends the signed-in user's short-lived JWT.
/// </summary>
public static class AssistantClient
{
    private static readonly HttpClient Http = Create();

    private static HttpClient Create()
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
            ConnectTimeout = TimeSpan.FromSeconds(10),
            EnableMultipleHttp2Connections = true,
        };
        return new HttpClient(handler)
        {
            Timeout = TimeSpan.FromMinutes(5),
            DefaultRequestVersion = System.Net.HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower,
        };
    }

    private static object BuildContent(AssistantMessage message)
    {
        if (message.Images.Count == 0) return message.Text;
        var parts = new List<object> { new { type = "text", text = message.Text } };
        foreach (var image in message.Images.Where(image => !string.IsNullOrWhiteSpace(image.Base64)).Take(3))
        {
            var mime = image.MimeType is "image/png" or "image/webp" or "image/gif" ? image.MimeType : "image/jpeg";
            parts.Add(new { type = "image_url", image_url = new { url = $"data:{mime};base64,{image.Base64}" } });
        }
        return parts.ToArray();
    }

    public static async Task<AssistantQuota?> GetQuotaAsync(string accessToken, CancellationToken token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Authentication.SupabaseOptions.ProjectUrl + "/rest/v1/rpc/get_my_assistant_quota")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("apikey", Authentication.SupabaseOptions.PublishableKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var response = await Http.SendAsync(request, token);
        if (!response.IsSuccessStatusCode) return null;
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(token));
        var root = doc.RootElement;
        int Number(string name, int fallback = 0) => root.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : fallback;
        return new AssistantQuota
        {
            Limit = Number("limit", 15), Used = Number("used"), Remaining = Number("remaining", 15),
            Unlimited = root.TryGetProperty("unlimited", out var unlimited) && unlimited.ValueKind == JsonValueKind.True,
        };
    }

    public static async Task<AssistantQuota?> StreamAsync(
        string accessToken,
        string tier,
        IEnumerable<AssistantMessage> messages,
        Func<string, Task> onDelta,
        CancellationToken token)
    {
        var payload = new
        {
            model = tier is "pro" ? "pro" : "fast",
            stream = true,
            messages = messages.Select(message => new { role = message.Role, content = BuildContent(message) }).ToArray(),
        };
        using var request = new HttpRequestMessage(HttpMethod.Post, Authentication.SupabaseOptions.ProjectUrl + "/functions/v1/luma-assistant")
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };
        request.Headers.TryAddWithoutValidation("apikey", Authentication.SupabaseOptions.PublishableKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        request.Headers.Accept.ParseAdd("text/event-stream");

        using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(token);
            throw new InvalidOperationException(Explain((int)response.StatusCode, body));
        }

        var quota = ReadQuota(response);
        using var stream = await response.Content.ReadAsStreamAsync(token);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (!reader.EndOfStream)
        {
            token.ThrowIfCancellationRequested();
            using var idleCts = CancellationTokenSource.CreateLinkedTokenSource(token);
            idleCts.CancelAfter(TimeSpan.FromSeconds(25));
            string? line;
            try
            {
                line = await reader.ReadLineAsync(idleCts.Token);
            }
            catch (OperationCanceledException) when (!token.IsCancellationRequested)
            {
                throw new TimeoutException("Время ожидания ответа ассистента истекло (таймаут соединения).");
            }
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:", StringComparison.Ordinal)) continue;
            var data = line[5..].Trim();
            if (data == "[DONE]") break;
            string? delta = null;
            try
            {
                using var doc = JsonDocument.Parse(data);
                if (doc.RootElement.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0
                    && choices[0].TryGetProperty("delta", out var deltaNode)
                    && deltaNode.TryGetProperty("content", out var content)
                    && content.ValueKind == JsonValueKind.String)
                    delta = content.GetString();
            }
            catch { continue; }
            if (!string.IsNullOrEmpty(delta)) await onDelta(delta);
        }
        return quota;
    }

    private static AssistantQuota? ReadQuota(HttpResponseMessage response)
    {
        static int Header(HttpResponseMessage response, string name)
            => response.Headers.TryGetValues(name, out var values) && int.TryParse(values.FirstOrDefault(), out var value) ? value : 0;
        if (!response.Headers.TryGetValues("X-Luma-Quota-Limit", out _)) return null;
        var unlimited = response.Headers.TryGetValues("X-Luma-Quota-Unlimited", out var values)
            && string.Equals(values.FirstOrDefault(), "true", StringComparison.OrdinalIgnoreCase);
        return new AssistantQuota
        {
            Limit = Header(response, "X-Luma-Quota-Limit"),
            Used = Header(response, "X-Luma-Quota-Used"),
            Remaining = Header(response, "X-Luma-Quota-Remaining"),
            Unlimited = unlimited,
        };
    }

    private static string Explain(int status, string body)
    {
        string detail = "";
        string code = "";
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out var error)) code = error.GetString() ?? "";
            if (doc.RootElement.TryGetProperty("message", out var message)) detail = message.GetString() ?? "";
        }
        catch { detail = body.Length > 300 ? body[..300] : body; }
        var hint = (status, code) switch
        {
            (401, _) => "Войдите в аккаунт Luma, чтобы пользоваться LumaAI.",
            (429, "daily_limit_reached") => "Сегодняшний лимит LumaAI исчерпан. Новый лимит откроется после полуночи UTC.",
            (413, _) => "Изображение или контекст слишком большие.",
            (422, _) => "Выбранная модель не смогла обработать изображение.",
            (>= 500, _) => "LumaAI временно недоступен.",
            _ => "Не удалось выполнить запрос LumaAI.",
        };
        return string.IsNullOrWhiteSpace(detail) ? hint : $"{hint} {detail}";
    }
}

internal static class AssistantChoices
{
    public const string Marker = "%%CHOICES%%";
    public readonly record struct Choice(string Label, string Prompt);

    public const string Protocol = """

Если вопрос требует предпочтения пользователя и есть 2–5 понятных вариантов ответа, сначала дай короткое пояснение,
а в самом конце добавь блок %%CHOICES%%. Если можно уверенно ответить самостоятельно — НЕ добавляй блок.
Формат каждой строки блока:
choice | короткая надпись на кнопке | точный текст, который надо отправить тебе после нажатия
Максимум 5 строк. Надписи должны быть самостоятельными и короче 38 символов.
Не предлагай варианты для простых фактических вопросов, перевода, пересказа или очевидного действия.
После блока ничего не пиши.
""";

    public static List<Choice> Parse(string answer)
    {
        var result = new List<Choice>();
        var cut = answer.LastIndexOf(Marker, StringComparison.Ordinal);
        if (cut < 0) return result;
        foreach (var raw in answer[(cut + Marker.Length)..].Split(["\r\n", "\n", "\r"], StringSplitOptions.None))
        {
            var line = raw.Trim().TrimStart('-', '*', ' ');
            if (!line.StartsWith("choice", StringComparison.OrdinalIgnoreCase)) continue;
            var parts = line.Split('|', 3, StringSplitOptions.TrimEntries);
            if (parts.Length != 3 || parts[1].Length is < 1 or > 60 || parts[2].Length < 1) continue;
            if (result.Any(choice => string.Equals(choice.Label, parts[1], StringComparison.OrdinalIgnoreCase))) continue;
            result.Add(new Choice(parts[1], parts[2]));
            if (result.Count == 5) break;
        }
        return result;
    }
}

internal static class AssistantActions
{
    public const string Marker = "%%LUMA%%";

    private static readonly string[] LineBreaks = ["\r\n", "\n", "\r"];
    private static readonly char[] UrlTrimChars = ['<', '>', '"', '\'', '`', ',', ';', ')', '.', ' '];

    public const string Protocol = """

Ты умеешь сам открывать сайты во вкладках.
Если пользователь просит что-то открыть, включить, запустить или ссылается на пункт из твоего прошлого списка
(«открой топ 2», «второе давай», «включи первое», «открой 1, 2 и 3») — в САМОМ КОНЦЕ ответа добавь блок %%LUMA%%.

В блоке два вида строк:

find <тип> | <точное название>
open <полный URL> | <короткая подпись>

ГЛАВНОЕ правило: для аниме, фильмов, сериалов, видео и музыки всегда пиши find, а НЕ open.
Адреса страниц с тайтлами ты не знаешь и никогда их не выдумываешь — браузер сам найдёт страницу тайтла и откроет её.

Типы для find:
- anime — аниме (сайт yummyanime.tv)
- movie — фильмы и сериалы (сайт ag.gidonline.fun)
- video — ролики, каналы, блогеры, клипы (YouTube)
- music — треки и артисты (SoundCloud)

В find после черты пиши ТОЛЬКО чистое название тайтла без номеров, кавычек, года, слов «аниме»/«смотреть» и пояснений.
Название бери ровно то, что стояло в твоём списке, и пиши его по-русски, если русское название существует.

open используй только для главных страниц известных сервисов («открой саундклауд» → https://soundcloud.com/ ,
«открой ютуб» → https://www.youtube.com/ , телеграм, гитхаб, википедия и т.д.) или для ссылки,
которую дал сам пользователь. Для обычного веб-поиска — find web | <запрос>.

Правила блока:
- Каждая строка — одно действие, максимум 4 строки.
- После блока не пиши ничего. До блока — короткий ответ без голых ссылок.
- Блок ставь только когда действительно надо что-то открыть. Просто совет или список — без блока.
- Помни свой предыдущий список: номер от пользователя — это пункт из него.

Пример. Список аниме был: 1. Атака титанов  2. Стальной алхимик  3. Смертьная тетрадь
Пользователь: «открой топ 3»
Открываю «Смертную тетрадь».
%%LUMA%%
find anime | Смертная тетрадь
""";

    /// <summary>
    /// Kind is "open" for a ready URL, or a catalogue kind (anime / movie / video / music / web)
    /// whose page address the browser has to find on the web first.
    /// </summary>
    public readonly record struct Target(string Kind, string Value, string Label);

    /// <summary>Everything the user should actually read, without the machine block.</summary>
    public static string StripDirectives(string answer)
    {
        var actionCut = answer.IndexOf(Marker, StringComparison.Ordinal);
        var choiceCut = answer.IndexOf(AssistantChoices.Marker, StringComparison.Ordinal);
        var cut = actionCut < 0 ? choiceCut : choiceCut < 0 ? actionCut : Math.Min(actionCut, choiceCut);
        return (cut >= 0 ? answer[..cut] : answer).TrimEnd();
    }

    private static readonly char[] QueryTrimChars = ['"', '\'', '«', '»', '—', '-', '*', '.', ',', ':', ';', '!', '?', ' '];

    public static List<Target> Parse(string answer)
    {
        var result = new List<Target>();
        var cut = answer.LastIndexOf(Marker, StringComparison.Ordinal);
        if (cut < 0) return result;

        foreach (var rawLine in answer[(cut + Marker.Length)..].Split(LineBreaks, StringSplitOptions.None))
        {
            var line = rawLine.Trim().TrimStart('-', '*', ' ').Trim();
            if (line.Length == 0) continue;

            if (line.StartsWith("find", StringComparison.OrdinalIgnoreCase))
            {
                var rest = line[4..].Trim();
                var bar = rest.IndexOf('|');
                if (bar < 0) continue;
                var kind = NormalizeKind(rest[..bar].Trim());
                var tail = rest[(bar + 1)..];
                var second = tail.IndexOf('|');
                var label = second >= 0 ? tail[(second + 1)..].Trim() : "";
                var query = (second >= 0 ? tail[..second] : tail).Trim().Trim(QueryTrimChars).Trim();
                if (query.Length == 0) continue;
                if (result.Any(t => t.Kind == kind && string.Equals(t.Value, query, StringComparison.OrdinalIgnoreCase))) continue;
                result.Add(new Target(kind, query, label.Length > 0 ? label : query));
            }
            else
            {
                if (line.StartsWith("open", StringComparison.OrdinalIgnoreCase)) line = line[4..].Trim();

                var label = "";
                var bar = line.IndexOf('|');
                if (bar >= 0) { label = line[(bar + 1)..].Trim(); line = line[..bar].Trim(); }

                var url = line.Trim(UrlTrimChars).Trim();
                if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed)) continue;
                if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps) continue;
                if (result.Any(t => t.Kind == "open" && string.Equals(t.Value, parsed.ToString(), StringComparison.OrdinalIgnoreCase))) continue;

                // The model still sometimes hands over a catalogue search address (often with a
                // mangled query). Treat that as a title lookup instead of opening it as is.
                var downgraded = AsLookup(parsed, label);
                result.Add(downgraded ?? new Target("open", parsed.ToString(), label));
            }

            if (result.Count == 4) break;
        }
        return result;
    }

    /// <summary>Turns "https://yummyanime.tv/search?text=..." into a proper title lookup.</summary>
    private static Target? AsLookup(Uri uri, string label)
    {
        var host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
        var kind = host switch
        {
            "yummyanime.tv" => "anime",
            "ag.gidonline.fun" => "movie",
            "youtube.com" => "video",
            "soundcloud.com" => "music",
            _ => null,
        };
        if (kind is null) return null;
        if (!SiteResolver.IsSearchPage(uri.ToString())) return null;

        var query = QueryValue(uri.Query);
        if (string.IsNullOrWhiteSpace(query)) query = label;
        query = query.Trim().Trim(QueryTrimChars).Trim();
        if (query.Length == 0) return null;
        return new Target(kind, query, label.Length > 0 ? label : query);
    }

    /// <summary>Reads the search term out of ?text= / ?q= / ?search_query= / ?query=.</summary>
    private static string QueryValue(string rawQuery)
    {
        foreach (var pair in rawQuery.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            if (eq <= 0) continue;
            var name = pair[..eq].ToLowerInvariant();
            if (name is not ("text" or "q" or "search_query" or "query")) continue;
            try { return Uri.UnescapeDataString(pair[(eq + 1)..].Replace('+', ' ')); }
            catch { return ""; }
        }
        return "";
    }

    private static string NormalizeKind(string kind)
    {
        kind = kind.ToLowerInvariant();
        if (kind.Contains("anim") || kind.Contains("аним")) return "anime";
        if (kind.Contains("movie") || kind.Contains("serial") || kind.Contains("film") || kind.Contains("фильм") || kind.Contains("сериал")) return "movie";
        if (kind.Contains("video") || kind.Contains("youtube") || kind.Contains("видео")) return "video";
        if (kind.Contains("music") || kind.Contains("sound") || kind.Contains("муз")) return "music";
        return "web";
    }

    public static string PrettyHost(string url)
    {
        try
        {
            var host = new Uri(url).Host;
            return host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;
        }
        catch { return url; }
    }
}

/// <summary>
/// Turns "anime + Death Note" into the real address of that title on the catalogue site.
/// The model is bad at guessing deep links and site search pages often answer with garbage, so
/// Luma asks a normal web search engine for "site:yummyanime.tv &lt;title&gt;" and opens the first
/// result that is an actual title page. The site's own search page stays as a fallback only.
/// </summary>
internal static class SiteResolver
{
    private static readonly HttpClient Http = CreateHttp();
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, string> Cache = new(StringComparer.OrdinalIgnoreCase);

    private static HttpClient CreateHttp()
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = true, AutomaticDecompression = System.Net.DecompressionMethods.All };
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(12) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/140.0.0.0 Safari/537.36");
        http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("ru-RU,ru;q=0.9,en;q=0.8");
        return http;
    }

    public static string? Domain(string kind) => kind switch
    {
        "anime" => "yummyanime.tv",
        "movie" => "ag.gidonline.fun",
        "video" => "youtube.com",
        "music" => "soundcloud.com",
        _ => null,
    };

    /// <summary>Where to go when nothing better was found: the site's own search page.</summary>
    public static string Fallback(string kind, string query)
    {
        var q = Uri.EscapeDataString(query);
        return kind switch
        {
            // yummyanime runs on DLE: its real search is index.php?do=search, the pretty
            // /search page is a stub that just replays whatever was typed there last time.
            "anime" => "https://yummyanime.tv/index.php?do=search&subaction=search&search_start=0&full_search=0&story=" + q,
            "movie" => "https://ag.gidonline.fun/search/" + q,
            "video" => "https://www.youtube.com/results?search_query=" + q,
            "music" => "https://soundcloud.com/search?q=" + q,
            _ => "https://www.google.com/search?q=" + q,
        };
    }

    /// <summary>True for a site search page, i.e. a place the user did not actually ask for.</summary>
    public static bool IsSearchPage(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        var path = uri.AbsolutePath.Trim('/').ToLowerInvariant();
        return path.StartsWith("search") || path.StartsWith("results")
            || path.StartsWith("index.php")
            || uri.Query.Contains("do=search", StringComparison.Ordinal)
            || uri.Query.Contains("story=", StringComparison.Ordinal)
            || uri.Query.Contains("search_query=", StringComparison.Ordinal)
            || uri.Query.Contains("text=", StringComparison.Ordinal)
            || uri.Query.Contains("q=", StringComparison.Ordinal);
    }

    public static async Task<string> ResolveAsync(string kind, string query, CancellationToken token = default)
    {
        var domain = Domain(kind);
        if (domain is null || string.IsNullOrWhiteSpace(query)) return Fallback(kind, query);

        var cacheKey = kind + "\u0000" + query;
        if (Cache.TryGetValue(cacheKey, out var known)) return known;

        // First ask the catalogue itself: its own index knows the title page exactly, while a
        // web search engine may be throttled, localised or simply out of date.
        foreach (var endpoint in SiteSearches(kind, query))
        {
            try
            {
                var html = await Http.GetStringAsync(endpoint, token);
                var hit = FirstLink(html, domain);
                if (hit is not null) { Cache[cacheKey] = hit; return hit; }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { App.Log(ex); }
        }

        foreach (var engine in Engines(domain, query, kind))
        {
            try
            {
                var html = await Http.GetStringAsync(engine, token);
                var hit = FirstLink(html, domain);
                if (hit is not null) { Cache[cacheKey] = hit; return hit; }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex) { App.Log(ex); }
        }
        return Fallback(kind, query);
    }

    /// <summary>Search endpoints served by the catalogue sites themselves.</summary>
    private static IEnumerable<string> SiteSearches(string kind, string query)
    {
        var q = Uri.EscapeDataString(query);
        if (kind == "anime")
        {
            yield return "https://yummyanime.tv/index.php?do=search&subaction=search&search_start=0&full_search=0&story=" + q;
        }
        else if (kind == "movie")
        {
            yield return "https://ag.gidonline.fun/search/" + q;
            yield return "https://ag.gidonline.fun/index.php?do=search&subaction=search&story=" + q;
        }
    }

    private static IEnumerable<string> Engines(string domain, string query, string kind)
    {
        var hint = kind switch
        {
            "anime" => " аниме смотреть",
            "movie" => " смотреть онлайн",
            _ => "",
        };
        var strict = Uri.EscapeDataString("site:" + domain + " " + query);
        var loose = Uri.EscapeDataString("site:" + domain + " " + query + hint);
        yield return "https://html.duckduckgo.com/html/?q=" + strict;
        yield return "https://lite.duckduckgo.com/lite/?q=" + loose;
        yield return "https://www.bing.com/search?q=" + strict;
    }

    /// <summary>Title pages on yummyanime always look like /66-stalnoj-alhimik-bratstvo-q1.html</summary>
    private static readonly Regex YummyTitlePath = new(@"^\d+-[^/]+\.html$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>First search result that lives on the catalogue site and is a real content page.</summary>
    private static string? FirstLink(string html, string domain)
    {
        var candidates = new List<string>();
        foreach (Match m in Regex.Matches(html, "uddg=([^&\"'<> ]+)"))
            candidates.Add(Uri.UnescapeDataString(m.Groups[1].Value));
        foreach (Match m in Regex.Matches(html, "href=\"(https?://[^\"]+)\""))
            candidates.Add(System.Net.WebUtility.HtmlDecode(m.Groups[1].Value));

        foreach (var candidate in candidates)
        {
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out var uri)) continue;
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) continue;
            var host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
            if (!host.Equals(domain, StringComparison.OrdinalIgnoreCase) && !host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase)) continue;

            var path = uri.AbsolutePath.Trim('/');
            var lowerPath = path.ToLowerInvariant();
            if (lowerPath.Length == 0 || lowerPath.EndsWith("rss.xml") || lowerPath.EndsWith(".xml")
                || lowerPath.Contains("sitemap") || lowerPath is "feed" or "rss"
                || Regex.IsMatch(lowerPath, @"(^|/)(feed|rss|category|categories|tag|tags|genre|genres|news|archive|archives|search)(/|$)")) continue;
            var isYoutubeWatch = domain.Contains("youtube") && (path.StartsWith("watch", StringComparison.OrdinalIgnoreCase) || path.StartsWith("shorts", StringComparison.OrdinalIgnoreCase) || path.StartsWith("@", StringComparison.Ordinal) || path.StartsWith("channel", StringComparison.OrdinalIgnoreCase));
            if (domain.Equals("yummyanime.tv", StringComparison.OrdinalIgnoreCase) && !YummyTitlePath.IsMatch(path)) continue;
            if (path.StartsWith("index.php", StringComparison.OrdinalIgnoreCase)) continue;
            if (uri.Query.Contains("do=search", StringComparison.Ordinal) || uri.Query.Contains("story=", StringComparison.Ordinal)) continue;

            if (!isYoutubeWatch)
            {
                if (path.Length < 3) continue;
                if (path.StartsWith("search", StringComparison.OrdinalIgnoreCase) || path.StartsWith("results", StringComparison.OrdinalIgnoreCase)) continue;
                if (uri.Query.Contains("q=", StringComparison.Ordinal) || uri.Query.Contains("text=", StringComparison.Ordinal) || uri.Query.Contains("search_query=", StringComparison.Ordinal)) continue;
            }
            return uri.ToString();
        }
        return null;
    }
}
