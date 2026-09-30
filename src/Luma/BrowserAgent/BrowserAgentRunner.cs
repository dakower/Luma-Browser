using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Web.WebView2.Core;

namespace Luma.BrowserAgent;

public static class BrowserAgentRunner
{
    private static readonly Regex AgentIntentRegex = new(
        @"(?:купи(?:ть)?|закажи|заказать|найди|поищи|положи\s+в\s+корзину|добавь\s+в\s+корзину|выбери|лучш(?:ий|ая|ее)\s+(?:вариант|товар|квартир|машин)|цена[\s-]качество|сравни|открой\s+\d+\s+вкладок|аренд)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool IsAgentGoal(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt)) return false;
        return AgentIntentRegex.IsMatch(prompt);
    }

    public static async Task ExecuteAgentTaskAsync(
        string goal,
        MainWindow window,
        string accessToken,
        string language,
        Func<string, Task> onDelta,
        CancellationToken token)
    {
        var lang = (language ?? "ru").ToLowerInvariant();
        var badge = lang switch
        {
            "uk" => "Агент: увімкнено",
            "en" => "Agent: active",
            _ => "Агент: включен"
        };

        // LLM Plan
        var planPrompt = $@"Пользователь просит: '{goal}'.
Твоя задача — извлечь параметры поиска для интернет-магазинов или сайтов.
Ключевые слова (keywords) ДОЛЖНЫ содержать ТОЛЬКО наименование товара/услуги (например: ""ноутбук игровой"" или ""аренда квартиры Черноморск"").
КАТЕГОРИЧЕСКИ НЕ ИСПОЛЬЗУЙ слова ""найди"", ""купи"", ""до 50к"", ""грн"", ""бюджет"" в keywords!

Ответь СТРОГО в формате JSON:
{{
  ""category"": ""electronics"", // или ""real_estate"", ""general""
  ""budget"": 50000, // число в грн или 0 если не указан
  ""sites"": [""rozetka.com.ua"", ""comfy.ua"", ""moyo.ua"", ""foxtrot.com.ua""],
  ""keywords"": ""ноутбук игровой""
}}";

        string llmResponse = "";
        try
        {
            if (!string.IsNullOrWhiteSpace(accessToken))
            {
                var planMessages = new List<AssistantMessage> { new() { Role = "user", Text = planPrompt } };
                await AssistantClient.StreamAsync(accessToken, "fast", planMessages, delta => { llmResponse += delta; return Task.CompletedTask; }, token);
            }
        }
        catch { }

        llmResponse = llmResponse.Trim();
        if (llmResponse.StartsWith("```json")) llmResponse = llmResponse.Substring(7).TrimEnd('`').Trim();
        else if (llmResponse.StartsWith("```")) llmResponse = llmResponse.Substring(3).TrimEnd('`').Trim();

        string category = "general";
        decimal budget = 0;
        string[] sites = Array.Empty<string>();
        string keywords = "";

        try
        {
            using var doc = JsonDocument.Parse(llmResponse);
            if (doc.RootElement.TryGetProperty("category", out var catProp)) category = catProp.GetString() ?? category;
            if (doc.RootElement.TryGetProperty("budget", out var budProp)) budget = budProp.GetDecimal();
            if (doc.RootElement.TryGetProperty("sites", out var sitesProp)) sites = sitesProp.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x != "").ToArray();
            if (doc.RootElement.TryGetProperty("keywords", out var kwProp)) keywords = kwProp.GetString() ?? keywords;
        }
        catch { }

        if (budget <= 0) budget = HardwareScorer.ExtractBudget(goal, 0);
        keywords = CleanKeywords(keywords, goal);

        var isLaptop = goal.Contains("ноут", StringComparison.OrdinalIgnoreCase) || goal.Contains("laptop", StringComparison.OrdinalIgnoreCase);
        if (sites.Length == 0 || isLaptop || category == "electronics")
        {
            sites = new[] { "rozetka.com.ua", "comfy.ua", "moyo.ua", "foxtrot.com.ua" };
        }

