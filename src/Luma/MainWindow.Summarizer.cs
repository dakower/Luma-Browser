using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Luma;

public partial class MainWindow
{
    private string _summaryTargetUrl = "";
    private string _summaryTargetTitle = "";
    private string _summaryExtractedText = "";
    private bool _summaryIsDetailed = false;
    private CancellationTokenSource? _summaryCts;

    private void UrlBarSummarizeBtn_Click(object sender, RoutedEventArgs e)
    {
        if (CurrentTab is null || CurrentTab.IsHome)
        {
            ShowToast("LumaAI Пересказ", "Откройте видео или веб-страницу для пересказа.");
            return;
        }

        var tab = CurrentTab;
        var isYt = tab.Domain.Contains("youtube.com") || tab.Domain.Contains("youtu.be");

        var menu = NewMenu();
        Add(menu, isYt ? "🎬 Пересказ видео (LumaAI):" : "📰 Пересказ страницы (LumaAI):", () => { }, false);
        Sep(menu);

        Add(menu, "⚡ Краткий пересказ (3-5 главных тезисов)", () => _ = SummarizeCurrentPageAsync(false), true, "Alt+S", "IconSpark");
        Add(menu, "📖 Подробный пересказ (структура, аргументы, выводы)", () => _ = SummarizeCurrentPageAsync(true), true, "", "IconBookOpen");
        Sep(menu);
        Add(menu, "💬 Открыть в чате с LumaAI", () =>
        {
            _ = ToggleAssistantAsync(true);
            _ = AskAssistantAsync(isYt ? "Сделай анализ этого видео и выдели главное." : "Сделай краткий пересказ этой страницы.");
        });

        OpenMenu(menu);
    }

