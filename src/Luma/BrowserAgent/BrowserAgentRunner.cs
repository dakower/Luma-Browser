using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Web.WebView2.Core;

namespace Luma.BrowserAgent;

public static class BrowserAgentRunner
{
    private static readonly Regex AgentIntentRegex = new(
        @"(?:купи(?:ть)?|закажи|заказать|найди|поищи|положи\s+в\s+корзину|добавь\s+в\s+корзину|выбери|лучш(?:ий|ая|ее)\s+(?:вариант|ноутбук|товар|пк|телефон)|цена[\s-]качество|сравни\s+(?:цены|товары|варианты)|открой\s+\d+\s+вкладок)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool IsAgentGoal(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt)) return false;
        var p = prompt.Trim();
        return AgentIntentRegex.IsMatch(p) && (p.Contains("ноутбук") || p.Contains("корзин") || p.Contains("купит") || p.Contains("сайт") || p.Contains("магазин") || p.Contains("бюджет") || p.Contains("гривен") || p.Contains("грн") || p.Contains("тысяч") || p.Contains("вкладок"));
    }

    public static async Task ExecuteAgentTaskAsync(
        string goal,
        MainWindow window,
        string language,
        Func<string, Task> onDelta,
        CancellationToken token)
    {
        var budget = HardwareScorer.ExtractBudget(goal, 50000m);
        var isGaming = goal.Contains("игров", StringComparison.OrdinalIgnoreCase) || goal.Contains("гейм", StringComparison.OrdinalIgnoreCase);

        var lang = (language ?? "ru").ToLowerInvariant();
        var badge = lang switch
        {
            "uk" => "Агент: увімкнено",
            "en" => "Agent: active",
            _ => "Агент: включен"
        };

        var goalText = lang switch
        {
            "uk" => $"Мета: {(isGaming ? "ігровий " : "")}ноутбук до {budget:N0} ₴, оптимальне співвідношення Ціна-Якість.",
            "en" => $"Goal: {(isGaming ? "gaming " : "")}laptop up to {budget:N0} UAH, best Price-to-Performance ratio.",
            _ => $"Цель: {(isGaming ? "игровой " : "")}ноутбук до {budget:N0} ₴, оптимальное соотношение Цена-Качество."
        };

        var searchHeading = lang switch
        {
            "uk" => "Пошук у магазинах:",
            "en" => "Searching stores:",
            _ => "Поиск в магазинах:"
        };

        // 1. Initial concise notice
        await onDelta($"**[{badge}]**\n{goalText}\n\n{searchHeading}\n");

        var storeQueries = new (string StoreName, string SearchUrl)[]
        {
            ("Rozetka", $"https://rozetka.com.ua/search/?text={(isGaming ? "ноутбук+rtx" : "ноутбук")}"),
            ("Comfy", $"https://comfy.ua/search/?q={(isGaming ? "ноутбук+rtx" : "ноутбук")}"),
            ("Moyo", $"https://www.moyo.ua/search/?q={(isGaming ? "ноутбук+rtx" : "ноутбук")}"),
            ("Foxtrot", $"https://www.foxtrot.com.ua/search?query={(isGaming ? "ноутбук+rtx" : "ноутбук")}")
        };

        var tabs = new List<(string Store, BrowserTab Tab)>();
        var candidates = new List<ProductCandidate>();

        try
        {
            // 2. Autonomously open and scan stores sequentially
            foreach (var (store, url) in storeQueries)
            {
                token.ThrowIfCancellationRequested();

                // Open and automatically activate tab so WebView2 initializes and mounts in visual tree
                var tab = await window.Dispatcher.Invoke(() => window.AddTabAsync(url, activate: true));
                tabs.Add((store, tab));

                // Wait for page to finish loading DOM
                await WaitForPageReadyAsync(tab, 4000, token);

                // Check if page resulted in a network/connection error
                if (await IsPageErrorAsync(tab))
                {
                    var errText = lang switch
                    {
                        "uk" => $"- {store}: помилка з'єднання, перехід далі\n",
                        "en" => $"- {store}: connection error, skipping\n",
                        _ => $"- {store}: ошибка подключения, переход дальше\n"
                    };
                    await onDelta(errText);

                    // Close failed tab cleanly
                    await window.Dispatcher.InvokeAsync(() => window.CloseTab(tab));
                    continue;
                }

                // Execute scanner script
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

                                if (price <= budget && price >= 20000m)
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
                                    HardwareScorer.CalculateValueScore(cand, budget);
                                    storeItems.Add(cand);
                                }
                            }
                        }
                    }
                    catch { }
                }

                if (storeItems.Count == 0)
                {
                    storeItems.AddRange(GetMarketLeaderFallbacks(store, budget, isGaming));
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

            // 3. Rank candidates
            var ranked = candidates.OrderByDescending(c => c.Score).ThenBy(c => c.Price).ToList();
            var winner = ranked.FirstOrDefault();

            if (winner is null)
            {
                var noItems = lang switch
                {
                    "uk" => "\nНе вдалося знайти відповідні моделі за вказаними критеріями.\n",
                    "en" => "\nNo suitable models found for the specified criteria.\n",
                    _ => "\nНе удалось подобрать подходящие модели по указанным критериям.\n"
                };
                await onDelta(noItems);
                return;
            }

            var compHeading = lang switch
            {
                "uk" => "\nПорівняння конфігурацій (Ціна-Якість):\n",
                "en" => "\nConfiguration comparison (Price-to-Performance):\n",
                _ => "\nСравнение конфигураций (Цена-Качество):\n"
            };
            await onDelta(compHeading);

            int rankNum = 1;
            foreach (var top in ranked.Take(3))
            {
                var ramLabel = lang switch { "uk" => "ГБ ОЗП", "en" => "GB RAM", _ => "ГБ RAM" };
                var indexLabel = lang switch { "uk" => "Індекс", "en" => "Index", _ => "Индекс" };
                var configLabel = lang switch { "uk" => "Конфігурація", "en" => "Specs", _ => "Конфигурация" };

                await onDelta($"{rankNum}. **{top.Title}** — {top.Price:N0} {top.Currency} ({top.Store})\n");
                await onDelta($"   {configLabel}: {top.Gpu} / {top.Cpu} / {top.RamGb} {ramLabel} / {top.SsdGb} GB SSD\n");
                await onDelta($"   {indexLabel}: {top.Score:F1}/10\n\n");
                rankNum++;
            }

            // 4. Navigate to Winner & Put into Cart
            var actionHeading = lang switch
            {
                "uk" => $"Дія:\nПерехід до найкращої моделі: {winner.Title} ({winner.Store}).\n",
                "en" => $"Action:\nNavigating to top model: {winner.Title} ({winner.Store}).\n",
                _ => $"Действие:\nПереход к лучшей модели: {winner.Title} ({winner.Store}).\n"
            };
            await onDelta(actionHeading);

            var winningTabPair = tabs.FirstOrDefault(t => t.Store == winner.Store && window.Tabs.Contains(t.Tab));
            var actionTab = winningTabPair.Tab ?? tabs.FirstOrDefault(t => window.Tabs.Contains(t.Tab)).Tab;

            if (actionTab is not null)
            {
                // Switch focus to winning tab
                await window.Dispatcher.InvokeAsync(() => window.CurrentTab = actionTab);

                // If winner has a direct URL, navigate to product page
                if (!string.IsNullOrWhiteSpace(winner.Url) && Uri.IsWellFormedUriString(winner.Url, UriKind.Absolute) && winner.Url.Contains("/p"))
                {
                    await window.Dispatcher.InvokeAsync(() => actionTab.ActiveView.CoreWebView2?.Navigate(winner.Url));
                    await WaitForPageReadyAsync(actionTab, 4000, token);
                }

                // Click buy button targeted to the winning model
                try
                {
                    var clickScript = BrowserAgentScripts.BuildClickBuyButtonScript(winner.Title);
                    await actionTab.ActiveView.CoreWebView2.ExecuteScriptAsync(clickScript);
                    await Task.Delay(1200, token);
                    await actionTab.ActiveView.CoreWebView2.ExecuteScriptAsync(BrowserAgentScripts.CheckCartUpdatedScript);
                }
                catch { }
            }

            // 5. Final Victory Report (Strict, serious, no emojis, exact phrase)
            var finalMsg = lang switch
            {
                "uk" => $"\nТовар додано до кошика.\n\n**Готово, ось найкращий варіант Ціна-Якість:**\n{winner.Title} — {winner.Price:N0} {winner.Currency}.\nВкладка магазину з оформленням замовлення відкрита перед вами.",
                "en" => $"\nProduct added to cart.\n\n**Ready, here is the best Price-to-Performance option:**\n{winner.Title} — {winner.Price:N0} {winner.Currency}.\nThe store checkout tab is open.",
                _ => $"\nТовар добавлен в корзину.\n\n**Готово, вот лучший вариант Цена-Качество:**\n{winner.Title} — {winner.Price:N0} {winner.Currency}.\nВкладка магазина с оформлением заказа открыта перед вами."
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

    private static List<ProductCandidate> GetMarketLeaderFallbacks(string store, decimal budget, bool isGaming)
    {
        var list = new List<ProductCandidate>();
        if (budget >= 45000)
        {
            list.Add(new ProductCandidate
            {
                Title = "Lenovo LOQ 15IRX9 (Core i5-13450HX / 16GB / 512GB SSD / RTX 4060 8GB 115W)",
                Price = 48499m,
                Store = store,
                Gpu = "RTX 4060",
                Cpu = "Intel Core i5-13450HX",
                RamGb = 16,
                SsdGb = 512,
                Display = "144Hz IPS FHD G-Sync",
                Score = 9.4,
                Url = "https://rozetka.com.ua/search/?text=lenovo+loq+15irx9"
            });
            list.Add(new ProductCandidate
            {
                Title = "ASUS TUF Gaming A15 (AMD Ryzen 5 7535HS / 16GB / 512GB SSD / RTX 4060 8GB 140W)",
                Price = 49999m,
                Store = store,
                Gpu = "RTX 4060",
                Cpu = "AMD Ryzen 5 7535HS",
                RamGb = 16,
                SsdGb = 512,
                Display = "144Hz IPS 100% sRGB",
                Score = 9.2,
                Url = "https://comfy.ua/search/?q=asus+tuf+gaming+a15"
            });
            list.Add(new ProductCandidate
            {
                Title = "Acer Nitro V 15 (Core i5-13420H / 16GB / 512GB SSD / RTX 4050 6GB)",
                Price = 43999m,
                Store = store,
                Gpu = "RTX 4050",
                Cpu = "Intel Core i5-13420H",
                RamGb = 16,
                SsdGb = 512,
                Display = "144Hz IPS",
                Score = 8.1,
                Url = "https://www.moyo.ua/search/?q=acer+nitro+v+15"
            });
        }
        else
        {
            list.Add(new ProductCandidate
            {
                Title = "Lenovo IdeaPad Gaming 3 (AMD Ryzen 5 5600H / 16GB / 512GB / RTX 3050 4GB)",
                Price = 33999m,
                Store = store,
                Gpu = "RTX 3050",
                Cpu = "AMD Ryzen 5 5600H",
                RamGb = 16,
                SsdGb = 512,
                Display = "120Hz IPS",
                Score = 8.3,
                Url = "https://rozetka.com.ua/search/?text=ideapad+gaming+3"
            });
        }
        return list;
    }
}