        var goalText = lang switch
        {
            "uk" => $"Мета: {goal} (Бюджет: {(budget > 0 ? budget.ToString("N0") + " ₴" : "не обмежений")}).",
            "en" => $"Goal: {goal} (Budget: {(budget > 0 ? budget.ToString("N0") + " UAH" : "unlimited")}).",
            _ => $"Цель: {goal} (Бюджет: {(budget > 0 ? budget.ToString("N0") + " ₴" : "не ограничен")})."
        };

        var searchHeading = lang switch
        {
            "uk" => "Пошук на сайтах:",
            "en" => "Searching sites:",
            _ => "Поиск на сайтах:"
        };

        // 1. Initial notice
        await onDelta($"**[{badge}]**\n{goalText}\n\n{searchHeading}\n");

        var storeQueries = sites.Select(s => 
        {
            var cleanSite = s.Replace("www.", "");
            var q = Uri.EscapeDataString(keywords);
            var searchUrl = cleanSite switch
            {
                "rozetka.com.ua" => $"https://rozetka.com.ua/search/?text={q}",
                "comfy.ua" => $"https://comfy.ua/search/?q={q}",
                "moyo.ua" => $"https://moyo.ua/search/?q={q}",
                "foxtrot.com.ua" => $"https://foxtrot.com.ua/search?query={q}",
                "hotline.ua" => $"https://hotline.ua/sr/?q={q}",
                "brain.com.ua" => $"https://brain.com.ua/search/?q={q}",
                "olx.ua" => $"https://www.olx.ua/uk/list/q-{q}/",
                "dim.ria.com" => $"https://dim.ria.com/uk/search/?keyword={q}",
                _ => $"https://www.google.com/search?q=site:{cleanSite}+{q}"
            };
            return (cleanSite, searchUrl);
        }).ToArray();

        var tabs = new List<(string Store, BrowserTab Tab)>();
        var candidates = new List<ProductCandidate>();

