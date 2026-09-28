# LumaAI relay deployment

The desktop app no longer contains a provider API key. It calls the authenticated `luma-assistant` Supabase Edge Function.

## Deploy

### Simplest Windows method

From the extracted source folder, run `deploy-luma-ai.ps1`. It checks for Node.js, signs in to Supabase and deploys the correct function to project `ejwjlifyfnhgsbvsjefd`. If Windows blocks scripts, open PowerShell in that folder and run:

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\deploy-luma-ai.ps1
```


1. Apply migrations through `005_assistant_quota_status.sql`.
2. Configure Edge Function secrets in the Supabase project:
   - `LUMA_AI_API_KEY` — provider API key.
   - `LUMA_AI_ENDPOINT` — OpenAI-compatible chat-completions endpoint.
   - `LUMA_AI_FAST_MODEL` — fast text-capable model ID.
   - `LUMA_AI_PRO_MODEL` — higher-quality model ID.
   - `LUMA_AI_VISION_MODEL` — model ID that accepts OpenAI `image_url` content parts. This is required for uploaded images and tab screenshots unless the fast/pro models already support vision.
3. Deploy the updated relay:

```powershell
supabase functions deploy luma-assistant --project-ref ejwjlifyfnhgsbvsjefd
```

If you use the CLI through npm:

```powershell
npx supabase functions deploy luma-assistant --project-ref ejwjlifyfnhgsbvsjefd
```

Rebuilding only the Windows application is not enough after changes to `supabase/functions/luma-assistant/index.ts`; the deployed Edge Function must be updated too.

Supabase automatically provides `SUPABASE_URL`, `SUPABASE_ANON_KEY`, and `SUPABASE_SERVICE_ROLE_KEY` to the function. Never copy the service-role key into the browser or repository.

## Quota

`consume_my_assistant_quota()` atomically allows 15 successful relay starts per authenticated user per UTC day. The administrator account (configured in the migrations) is explicitly unlimited. Provider requests rejected before streaming are refunded server-side. `get_my_assistant_quota()` reads the current counter without consuming a request, so the desktop UI can restore the real remaining quota after restart.

