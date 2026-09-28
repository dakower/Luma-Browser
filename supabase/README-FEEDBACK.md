# Luma Test Center / Telegram relay

1. Apply both migrations in order in Supabase SQL Editor:
   - `migrations/006_feedback_center.sql`
   - `migrations/007_feedback_workflow.sql`
   Existing installations must also apply `007_feedback_workflow.sql`; it migrates old statuses and adds task progress.
2. Deploy `luma-feedback` with JWT verification enabled. Deploy `luma-feedback-telegram` with `--no-verify-jwt`; the Telegram webhook authenticates requests with its secret header.
3. Add Edge Function secrets: `TELEGRAM_BOT_TOKEN`, `TELEGRAM_ADMIN_CHAT_ID`, `TELEGRAM_ADMIN_USER_ID`, `TELEGRAM_WEBHOOK_SECRET`.
4. Register the webhook (replace values):

```text
https://api.telegram.org/bot<BOT_TOKEN>/setWebhook?url=https://<PROJECT>.supabase.co/functions/v1/luma-feedback-telegram&secret_token=<TELEGRAM_WEBHOOK_SECRET>
```

`TELEGRAM_WEBHOOK_SECRET` must be the literal secret without Markdown escaping or backslashes.

Reply to the bot's main report message to send a message back to the tester. Inline buttons update the report to one of: `new`, `checking`, `need_info`, `fixing`, `fixed`, `cannot_reproduce`, `duplicate`. A tester can confirm a fixed report or mark that the bug remains; the latter reopens it as `checking`.

Tester tasks support `started`, `works`, and `bug`, and the app receives aggregate participant counts. Bot credentials are never embedded in Luma; Supabase remains the source of truth.

The included `deploy-feedback.ps1` runs `supabase db push`, deploys the app-facing function with JWT verification, and deploys only the webhook function without JWT verification.