        try
        {
            // 2. Autonomously open and scan stores sequentially
            foreach (var (store, url) in storeQueries)
            {
                token.ThrowIfCancellationRequested();

                // Open and activate tab so WebView2 is mounted and starts loading
                var tab = await window.Dispatcher.Invoke(() => window.AddTabAsync(url, activate: true));
                tabs.Add((store, tab));

                // Wait for DOM readiness
                await WaitForPageReadyAsync(tab, 4000, token);

                // Check for connection/error page
                if (await IsPageErrorAsync(tab))
                {
                    var errText = lang switch
                    {
                        "uk" => $"- {store}: помилка з'єднання, перехід далі\n",
                        "en" => $"- {store}: connection error, skipping\n",
                        _ => $"- {store}: ошибка подключения, переход дальше\n"
                    };
                    await onDelta(errText);
                    await window.Dispatcher.InvokeAsync(() => window.CloseTab(tab));
                    continue;
                }

                // Scrape product cards from page
                var storeItems = new List<ProductCandidate>();
                var view = tab.ActiveView;
                if (view?.CoreWebView2 is not null)
                {
                    try
                    {
                        var rawJson = await view.CoreWebView2.ExecuteScriptAsync(BrowserAgentScripts.ScanECommercePageScript);
                        var parsed = JsonSerializer.Deserialize<string>(rawJson);
                        if (!string.IsNullOrWhiteSpace(parsed))
                        {
                            using var doc = JsonDocument.Parse(parsed);
                            foreach (var el in doc.RootElement.EnumerateArray())
                            {
                                var title = el.GetProperty("title").GetString() ?? "";
                                var price = el.GetProperty("price").GetDecimal();
                                var itemUrl = el.GetProperty("url").GetString() ?? "";
                                var specs = el.GetProperty("specs").GetString() ?? "";

                                bool isWithinBudget = budget <= 0 || price <= budget * 1.05m;
                                if (isWithinBudget && title.Length >= 4)
                                {
                                    var cand = new ProductCandidate
                                    {
                                        Title = title,
                                        Price = price,
                                        Url = itemUrl,
                                        Store = store,
                                        Specs = specs
                                    };
                                    HardwareScorer.ParseSpecs(cand);
                                    HardwareScorer.CalculateValueScore(cand, budget > 0 ? budget : 50000m);
                                    storeItems.Add(cand);
                                }
                            }
                        }
                    }
                    catch { }
                }

                candidates.AddRange(storeItems);

                var successText = lang switch
                {
                    "uk" => $"- {store}: пошук виконано, знайдено моделей: {storeItems.Count}\n",
                    "en" => $"- {store}: search completed, found models: {storeItems.Count}\n",
                    _ => $"- {store}: поиск выполнен, найдено моделей: {storeItems.Count}\n"
                };
                await onDelta(successText);

                await Task.Delay(400, token);
            }

            // Rank candidates via LLM if general intent
            var candidatesJson = JsonSerializer.Serialize(candidates.Select((c, i) => new { Index = i, c.Title, c.Price, c.Store, c.Url }).Take(40));
            var rankPrompt = $@"Пользователь ищет: '{goal}'.
Бюджет: {budget}.
Список найденных вариантов (в JSON):
{candidatesJson}

Выбери топ 1 вариант, который лучше всего подходит по соотношению цена-качество и удовлетворяет бюджету. 
Ответь ТОЛЬКО в формате JSON:
{{
  ""best_index"": 0 // индекс в массиве
}}";
            
            int bestIndex = 0;
            try
            {
                if (!string.IsNullOrWhiteSpace(accessToken))
                {
                    string rankResponse = "";
                    var rankMessages = new List<AssistantMessage> { new() { Role = "user", Text = rankPrompt } };
                    await AssistantClient.StreamAsync(accessToken, "fast", rankMessages, delta => { rankResponse += delta; return Task.CompletedTask; }, token);
                    rankResponse = rankResponse.Trim();
                    if (rankResponse.StartsWith("```json")) rankResponse = rankResponse.Substring(7).TrimEnd('`').Trim();
                    else if (rankResponse.StartsWith("```")) rankResponse = rankResponse.Substring(3).TrimEnd('`').Trim();
                    using var doc = JsonDocument.Parse(rankResponse);
                    if (doc.RootElement.TryGetProperty("best_index", out var bIndex)) bestIndex = bIndex.GetInt32();
                }
            }
            catch { }

            var winner = candidates.Count > bestIndex && bestIndex >= 0 ? candidates[bestIndex] : candidates.FirstOrDefault();

            if (winner is null)
            {
                var noItems = lang switch
                {
                    "uk" => "\nНе вдалося знайти відповідні варіанти за вказаними критеріями.\n",
                    "en" => "\nNo suitable options found for the specified criteria.\n",
                    _ => "\nНе удалось подобрать подходящие варианты по указанным критериям.\n"
                };
                await onDelta(noItems);
                return;
            }

            var compHeading = lang switch
            {
                "uk" => "\nНайкращий варіант (Ціна-Якість):\n",
                "en" => "\nBest option (Price-to-Performance):\n",
                _ => "\nЛучший вариант (Цена-Качество):\n"
            };
            await onDelta(compHeading);

            await onDelta($"**{winner.Title}** — {winner.Price:N0} {winner.Currency} ({winner.Store})\n\n");

            // 4. Navigate to Winner
            var actionHeading = lang switch
            {
                "uk" => $"Дія:\nПерехід до найкращого варіанту: {winner.Title} ({winner.Store}).\n",
                "en" => $"Action:\nNavigating to best option: {winner.Title} ({winner.Store}).\n",
                _ => $"Действие:\nПереход к лучшему варианту: {winner.Title} ({winner.Store}).\n"
            };
            await onDelta(actionHeading);

            var winningTabPair = tabs.FirstOrDefault(t => t.Store == winner.Store && window.Tabs.Contains(t.Tab));
            var actionTab = winningTabPair.Tab ?? tabs.FirstOrDefault(t => window.Tabs.Contains(t.Tab)).Tab;

            if (actionTab is not null)
            {
                // Switch focus to winning tab
                await window.Dispatcher.InvokeAsync(() => window.CurrentTab = actionTab);

                // If winner has a direct URL, navigate to product page
                if (!string.IsNullOrWhiteSpace(winner.Url) && Uri.IsWellFormedUriString(winner.Url, UriKind.Absolute))
                {
                    await window.Dispatcher.InvokeAsync(() => actionTab.ActiveView.CoreWebView2?.Navigate(winner.Url));
                    await WaitForPageReadyAsync(actionTab, 4000, token);
                }

                // If the goal implies buying or carting, try to click buy
                if (goal.Contains("купи", StringComparison.OrdinalIgnoreCase) || goal.Contains("корзин", StringComparison.OrdinalIgnoreCase) || goal.Contains("закаж", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        var clickScript = BrowserAgentScripts.BuildClickBuyButtonScript(winner.Title);
                        await actionTab.ActiveView.CoreWebView2.ExecuteScriptAsync(clickScript);
                        await Task.Delay(1400, token);
                        await actionTab.ActiveView.CoreWebView2.ExecuteScriptAsync(BrowserAgentScripts.ProceedToCheckoutAndPurgePowderScript);
                    }
                    catch { }
                }
            }

            // 5. Final Victory Report
            var finalMsg = lang switch
            {
                "uk" => $"\n**Готово, ось найкращий варіант:**\n{winner.Title} — {winner.Price:N0} {winner.Currency}.\nВкладка з результатом відкрита перед вами.",
                "en" => $"\n**Ready, here is the best option:**\n{winner.Title} — {winner.Price:N0} {winner.Currency}.\nThe result tab is open.",
                _ => $"\n**Готово, вот лучший вариант:**\n{winner.Title} — {winner.Price:N0} {winner.Currency}.\nВкладка с результатом открыта перед вами."
            };
            await onDelta(finalMsg);
        }
        catch (OperationCanceledException)
        {
            var cancelMsg = lang switch
            {
                "uk" => "\n\nДію браузер-агента скасовано.",
                "en" => "\n\nBrowser agent action canceled.",
                _ => "\n\nДействие браузер-агента отменено."
            };
            await onDelta(cancelMsg);
        }
        catch (Exception ex)
        {
            await onDelta($"\n\nОшибка агента: {ex.Message}");
        }
    }

    private static async Task WaitForPageReadyAsync(BrowserTab tab, int timeoutMs, CancellationToken token)
    {
        var start = DateTime.UtcNow;
        while (tab.ActiveView?.CoreWebView2 is null)
        {
            if ((DateTime.UtcNow - start).TotalMilliseconds > timeoutMs) return;
            await Task.Delay(100, token);
        }

        var web = tab.ActiveView.CoreWebView2;
        while ((DateTime.UtcNow - start).TotalMilliseconds < timeoutMs)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var ready = await web.ExecuteScriptAsync("document.readyState");
                if (ready is "\"interactive\"" or "\"complete\"")
                {
                    await Task.Delay(600, token);
                    return;
                }
            }
            catch { }
            await Task.Delay(200, token);
        }
    }

    private static async Task<bool> IsPageErrorAsync(BrowserTab tab)
    {
        try
        {
            var web = tab.ActiveView?.CoreWebView2;
            if (web is null) return true;
            var res = await web.ExecuteScriptAsync(
                "Boolean(document.querySelector('.error-card') || (document.title && document.title.includes('Не удалось')) || (document.body && document.body.innerText.includes('Не удалось открыть страницу')))");
            return res == "true";
        }
        catch { return false; }
    }

    private static List<ProductCandidate> GetMarketLeaderFallbacks(decimal budget)
    {
        var list = new List<ProductCandidate>();
        if (budget >= 45000)
        {
            list.Add(new ProductCandidate
            {
                Title = "Lenovo LOQ 15IRX9 (Core i5-13450HX / 16GB / 512GB SSD / RTX 4060 8GB 115W)",
                Price = 48499m,
                Store = "Comfy",
                Gpu = "RTX 4060",
                Cpu = "Intel Core i5-13450HX",
                RamGb = 16,
                SsdGb = 512,
                Display = "144Hz IPS FHD G-Sync",
                Score = 9.4,
                Url = "https://comfy.ua/search/?q=lenovo+loq+15irx9"
            });
            list.Add(new ProductCandidate
            {
                Title = "ASUS TUF Gaming A15 (AMD Ryzen 5 7535HS / 16GB / 512GB SSD / RTX 4060 8GB 140W)",
                Price = 49999m,
                Store = "Rozetka",
                Gpu = "RTX 4060",
                Cpu = "AMD Ryzen 5 7535HS",
                RamGb = 16,
                SsdGb = 512,
                Display = "144Hz IPS 100% sRGB",
                Score = 9.2,
                Url = "https://rozetka.com.ua/search/?text=asus+tuf+gaming+a15"
            });
            list.Add(new ProductCandidate
            {
                Title = "Acer Nitro V 15 (Core i5-13420H / 16GB / 512GB SSD / RTX 4050 6GB)",
                Price = 43999m,
                Store = "Moyo",
                Gpu = "RTX 4050",
                Cpu = "Intel Core i5-13420H",
                RamGb = 16,
                SsdGb = 512,
                Display = "144Hz IPS",
                Score = 8.1,
                Url = "https://moyo.ua/search/?q=acer+nitro+v+15"
            });
        }
        else
        {
            list.Add(new ProductCandidate
            {
                Title = "MSI Thin 15 B13UC (Intel Core i5-13420H / 16GB / 512GB / RTX 3050 4GB)",
                Price = 39999m,
                Store = "Rozetka",
                Gpu = "RTX 3050",
                Cpu = "Intel Core i5-13420H",
                RamGb = 16,
                SsdGb = 512,
                Display = "144Hz IPS",
                Score = 8.4,
                Url = "https://rozetka.com.ua/search/?text=msi+thin+15"
            });
        }
        return list;
    }

    private static string CleanKeywords(string raw, string goal)
    {
        var text = string.IsNullOrWhiteSpace(raw) ? goal : raw;
        var isLaptop = goal.Contains("ноут", StringComparison.OrdinalIgnoreCase) || text.Contains("ноут", StringComparison.OrdinalIgnoreCase) || goal.Contains("laptop", StringComparison.OrdinalIgnoreCase);
        var isGaming = goal.Contains("игров", StringComparison.OrdinalIgnoreCase) || text.Contains("игров", StringComparison.OrdinalIgnoreCase) || goal.Contains("гейм", StringComparison.OrdinalIgnoreCase);

        var terms = Regex.Matches(text, @"[a-zA-Zа-яА-Я0-9]{3,}")
            .Cast<Match>()
            .Select(m => m.Value.ToLowerInvariant())
            .Where(w => w is not ("найди" or "поищи" or "купи" or "купить" or "закажи" or "заказать" or "хочу" or "выбери" or "лучший" or "вариант" or "гривен" or "грн" or "тысяч" or "тыс"))
            .ToList();

        if (isLaptop)
        {
            terms.RemoveAll(w => w is "ноут" or "ноуты" or "ноутбук" or "ноутбука");
            terms.Insert(0, "ноутбук");
        }
        if (isGaming && !terms.Contains("игровой"))
        {
            terms.Add("игровой");
        }

        var result = string.Join(" ", terms.Distinct());
        return string.IsNullOrWhiteSpace(result) ? (isLaptop ? "ноутбук игровой" : goal) : result;
    }
}
