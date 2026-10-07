using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Web.WebView2.Core;

namespace Luma.BrowserAgent;

/// <summary>
/// Universal browser agent. Instead of a fixed "find and buy" pipeline it runs an
/// observe → think → act loop: every step the model sees the live page (indexed interactive
/// elements + visible text), picks exactly one action, Luma performs it with trusted input, and
/// the result is fed back. This lets it do whatever the user actually asked for — open Gmail and
/// start a message, find a specific comment under a video, fill a form, compare products, etc.
/// </summary>
public static class BrowserAgentRunner
{
    private const int MaxSteps = 30;

    // Only clear "do something in the browser for me" requests auto-start the agent; plain
    // questions stay with the regular assistant. The agent toggle in the panel forces it anyway.
    private static readonly Regex AgentIntentRegex = new(
        @"(?:\b(?:зайди|перейди|нажми|натисни|кликни|заполни|залогинься|авторизуйся|подпишись|отпишись|лайкни|дизлайкни|прокрути|пролистай|закажи|купи|оформи|замов|пройди|реши|сдай|ответь)\b"
        + @"|(?:пройди|реши|сдай|ответь|выполни)\s+.{0,60}?(?:тест|опрос|квиз|вопрос|экзамен|задание|викторин|опитування|тестування)"
        + @"|(?:добавь|положи|додай)\s+в\s+(?:корзину|кошик)"
        + @"|поставь\s+лайк"
        + @"|(?:найди|знайди|отыщи)\s+.{0,90}?(?:коммент|отзыв|відгук|пост|сообщени|повідомлен|ответ|твит|видео|відео|ролик|канал|на\s+сайте|на\s+странице|на\s+ютуб|в\s+ютуб|на\s+youtube|в\s+гугл)"
        + @"|(?:найди|знайди|отыщи|скачай|збережи|сохрани|поставь|встанови|скинь)\s+.{0,90}?(?:аву|аватар|картинк|зображен|пикчу|фото|шпалери|обои|референс|пин|арт)"
        + @"|(?:поставь|встанови)\s+.{0,60}?(?:в\s+тг|в\s+телеграм|на\s+аву|в\s+профиль)"
        + @"|(?:открой|відкрий)\s+.{1,90}?\s(?:и|і|затем|потом|а\s+потом|та)\s+(?:найди|знайди|открой|відкрий|нажми|натисни|напиши|включи|перейди|зайди|отправ|надішли|проверь|посмотри|вбей|введи)"
        + @"|(?:напиши|отправь|надішли)\s+.{0,60}?(?:письмо|лист|сообщение|повідомлення|коммент|в\s+чат|в\s+поддержку)"
        + @"|\b(?:go\s+to|click|fill\s+(?:in|out)|add\s+to\s+cart|log\s+in\s+to|sign\s+in\s+to|solve|take|pass)\b"
        + @"|\b(?:solve|take|pass|answer)\s+.{0,60}?(?:test|quiz|exam|questions|form)"
        + @"|\bfind\s+.{0,80}?(?:comment|review|post|video|on\s+youtube|on\s+the\s+site)"
        + @"|\b(?:find|download|save|set)\s+.{0,80}?(?:avatar|pfp|image|picture|photo|wallpaper|art)\b"
        + @"|\bopen\s+.{1,80}?\sand\s+(?:find|open|click|write|type|send|check|go))",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static bool IsAgentGoal(string prompt)
    {
        if (string.IsNullOrWhiteSpace(prompt)) return false;
        return AgentIntentRegex.IsMatch(prompt);
    }

    private const string SystemPrompt = """
Ты — Luma Agent, автономный агент внутри браузера Luma. Ты управляешь браузером вместо пользователя:
открываешь сайты, нажимаешь, вводишь текст, прокручиваешь, читаешь страницы — как внимательный опытный человек за компьютером.

ГЛАВНОЕ:
1. Сначала точно пойми, ЧТО именно просит пользователь, и выполняй ровно эту просьбу — не больше и не меньше.
   Не превращай задачу в покупку, сравнение товаров или поиск по магазинам, если об этом не просили.
2. Выбирай самый короткий надёжный путь. Если знаешь прямой адрес — сразу переходи туда. Примеры:
   - Поиск в Pinterest: https://www.pinterest.com/search/pins/?q=ЗАПРОС
   - Поиск картинок Google: https://www.google.com/search?tbm=isch&q=ЗАПРОС
   - Поиск в Яндекс Картинках: https://yandex.ru/images/search?text=ЗАПРОС
   - Gmail, новое письмо: https://mail.google.com/mail/u/0/#inbox?compose=new
   - Поиск на YouTube: https://www.youtube.com/results?search_query=ЗАПРОС
   - Канал YouTube: https://www.youtube.com/@ИМЯ/videos
   - Поиск Google: https://www.google.com/search?q=ЗАПРОС
   Если адрес не знаешь — используй web_search, а потом navigate по найденной ссылке.
3. После каждого действия смотри на новое состояние страницы и проверяй, получилось ли. Если нет — меняй подход
   (другой элемент, прокрутка, find_text, прямой URL, web_search). Никогда не повторяй одно и то же неудачное действие.
4. Комментарии, отзывы, ответы и ленты часто подгружаются только при прокрутке. Чтобы найти конкретный комментарий,
   открой нужное видео/пост, прокрути к комментариям (scroll по несколько экранов), при необходимости поменяй сортировку,
   и используй find_text с ключевыми словами. Ищи по коротким характерным словам, а не по целой фразе.
5. Если пользователь ссылается на текущую страницу («тут», «на этой странице», «под этим видео») — работай с текущей вкладкой.
6. Вход в аккаунт, пароли, коды, данные карт и оплату не вводи сам — используй ask_user и объясни, что сделать пользователю.
7. Необратимые действия (отправить письмо/сообщение, опубликовать, оплатить, оформить заказ, удалить) делай только если
   пользователь прямо попросил именно это. Если просили «открыть», «подготовить», «написать черновик» — остановись перед отправкой.
8. Как только задача выполнена или нужное найдено — сразу action "done". В answer коротко и по делу опиши результат
   (что сделано / что найдено, с цитатой найденного текста и ссылкой, если уместно). Оставь пользователю открытой нужную страницу,
   а найденный элемент — на экране (для этого подходит find_text).
9. Пиши thought и answer на языке пользователя.
10. Важные найденные факты, которые понадобятся позже, сохраняй в поле "memory" — оно будет показываться тебе на следующих шагах.
11. ПРОХОЖДЕНИЕ ТЕСТОВ И ОПРОСОВ (МАКСИМАЛЬНАЯ СКОРОСТЬ И ТОЧНОСТЬ):
    - Все варианты ответов уже есть в списке элементов [N] с пометкой kindOf option.
    - КАТЕГОРИЧЕСКИ НЕ ИСПОЛЬЗУЙ eval, find_text или scroll в тестах — варианты уже на экране!
    - ОДИН ВЫБОР: нажми ровно ОДИН раз на нужный вариант (action: click, id: N). Сайт сразу зафиксирует ответ.
    - НЕСКОЛЬКО ВАРИАНТОВ (мультивыбор / чекбоксы): выбери ВСЕ правильные варианты и нажми «Відповісти» / «Далі» за ОДИН шаг через batch:
      {"thought":"Вопрос 1: весенние месяцы — Березень [5], Травень [6], Квітень [8], жму «Відповісти» [9]","action":"batch","actions":[{"action":"click","id":5},{"action":"click","id":6},{"action":"click","id":8},{"action":"click","id":9}]}
    - ВАЖНО: варианты с пометкой (отмечено) уже выбраны — НЕ нажимай на них повторно, чтобы не снять галочку!
    - В поле "thought" пиши КРАТКО (1 строка): суть вопроса и правильный ответ (например: «Вопрос 1: весенние месяцы — Березень, Травень, Квітень»). Не повторяй длинные формулировки!
12. РАБОТА С ИЗОБРАЖЕНИЯМИ И РЕФЕРЕНСАМИ (ВИЗУАЛЬНЫЙ АНАЛИЗ):
    - Если пользователь приложил изображение («как эта», «в таком стиле», «похожее на референс», «найди такую аву») — ты ВИДИШЬ его прямо во входном сообщении! КАТЕГОРИЧЕСКИ НЕ ГОВОРИ «я не вижу картинку»!
    - Внимательно определи:
      * Стиль: аниме / манга / скетч / цифровой рисунок / акварель / 3D / реализм / киберпанк / ретро / пиксель-арт / минимализм / гранж.
      * Персонаж/объект: пол, цвет и форма прически, цвет и разрез глаз, выражение лица, поза (например, рука у лица), одежда, аксессуары.
      * Цветовая гамма и вайб: теплые/холодные тона, сепия, монохром, неон, контраст, освещение, настроение (эстетика, dark, edgy, soft, cozy).
      * Имя персонажа/художника/тайтла, если узнаваемо.
    - Для поиска аватарок, пинов, обоев или товаров по референсу (в Pinterest, Google Картинках и др.) используй ТОЧНЫЕ, богатые ключевыми словами поисковые запросы на английском языке (на Pinterest английские теги находят идеальные арты):
      Например: https://www.pinterest.com/search/pins/?q=anime+boy+messy+hair+sketch+warm+aesthetic+pfp
    - В thought опиши, что ты распознал на референсе (стиль, персонаж, гамма), и сразу переходи к целевому поиску.
13. СКАЧИВАНИЕ И УСТАНОВКА АВАТАРОК И КАРТИНОК (TELEGRAM, DISCORD И ДР.):
    - Когда пользователь просит «скачай аву», «сохрани картинку», «поставь на аву в тг» или найти арт:
      1. Найди подходящее изображение на Pinterest, в Google Картинках или на текущей странице.
      2. Скачай его действием save_image (с id элемента картинки или ссылки на пин). Агент автоматически получит картинку в максимальном качестве, сохранит её в Загрузки пользователя и СКОПИРУЕТ В БУФЕР ОБМЕНА Windows!
      3. Если пользователь просил поставить в Telegram:
         - Если в браузере открыт Telegram Web (web.telegram.org) или пользователь авторизован, можешь перейти туда в настройки профиля.
         - Если Telegram открыт на компьютере как отдельное приложение — картинка уже в буфере обмена Windows и в Загрузках! В ответе (done) четко объясни пользователю: «Аватарка сохранена в Загрузки и скопирована в буфер обмена Windows. В Telegram нажми Настройки → Изменить фото профиля и просто нажми Ctrl+V или выбери файл из Загрузок».

ДЕЙСТВИЯ (ровно одно за шаг):
{"action":"navigate","url":"https://..."}          — открыть адрес во вкладке агента
{"action":"new_tab","url":"https://..."}           — открыть адрес в новой вкладке (если нужно держать несколько страниц)
{"action":"web_search","query":"..."}              — поиск в интернете; вернёт ссылки с описаниями, страницу не меняет
{"action":"save_image","id":N,"name":"avatar.png"} — скачать изображение элемента [N] (или всей страницы) в Загрузки в высоком качестве и скопировать в буфер обмена Windows (Ctrl+V)
{"action":"copy_image","id":N}                     — скопировать изображение элемента [N] в буфер обмена Windows для быстрой вставки (Ctrl+V)
{"action":"click","id":N}                          — нажать элемент [N] из списка
{"action":"js_click","id":N}                       — прямой клик через JS по [N] (если обычный click перекрыт оверлеем или баннером)
{"action":"hover","id":N}                          — навести курсор на элемент [N] (для выпадающих меню, подсказок, списков)
{"action":"check","id":N,"checked":true}           — отметить или снять отметку с чекбокса/радиокнопки [N] (для тестов, форм, настроек)
{"action":"type","id":N,"text":"...","submit":false} — очистить поле [N] и ввести текст; submit=true нажмёт Enter
{"action":"select","id":N,"text":"..."}            — выбрать вариант в выпадающем списке [N]
{"action":"drag","id":N,"deltaX":100,"deltaY":0}   — перетащить ползунок/слайдер от элемента [N] на deltaX, deltaY пикселей
{"action":"eval","script":"...","description":"..."} — выполнить JS на странице (только если обычные действия не подходят; укажи description)
{"action":"batch","actions":[...]}                 — выполнить серию действий за один шаг (например: выбрать ответ check + нажать Далее click)
{"action":"press","key":"Enter"}                   — клавиша: Enter, Escape, Tab, Backspace, ArrowDown, ArrowUp, PageDown, PageUp, Space
{"action":"scroll","direction":"down","amount":2}  — прокрутить на amount экранов (1–6), direction: down или up
{"action":"find_text","query":"..."}               — найти текст на всей странице, прокрутить к нему, вернуть контекст совпадений
{"action":"read","offset":0}                       — прочитать полный текст страницы кусками по 6000 символов
{"action":"back"}                                  — назад
{"action":"switch_tab","tab":N}                    — переключиться на вкладку N из списка открытых вкладок
{"action":"wait","seconds":2}                      — подождать загрузку
{"action":"done","answer":"итог для пользователя (markdown)"}
{"action":"ask_user","question":"что нужно от пользователя"}

ФОРМАТ ОТВЕТА — строго один JSON-объект, без текста вокруг и без ```:
{"thought":"кратко: суть действия и ответ для пользователя","memory":"(необязательно) что запомнить","action":"...", ...параметры}
""";

    private sealed record Observation(string Title, string Url, string Text, List<string> Elements, int ScrollY, int MaxScroll, int Vw, int Vh, bool Dialog);

    private sealed class AgentContext
    {
        public required MainWindow Window { get; init; }
        public required string Lang { get; init; }
        public bool IsTestSolving { get; set; }
        public bool HasReferenceImage { get; set; }
        public BrowserTab? Tab { get; set; }
        public Observation? Last { get; set; }
    }

    public static async Task<string> ExecuteAgentTaskAsync(
        string goal,
        MainWindow window,
        string accessToken,
        string language,
        Func<string, Task> onDelta,
        CancellationToken token,
        string conversationContext = "",
        Action<AssistantQuota>? onQuota = null,
        string? preferredTier = null,
        string? attachmentBase64 = null,
        string? attachmentMime = null,
        string? attachmentName = null)
    {
        var lang = (language ?? "ru").ToLowerInvariant();
        string L(string ru, string uk, string en) => lang switch { "uk" => uk, "en" => en, _ => ru };

        var hasAttachment = !string.IsNullOrWhiteSpace(attachmentBase64) && attachmentBase64.Length <= 8_000_000;
        var isTestSolving = Regex.IsMatch(goal, @"\b(?:тест|опрос|квиз|quiz|exam|вопрос|экзамен)\b", RegexOptions.IgnoreCase);
        var ctx = new AgentContext
        {
            Window = window,
            Lang = lang,
            IsTestSolving = isTestSolving,
            HasReferenceImage = hasAttachment,
        };
        var current = window.CurrentTab;
        if (current is { IsInternal: false } && current.ActiveView?.CoreWebView2 is not null) ctx.Tab = current;

        var session = new AssistantAgentSession();
        var history = new List<string>();
        var memory = new List<string>();
        var recent = new List<string>();
        var lastResult = "";
        var tier = isTestSolving ? "fast" : (preferredTier ?? "fast");

        AssistantImage? refImage = hasAttachment
            ? new AssistantImage
            {
                Base64 = attachmentBase64!,
                MimeType = attachmentMime is "image/png" or "image/webp" or "image/gif" ? attachmentMime : "image/jpeg",
                Name = string.IsNullOrWhiteSpace(attachmentName) ? "reference.png" : attachmentName[..Math.Min(attachmentName.Length, 120)]
            }
            : null;

        await onDelta($"**Luma Agent** · {L("выполняю задачу", "виконую завдання", "working on it")}\n\n");

        try
        {
            for (var step = 1; step <= MaxSteps; step++)
            {
                token.ThrowIfCancellationRequested();
                ctx.Last = ctx.Tab is null ? null : await ObserveAsync(ctx.Tab);

                var prompt = BuildStepPrompt(goal, conversationContext, step, history, memory, lastResult, ctx);
                var userMsg = new AssistantMessage { Role = "user", Text = prompt };
                if (refImage is not null)
                {
                    userMsg.Images.Add(refImage);
                }

                var messages = new List<AssistantMessage>
                {
                    new() { Role = "system", Text = SystemPrompt },
                    userMsg,
                };

                JsonElement? decision = null;
                for (var attempt = 0; attempt < 2 && decision is null; attempt++)
                {
                    var raw = new StringBuilder();
                    try
                    {
                        var quota = await AssistantClient.StreamAsync(accessToken, tier, messages,
                            d => { raw.Append(d); return Task.CompletedTask; }, token, session);
                        if (quota is not null) onQuota?.Invoke(quota);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch when (tier == "pro" && attempt == 0)
                    {
                        // The larger model may be unavailable; continue the task on the fast one.
                        tier = "fast";
                        continue;
                    }
                    decision = ParseDecision(raw.ToString());
                    if (decision is null)
                        messages.Add(new AssistantMessage { Role = "user", Text = "Ответ не распознан. Верни ТОЛЬКО один JSON-объект с полями thought и action." });
                }

                if (decision is null)
                {
                    history.Add($"{step}. (ошибка: модель не вернула действие)");
                    lastResult = "Предыдущий ответ не был валидным JSON. Ответь строго одним JSON-объектом.";
                    continue;
                }

                var d = decision.Value;
                var action = Str(d, "action").ToLowerInvariant();
                var thought = Str(d, "thought");
                var mem = Str(d, "memory");
                if (mem.Length > 0 && memory.Count < 30) memory.Add(mem.Length > 400 ? mem[..400] : mem);

                if (action is "done" or "finish" or "answer")
                {
                    var answer = Str(d, "answer");
                    if (answer.Length == 0) answer = thought;
                    await onDelta("\n" + answer.Trim() + "\n");
                    return answer.Trim();
                }
                if (action is "ask_user" or "ask")
                {
                    var question = Str(d, "question");
                    if (question.Length == 0) question = thought;
                    await onDelta("\n" + question.Trim() + "\n");
                    return question.Trim();
                }

                var signature = action + "|" + Str(d, "id") + "|" + Str(d, "url") + "|" + Str(d, "text") + "|" + Str(d, "query") + "|" + Str(d, "direction");
                recent.Add(signature);
                if (recent.Count > 4) recent.RemoveAt(0);
                var looping = recent.Count >= 3 && recent.Skip(recent.Count - 3).All(s => s == signature) && action != "scroll";

                var label = DescribeAction(action, d, ctx, L);
                string display;
                if (!string.IsNullOrWhiteSpace(thought))
                {
                    var cleanThought = Regex.Replace(thought.Trim(), @"\s+", " ");
                    var firstSentence = cleanThought.Split(new[] { '.', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? cleanThought;
                    if (firstSentence.Length > 110) firstSentence = firstSentence[..110].TrimEnd() + "…";
                    display = firstSentence.Equals(label, StringComparison.OrdinalIgnoreCase)
                        ? label
                        : $"{firstSentence} ({label})";
                }
                else
                {
                    display = label;
                }
                await onDelta($"- {display}\n");

                string result;
                try
                {
                    result = await PerformAsync(action, d, ctx, window, token);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    result = "Ошибка выполнения: " + ex.Message;
                }
                if (looping) result += "\nВНИМАНИЕ: ты повторяешь одно и то же действие без прогресса. Смени подход.";

                lastResult = result;
                var shortResult = Regex.Replace(result, @"\s+", " ");
                if (shortResult.Length > 180) shortResult = shortResult[..180] + "…";
                var thoughtShort = thought.Length > 140 ? thought[..140] + "…" : thought;
                history.Add($"{step}. {signature.Replace("|||", "|").TrimEnd('|')} — {thoughtShort} → {shortResult}");
                if (history.Count > 18) history.RemoveAt(0);
            }

            var timeout = L(
                "\nНе удалось полностью завершить задачу за отведённое число шагов. Последняя открытая страница оставлена перед вами.",
                "\nНе вдалося повністю завершити завдання за відведену кількість кроків. Остання сторінка залишена відкритою.",
                "\nCould not fully finish the task within the step limit. The last page is left open for you.");
            if (memory.Count > 0) timeout += "\n\n" + L("Что удалось выяснить:", "Що вдалося з'ясувати:", "What I found:") + "\n" + string.Join("\n", memory.Select(m => "- " + m));
            await onDelta(timeout + "\n");
            return timeout;
        }
        catch (OperationCanceledException)
        {
            var cancel = L("\nДействие агента отменено.", "\nДію агента скасовано.", "\nAgent action canceled.");
            await onDelta(cancel);
            return cancel;
        }
        catch (Exception ex)
        {
            App.Log(ex);
            var error = L("\nОшибка агента: ", "\nПомилка агента: ", "\nAgent error: ") + ex.Message;
            await onDelta(error);
            return error;
        }
    }

    // ───────────────────────────── prompt ─────────────────────────────

    private static string BuildStepPrompt(string goal, string conversation, int step, List<string> history,
        List<string> memory, string lastResult, AgentContext ctx)
    {
        var sb = new StringBuilder();
        sb.AppendLine("ЗАДАЧА ПОЛЬЗОВАТЕЛЯ: " + goal);
        if (!string.IsNullOrWhiteSpace(conversation))
        {
            sb.AppendLine();
            sb.AppendLine("Контекст предыдущего диалога (для понимания отсылок вроде «второй», «его», «этот»):");
            sb.AppendLine(conversation);
        }
        sb.AppendLine();
        sb.AppendLine($"Шаг {step} из {MaxSteps}.");
        if (history.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("ВЫПОЛНЕННЫЕ ШАГИ:");
            foreach (var h in history) sb.AppendLine(h);
        }
        if (memory.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("ЗАПОМНЕНО:");
            foreach (var m in memory) sb.AppendLine("- " + m);
        }
        if (!string.IsNullOrWhiteSpace(lastResult))
        {
            sb.AppendLine();
            sb.AppendLine("РЕЗУЛЬТАТ ПОСЛЕДНЕГО ДЕЙСТВИЯ:");
            sb.AppendLine(lastResult.Length > 7000 ? lastResult[..7000] + "…" : lastResult);
        }

        sb.AppendLine();
        sb.AppendLine("ОТКРЫТЫЕ ВКЛАДКИ:");
        var tabs = ctx.Window.Tabs.ToList();
        for (var i = 0; i < tabs.Count && i < 15; i++)
        {
            var t = tabs[i];
            var mark = ReferenceEquals(t, ctx.Tab) ? " ← вкладка агента" : ReferenceEquals(t, ctx.Window.CurrentTab) ? " (активная)" : "";
            var url = t.IsInternal ? (t.IsHome ? "стартовая страница" : t.InternalPageKind) : t.ActiveUrl;
            sb.AppendLine($"[{i}] {Trim(t.DisplayTitle, 70)} — {Trim(url, 100)}{mark}");
        }

        sb.AppendLine();
        var o = ctx.Last;
        if (ctx.Tab is null || o is null)
        {
            sb.AppendLine("ТЕКУЩАЯ СТРАНИЦА: вкладка агента ещё не открыта (у пользователя стартовая страница браузера). Начни с navigate или web_search.");
        }
        else
        {
            sb.AppendLine("ТЕКУЩАЯ СТРАНИЦА:");
            sb.AppendLine("Заголовок: " + o.Title);
            sb.AppendLine("Адрес: " + o.Url);
            var pct = o.MaxScroll <= 0 ? 100 : (int)Math.Round(100.0 * o.ScrollY / o.MaxScroll);
            sb.AppendLine(o.MaxScroll <= 0
                ? "Прокрутка: страница помещается в экран (или прокручивается внутренний контейнер)."
                : $"Прокрутка: {pct}% ({(pct >= 99 ? "конец страницы" : "можно ниже")}).");
            if (o.Dialog) sb.AppendLine("На странице открыт диалог/всплывающее окно.");
            sb.AppendLine();
            sb.AppendLine("ЭЛЕМЕНТЫ НА ЭКРАНЕ:");
            if (o.Elements.Count == 0) sb.AppendLine("(не найдено — возможно, страница ещё грузится)");
            foreach (var e in o.Elements) sb.AppendLine(e);
            sb.AppendLine();
            sb.AppendLine("ВИДИМЫЙ ТЕКСТ:");
            var maxTextLen = (ctx.IsTestSolving || o.Elements.Any(e => e.Contains("option") || e.Contains("[вопрос]"))) ? 600 : 5000;
            sb.AppendLine(o.Text.Length > maxTextLen ? o.Text[..maxTextLen] + "…" : o.Text);
            if (ctx.IsTestSolving || o.Elements.Any(e => e.Contains("option") || e.Contains("[вопрос]")))
            {
                sb.AppendLine();
                sb.AppendLine("ИНСТРУКЦИЯ К ТЕСТУ (РЕШАЙ БЫСТРО И ТОЧНО):");
                sb.AppendLine("1. Посмотри на вопрос [вопрос] и сравни с вариантами option [N]. Выбирай правильный ответ по смыслу.");
                sb.AppendLine("2. Если вопрос с одним выбором — сразу жми нужный вариант: {\"thought\":\"Вопрос: ...\",\"action\":\"click\",\"id\":N}");
                sb.AppendLine("3. Если вопрос с несколькими вариантами — выбери ВСЕ правильные варианты и кнопку «Відповісти» / «Далі» за ОДИН шаг через batch:");
                sb.AppendLine("   {\"thought\":\"Вопрос: ...\",\"action\":\"batch\",\"actions\":[{\"action\":\"click\",\"id\":A},{\"action\":\"click\",\"id\":B},{\"action\":\"click\",\"id\":SubmitId}]}");
                sb.AppendLine("4. ВАЖНО: НЕ нажимай на уже (отмечено) варианты повторно, чтобы случайно не снять отметку!");
                sb.AppendLine("5. НЕ используй find_text, scroll или eval — все варианты уже перед тобой!");
            }
        }
        if (ctx.HasReferenceImage)
        {
            sb.AppendLine();
            sb.AppendLine("ИЗОБРАЖЕНИЕ-РЕФЕРЕНС ОТ ПОЛЬЗОВАТЕЛЯ:");
            sb.AppendLine("Пользователь прикрепил картинку-образец к задаче (ты видишь её во входных данных).");
            sb.AppendLine("Внимательно определи: стиль рисовки (скетч, аниме, 3D и т.д.), персонажа/объект, цветовую гамму, настроение и ключевые черты.");
            sb.AppendLine("Используй эти визуальные теги для точного поиска в Pinterest, Google Картинках и других сервисах (на английском языке для максимальной релевантности пинов)!");
        }
        sb.AppendLine();
        sb.AppendLine("Выбери следующее действие. Ответ — один JSON-объект.");
        return sb.ToString();
    }

    private static string DescribeAction(string action, JsonElement d, AgentContext ctx, Func<string, string, string, string> L)
    {
        string El()
        {
            var id = Int(d, "id", -1);
            var line = ctx.Last?.Elements.FirstOrDefault(e => e.StartsWith($"[{id}] ", StringComparison.Ordinal)) ?? "";
            var m = Regex.Match(line, "\"([^\"]*)\"");
            var text = m.Success ? m.Groups[1].Value : $"#{id}";
            return Trim(text, 60);
        }
        return action switch
        {
            "navigate" => L("Открываю ", "Відкриваю ", "Opening ") + PrettyUrl(Str(d, "url")),
            "new_tab" => L("Открываю в новой вкладке ", "Відкриваю в новій вкладці ", "Opening in a new tab ") + PrettyUrl(Str(d, "url")),
            "web_search" => L("Ищу в интернете: «", "Шукаю в інтернеті: «", "Searching the web: «") + Trim(Str(d, "query"), 80) + "»",
            "save_image" => L("Сохраняю изображение", "Зберігаю зображення", "Saving image"),
            "copy_image" => L("Копирую изображение в буфер", "Копіюю зображення в буфер", "Copying image to clipboard"),
            "click" => L("Нажимаю «", "Натискаю «", "Clicking «") + El() + "»",
            "js_click" => L("Нажимаю через JS «", "Натискаю через JS «", "JS-clicking «") + El() + "»",
            "hover" => L("Навожу курсор на «", "Наводжу курсор на «", "Hovering over «") + El() + "»",
            "check" => L("Отмечаю «", "Відмічаю «", "Checking «") + El() + "»",
            "drag" => L("Перетаскиваю ползунок «", "Перетягую повзунок «", "Dragging «") + El() + "»",
            "eval" => !string.IsNullOrWhiteSpace(Str(d, "description"))
                ? Str(d, "description")
                : L("Анализирую элементы страницы", "Аналізую елементи сторінки", "Analyzing page elements"),
            "batch" => DescribeBatch(d, ctx, L),
            "type" => L("Ввожу «", "Вводжу «", "Typing «") + Trim(Str(d, "text"), 60) + "»" + (Bool(d, "submit") ? " ⏎" : ""),
            "select" => L("Выбираю «", "Обираю «", "Selecting «") + Trim(Str(d, "text"), 60) + "»",
            "press" => L("Нажимаю клавишу ", "Натискаю клавішу ", "Pressing ") + Str(d, "key"),
            "scroll" => Str(d, "direction") == "up" ? L("Прокручиваю вверх", "Прокручую вгору", "Scrolling up") : L("Прокручиваю страницу", "Прокручую сторінку", "Scrolling down"),
            "find_text" => L("Ищу на странице: «", "Шукаю на сторінці: «", "Finding on page: «") + Trim(Str(d, "query"), 60) + "»",
            "read" => L("Читаю страницу", "Читаю сторінку", "Reading the page"),
            "back" => L("Возвращаюсь назад", "Повертаюся назад", "Going back"),
            "switch_tab" => L("Переключаюсь на вкладку", "Перемикаюся на вкладку", "Switching tab"),
            "wait" => L("Жду загрузку", "Чекаю завантаження", "Waiting for the page"),
            _ => action,
        };
    }

    private static string DescribeBatch(JsonElement d, AgentContext ctx, Func<string, string, string, string> L)
    {
        if (d.TryGetProperty("actions", out var arr) && arr.ValueKind == JsonValueKind.Array)
        {
            var parts = arr.EnumerateArray().Take(4)
                .Select(sub => DescribeAction(Str(sub, "action").ToLowerInvariant(), sub, ctx, L))
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .ToList();
            if (parts.Count > 0) return string.Join(" → ", parts);
        }
        return L("Выполняю цепочку действий", "Виконую ланцюжок дій", "Executing batch actions");
    }

    // ───────────────────────────── actions ─────────────────────────────

    private static async Task<string> PerformAsync(string action, JsonElement d, AgentContext ctx, MainWindow window, CancellationToken token)
    {
        switch (action)
        {
            case "navigate":
            case "new_tab":
            {
                var url = NormalizeUrl(Str(d, "url"));
                if (url is null) return "Некорректный адрес. Укажи полный URL, начинающийся с https://";
                if (action == "new_tab" || ctx.Tab is null || ctx.Tab.IsInternal || !window.Tabs.Contains(ctx.Tab))
                {
                    ctx.Tab = await window.AddTabAsync(url, activate: true);
                }
                else
                {
                    window.CurrentTab = ctx.Tab;
                    ctx.Tab.ActiveView.CoreWebView2?.Navigate(url);
                }
                await WaitForLoadAsync(ctx.Tab, 9000, token, navigation: true);
                return await PageSummaryAsync(ctx.Tab, "Открыто");
            }

            case "web_search":
            {
                var query = Str(d, "query");
                if (query.Length == 0) return "Пустой запрос.";
                var results = await window.AgentWebSearchAsync(query, token);
                if (results.Count == 0) return "Поиск ничего не дал. Попробуй другую формулировку или navigate на https://www.google.com/search?q=...";
                var sb = new StringBuilder("Результаты поиска:\n");
                var i = 1;
                foreach (var r in results.Take(8)) sb.AppendLine($"{i++}. {r.Title} — {r.Url}\n   {Trim(r.Snippet, 220)}");
                return sb.ToString();
            }

            case "switch_tab":
            {
                var index = Int(d, "tab", -1);
                var tabs = window.Tabs.ToList();
                if (index < 0 || index >= tabs.Count) return "Нет вкладки с таким номером.";
                ctx.Tab = tabs[index];
                window.CurrentTab = ctx.Tab;
                await WaitForLoadAsync(ctx.Tab, 3000, token, navigation: false);
                return await PageSummaryAsync(ctx.Tab, "Переключено на вкладку");
            }

            case "wait":
            {
                var seconds = Math.Clamp(Int(d, "seconds", 2), 1, 8);
                await Task.Delay(seconds * 1000, token);
                return "Подождал " + seconds + " с.";
            }
        }

        if (ctx.Tab is null || ctx.Tab.ActiveView?.CoreWebView2 is null || !window.Tabs.Contains(ctx.Tab))
            return "Вкладка агента не открыта. Сначала navigate.";

        var tab = ctx.Tab;
        var core = tab.ActiveView.CoreWebView2;
        if (!ReferenceEquals(window.CurrentTab, tab)) window.CurrentTab = tab;
        var tabsBefore = window.Tabs.ToList();
        var urlBefore = core.Source;
        string outcome;

        switch (action)
        {
            case "click":
            {
                var id = Int(d, "id", -1);
                var point = await JsonAsync(core, BrowserAgentScripts.PointScript(id));
                if (point is null || !Bool(point.Value, "ok"))
                    return $"Элемент [{id}] не найден (страница изменилась). Посмотри на новый список элементов.";
                await Task.Delay(40, token);
                var x = Dbl(point.Value, "x");
                var y = Dbl(point.Value, "y");
                var clicked = await TrustedClickAsync(core, x, y);
                if (!clicked)
                {
                    await core.ExecuteScriptAsync(BrowserAgentScripts.JsClickScript(id));
                }
                outcome = "Нажато.";
                break;
            }

            case "js_click":
            {
                var id = Int(d, "id", -1);
                var res = await JsonAsync(core, BrowserAgentScripts.JsClickScript(id));
                if (res is null || !Bool(res.Value, "ok"))
                    return $"Элемент [{id}] не найден для JS-клика.";
                outcome = "Нажато через JS.";
                break;
            }

            case "hover":
            {
                var id = Int(d, "id", -1);
                var hoverRes = await JsonAsync(core, BrowserAgentScripts.HoverScript(id));
                if (hoverRes is null || !Bool(hoverRes.Value, "ok"))
                    return $"Элемент [{id}] не найден для наведения курсора.";
                outcome = "Курсор наведён.";
                break;
            }

            case "check":
            {
                var id = Int(d, "id", -1);
                bool? want = d.TryGetProperty("checked", out var ch) ? ch.GetBoolean() : null;
                var res = await JsonAsync(core, BrowserAgentScripts.CheckScript(id, want));
                if (res is null || !Bool(res.Value, "ok"))
                    return $"Элемент [{id}] не найден для изменения отметки.";
                outcome = Bool(res.Value, "checked") ? "Отметка установлена." : "Отметка снята.";
                break;
            }

            case "drag":
            {
                var id = Int(d, "id", -1);
                var dx = Dbl(d, "deltaX", 50);
                var dy = Dbl(d, "deltaY", 0);
                var res = await JsonAsync(core, BrowserAgentScripts.DragScript(id, dx, dy));
                if (res is null || !Bool(res.Value, "ok"))
                    return $"Элемент [{id}] не найден для перетаскивания.";
                outcome = "Перетащено.";
                break;
            }

            case "eval":
            {
                var script = Str(d, "script");
                if (string.IsNullOrWhiteSpace(script)) return "Пустой скрипт.";
                var res = await JsonAsync(core, BrowserAgentScripts.EvalScript(script));
                if (res is null) return "Ошибка выполнения eval.";
                outcome = Bool(res.Value, "ok") ? "Скрипт выполнен: " + Str(res.Value, "value") : "Ошибка скрипта: " + Str(res.Value, "error");
                break;
            }

            case "batch":
            {
                if (!d.TryGetProperty("actions", out var arr) || arr.ValueKind != JsonValueKind.Array)
                    return "Параметр 'actions' должен быть массивом действий.";
                var results = new List<string>();
                foreach (var item in arr.EnumerateArray().Take(6))
                {
                    var subAction = Str(item, "action").ToLowerInvariant();
                    results.Add(await PerformAsync(subAction, item, ctx, window, token));
                    await Task.Delay(150, token);
                }
                outcome = "Серия действий завершена:\n" + string.Join("\n", results);
                break;
            }

            case "type":
            {
                var id = Int(d, "id", -1);
                var text = Str(d, "text");
                var clear = !d.TryGetProperty("clear", out var cl) || cl.ValueKind != JsonValueKind.False;
                var focus = await JsonAsync(core, BrowserAgentScripts.FocusFieldScript(id, clear));
                if (focus is null || !Bool(focus.Value, "ok"))
                    return $"Поле [{id}] не найдено (страница изменилась).";
                if (Bool(focus.Value, "select"))
                {
                    var sel = await JsonAsync(core, BrowserAgentScripts.SelectOptionScript(id, text));
                    outcome = sel is not null && Bool(sel.Value, "ok") ? "Выбрано: " + Str(sel.Value, "chosen") : "Такого варианта нет в списке.";
                    break;
                }
                if (!Bool(focus.Value, "focused"))
                {
                    // Some custom fields only take focus from a real click.
                    await TrustedClickAsync(core, Dbl(focus.Value, "x"), Dbl(focus.Value, "y"));
                    await Task.Delay(150, token);
                    await JsonAsync(core, BrowserAgentScripts.FocusFieldScript(id, clear));
                }
                if (clear) await KeyAsync(core, "Delete");
                if (text.Length > 0)
                {
                    try { await core.CallDevToolsProtocolMethodAsync("Input.insertText", JsonSerializer.Serialize(new { text })); }
                    catch { await core.ExecuteScriptAsync($"document.execCommand('insertText', false, {JsonSerializer.Serialize(text)})"); }
                }
                if (Bool(d, "submit"))
                {
                    await Task.Delay(150, token);
                    await KeyAsync(core, "Enter");
                }
                outcome = Bool(d, "submit") ? "Текст введён и отправлен Enter." : "Текст введён.";
                break;
            }

            case "select":
            {
                var sel = await JsonAsync(core, BrowserAgentScripts.SelectOptionScript(Int(d, "id", -1), Str(d, "text")));
                outcome = sel is not null && Bool(sel.Value, "ok") ? "Выбрано: " + Str(sel.Value, "chosen") : "Не удалось выбрать вариант (это не обычный список — попробуй click).";
                break;
            }

            case "press":
            {
                var key = Str(d, "key");
                if (!await KeyAsync(core, key)) return "Неизвестная клавиша: " + key;
                outcome = "Клавиша нажата.";
                break;
            }

            case "scroll":
            {
                var amount = Math.Clamp(Int(d, "amount", 1), 1, 6);
                var up = Str(d, "direction").Equals("up", StringComparison.OrdinalIgnoreCase);
                var vh = ctx.Last is { Vh: > 0 } lastVh ? lastVh.Vh : 800;
                var vw = ctx.Last is { Vw: > 0 } lastVw ? lastVw.Vw : 1200;
                var beforeY = ctx.Last?.ScrollY ?? 0;
                for (var i = 0; i < amount; i++)
                {
                    var delta = (int)(vh * 0.85) * (up ? -1 : 1);
                    var wheeled = false;
                    try
                    {
                        await core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", JsonSerializer.Serialize(new
                        {
                            type = "mouseWheel", x = vw / 2.0, y = vh / 2.0, deltaX = 0, deltaY = delta, modifiers = 0,
                        }));
                        wheeled = true;
                    }
                    catch { }
                    if (!wheeled) await core.ExecuteScriptAsync(BrowserAgentScripts.ScrollScript(delta));
                    await Task.Delay(450, token);
                }
                // Lazy lists (comments, feeds) load after the scroll settles.
                await Task.Delay(700, token);
                var after = await ObserveAsync(tab);
                ctx.Last = after;
                outcome = after is not null && after.MaxScroll > 0 && Math.Abs(after.ScrollY - beforeY) < 5 && !up
                    ? "Страница не прокрутилась (возможно, конец страницы или прокручивается внутренний блок)."
                    : "Прокручено.";
                break;
            }

            case "find_text":
            {
                var query = Str(d, "query");
                var res = await JsonAsync(core, BrowserAgentScripts.FindTextScript(query));
                if (res is null) return "Не удалось выполнить поиск по странице.";
                var count = Int(res.Value, "count", 0);
                if (count == 0) return $"«{query}» на странице не найдено. Если это лента/комментарии — прокрути ниже, чтобы подгрузить ещё, или ищи по другим словам.";
                var sb = new StringBuilder($"Найдено совпадений: {count}. Первое прокручено на экран и подсвечено.\n");
                if (res.Value.TryGetProperty("matches", out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    var i = 1;
                    foreach (var m in arr.EnumerateArray()) sb.AppendLine($"{i++}) {m.GetString()}");
                }
                return sb.ToString();
            }

            case "read":
            {
                var res = await JsonAsync(core, BrowserAgentScripts.ReadScript(Math.Max(0, Int(d, "offset", 0))));
                if (res is null) return "Не удалось прочитать страницу.";
                var total = Int(res.Value, "total", 0);
                var offset = Int(res.Value, "offset", 0);
                var text = Str(res.Value, "text");
                var next = offset + text.Length;
                return $"Текст страницы (символы {offset}–{next} из {total}):\n{text}" + (next < total ? $"\n… продолжение: read offset {next}" : "\n(конец текста)");
            }

            case "save_image":
            case "copy_image":
            {
                var id = Int(d, "id", -1);
                var url = Str(d, "url");
                if (string.IsNullOrWhiteSpace(url))
                {
                    if (id >= 0)
                    {
                        var imgRes = await JsonAsync(core, BrowserAgentScripts.ExtractImageUrlScript(id));
                        if (imgRes is not null && Bool(imgRes.Value, "ok"))
                            url = Str(imgRes.Value, "url");
                    }
                    if (string.IsNullOrWhiteSpace(url))
                    {
                        var pageImgRes = await JsonAsync(core, BrowserAgentScripts.ExtractPageImageScript);
                        if (pageImgRes is not null && Bool(pageImgRes.Value, "ok"))
                            url = Str(pageImgRes.Value, "url");
                    }
                }

                if (string.IsNullOrWhiteSpace(url))
                    return "Не удалось найти изображение на указанном элементе. Укажи правильный [id] картинки или открой страницу с изображением.";

                var upgraded = UpgradeImageUrl(url);
                var bytes = await DownloadImageBytesAsync(core, upgraded) ?? await DownloadImageBytesAsync(core, url);
                if (bytes is null || bytes.Length == 0)
                    return $"Не удалось скачать изображение по адресу {url}.";

                var customName = Str(d, "name");
                var ext = ".png";
                if (url.Contains(".jpg", StringComparison.OrdinalIgnoreCase) || url.Contains(".jpeg", StringComparison.OrdinalIgnoreCase)) ext = ".jpg";
                else if (url.Contains(".webp", StringComparison.OrdinalIgnoreCase)) ext = ".webp";

                string fileName;
                if (!string.IsNullOrWhiteSpace(customName))
                {
                    fileName = Path.GetFileName(customName);
                    if (!Path.HasExtension(fileName)) fileName += ext;
                }
                else
                {
                    fileName = $"luma_avatar_{DateTime.Now:yyyyMMdd_HHmmss}{ext}";
                }

                var downloads = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
                if (!Directory.Exists(downloads)) Directory.CreateDirectory(downloads);
                var filePath = Path.Combine(downloads, fileName);
                await File.WriteAllBytesAsync(filePath, bytes, token);

                window.Dispatcher.Invoke(() =>
                {
                    try
                    {
                        using var ms = new MemoryStream(bytes);
                        var bi = new System.Windows.Media.Imaging.BitmapImage();
                        bi.BeginInit();
                        bi.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                        bi.StreamSource = ms;
                        bi.EndInit();
                        bi.Freeze();

                        var data = new System.Windows.DataObject();
                        data.SetImage(bi);
                        data.SetFileDropList(new System.Collections.Specialized.StringCollection { filePath });
                        System.Windows.Clipboard.SetDataObject(data, true);
                        window.ShowToast("Изображение сохранено", $"{fileName} (скопировано в буфер обмена)");
                    }
                    catch (Exception ex)
                    {
                        App.Log(ex);
                    }
                });

                outcome = $"Изображение успешно получено в высоком качестве и сохранено в «{filePath}». Также оно скопировано в буфер обмена Windows (готово для быстрой вставки через Ctrl+V в Telegram, Discord или любую программу).";
                break;
            }

            case "back":
            {
                if (core.CanGoBack) core.GoBack();
                else return "Назад идти некуда.";
                await WaitForLoadAsync(tab, 7000, token, navigation: true);
                return await PageSummaryAsync(tab, "Вернулся");
            }

            default:
                return $"Неизвестное действие «{action}». Используй только действия из списка.";
        }

        var isTestContext = ctx.IsTestSolving
            || (ctx.Last?.Url?.Contains("naurok", StringComparison.OrdinalIgnoreCase) == true)
            || (ctx.Last?.Text?.Contains("тест", StringComparison.OrdinalIgnoreCase) == true && ctx.Last?.Elements.Any(e => e.Contains("option") || e.Contains("контекст вопроса")) == true);

        var clickedId = Int(d, "id", -1);
        var isOptionInteraction = (action is "click" or "js_click" or "check") &&
            (ctx.Last?.Elements.Any(e => e.StartsWith($"[{clickedId}]") && (e.Contains("option") || e.Contains("отмечено") || e.Contains("контекст вопроса"))) == true);

        var isQuick = action is "check" or "hover" or "select" or "type" or "press" or "batch" or "eval" or "save_image" or "copy_image";

        if (isTestContext || isOptionInteraction)
        {
            // Pacing delay: Give the test platform and animations 1100ms to register the answer and transition smoothly
            await Task.Delay(1100, token);
        }
        else
        {
            await Task.Delay(isQuick ? 100 : 200, token);
        }

        var newTab = window.Tabs.Where(t => !tabsBefore.Contains(t) && !t.IsInternal).LastOrDefault();
        if (newTab is not null)
        {
            ctx.Tab = newTab;
            window.CurrentTab = newTab;
            await WaitForLoadAsync(newTab, 6000, token, navigation: true);
            return outcome + " Открылась новая вкладка — агент перешёл в неё. " + await PageSummaryAsync(newTab, "Страница");
        }
        var navigated = !string.Equals(core.Source, urlBefore, StringComparison.Ordinal);
        if (navigated)
        {
            await WaitForLoadAsync(tab, 6000, token, navigation: true);
            return outcome + " " + await PageSummaryAsync(tab, "Перешёл на страницу");
        }
        else if (isTestContext || isOptionInteraction || isQuick)
        {
            return outcome;
        }
        else
        {
            await WaitForLoadAsync(tab, 1200, token, navigation: false);
            return outcome;
        }
    }

    private static async Task<bool> TrustedClickAsync(CoreWebView2 core, double x, double y)
    {
        try
        {
            await core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", JsonSerializer.Serialize(new { type = "mouseMoved", x, y, button = "none", modifiers = 0 }));
            await core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", JsonSerializer.Serialize(new { type = "mousePressed", x, y, button = "left", buttons = 1, clickCount = 1, modifiers = 0 }));
            await Task.Delay(40);
            await core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", JsonSerializer.Serialize(new { type = "mouseReleased", x, y, button = "left", buttons = 0, clickCount = 1, modifiers = 0 }));
            return true;
        }
        catch (Exception ex)
        {
            App.Log(ex);
            return false;
        }
    }

    private static readonly Dictionary<string, (string Key, string Code, int Vk, string Text)> Keys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["enter"] = ("Enter", "Enter", 13, "\r"),
        ["return"] = ("Enter", "Enter", 13, "\r"),
        ["escape"] = ("Escape", "Escape", 27, ""),
        ["esc"] = ("Escape", "Escape", 27, ""),
        ["tab"] = ("Tab", "Tab", 9, ""),
        ["backspace"] = ("Backspace", "Backspace", 8, ""),
        ["delete"] = ("Delete", "Delete", 46, ""),
        ["arrowdown"] = ("ArrowDown", "ArrowDown", 40, ""),
        ["down"] = ("ArrowDown", "ArrowDown", 40, ""),
        ["arrowup"] = ("ArrowUp", "ArrowUp", 38, ""),
        ["up"] = ("ArrowUp", "ArrowUp", 38, ""),
        ["arrowleft"] = ("ArrowLeft", "ArrowLeft", 37, ""),
        ["arrowright"] = ("ArrowRight", "ArrowRight", 39, ""),
        ["pagedown"] = ("PageDown", "PageDown", 34, ""),
        ["pageup"] = ("PageUp", "PageUp", 33, ""),
        ["home"] = ("Home", "Home", 36, ""),
        ["end"] = ("End", "End", 35, ""),
        ["space"] = (" ", "Space", 32, " "),
    };

    private static async Task<bool> KeyAsync(CoreWebView2 core, string name)
    {
        if (!Keys.TryGetValue((name ?? "").Trim(), out var k)) return false;
        try
        {
            object down = k.Text.Length > 0
                ? new { type = "keyDown", key = k.Key, code = k.Code, windowsVirtualKeyCode = k.Vk, nativeVirtualKeyCode = k.Vk, text = k.Text, unmodifiedText = k.Text }
                : new { type = "rawKeyDown", key = k.Key, code = k.Code, windowsVirtualKeyCode = k.Vk, nativeVirtualKeyCode = k.Vk };
            await core.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent", JsonSerializer.Serialize(down));
            await core.CallDevToolsProtocolMethodAsync("Input.dispatchKeyEvent", JsonSerializer.Serialize(new { type = "keyUp", key = k.Key, code = k.Code, windowsVirtualKeyCode = k.Vk, nativeVirtualKeyCode = k.Vk }));
            return true;
        }
        catch (Exception ex)
        {
            App.Log(ex);
            var js = $"(()=>{{const e=document.activeElement||document.body;['keydown','keyup'].forEach(t=>e.dispatchEvent(new KeyboardEvent(t,{{key:{JsonSerializer.Serialize(k.Key)},code:{JsonSerializer.Serialize(k.Code)},keyCode:{k.Vk},which:{k.Vk},bubbles:true}})));if({JsonSerializer.Serialize(k.Key)}==='Enter'&&e.form)e.form.requestSubmit?e.form.requestSubmit():e.form.submit();}})()";
            await core.ExecuteScriptAsync(js);
            return true;
        }
    }

    private static string UpgradeImageUrl(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return url;
        // Pinterest: upgrade small thumbnail to high-res (736x or originals)
        if (url.Contains("pinimg.com", StringComparison.OrdinalIgnoreCase))
        {
            url = Regex.Replace(url, @"/(?:236x|474x|564x)/", "/736x/");
        }
        // Twitter / X: upgrade to large
        if (url.Contains("pbs.twimg.com", StringComparison.OrdinalIgnoreCase))
        {
            url = Regex.Replace(url, @"name=(?:small|medium|thumb)", "name=large");
        }
        return url;
    }

    private static async Task<byte[]?> DownloadImageBytesAsync(CoreWebView2? core, string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;
        if (url.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
        {
            var comma = url.IndexOf(',');
            if (comma > 0)
            {
                try { return Convert.FromBase64String(url[(comma + 1)..]); } catch { }
            }
        }

        // Try page context fetch first (preserves auth cookies, CORS bypass for same-origin)
        if (core is not null && !url.StartsWith("blob:", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var js = $$"""
                (async () => {
                  try {
                    const r = await fetch({{JsonSerializer.Serialize(url)}}, { credentials: 'include' });
                    if (!r.ok) return null;
                    const blob = await r.blob();
                    return await new Promise((res, rej) => {
                      const reader = new FileReader();
                      reader.onloadend = () => {
                        const s = reader.result;
                        res(typeof s === 'string' ? s.split(',')[1] : null);
                      };
                      reader.onerror = rej;
                      reader.readAsDataURL(blob);
                    });
                  } catch (_) { return null; }
                })()
                """;
                var raw = await core.ExecuteScriptAsync(js);
                if (!string.IsNullOrWhiteSpace(raw) && raw != "null")
                {
                    var b64 = JsonSerializer.Deserialize<string>(raw);
                    if (!string.IsNullOrWhiteSpace(b64))
                    {
                        return Convert.FromBase64String(b64);
                    }
                }
            }
            catch { }
        }

        // Fallback: HttpClient
        try
        {
            using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true });
            client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/130.0.0.0 Safari/537.36");
            client.Timeout = TimeSpan.FromSeconds(15);
            return await client.GetByteArrayAsync(url);
        }
        catch (Exception ex)
        {
            App.Log(ex);
            return null;
        }
    }

    // ───────────────────────────── page state ─────────────────────────────

    private static async Task<Observation?> ObserveAsync(BrowserTab tab)
    {
        var core = tab.ActiveView?.CoreWebView2;
        if (core is null) return null;
        try
        {
            var el = await JsonAsync(core, BrowserAgentScripts.ObserveScript);
            if (el is null) return new Observation(tab.DisplayTitle, core.Source, "", [], 0, 0, 1200, 800, false);
            var v = el.Value;
            var elements = new List<string>();
            if (v.TryGetProperty("elements", out var arr) && arr.ValueKind == JsonValueKind.Array)
                elements.AddRange(arr.EnumerateArray().Select(x => x.GetString() ?? "").Where(x => x.Length > 0));
            return new Observation(Str(v, "title"), Str(v, "url"), Str(v, "text"), elements,
                Int(v, "scrollY", 0), Int(v, "maxScroll", 0), Int(v, "vw", 1200), Int(v, "vh", 800), Bool(v, "dialog"));
        }
        catch (Exception ex)
        {
            App.Log(ex);
            return new Observation(tab.DisplayTitle, core.Source, "", [], 0, 0, 1200, 800, false);
        }
    }

    private static Task<string> PageSummaryAsync(BrowserTab tab, string prefix)
    {
        var core = tab.ActiveView?.CoreWebView2;
        if (core is null) return Task.FromResult(prefix + ".");
        string title;
        try { title = core.DocumentTitle; } catch { title = tab.DisplayTitle; }
        return Task.FromResult($"{prefix}: {Trim(title, 100)} ({Trim(core.Source, 140)})");
    }

    private static async Task WaitForLoadAsync(BrowserTab tab, int timeoutMs, CancellationToken token, bool navigation)
    {
        var start = DateTime.UtcNow;
        while (tab.ActiveView?.CoreWebView2 is null)
        {
            if ((DateTime.UtcNow - start).TotalMilliseconds > timeoutMs) return;
            await Task.Delay(40, token);
        }
        var core = tab.ActiveView.CoreWebView2;
        if (navigation) await Task.Delay(150, token);
        while ((DateTime.UtcNow - start).TotalMilliseconds < timeoutMs)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                var state = await core.ExecuteScriptAsync("document.readyState");
                if (state == "\"complete\"" || (state == "\"interactive\"" && (DateTime.UtcNow - start).TotalMilliseconds > 1500)) break;
            }
            catch { }
            await Task.Delay(50, token);
        }
        // Let client-side frameworks render after the document itself is ready.
        if (navigation) await Task.Delay(300, token);
    }

    private static async Task<JsonElement?> JsonAsync(CoreWebView2 core, string script)
    {
        var raw = await core.ExecuteScriptAsync(script);
        if (string.IsNullOrWhiteSpace(raw) || raw == "null") return null;
        string? inner;
        try { inner = JsonSerializer.Deserialize<string>(raw); } catch { return null; }
        if (string.IsNullOrWhiteSpace(inner)) return null;
        try
        {
            using var doc = JsonDocument.Parse(inner);
            return doc.RootElement.Clone();
        }
        catch { return null; }
    }

    // ───────────────────────────── helpers ─────────────────────────────

    private static JsonElement? ParseDecision(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var text = raw.Trim();
        // Reasoning models sometimes wrap the object in prose or code fences.
        var start = text.IndexOf('{');
        while (start >= 0)
        {
            var depth = 0;
            var inString = false;
            for (var i = start; i < text.Length; i++)
            {
                var c = text[i];
                if (inString)
                {
                    if (c == '\\') { i++; continue; }
                    if (c == '"') inString = false;
                    continue;
                }
                if (c == '"') inString = true;
                else if (c == '{') depth++;
                else if (c == '}' && --depth == 0)
                {
                    try
                    {
                        using var doc = JsonDocument.Parse(text[start..(i + 1)]);
                        if (doc.RootElement.ValueKind == JsonValueKind.Object && doc.RootElement.TryGetProperty("action", out _))
                            return doc.RootElement.Clone();
                    }
                    catch { }
                    break;
                }
            }
            start = text.IndexOf('{', start + 1);
        }
        return null;
    }

    private static string? NormalizeUrl(string url)
    {
        url = (url ?? "").Trim().Trim('"', '\'', '<', '>', '`');
        if (url.Length == 0) return null;
        if (!url.Contains("://", StringComparison.Ordinal)) url = "https://" + url;
        return Uri.TryCreate(url, UriKind.Absolute, out var u) && (u.Scheme == Uri.UriSchemeHttp || u.Scheme == Uri.UriSchemeHttps) ? u.ToString() : null;
    }

    private static string PrettyUrl(string url)
    {
        if (Uri.TryCreate(NormalizeUrl(url) ?? url, UriKind.Absolute, out var u))
        {
            var host = u.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? u.Host[4..] : u.Host;
            var path = u.AbsolutePath.TrimEnd('/');
            return Trim(host + path, 70);
        }
        return Trim(url, 70);
    }

    private static string Trim(string? s, int max)
    {
        s ??= "";
        return s.Length > max ? s[..max] + "…" : s;
    }

    private static string Str(JsonElement e, string name)
    {
        if (!e.TryGetProperty(name, out var v)) return "";
        return v.ValueKind switch
        {
            JsonValueKind.String => v.GetString() ?? "",
            JsonValueKind.Number => v.GetRawText(),
            JsonValueKind.True => "true",
            JsonValueKind.False => "false",
            _ => "",
        };
    }

    private static int Int(JsonElement e, string name, int fallback)
    {
        if (!e.TryGetProperty(name, out var v)) return fallback;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var n)) return (int)n;
        if (v.ValueKind == JsonValueKind.String && int.TryParse((v.GetString() ?? "").Trim('[', ']', ' ', '#'), out var p)) return p;
        return fallback;
    }

    private static double Dbl(JsonElement e, string name, double fallback = 0)
    {
        if (!e.TryGetProperty(name, out var v)) return fallback;
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)) return d;
        if (v.ValueKind == JsonValueKind.String && double.TryParse(v.GetString(), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var p)) return p;
        return fallback;
    }

    private static bool Bool(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && (v.ValueKind == JsonValueKind.True || (v.ValueKind == JsonValueKind.String && v.GetString() == "true"));
}
