using System.Text.RegularExpressions;

namespace Luma;

/// <summary>
/// Lightweight heuristic that decides whether a user's question to LumaAI
/// requires a silent background web search to answer factually and up-to-date.
///
/// The goal: inject fresh web snippets into the system context so the AI appears
/// to already know the answer — the user never sees a search happening.
/// </summary>
internal static class AssistantWebSearch
{
    // ─── Patterns that strongly suggest a factual / person / event lookup ───

    private static readonly Regex[] FactSignals =
    [
        // "кто такой X", "что за X", "что такое X"
        new(@"\b(кто\s+так(ой|ая|ие)|что\s+за|что\s+такое|what\s+is|who\s+is)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        // Current events / news
        new(@"\b(новост|последн|сегодня|вчера|сейчас|текущ|нынешн|недавно|latest|news|today|recently)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        // Release dates, versions, prices
        new(@"\b(когда\s+выйдет|дата\s+выхода|релиз|стоит\s+сколько|цена|price|release\s+date|when\s+does)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        // Explicit years 2024-2029 suggest recent/future events
        new(@"\b(202[4-9]|2030)\b", RegexOptions.Compiled),
        // Proper-noun-heavy questions: e.g. a word with a capital letter after a question word
        new(@"^[^.?!]*\?\s*$", RegexOptions.Compiled),      // question ending with "?"
        // Direct "what happened / why did / how does"
        new(@"\b(что\s+произошло|почему|как\s+сделать|how\s+to|why\s+did|explain)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        // "расскажи о", "расскажи про"
        new(@"\b(расскажи\s+(о|про|об)|tell\s+me\s+about)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    ];

    // ─── Patterns that indicate the question is self-contained (no web needed) ───

    private static readonly Regex[] SelfContained =
    [
        // Code / programming
        new(@"\b(функц|метод|класс|код|алгоритм|function|method|class|code|algorithm|script|regex)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        // Translation requests
        new(@"\b(переведи|перевод|translate|translation)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        // Summary of the current page (context is already injected)
        new(@"\b(эт[аоуй]?\s+страниц|кратко\s+о\s+страниц|суммаризируй|summarize\s+this|summarize\s+the\s+page)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        // Math / formulas
        new(@"[\d]+\s*[+\-*/^=]\s*[\d]+", RegexOptions.Compiled),
        // Very short questions (single word, likely a follow-up in context)
        // handled below by length check
    ];

    /// <summary>
    /// Returns true when the assistant should perform a silent background
    /// web search before replying. The search result is injected into the
    /// system prompt — never shown to the user.
    /// </summary>
    public static bool ShouldSearch(string question)
    {
        if (string.IsNullOrWhiteSpace(question)) return false;

        var q = question.Trim();

        // Very short inputs are usually follow-ups or commands — skip search.
        if (q.Length < 8) return false;

        // If the question looks clearly self-contained, skip search.
        foreach (var rx in SelfContained)
            if (rx.IsMatch(q)) return false;

        // If any factual signal fires, do a search.
        foreach (var rx in FactSignals)
            if (rx.IsMatch(q)) return true;

        // Fallback heuristic: the question is long enough and contains a word
        // that looks like a proper noun (starts with a capital, ≥4 chars,
        // not the first word in the sentence).
        var words = q.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length >= 3)
        {
            // Check words after the first one for possible proper nouns
            for (var i = 1; i < words.Length; i++)
            {
                var w = words[i];
                if (w.Length >= 4 && char.IsUpper(w[0]) && !w.All(char.IsUpper))
                    return true;
            }
        }

        return false;
    }
}
