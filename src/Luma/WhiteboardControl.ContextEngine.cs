using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Luma;

public partial class WhiteboardControl
{
    // =========================================================================
    // ІНТЕЛЕКТУАЛЬНИЙ КОНТЕКСТНИЙ ДВИГУН УКРАЇНСЬКОЇ КАЛІГРАФІЇ ТА ШІ-ПРОПИСУ
    // =========================================================================

    private static readonly HashSet<string> ContextQuestionWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "як", "що", "де", "куди", "звідки", "чому", "навіщо", "скільки", "хто", "кому", "ким",
        "чим", "чий", "чия", "чиє", "чиї", "який", "яка", "яке", "які", "коли"
    };

    private static readonly HashSet<string> ContextCommonWords = new(StringComparer.OrdinalIgnoreCase)
    {
        // Привітання та ввічливість
        "привіт", "добрий", "доброго", "день", "дня", "ранок", "ранку", "вечір", "вечора", "ніч",
        "добраніч", "дякую", "дякуємо", "будь", "ласка", "прошу", "побачення", "зустрічі", "бувай",
        "бувайте", "всього", "найкращого", "успіхів", "вітання", "вітаємо", "ласкаво", "просимо",

        // Питання, займенники та розмовні слова
        "як", "що", "де", "куди", "звідки", "чому", "навіщо", "хто", "скільки", "коли",
        "твої", "ваші", "справи", "мене", "тебе", "вас", "нас", "його", "її", "їх", "звати",
        "добре", "чудово", "відмінно", "гарно", "погано", "так", "ні", "дуже", "багато", "мало",
        "все", "всі", "всього", "завжди", "ніколи", "також", "тому", "сьогодні", "завтра", "вчора",
        "зараз", "тепер", "потім", "вже", "ще", "тільки", "швидко", "повільно", "правильно",

        // Шкільна та навчальна лексика
        "школа", "школі", "урок", "уроку", "клас", "класна", "домашня", "робота", "роботи",
        "тема", "теми", "завдання", "вправа", "вправи", "приклад", "приклади", "задача", "задачі",
        "відповідь", "розв'язання", "розв'язок", "розв'язати", "варіант", "тест", "зошит", "аркуш",
        "дошка", "лінійка", "олівець", "ручка", "пропис", "прописи", "буква", "літера", "слово",
        "слова", "речення", "текст", "правило", "правила", "математика", "мова", "українська",
        "література", "читання", "фізика", "хімія", "біологія", "географія", "історія",

        // Математичні терміни
        "число", "цифра", "сума", "різниця", "добуток", "частка", "дорівнює", "плюс", "мінус",
        "більше", "менше", "точка", "пряма", "відрізок", "кут", "трикутник", "квадрат", "коло",
        "круг", "довжина", "ширина", "площа", "периметр",

        // Люди, повсякденність та дієслова
        "буде", "було", "бути", "є", "можу", "хочу", "знаю", "думаю", "розумію", "читаю", "пишу",
        "вчимо", "робимо", "маю", "маєш", "має", "маємо", "жити", "бачити", "чути", "друг",
        "подруга", "друзі", "людина", "люди", "діти", "учень", "учениця", "вчитель", "вчителька",
        "батьки", "мама", "тато", "сім'я", "родина", "місто", "дім", "село", "Україна", "країна",
        "час", "життя", "світ", "пам'ять", "ім'я", "п'ять", "дев'ять", "м'яч", "пір'я", "в'ється",
        "солов'ї", "об'єкт", "зв'язок"
    };

    private static readonly string[] ContextKnownPhrases = new[]
    {
        "як твої справи", "як ваші справи", "як справи", "привіт як", "як ся маєш", "що нового",
        "як життя", "що робиш", "що робите",
        "добрий день", "доброго дня", "добрий ранок", "доброго ранку", "добрий вечір", "доброго вечора",
        "спокійної ночі", "на добраніч",
        "до побачення", "до зустрічі", "до скорого",
        "дуже дякую", "щиро дякую", "велике дякую", "дякую тобі", "дякую вам",
        "будь ласка", "всього найкращого", "гарного дня", "чудового дня",
        "домашня робота", "класна робота", "самостійна робота", "контрольна робота",
        "тема уроку", "розв'язання задачі", "відповідь на питання",
        "ласкаво просимо", "радий тебе бачити", "рада тебе бачити", "радий вас бачити", "рада вас бачити",
        "приємно познайомитися"
    };

    private static readonly Dictionary<string, string> ContextWordDistortions = new(StringComparer.OrdinalIgnoreCase)
    {
        // Спотворення фраз "як справи", "як твої справи"
        { "спрви", "справи" },
        { "справы", "справи" },
        { "твоі", "твої" },
        { "твой", "твої" },
        { "ткоі", "твої" },
        { "ткої", "твої" },
        { "спряви", "справи" },
        { "справе", "справи" },
        { "српави", "справи" },

        // Привітання та ввічливість
        { "превіт", "привіт" },
        { "привит", "привіт" },
        { "прівет", "привіт" },
        { "привєт", "привіт" },
        { "првіт", "привіт" },
        { "привііт", "привіт" },
        { "дякуу", "дякую" },
        { "декую", "дякую" },
        { "дякуюю", "дякую" },
        { "будьласка", "будь ласка" },
        { "бутьласка", "будь ласка" },
        { "буть ласка", "будь ласка" },
        { "пожалуйста", "будь ласка" },
        { "допобачення", "до побачення" },
        { "допобаченя", "до побачення" },
        { "до побаченя", "до побачення" },
        { "досвидания", "до побачення" },
        { "досвиданья", "до побачення" },
        { "бувайй", "бувай" },
        { "здравствуй", "привіт" },
        { "здравствуйте", "добрий день" },

        // Шкільні типові помилки / русизми
        { "класнаробота", "класна робота" },
        { "класная робота", "класна робота" },
        { "класна работа", "класна робота" },
        { "классная работа", "класна робота" },
        { "домашняробота", "домашня робота" },
        { "домашняя работа", "домашня робота" },
        { "домашня работа", "домашня робота" },
        { "роботаа", "робота" },
        { "вправаа", "вправа" },
        { "розвязання", "розв'язання" },
        { "розвязок", "розв'язок" },
        { "розвязати", "розв'язати" },

        // Слова з апострофом
        { "память", "пам'ять" },
        { "імя", "ім'я" },
        { "сімя", "сім'я" },
        { "пять", "п'ять" },
        { "девять", "дев'ять" },
        { "мяч", "м'яч" },
        { "піря", "пір'я" },
        { "вється", "в'ється" },
        { "солові", "солов'ї" },
        { "соловї", "солов'ї" },
        { "обєкт", "об'єкт" },
        { "звязок", "зв'язок" },

        // Загальні спотворення
        { "сьогодня", "сьогодні" },
        { "сегодня", "сьогодні" },
        { "харашо", "добре" },
        { "хорошо", "добре" },
        { "чюдово", "чудово" },
        { "што", "що" },
        { "шо", "що" },
        { "чомусь", "чомусь" },
        { "пака", "бувай" }
    };

    private static string SelectBestContextualCandidate(List<string> candidates)
    {
        if (candidates == null || candidates.Count == 0) return string.Empty;
        if (candidates.Count == 1) return candidates[0];

        string bestCandidate = candidates[0];
        double bestScore = double.MinValue;

        for (int i = 0; i < candidates.Count; i++)
        {
            string cand = candidates[i];
            if (string.IsNullOrWhiteSpace(cand)) continue;

            double score = EvaluateCandidateQuality(cand);
            // Приоритет исходного ранжирования Google при близком качестве
            score += (candidates.Count - i) * 1.5;

            if (score > bestScore)
            {
                bestScore = score;
                bestCandidate = cand;
            }
        }

        return bestCandidate;
    }

    private static double EvaluateCandidateQuality(string candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate)) return -100;
        double score = 0;

        string text = candidate.Trim();
        string lower = text.ToLowerInvariant();

        // 1. Перевірка стійких українських виразів та словосполучень (+55 балів)
        foreach (var phrase in ContextKnownPhrases)
        {
            if (lower.Contains(phrase))
            {
                score += 55.0;
            }
        }

        // 2. Оцінка слів за українським словником
        var words = Regex.Matches(lower, @"\b[\p{L}\p{Nd}\'\’\-]+\b")
            .Select(m => m.Value)
            .ToList();

        if (words.Count == 0) return -10;

        foreach (var word in words)
        {
            if (ContextCommonWords.Contains(word))
            {
                score += 15.0;
            }
            else if (ContextWordDistortions.ContainsKey(word))
            {
                score -= 8.0;
            }
            else if (word.Contains('ы') || word.Contains('э') || word.Contains('ъ') || word.Contains('ё'))
            {
                // Штраф за неукраїнські літери в кандидаті
                score -= 15.0;
            }
            else
            {
                score -= 3.0;
            }
        }

        // 3. Питальний контекст
        string firstWord = words.FirstOrDefault() ?? "";
        if (ContextQuestionWords.Contains(firstWord))
        {
            if (text.EndsWith("?")) score += 15.0;
            else if (text.EndsWith(".")) score -= 5.0;
        }

        // 4. Регістр першої літери
        if (char.IsUpper(text[0])) score += 5.0;

        return score;
    }

    public static string PostProcessContextualText(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;

        // Нормалізація пробілів
        text = Regex.Replace(text, @"\s+", " ").Trim();

        // 1. Автоматична заміна неукраїнських літер при OCR розпізнаванні
        text = text.Replace('ы', 'и').Replace('Ы', 'И')
                   .Replace('ъ', '\'').Replace('Ъ', '\'')
                   .Replace('э', 'е').Replace('Э', 'Е')
                   .Replace('ё', 'е').Replace('Ё', 'Е');

        // 2. Контекстна заміна частих рукописних спотворень фраз
        text = Regex.Replace(text, @"\b[яЯ][кК]\s+[тТ][вВ][оО][ііеєий]\s+[сС][пП][рР][аАвВ][вВ][иіеы]\b", "як твої справи", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\b[яЯ][кК]\s+[сС][пП][рР][аАвВ][вВ][иіеы]\b", "як справи", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\b[яЯ][кК]\s+[вВ][аА][шШ][іи]\s+[сС][пП][рР][аАвВ][вВ][иіеы]\b", "як ваші справи", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\b[дД][оО]\s*[пП][оО][бБ][аА][чЧ][еЕ][нН][яЯіІеЕ]\b", "до побачення", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\b[дД][оО][мМ][аА][шШ][нН][яЯ]\s*[рР][оО][бБ][оО][тТ][аА]\b", "домашня робота", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"\b[кК][лЛ][аА][сС]+[нН][аА]\s*[рР][оО][бБ][оО][тТ][аА]\b", "класна робота", RegexOptions.IgnoreCase);

        // 3. Усунення спотворень на рівні окремих слів
        var words = text.Split(' ');
        for (int i = 0; i < words.Length; i++)
        {
            string rawWord = words[i];
            string cleanWord = rawWord.Trim(',', '.', '!', '?', ';', ':', '"', '\'');
            string lowerClean = cleanWord.ToLowerInvariant();

            if (ContextWordDistortions.TryGetValue(lowerClean, out string? fixedWord))
            {
                bool isUpper = cleanWord.Length > 0 && char.IsUpper(cleanWord[0]);
                string replacement = isUpper ? char.ToUpper(fixedWord[0]) + fixedWord.Substring(1) : fixedWord;
                words[i] = rawWord.Replace(cleanWord, replacement);
            }
            else if (cleanWord.Length >= 4 && !ContextCommonWords.Contains(lowerClean))
            {
                // Пошук найближчого українського словникового слова на відстані Левенштейна = 1
                string? nearest = FindNearestContextWord(lowerClean);
                if (nearest != null)
                {
                    bool isUpper = cleanWord.Length > 0 && char.IsUpper(cleanWord[0]);
                    string replacement = isUpper ? char.ToUpper(nearest[0]) + nearest.Substring(1) : nearest;
                    words[i] = rawWord.Replace(cleanWord, replacement);
                }
            }
        }
        text = string.Join(" ", words);

        // 4. Нормалізація розділових знаків: прибираємо зайві пробіли перед знаками
        text = Regex.Replace(text, @"\s+([?.!,:;])", "$1");

        // 5. Перша літера речення велика
        if (text.Length > 0 && char.IsLower(text[0]))
        {
            text = char.ToUpper(text[0]) + text.Substring(1);
        }

        // 6. Знак питання для питальних речень
        string textLower = text.ToLowerInvariant();
        var firstWordMatch = Regex.Match(textLower, @"^\b(\w+)\b");
        if (firstWordMatch.Success && ContextQuestionWords.Contains(firstWordMatch.Value))
        {
            if (!text.EndsWith("?") && !text.EndsWith("!"))
            {
                if (text.EndsWith(".")) text = text.Substring(0, text.Length - 1) + "?";
                else text += "?";
            }
        }

        return text.Trim();
    }

    private static string? FindNearestContextWord(string word)
    {
        string? bestMatch = null;
        int minDistance = 2;

        foreach (var dictWord in ContextCommonWords)
        {
            if (Math.Abs(dictWord.Length - word.Length) > 1) continue;
            int dist = LevenshteinDistance(word, dictWord);
            if (dist < minDistance)
            {
                minDistance = dist;
                bestMatch = dictWord;
            }
        }

        return bestMatch;
    }

    private static int LevenshteinDistance(string s, string t)
    {
        if (string.IsNullOrEmpty(s)) return t?.Length ?? 0;
        if (string.IsNullOrEmpty(t)) return s.Length;

        int n = s.Length;
        int m = t.Length;
        int[,] d = new int[n + 1, m + 1];

        for (int i = 0; i <= n; d[i, 0] = i++) ;
        for (int j = 0; j <= m; d[0, j] = j++) ;

        for (int i = 1; i <= n; i++)
        {
            for (int j = 1; j <= m; j++)
            {
                int cost = (t[j - 1] == s[i - 1]) ? 0 : 1;
                d[i, j] = Math.Min(
                    Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                    d[i - 1, j - 1] + cost);
            }
        }

        return d[n, m];
    }
}