    public async Task SummarizeCurrentPageAsync(bool detailed)
    {
        if (CurrentTab is null || CurrentTab.IsHome) return;
        var tab = CurrentTab;
        _summaryTargetUrl = tab.ActiveUrl;
        _summaryTargetTitle = !string.IsNullOrWhiteSpace(tab.DisplayTitle) ? tab.DisplayTitle : tab.Domain;
        _summaryIsDetailed = detailed;

        _summaryCts?.Cancel();
        _summaryCts = new CancellationTokenSource();
        var token = _summaryCts.Token;

        // Open and configure Summary Dialog
        SummaryTitleText.Text = _summaryTargetTitle;
        SummarySourceText.Text = tab.Domain + " • " + (detailed ? "Подробный разбор" : "Краткий пересказ");
        SummaryBodyText.Text = "✨ Извлекаю содержание и анализирую материал...";
        SummaryStatusText.Text = "Генерация пересказа...";

        SummaryBriefBtn.Background = !detailed ? RawBrush("#3F3F46") : System.Windows.Media.Brushes.Transparent;
        SummaryBriefBtn.Foreground = !detailed ? System.Windows.Media.Brushes.White : RawBrush("#A1A1AA");
        SummaryDetailedBtn.Background = detailed ? RawBrush("#3F3F46") : System.Windows.Media.Brushes.Transparent;
        SummaryDetailedBtn.Foreground = detailed ? System.Windows.Media.Brushes.White : RawBrush("#A1A1AA");

        SummaryPopup.HorizontalOffset = Math.Max(20, (SiteShell.ActualWidth - 640) / 2);
        SummaryPopup.VerticalOffset = Math.Max(40, (SiteShell.ActualHeight - 620) / 2);
        SummaryPopup.IsOpen = true;

        var isYt = tab.Domain.Contains("youtube.com") || tab.Domain.Contains("youtu.be");

        // 1. Extract content text
        string contentText = "";
        if (isYt)
        {
            SummaryBodyText.Text = "✨ Получаю субтитры и таймкоды видео...";
            contentText = await ExtractYouTubeTranscriptAsync(tab);
        }

        if (string.IsNullOrWhiteSpace(contentText))
        {
            var page = await ReadPageContextAsync();
            contentText = !string.IsNullOrWhiteSpace(page.Selection) ? page.Selection : page.Text;
        }

        if (string.IsNullOrWhiteSpace(contentText) || contentText.Length < 30)
        {
            SummaryBodyText.Text = "Не удалось извлечь достаточно текста с этой страницы для анализа.\nУбедитесь, что страница полностью загрузилась, или выделите нужный фрагмент текста.";
            SummaryStatusText.Text = "Недостаточно данных";
            return;
        }

        _summaryExtractedText = contentText;

        // 2. Generate Summary
        SummaryBodyText.Text = "✨ Нейросеть LumaAI анализирует материал и формирует тезисы...";

        try
        {
            var summaryResult = await GenerateAiSummaryTextAsync(_summaryTargetTitle, contentText, detailed, token);
            if (!token.IsCancellationRequested)
            {
                SummaryBodyText.Text = summaryResult;
                SummaryStatusText.Text = "Готово";
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            SummaryBodyText.Text = "Ошибка генерации пересказа: " + ex.Message;
            SummaryStatusText.Text = "Ошибка";
        }
    }

    private async Task<string> ExtractYouTubeTranscriptAsync(BrowserTab tab)
    {
        var view = tab.ActiveView;
        if (view?.CoreWebView2 is null) return "";

        const string script = """
        (async () => {
          try {
            // Check caption tracks in ytInitialPlayerResponse
            const pr = window.ytInitialPlayerResponse;
            const tracks = pr?.captions?.playerCaptionsTracklistRenderer?.captionTracks;
            if (tracks && tracks.length > 0) {
              const tr = tracks.find(t => t.languageCode === 'ru') 
                      || tracks.find(t => t.languageCode === 'uk') 
                      || tracks.find(t => t.languageCode === 'en') 
                      || tracks[0];
              try {
                const resp = await fetch(tr.baseUrl + '&fmt=json3');
                if (resp.ok) {
                  const data = await resp.json();
                  const lines = [];
                  if (data.events) {
                    for (const ev of data.events) {
                      if (ev.segs) {
                        const s = ev.segs.map(x => x.utf8).join('').trim();
                        if (s && s !== '\n') lines.push(s);
                      }
                    }
                  }
                  if (lines.length > 0) {
                    return JSON.stringify({ text: lines.join(' ').slice(0, 45000) });
                  }
                }
              } catch(e) {}
            }
            // Fallback: video description & title
            const desc = document.querySelector('#description-inline-expander, ytd-text-inline-expander#description')?.innerText || '';
            const title = document.querySelector('h1.ytd-watch-metadata, #title h1')?.innerText || document.title;
            return JSON.stringify({ text: ('Название: ' + title + '\nОписание:\n' + desc).slice(0, 15000) });
          } catch(e) {
            return JSON.stringify({ text: '' });
          }
        })()
        """;

        try
        {
            var rawJson = await view.CoreWebView2.ExecuteScriptAsync(script);
            if (!string.IsNullOrWhiteSpace(rawJson) && rawJson != "null")
            {
                var inner = JsonSerializer.Deserialize<string>(rawJson);
                if (!string.IsNullOrWhiteSpace(inner))
                {
                    using var doc = JsonDocument.Parse(inner);
                    if (doc.RootElement.TryGetProperty("text", out var t))
                    {
                        var text = t.GetString() ?? "";
                        if (!string.IsNullOrWhiteSpace(text)) return text;
                    }
                }
            }
        }
        catch { }

        return "";
    }

    private async Task<string> GenerateAiSummaryTextAsync(string title, string content, bool detailed, CancellationToken token)
    {
        string prompt = detailed
            ? $"Сделай ПОДРОБНЫЙ РАЗВЁРНУТЫЙ ПЕРЕСКАЗ следующего материала:\nЗаголовок: {title}\n\nТребования:\n1. 📖 Обзор темы и контекст\n2. 📑 Разбор по ключевым разделам и главам (с аргументами, цифрами и фактами)\n3. 💡 Главные инсайты и полезные мысли\n4. 🏁 Итоговый вывод\n\nМатериал:\n{content[..Math.Min(content.Length, 35000)]}"
            : $"Сделай КРАТКИЙ ПЕРЕСКАЗ следующего материала:\nЗаголовок: {title}\n\nТребования:\n1. 💡 Главная суть (1-2 ёмких предложения)\n2. 📌 3-5 ключевых тезисов (самое важное списком)\n3. 🎯 Главный вывод\n\nМатериал:\n{content[..Math.Min(content.Length, 25000)]}";

        // Attempt 1: LumaAI StreamAsync (Gemini via Supabase)
        try
        {
            var accessToken = await _auth.GetAccessTokenAsync(token);
            if (!string.IsNullOrWhiteSpace(accessToken))
            {
                var messages = new List<AssistantMessage>
                {
                    new()
                    {
                        Role = "system",
                        Text = "Ты — LumaAI, экспертный аналитический ассистент браузера Luma. Твоя задача — сделать первоклассный, структурированный и понятный пересказ материала на русском языке. Форматируй заголовки, списки и выделяй важное жирным. Сразу переходи к пересказу без лишних приветствий."
                    },
                    new() { Role = "user", Text = prompt }
                };

                var sb = new StringBuilder();
                await AssistantClient.StreamAsync(accessToken, "fast", messages, delta =>
                {
                    sb.Append(delta);
                    Dispatcher.InvokeAsync(() =>
                    {
                        SummaryBodyText.Text = sb.ToString();
                    });
                    return Task.CompletedTask;
                }, token);

                var result = sb.ToString().Trim();
                if (result.Length > 20) return result;
            }
        }
        catch { }

        // Attempt 2: Smart Extractive Summarizer (100% offline & instant)
        return GenerateSmartLocalSummary(title, content, detailed);
    }

    private static string GenerateSmartLocalSummary(string title, string content, bool detailed)
    {
        var clean = Regex.Replace(content, @"[\t\u00a0 ]+", " ").Trim();
        var rawSentences = Regex.Split(clean, @"(?<=[.!?])\s+")
            .Select(s => s.Trim())
            .Where(s => s.Length >= 25 && s.Length <= 400 && !s.StartsWith("http") && !s.Contains("cookie") && !s.Contains("copyright"))
            .ToList();

        if (rawSentences.Count == 0)
        {
            return "Не удалось сформировать пересказ: текст страницы слишком фрагментирован.";
        }

        // Word frequency scoring
        var words = Regex.Matches(clean.ToLowerInvariant(), @"\b[а-яёa-z]{4,}\b")
            .Select(m => m.Value)
            .Where(w => !IsStopWord(w))
            .GroupBy(w => w)
            .ToDictionary(g => g.Key, g => g.Count());

        var scored = rawSentences.Select(s =>
        {
            var sWords = Regex.Matches(s.ToLowerInvariant(), @"\b[а-яёa-z]{4,}\b")
                .Select(m => m.Value)
                .Where(words.ContainsKey);
            var score = sWords.Sum(w => words[w]);
            return (Score: score, Sentence: s);
        }).OrderByDescending(x => x.Score).ToList();

        var topCount = detailed ? Math.Min(7, scored.Count) : Math.Min(4, scored.Count);
        var bestSentences = scored.Take(topCount).Select(x => x.Sentence).ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"💡 **Главная суть:**");
        sb.AppendLine(rawSentences[0]);
        sb.AppendLine();
        sb.AppendLine(detailed ? "📑 **Ключевые факты и тезисы материала:**" : "📌 **Ключевые тезисы:**");
        foreach (var s in bestSentences)
        {
            sb.AppendLine($"• {s}");
        }

        if (rawSentences.Count > 1)
        {
            sb.AppendLine();
            sb.AppendLine("🎯 **Итоговый вывод:**");
            sb.AppendLine(rawSentences[^1]);
        }

        sb.AppendLine();
        sb.AppendLine("*(✨ Совет: Войдите в аккаунт Luma в боковой панели для генерации углублённого AI-пересказа через Gemini)*");

        return sb.ToString();
    }

