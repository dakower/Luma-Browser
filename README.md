# Luma Browser

Нативный браузер для Windows на **WPF** и **Chromium WebView2**: Spaces и Split View, собственный поиск **Luma Search** с ИИ-режимом, ассистент **LumaAI**, перевод страниц, плавающие медиаплееры и подписанные автообновления.

> Luma is a native Windows browser built on WPF and Chromium WebView2, featuring Spaces, Split View, its own Luma Search with an AI mode, the LumaAI assistant, page translation, floating music/video players and signed auto-updates. UI languages: Russian, English, Ukrainian.

**Текущая версия:** 2.1.0 · **Платформа:** Windows 10/11 x64 · **Интерфейс:** RU / EN / UK

## Установка

1. Откройте раздел [**Releases**](../../releases/latest).
2. Скачайте `LumaSetup-<версия>-x64.exe` и `SHA256SUMS.txt`.
3. Проверьте хеш:
   ```powershell
   (Get-FileHash .\LumaSetup-2.1.0-x64.exe -Algorithm SHA256).Hash
   ```
4. Запустите установщик. Доступны установка для текущего пользователя (без прав администратора) и для всех пользователей (Program Files).

> Если релиз не подписан Authenticode-сертификатом, Windows SmartScreen может показать предупреждение.

WebView2 Runtime устанавливается и восстанавливается установщиком автоматически.

## Возможности

**Организация вкладок**
- Spaces, папки и закреплённые вкладки в боковой панели
- Split View — две страницы рядом
- Быстрый поиск по открытым вкладкам (`Ctrl+K`)
- Закреплённые вкладки и вкладки со звуком не «засыпают»

**Luma Search**
- Собственный интерфейс поиска: Всё, Картинки, Покупки, Видео, Короткие видео, Новости
- Транслит, исправление неверной раскладки, расширение сокращений
- Поддержка Google Dorks: `site:`, `filetype:`/`ext:`, `intitle:`, `inurl:`, `intext:`, `before:`, `after:`, `-`, `OR`/`AND`, точные фразы

**ИИ**
- LumaAI Overview над результатами поиска — ответ с источниками
- «Режим ИИ» — отдельный чат с контекстом и уточняющими вопросами
- Ассистент видит текст страницы, скриншот вкладки и прикреплённые изображения
- Запросы идут через Supabase Edge Function, ключей ИИ-провайдера в приложении нет

**Медиа и перевод**
- Плавающий музыкальный виджет: YouTube Music, Spotify, SoundCloud
- Плавающее видео с выбором качества
- Перевод страниц, в том числе динамического (SPA) контента

**Приватность и данные**
- Приватный режим (без истории, сессии и автозаполнения)
- Блокировка сторонних cookie
- Менеджер загрузок
- Импорт из Google Chrome и паролей из CSV

**Обновления и безопасность**
- Автообновления с подписью ECDSA, проверкой SHA-256 и откатом при сбое
- Проверка обновлений не требует входа в аккаунт
- Установщик проверяет целостность пакета и защищён от zip-slip

Подробнее: [PRIVACY.md](PRIVACY.md), [CHANGELOG.md](CHANGELOG.md).

## Структура репозитория

| Путь | Назначение |
|---|---|
| `src/Luma` | Оболочка WPF/WebView2, разбитая на partial-модули `MainWindow.*` |
| `src/Luma.Core` | Независимая от UI логика (URL, поиск, правила обновлений), тестируется на любой платформе .NET 8 |
| `src/Luma.Setup` | Установщик (single-file exe) |
| `tests/Luma.Core.Tests` | xUnit-тесты Core |
| `supabase/` | Миграции БД и Edge Functions (LumaAI, поиск, обновления, обратная связь) |
| `tools/` | Скрипты подписи и публикации обновлений |

Архитектура и правила зависимостей: [ARCHITECTURE.md](ARCHITECTURE.md).

## Сборка из исходников

**Требования:** Windows 10/11 x64, [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0), PowerShell 5.1+.

```powershell
git clone https://github.com/<user>/LumaBrowser.git
cd LumaBrowser
Set-ExecutionPolicy -Scope Process Bypass
.\build.ps1
```

Скрипт запускает тесты, публикует браузер и упаковывает его в установщик. Результат:

| Файл | Что это |
|---|---|
| `dist\LumaSetup-<версия>-x64.exe` | Установщик |
| `publish\Luma.exe` | Портативная папка с браузером (self-contained) |
| `dist\SHA256SUMS.txt` | Контрольная сумма установщика |

> `src/Luma.Setup` нельзя собрать обычным `dotnet build`: `Payload.zip` создаёт только `build.ps1`.

Только тесты Core (работает и на Linux/macOS):

```powershell
dotnet test tests/Luma.Core.Tests/Luma.Core.Tests.csproj
```

Локальная сборка не подписана. Подпись и выпуск обновлений — в [RELEASE.md](RELEASE.md) и [UPDATES.md](UPDATES.md); они требуют приватных ключей, которых в репозитории нет.

## Серверная часть

Аккаунты, LumaAI, поиск, обратная связь и обновления работают на Supabase. Свою копию можно развернуть по инструкциям [supabase/README-LUMAAI.md](supabase/README-LUMAAI.md), [supabase/README-LUMASEARCH.md](supabase/README-LUMASEARCH.md) и [BETA-SETUP.md](BETA-SETUP.md). Секретные ключи хранятся только в секретах Edge Functions.

## Выпуск релиза через GitHub Actions

```powershell
git tag v2.1.0
git push origin v2.1.0
```

Workflow `release.yml` соберёт установщик и приложит его к GitHub Release. Версия тега должна совпадать с `Version` в `src/Luma/Luma.csproj`.

## Лицензия

См. [LICENSE.txt](LICENSE.txt). Иконки — Lucide (ISC), см. [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
