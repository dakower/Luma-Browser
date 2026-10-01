import { createClient } from "https://esm.sh/@supabase/supabase-js@2";

const cors = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Headers": "authorization, x-client-info, apikey, content-type",
  "Access-Control-Allow-Methods": "POST, OPTIONS",
};

const json = (body: unknown, status = 200) =>
  new Response(JSON.stringify(body), {
    status,
    headers: { ...cors, "Content-Type": "application/json", "Cache-Control": "no-store" },
  });

const clean = (v: unknown, max = 6000) => String(v ?? "").trim().slice(0, max);
const escapeHtml = (v: string) => v.replace(/[&<>]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;" }[c]!));

async function telegram(method: string, body: Record<string, unknown>) {
  const token = Deno.env.get("TELEGRAM_BOT_TOKEN") ?? "";
  if (!token) return null;
  const r = await fetch("https://api.telegram.org/bot" + token + "/" + method, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  });
  if (!r.ok) {
    console.error("telegram", method, r.status, await r.text());
    return null;
  }
  return await r.json();
}

function formatSupportCard(
  threadId: string,
  displayName: string,
  userIdentifier: string,
  version: string,
  messages: Array<{ sender_type: string; sender_name?: string; body: string; created_at: string }>,
  isActiveChat = false
) {
  const shortId = threadId.slice(0, 8).toUpperCase();
  let text = `💬 <b>Чат с пользователем #${shortId}</b>\n`;
  text += `<b>Пользователь:</b> ${escapeHtml(displayName)}\n`;
  text += `<b>ID:</b> <code>${escapeHtml(userIdentifier)}</code>\n`;
  text += `<b>Версия Luma:</b> ${escapeHtml(version || "—")}\n`;

  text += `\n<b>История сообщений (${messages.length}):</b>\n`;
  const recent = messages.slice(-12);
  for (const m of recent) {
    const time = new Date(m.created_at).toLocaleTimeString("ru-RU", { hour: "2-digit", minute: "2-digit" });
    const author = m.sender_type === "admin" ? "dakower" : escapeHtml(m.sender_name || displayName || "Пользователь");
    text += `• <b>${time}</b> [${author}]: ${escapeHtml(clean(m.body, 180))}\n`;
  }

  const keyboard = [
    [{ text: isActiveChat ? "🟢 В диалоге (активен)" : "🟢 Принять чат", callback_data: `support_accept:${threadId}` }],
    [{ text: "🔴 Завершить чат", callback_data: `support_leave:${threadId}` }]
  ];

  return { text, reply_markup: { inline_keyboard: keyboard } };
}