    private static bool IsStopWord(string w)
    {
        return w is "это" or "быть" or "который" or "также" or "если" or "когда" or "после" 
            or "только" or "более" or "может" or "этого" or "свой" or "всего" or "будет" 
            or "года" or "году" or "очень" or "даже" or "через" or "можно" or "надо";
    }

    private void SummaryBriefBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!_summaryIsDetailed) return;
        _ = SummarizeCurrentPageAsync(false);
    }

    private void SummaryDetailedBtn_Click(object sender, RoutedEventArgs e)
    {
        if (_summaryIsDetailed) return;
        _ = SummarizeCurrentPageAsync(true);
    }

    private void SummaryCopy_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Windows.Clipboard.SetText(SummaryBodyText.Text);
            ShowToast("Пересказ скопирован", "Текст пересказа скопирован в буфер обмена");
        }
        catch { }
    }

    private void SummaryOpenInChat_Click(object sender, RoutedEventArgs e)
    {
        SummaryPopup.IsOpen = false;
        _ = ToggleAssistantAsync(true);
        var query = $"Вот пересказ материала «{_summaryTargetTitle}»:\n\n{SummaryBodyText.Text}\n\nОтвечай на мои вопросы по этому материалу.";
        _ = AskAssistantAsync(query);
    }

    private void SummaryClose_Click(object sender, RoutedEventArgs e)
    {
        _summaryCts?.Cancel();
        SummaryPopup.IsOpen = false;
    }
}
