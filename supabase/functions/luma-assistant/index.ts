import { createClient } from "https://esm.sh/@supabase/supabase-js@2";

const cors = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Headers": "authorization, x-client-info, apikey, content-type",
  "Access-Control-Allow-Methods": "POST, OPTIONS",
};
const json = (body: unknown, status = 200, extra: Record<string, string> = {}) =>
  new Response(JSON.stringify(body), { status, headers: { ...cors, ...extra, "Content-Type": "application/json", "Cache-Control": "no-store" } });

Deno.serve(async (request) => {
  if (request.method === "OPTIONS") return new Response("ok", { headers: cors });
  if (request.method !== "POST") return json({ error: "method_not_allowed" }, 405);

  const authorization = request.headers.get("Authorization") ?? "";
  const supabaseUrl = Deno.env.get("SUPABASE_URL") ?? "";
  const anonKey = Deno.env.get("SUPABASE_ANON_KEY") ?? "";
  const serviceKey = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY") ?? "";
  const providerKey = Deno.env.get("LUMA_AI_API_KEY") ?? "";
  const endpoint = Deno.env.get("LUMA_AI_ENDPOINT") ?? "https://api.deepseek.com/chat/completions";
  if (!supabaseUrl || !anonKey || !serviceKey || !providerKey) return json({ error: "configuration_incomplete" }, 503);

  const userClient = createClient(supabaseUrl, anonKey, {
    global: { headers: { Authorization: authorization } },
    auth: { persistSession: false, autoRefreshToken: false },
  });
  const admin = createClient(supabaseUrl, serviceKey, { auth: { persistSession: false, autoRefreshToken: false } });
  const { data: { user }, error: userError } = await userClient.auth.getUser();
  if (userError || !user) return json({ error: "unauthorized" }, 401);

  const length = Number(request.headers.get("content-length") ?? 0);
  if (length > 24_000_000) return json({ error: "payload_too_large" }, 413);
  let input: any;
  try { input = await request.json(); } catch { return json({ error: "invalid_json" }, 400); }
  if (!Array.isArray(input?.messages) || input.messages.length < 1)
    return json({ error: "invalid_messages" }, 400);

  // If conversation exceeds 12 messages, preserve system prompt and take the most recent turns
  if (input.messages.length > 12) {
    const hasSystem = input.messages[0]?.role === "system";
    if (hasSystem) {
      input.messages = [input.messages[0], ...input.messages.slice(-11)];
    } else {
      input.messages = input.messages.slice(-12);
    }
  }

  const serialized = JSON.stringify(input.messages);
  if (serialized.length > 24_000_000) return json({ error: "payload_too_large" }, 413);
  const hasImage = serialized.includes('"image_url"');
  const tier = input.model === "pro" ? "pro" : "fast";
  const fastModel = Deno.env.get("LUMA_AI_FAST_MODEL") ?? "deepseek-flash";
  const proModel = Deno.env.get("LUMA_AI_PRO_MODEL") ?? "deepseek-v4-pro";
  const visionModel = Deno.env.get("LUMA_AI_VISION_MODEL") ?? "";
  const model = hasImage && visionModel ? visionModel : tier === "pro" ? proModel : fastModel;

  // Check tester, VIP, beta and admin privilege across existing Supabase tables (profiles, beta_access)
  const { data: prof } = await admin.from("profiles").select("*").eq("id", user.id).maybeSingle();
  const { data: beta } = await admin.from("beta_access").select("status").eq("user_id", user.id).maybeSingle();
  const isPrivileged = prof?.role === "admin" || prof?.role === "tester" || prof?.role === "beta" ||
    prof?.is_vip === true || prof?.unlimited_ai === true || beta?.status === "approved" ||
    user.email?.toLowerCase() === "dakowerr@gmail.com";

  // Browser agent: one user task = many model calls (observe -> act loop). The first call of a
  // task is charged as one normal request and receives a signed run token; follow-up calls of the
  // same task carrying a valid, fresh token are free. The token is bound to the user and expires.
  const AGENT_RUN_MS = 20 * 60 * 1000;
  const sign = async (value: string) => {
    const key = await crypto.subtle.importKey("raw", new TextEncoder().encode(serviceKey + ":luma-agent"),
      { name: "HMAC", hash: "SHA-256" }, false, ["sign"]);
    const mac = new Uint8Array(await crypto.subtle.sign("HMAC", key, new TextEncoder().encode(value)));
    return Array.from(mac).map((b) => b.toString(16).padStart(2, "0")).join("");
  };
  const isAgentCall = input?.agent === true;
  let agentRunFree = false;
  if (isAgentCall && typeof input?.agent_token === "string" && input.agent_token.length < 300) {
    const [uid, ts, sig] = input.agent_token.split(".");
    const age = Date.now() - Number(ts);
    if (uid === user.id && Number.isFinite(age) && age >= 0 && age < AGENT_RUN_MS && sig === await sign(`${uid}.${ts}`))
      agentRunFree = true;
  }

  let quota: any;
  let quotaError: any = null;
  if (agentRunFree) {
    quota = { allowed: true, unlimited: true, limit: 0, used: 0, remaining: -1, usage_date: new Date().toISOString().slice(0, 10) };
  } else {
    ({ data: quota, error: quotaError } = await userClient.rpc("consume_my_assistant_quota"));
  }
  if (isPrivileged) {
    quota = { allowed: true, unlimited: true, limit: 0, used: 0, remaining: -1, usage_date: quota?.usage_date || new Date().toISOString().slice(0, 10) };
  } else if (!agentRunFree) {
    if (quotaError) return json({ error: "quota_unavailable" }, 503);
    if (!quota?.allowed) return json({ error: "daily_limit_reached", message: "15 requests per UTC day" }, 429);
  }

  const quotaHeaders: Record<string, string> = agentRunFree ? {} : {
    "X-Luma-Quota-Limit": String(quota.limit ?? 15),
    "X-Luma-Quota-Used": String(quota.used ?? 0),
    "X-Luma-Quota-Remaining": String(quota.remaining ?? 0),
    "X-Luma-Quota-Unlimited": String(Boolean(quota.unlimited)),
  };
  if (isAgentCall && !agentRunFree) {
    const ts = String(Date.now());
    quotaHeaders["X-Luma-Agent-Token"] = `${user.id}.${ts}.${await sign(`${user.id}.${ts}`)}`;
  }
  const refund = async () => {
    if (quota.unlimited) return;
    await admin.rpc("refund_assistant_quota", { p_user_id: user.id, p_usage_date: quota.usage_date });
  };

  try {
    const upstream = await fetch(endpoint, {
      method: "POST",
      headers: { "Authorization": `Bearer ${providerKey}`, "Content-Type": "application/json", "Accept": "text/event-stream" },
      body: JSON.stringify({ model, stream: true, temperature: 0.3, messages: input.messages }),
      signal: request.signal,
    });
    if (!upstream.ok || !upstream.body) {
      await refund();
      console.error("luma-assistant upstream", upstream.status, await upstream.text());
      return json({ error: "provider_error", message: "LumaAI provider rejected the request" }, upstream.status === 413 ? 413 : upstream.status === 422 ? 422 : 502, quotaHeaders);
    }
    const headers = new Headers({ ...cors, ...quotaHeaders, "Content-Type": upstream.headers.get("content-type") ?? "text/event-stream", "Cache-Control": "no-store" });
    return new Response(upstream.body, { status: 200, headers });
  } catch (error) {
    await refund();
    console.error("luma-assistant", error);
    return json({ error: "provider_unavailable" }, 502, quotaHeaders);
  }
});
