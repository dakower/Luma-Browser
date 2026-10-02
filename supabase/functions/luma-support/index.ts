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
  try {
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
  } catch (err) {
    console.error("telegram json error", method, err);
    return null;
  }
}

async function telegramSendPhotoDirect(
  chatId: string,
  imageBytes: Uint8Array,
  mime: string,
  caption?: string,
  replyToMessageId?: number
) {
  const token = Deno.env.get("TELEGRAM_BOT_TOKEN") ?? "";
  if (!token) return null;

  const form = new FormData();
  form.append("chat_id", chatId);
  if (replyToMessageId) form.append("reply_to_message_id", String(replyToMessageId));
  if (caption) {
    form.append("caption", caption.slice(0, 1024));
    form.append("parse_mode", "HTML");
  }
  const ext = mime.includes("png") ? "png" : mime.includes("webp") ? "webp" : "jpg";
  form.append("photo", new Blob([imageBytes], { type: mime }), `image.${ext}`);

  try {
    const res = await fetch(`https://api.telegram.org/bot${token}/sendPhoto`, {
      method: "POST",
      body: form,
    });
    if (!res.ok) {
      console.error("sendPhoto direct error", res.status, await res.text());
      return null;
    }
    return await res.json();
  } catch (err) {
    console.error("sendPhoto direct network error", err);
    return null;
  }
}

async function telegramSendMediaGroupDirect(
  chatId: string,
  images: Array<{ bytes: Uint8Array; mime: string }>,
  caption?: string,
  replyToMessageId?: number
) {
  const token = Deno.env.get("TELEGRAM_BOT_TOKEN") ?? "";
  if (!token) return null;

  const form = new FormData();
  form.append("chat_id", chatId);
  if (replyToMessageId) form.append("reply_to_message_id", String(replyToMessageId));

  const mediaArray = images.map((img, idx) => {
    const item: Record<string, unknown> = {
      type: "photo",
      media: `attach://photo_${idx}`,
    };
    if (idx === 0 && caption) {
      item.caption = caption.slice(0, 1024);
      item.parse_mode = "HTML";
    }
    return item;
  });

  form.append("media", JSON.stringify(mediaArray));
  images.forEach((img, idx) => {
    const ext = img.mime.includes("png") ? "png" : img.mime.includes("webp") ? "webp" : "jpg";
    form.append(`photo_${idx}`, new Blob([img.bytes], { type: img.mime }), `photo_${idx}.${ext}`);
  });

  try {
    const res = await fetch(`https://api.telegram.org/bot${token}/sendMediaGroup`, {
      method: "POST",
      body: form,
    });
    if (!res.ok) {
      console.error("sendMediaGroup direct error", res.status, await res.text());
      return null;
    }
    return await res.json();
  } catch (err) {
    console.error("sendMediaGroup direct network error", err);
    return null;
  }
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

  // Optional authenticated user
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
        const imgMatches = [...text.matchAll(/\[image:(https?:\/\/[^\]]+)\]/g)];
        const imageUrls = imgMatches.map((match) => match[1]).filter(Boolean);
        text = text.replace(/\[image:[^\]]+\]/g, "").trim();

        return {
          id: m.id,
          thread_id: m.thread_id,
          sender_type: m.sender_type,
          sender_name: m.sender_name,
          body: text,
          image_url: imageUrls[0] ?? null,
          image_urls: imageUrls,
          created_at: m.created_at,
        };
      });
      return json({ threadId, messages: formatted });
    }

    // 2. Send message to creator
    if (action === "send") {
      const body = clean(input?.body, 6000);

      // Collect attachments from array or single object
      const rawAttachments: Array<{ name?: string; mime?: string; data: string }> = [];
      if (Array.isArray(input?.attachments)) {
        for (const att of input.attachments) {
          if (att?.data) rawAttachments.push(att);
        }
      } else if (input?.attachment?.data) {
        rawAttachments.push(input.attachment);
      }

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

      // Process and upload attachments
      const processedImages: Array<{ bytes: Uint8Array; mime: string; signedUrl: string }> = [];
      for (const att of rawAttachments) {
        try {
          const raw = String(att.data).replace(/^data:[^;]+;base64,/, "");
          const bytes = Uint8Array.from(atob(raw), (c) => c.charCodeAt(0));
          const mime = clean(att.mime, 60).toLowerCase() || "image/jpeg";
          const ext = mime.includes("png") ? "png" : mime.includes("webp") ? "webp" : "jpg";
          const path = `support/${threadId}/${crypto.randomUUID()}.${ext}`;
          const uploadRes = await admin.storage.from("feedback-files").upload(path, bytes, { contentType: mime, upsert: true });
          let signedUrl = "";
          if (!uploadRes.error) {
            const signed = await admin.storage.from("feedback-files").createSignedUrl(path, 86400 * 30);
            signedUrl = signed.data?.signedUrl ?? "";
          }
          processedImages.push({ bytes, mime, signedUrl });
        } catch (uploadErr) {
          console.error("attachment upload error", uploadErr);
        }
      }

      if (!body && processedImages.length === 0) {
        return json({ error: "message_empty" }, 400);
      }

      // Build database body text
      let dbBody = body;
      const imageTags = processedImages
        .filter((img) => img.signedUrl)
        .map((img) => `[image:${img.signedUrl}]`)
        .join(" ");

      if (imageTags) {
        dbBody = dbBody ? `${dbBody}\n${imageTags}` : imageTags;
      }

      // Insert message into DB
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

      // Telegram notification to creator
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

        // Deliver message / images directly to creator in Telegram
        const prefix = activeSession?.is_active
          ? `💬 <b>${escapeHtml(displayName)}:</b>\n`
          : `💬 <b>${escapeHtml(displayName)}</b> · #${threadId.slice(0, 8).toUpperCase()}\n`;

        const replyTarget = activeSession?.is_active ? undefined : thread.telegram_message_id;
        let sentFollow: any = null;

        if (processedImages.length === 1) {
          const caption = `${prefix}${escapeHtml(body || "Изображение от пользователя")}`.slice(0, 1024);
          sentFollow = await telegramSendPhotoDirect(
            chatId,
            processedImages[0].bytes,
            processedImages[0].mime,
            caption,
            replyTarget
          );
        } else if (processedImages.length > 1) {
          const caption = `${prefix}${escapeHtml(body || `Изображения (${processedImages.length} шт.) от пользователя`)}`.slice(0, 1024);
          sentFollow = await telegramSendMediaGroupDirect(
            chatId,
            processedImages,
            caption,
            replyTarget
          );
        } else {
          sentFollow = await telegram("sendMessage", {
            chat_id: chatId,
            reply_to_message_id: replyTarget,
            text: `${prefix}${escapeHtml(body)}`,
            parse_mode: "HTML",
          });
        }

        if (sentFollow?.result) {
          const results = Array.isArray(sentFollow.result) ? sentFollow.result : [sentFollow.result];
          for (const item of results) {
            const mid = item?.message_id;
            if (mid) {
              try {
                await admin.from("telegram_support_messages").upsert({
                  telegram_message_id: mid,
                  thread_id: threadId,
                });
              } catch { }
            }
          }
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
