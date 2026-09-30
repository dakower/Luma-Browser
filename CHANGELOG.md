# 2.1.3

- Верхняя панель: исправлен сброс домена на «New tab» во время просмотра сайтов (YouTube и др.).
- Профиль и меню: устранена перезапись имени пользователя («dakower»), статуса и номера версии механизмом локализации.
- Боковая панель: восстановлено автоматическое всплытие при наведении курсора на левый край окна с сохранением защиты от случайных кликов по сайту при закрытой панели.
- Локализация: полностью переработан и дополнен английский языковой пакет интерфейса (устранены артефакты машинного перевода, добавлены все недостающие пункты меню).
- Обновления: подготовлен и подписан пакет автообновления для стабильного канала.

# 2.1.2

- LumaAI: добавлен фоновый веб-поиск (Silent Web Grounding) для актуальных фактов из интернета без отображения поиска пользователю.
- LumaAI: промпты больше не навязаны на русский язык — ассистент отвечает на языке запроса или интерфейса (EN / RU / UK).
- Ассистент: закрытие панели теперь немедленно отменяет активный стрим и не расходует квоту.
- Безопасность: выход из аккаунта теперь полностью очищает историю переписки с ассистентом, сохранённые диалоги и квоту.
- Медиа: закрытие вкладки с PiP или музыкой теперь корректно останавливает плавающее окно видео и обновляет плавающий аудиоплеер.
- Навигация и URL: адресная строка теперь корректно обрабатывает localhost, dev-серверы с портами (на HTTP), прямые IP, Windows-пути (C:\..., file://), blob: и убирает случайные переносы строк.
- Горячие клавиши: добавлены F12 (DevTools), Alt+Home (домой), Alt+Left/Right (назад/вперёд), Ctrl+F (поиск по вкладкам), F5, Ctrl+W, Ctrl+Tab, Ctrl+L, Ctrl+1..9.
- Исправлен перехват Ctrl+Shift+V, мешавший стандартной вставке в Google Docs.
- Фоновые вкладки: ссылки, открытые через контекстное меню или Ctrl/Middle-click, теперь открываются в фоне, не отвлекая от чтения.
- Поиск: клик по карточке видео теперь открывает его в новой вкладке.
- Память и стабильность: предотвращены сбои при внешней блокировке state.json; зум больше не тормозит UI; в кэше перевода устранена утечка памяти.

# 2.1.1

- Исправлены ложные страницы «Не удалось открыть страницу» (в том числе с кодом OperationCanceled): отменённые и заменённые навигации больше не считаются ошибкой, даже когда движок показывает служебную chrome-error страницу.
- Загрузки: завершение загрузки больше не «зависает» — статус, спиннер и сохранение обновляются надёжно даже при сбое уведомления, добавлена страховочная проверка состояния.
- Загрузки: у каждого файла теперь его настоящая иконка Windows (у установщиков — собственная иконка программы), для изображений — миниатюра без блокировки файла.
- Загрузки: убрана некрасивая голубая подсветка строки при наведении; исправлена «дёрганая» анимация спиннеров (двойной центр вращения).
- Состояние браузера сохраняется потокобезопасно; при повреждённом state.json создаётся резервная копия вместо тихого сброса профиля; закрытые вкладки больше не удерживаются в памяти служебными словарями.

# 2.1.0

- Установщик теперь на английском по умолчанию; на экране параметров добавлен выбор языка (English / Русский / Українська), выбранный язык применяется к установщику и к браузеру.
- Язык браузера по умолчанию — английский; при первом запуске используется язык, выбранный в установщике.
- Исправлены ложные страницы «Не удалось открыть страницу»: игнорируются заменённые и отменённые навигации и загрузки файлов, а кратковременные обрывы соединения (ConnectionAborted и др.) автоматически повторяются до показа ошибки.

# 2.0.9

- Кнопки «Назад» и «Вперёд» теперь обновляются сразу после изменения истории WebView2, включая SPA-навигацию через pushState/replaceState; переключать вкладку больше не нужно.

# 2.0.8

- Главный экран использует выбранную поисковую систему; Google теперь выбран по умолчанию, а Luma Search включается только вручную.
- User-Agent и Client Hints синхронизированы с установленным Evergreen WebView2; для Notion, Naurok и challenge-страниц включается совместимый Balanced tracking mode.
- Проверка обновлений начинается сразу при запуске и мгновенно сообщает о найденной подписанной версии до окончания фоновой загрузки.
- Аватар хранится в Supabase Storage, синхронизируется через Auth metadata и profiles, обновляется на сайте и восстанавливается после переустановки.

# 2.0.7

- Repaired webpage translation after public endpoints began returning empty/rate-limited responses.
- Reduced translation concurrency and added browser request headers, POST requests and a second Google endpoint.
- Added multi-query, marker-batch and bounded per-text fallback strategies with automatic recovery.
- Prevented failed unchanged responses from being remembered as a working strategy.
- Kept one-time translation active for lazy and SPA content without adding the domain to Always translate.
- Added translation support for non-Latin scripts while skipping already-Cyrillic text.

# 2.0.6

- Fixed the PowerShell parser error in `build.ps1` caused by `$Path:` inside an expandable string.
- Braced the interpolated path variable and audited all PowerShell scripts for the same invalid variable-colon pattern.

# 2.0.5

- Added Google Dorks support to Luma Search with preserved operator syntax.
- Added dedicated Google reader retrieval for advanced queries.
- Added local enforcement for site, filetype/ext, intitle, inurl and intext filters, including negative operators.
- Kept dork operands, quoted phrases and OR/AND expressions intact during transliteration and layout correction.
- Added an in-page indicator showing active advanced operators.

# 2.0.4

- Added automatic Latin-to-Cyrillic and Cyrillic-to-Latin search variants.
- Added wrong-keyboard-layout correction and common abbreviation expansion.
- Searches alternate query forms across web, image and video providers.
- Improved exact-site ranking and limited Wikipedia/YouTube dominance in general results.
- Increased host diversity so independent sites appear in the first page.

# 2.0.3

- Added stable-release preflight, Windows CI installer artifacts and synchronized version checks.
- Added Authenticode signing and SHA-256 output to the build/release pipeline.
- Made signed security updates available without requiring a Luma Account.
- Hardened the public update endpoint and unified installer/update payloads.
- Updated privacy, security, installer, beta and release documentation.

# 1.9.17 Luma Search

- Fixed typing in the New Folder and New Space dialogs by disabling the native WebView host while modal popups are open and explicitly transferring HWND/WPF keyboard focus to their text fields.
- Fixed folder rows being clipped under the sidebar surface after section animations by releasing stale parent `MaxHeight` constraints whenever a folder expands.
- Fixed WebView2 bootstrapper code `-2147219146`: Setup now checks availability through the actual WebView2 API before installing, accepts any installer result that leaves a usable Runtime, and retries once through UAC when repair is genuinely required.
- Fixed the WPF/WinForms `MessageBox` ambiguity in the WebView2 recovery path and removed a nullable resource lookup warning in `PopupWindow`.
- Bundled the signed Microsoft WebView2 Evergreen bootstrapper, added installer-time repair, and added startup self-repair before any WebView is created.
- Added a second WebView2 environment retry after repair and a clear fatal message only when Windows still cannot expose a compatible Runtime.
- Added a multi-source search pipeline with server fallback, direct-source fallback, deduplication and a 20-minute successful-result cache so switching tabs cannot erase previously found results.
- Added LumaAI Overview above normal web results, including sources and a “Спросить подробнее” handoff into the conversation.
- Turned “Режим ИИ” into a separate multi-turn chat that keeps recent context and supports follow-up questions.
- Added a desktop-side resilient search path, so regular Luma Search tabs no longer depend on the new Edge Function being deployed; AI mode reuses the existing authenticated LumaAI relay.
- Removed the promotional footer line from the Luma Search page.
- Added the first-party Luma Search experience with a responsive interface matching the Luma visual language.
- Added AI mode, All, Images, Shopping, Video, Short videos and News tabs.
- Added source-grounded AI answers with compact source links while keeping external aggregation providers out of the product UI.
- Made Luma Search the default search engine for new and upgraded profiles; Google, Bing and DuckDuckGo remain optional in Settings.
- Added an optional `luma-search` Supabase Edge Function for server deployments. Desktop search now works independently, while AI mode validates the signed-in Luma account through the existing `luma-assistant` relay and uses the normal LumaAI quota.

# 1.9.17 LumaAI, session and window reliability fix

- Restored the complete reference LumaAI request, directive and navigation pipeline while preserving the current assistant design and saved conversations.
- Restored catalogue-first title resolution and automatic in-page result selection for anime, films and series.
- Restricted every film and series result to `ag.gidonline.fun`; other catalogue domains are never opened.
- Rejected `rss.xml`, feeds, XML, category, tag, archive and search pages as media-title results, fixing series commands that opened an RSS document.
- Added persistent custom-command shortcuts in LumaAI: a Lucide plus button opens the “Кастомная команда” editor, and saved command chips send their text with one click.
- Kept authenticated relay quotas, image understanding, current-tab context and model selection.
- The updated `luma-assistant` Edge Function must be deployed after rebuilding.

# 1.9.17 final navigation and theme integration

- Fixed the tab-search inner surface to match the full hover outline; enlarged space glyphs and removed the fixed-purple popup surface.

- Search-by-tabs panel now fills the complete hover hit area, removing the right-side size mismatch.

- Added the framed space strip and section dividers requested from the sidebar reference.
- Added accent hover states for Ask LumaAI, the top menu and every main-menu row.
- Split “Check for updates” and “About”; About reports the current build and can launch an update check.
- Replaced native settings selects with theme-aware custom menus.
- Made the Chrome import page derive surfaces, borders, switches and actions from the active theme.
- Removed the requested third-party-browser sentence from About.

# 1.9.17 Reference-accurate component pass

- Rebuilt sidebar controls, space outlines, plus buttons, profile surfaces and LumaAI control borders against the supplied references.
- Removed remaining fixed purple chrome colors from visible controls; borders, hovers and active states now follow the selected theme.
- Account card continues to use the actual signed-in user name, role and avatar for every user.

# 1.9.17 Minimal full-palette redesign

- Recreated the shell, account profile and LumaAI panel around the approved dark graphite reference.
- Removed neon rails, ambient color blooms, glossy gradients and excessive glow.
- Added complete Dark, Blue, Purple, Warm and Mint palettes across shell, profile surfaces and LumaAI.
- Simplified plus controls, cards, buttons, borders and typography without changing information architecture.

# 1.9.16 UI reliability and media redesign

- Eliminated frequent black frames during tab switches by keeping WebView2 hosts attached and toggling persistent tab surfaces.
- Replaced the floating music popup with an animated integrated media tray that expands between the title bar and page.
- Redesigned compact and expanded playback controls, including rounded play/pause controls and responsive seek/volume tracks.
- Added YouTube thumbnails, Open Graph artwork, Media Session artwork and favicon fallback for media covers.
- Added live theme preview across browser chrome and Settings, with automatic rollback when Settings is closed without saving.
- Removed the redundant signed-in “Сессия защищена” profile card.
- Kept the native update prompt, five-minute update checks, signed manifests and rollback.
- LumaAI behavior and `Assistant.cs` are unchanged.
### UI quality fixes
- LumaAI history now represents the current conversation as one entry instead of duplicating every prompt.
- Increased LumaAI secondary-text contrast and enlarged model/quota labels.
- Avatar files are isolated per Luma Account and restored from that account’s Supabase avatar URL.
- Fixed the sidebar tab close glyph scaling on hover.
- Refined the compact music pill and expanded player with calmer controls, cleaner surfaces and a proper pause button.
# 2.0.2

- Fixed immediate restoration and live synchronization of the signed-in Luma account.
- Redesigned Downloads with file-type icons, animated rows and rotating activity indicators.
- Added dedicated YouTube Music and Spotify player detection and controls.
- Replaced the remaining WebView2 error page with a theme-aware Luma Browser page.

# 2.0.1

- Branded Luma network error page instead of the WebView2/Edge page.
- Fixed Luma Search back navigation and cursor-positioned context menus.
- Added animated image preview and direct image actions in Luma Search.
- Stabilized the SoundCloud music widget, close behavior and volume control.