Deno.serve(async (request) => {
  if (request.method === "OPTIONS") return new Response("ok", { headers: cors });
  if (request.method !== "POST") return json({ error: "method_not_allowed" }, 405);

  const url = Deno.env.get("SUPABASE_URL") ?? "";
  const anon = Deno.env.get("SUPABASE_ANON_KEY") ?? "";
  const service = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY") ?? "";
  if (!url || !anon || !service) return json({ error: "configuration_incomplete" }, 503);

  const authorization = request.headers.get("Authorization") ?? "";
  const admin = createClient(url, service, { auth: { persistSession: false, autoRefreshToken: false } });

  // Optional authenticated user (works for ANY user, no beta approval required!)
  let user: any = null;
  let userProfile: any = null;
  if (authorization.startsWith("Bearer ") && authorization.length > 20) {
    const userClient = createClient(url, anon, {
      global: { headers: { Authorization: authorization } },
      auth: { persistSession: false, autoRefreshToken: false },
    });
    try {
      const authRes = await userClient.auth.getUser();
      user = authRes.data.user;
      if (user) {
        const { data: prof } = await admin.from("profiles").select("display_name").eq("id", user.id).maybeSingle();
        userProfile = prof;
      }
    } catch { }
  }

  let input: any;
  try { input = await request.json(); } catch { return json({ error: "invalid_json" }, 400); }
  const action = clean(input?.action, 32);
  const guestId = clean(input?.guestId, 64) || null;
  const displayName = clean(input?.displayName, 60) || userProfile?.display_name || user?.email?.split("@")[0] || "Пользователь Luma";
  const appVersion = clean(input?.version, 30) || "2.1.4";

  try {
    // 1. Sync / Poll messages
    if (action === "sync" || action === "poll") {
      let threadId = clean(input?.threadId, 40);

      // If threadId not passed, find latest thread for this user or guest
      if (!threadId) {
        let q = admin.from("support_threads").select("id, status").order("updated_at", { ascending: false }).limit(1);
        if (user?.id) q = q.eq("user_id", user.id);
        else if (guestId) q = q.eq("guest_id", guestId);
        else return json({ messages: [] });

        const { data: foundThreads } = await q;
        if (foundThreads && foundThreads.length > 0) {
          threadId = foundThreads[0].id;
        }
      }

      if (!threadId) return json({ threadId: null, messages: [] });

      const { data: msgs, error } = await admin
        .from("support_messages")
        .select("id, thread_id, sender_type, sender_name, body, created_at")
        .eq("thread_id", threadId)
        .order("created_at");

      if (error) throw error;
      const formatted = (msgs ?? []).map((m: any) => {
        let text = m.body || "";
        let imgUrl: string | null = null;
        const imgMatch = text.match(/\[image:(https?:\/\/[^\]]+)\]/);
        if (imgMatch) {
          imgUrl = imgMatch[1];
          text = text.replace(/\[image:[^\]]+\]/, "").trim();
        }
        return {
          id: m.id,
          thread_id: m.thread_id,
          sender_type: m.sender_type,
          sender_name: m.sender_name,
          body: text,
          image_url: imgUrl,
          created_at: m.created_at,
        };
      });
      return json({ threadId, messages: formatted });
    }

    // 2. Send message to dakower
    if (action === "send") {
      let body = clean(input?.body, 6000);
      const attachment = input?.attachment;

      let signedImageUrl = "";
      if (attachment?.data) {
        try {
          const raw = String(attachment.data).replace(/^data:[^;]+;base64,/, "");
          const bytes = Uint8Array.from(atob(raw), (c) => c.charCodeAt(0));
          const mime = clean(attachment.mime, 60).toLowerCase() || "image/jpeg";
          const ext = mime.includes("png") ? "png" : mime.includes("webp") ? "webp" : "jpg";
          const path = `support/${threadId || crypto.randomUUID()}/${crypto.randomUUID()}.${ext}`;
          const uploadRes = await admin.storage.from("feedback-files").upload(path, bytes, { contentType: mime, upsert: true });
          if (!uploadRes.error) {
            const signed = await admin.storage.from("feedback-files").createSignedUrl(path, 86400 * 30);
            signedImageUrl = signed.data?.signedUrl ?? "";
          }
        } catch (uploadErr) {
          console.error("attachment upload error", uploadErr);
        }
      }

      if (!body && !signedImageUrl) return json({ error: "message_empty" }, 400);

      const dbBody = signedImageUrl
        ? (body ? `${body}\n[image:${signedImageUrl}]` : `[image:${signedImageUrl}]`)
        : body;

      let threadId = clean(input?.threadId, 40);
      let thread: any = null;

      if (threadId) {
        const { data: found } = await admin.from("support_threads").select("*").eq("id", threadId).maybeSingle();
        thread = found;
      }

      // Create new thread if needed
      if (!thread) {
        const ins = await admin.from("support_threads").insert({
          user_id: user?.id || null,
          guest_id: guestId,
          display_name: displayName,
          user_email: user?.email || null,
          app_version: appVersion,
          status: "active",
        }).select("*").single();

        if (ins.error) throw ins.error;
        thread = ins.data;
        threadId = thread.id;
      }

      // Insert message
      const msgIns = await admin.from("support_messages").insert({
        thread_id: threadId,
        sender_type: "user",
        sender_name: displayName,
        body: dbBody,
      }).select("*").single();

      if (msgIns.error) throw msgIns.error;

      await admin.from("support_threads").update({
        updated_at: new Date().toISOString(),
        status: "active",
      }).eq("id", threadId);

      // Telegram notification
      const chatId = Deno.env.get("TELEGRAM_ADMIN_CHAT_ID") ?? "";
      if (chatId) {
        const { data: allMessages } = await admin
          .from("support_messages")
          .select("sender_type,sender_name,body,created_at")
          .eq("thread_id", threadId)
          .order("created_at");

        let activeSession: any = null;
        try {
          const sessRes = await admin
            .from("support_active_sessions")
            .select("admin_id, is_active")
            .eq("thread_id", threadId)
            .eq("is_active", true)
            .maybeSingle();
          activeSession = sessRes?.data;
        } catch { }

        const userIdentifier = user?.email || user?.id || guestId || "Гость";
        const card = formatSupportCard(
          threadId,
          displayName,
          userIdentifier,
          appVersion,
          allMessages ?? [msgIns.data],
          activeSession?.is_active === true
        );

        if (!thread.telegram_message_id) {
          // Send initial Telegram card
          const sent = await telegram("sendMessage", {
            chat_id: chatId,
            text: card.text,
            parse_mode: "HTML",
            reply_markup: card.reply_markup,
          });

          const msgId = sent?.result?.message_id;
          if (msgId) {
            await admin.from("support_threads").update({ telegram_message_id: msgId }).eq("id", threadId);
            try {
              await admin.from("telegram_support_messages").upsert({
                telegram_message_id: msgId,
                thread_id: threadId,
              });
            } catch { }
          }
        } else {
          // Update main card transcript live
          await telegram("editMessageText", {
            chat_id: chatId,
            message_id: thread.telegram_message_id,
            text: card.text,
            parse_mode: "HTML",
            reply_markup: card.reply_markup,
          });
        }

        // Deliver message directly
        const prefix = activeSession?.is_active
          ? `💬 <b>${escapeHtml(displayName)}:</b>\n`
          : `💬 <b>${escapeHtml(displayName)}</b> · #${threadId.slice(0, 8).toUpperCase()}\n`;

        let sentFollow: any = null;
        if (signedImageUrl) {
          const caption = `${prefix}${escapeHtml(body || "Изображение от пользователя")}`.slice(0, 1024);
          sentFollow = await telegram("sendPhoto", {
            chat_id: chatId,
            reply_to_message_id: activeSession?.is_active ? undefined : thread.telegram_message_id,
            photo: signedImageUrl,
            caption,
            parse_mode: "HTML",
          });
        } else {
          sentFollow = await telegram("sendMessage", {
            chat_id: chatId,
            reply_to_message_id: activeSession?.is_active ? undefined : thread.telegram_message_id,
            text: `${prefix}${escapeHtml(body)}`,
            parse_mode: "HTML",
          });
        }

        const followId = sentFollow?.result?.message_id;
        if (followId) {
          try {
            await admin.from("telegram_support_messages").upsert({
              telegram_message_id: followId,
              thread_id: threadId,
            });
          } catch { }
        }
      }

      return json({ ok: true, threadId, messageId: msgIns.data.id });
    }

    return json({ error: "invalid_action" }, 400);
  } catch (err) {
    console.error("luma-support error", err);
    return json({ error: "support_unavailable", details: (err as any)?.message ?? String(err) }, 500);
  }
});
