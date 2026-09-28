# Luma Search deployment

Luma Search is rendered as an internal browser page. The desktop includes a resilient direct aggregation path, so regular search remains usable even before the optional `luma-search` Edge Function is deployed. AI mode reuses the authenticated `luma-assistant` relay. Provider credentials are never embedded in the desktop application.

## Features

- Web, image, shopping, video, short-video and news modes.
- LumaAI answer mode grounded in returned source links.
- Deduplication and removal of intermediary search-provider links.
- Search results are displayed only under the Luma Search interface.

## Deploy

The easiest Windows method is:

```powershell
.\deploy-luma-ai.ps1
```

The script deploys both `luma-assistant` and the optional `luma-search` server endpoint. Regular desktop search does not require that endpoint. AI mode validates the signed-in Luma account and consumes the normal LumaAI quota through `luma-assistant`.

Manual deployment:

```powershell
npx supabase functions deploy luma-search --no-verify-jwt --project-ref ejwjlifyfnhgsbvsjefd
```

Optional: configure `JINA_API_KEY` to add another resilient source. AI mode uses the same `LUMA_AI_API_KEY`, model and endpoint secrets as `luma-assistant`.
