import { createClient } from "https://esm.sh/@supabase/supabase-js@2";

const answer = (body: unknown = { ok: true }, status = 200) =>
  new Response(JSON.stringify(body), {
    status,
    headers: { "Content-Type": "application/json", "Cache-Control": "no-store" },
  });

const clean = (v: unknown, max = 6000) => String(v ?? "").trim().slice(0, max);
const escapeHtml = (v: string) => v.replace(/[&<>]/g, (c) => ({ "&": "&amp;", "<": "&lt;", ">": "&gt;" }[c]!));

const labels: Record<string, string> = {
  checking: "Проверяется",
  need_info: "Нужна информация",
  fixing: "Исправляется",
  fixed: "Исправлено",
  cannot_reproduce: "Не воспроизводится",
  duplicate: "Дубликат",
};

async function telegram(token: string, method: string, body: Record<string, unknown>) {
  return await fetch("https://api.telegram.org/bot" + token + "/" + method, {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify(body),
  }).catch(() => null);
}

// Update support chat card
async function updateSupportCard(admin: any, token: string, chatId: string, threadId: string, isActiveChat: boolean) {
  try {
    const { data: thread } = await admin.from("support_threads").select("*").eq("id", threadId).maybeSingle();
    if (!thread || !thread.telegram_message_id) return;

    const shortId = threadId.slice(0, 8).toUpperCase();
    const { data: msgs } = await admin
      .from("support_messages")
      .select("sender_type,sender_name,body,created_at")
      .eq("thread_id", threadId)
      .order("created_at");

    let text = `💬 <b>Чат с пользователем #${shortId}</b>\n`;
    text += `<b>Пользователь:</b> ${escapeHtml(thread.display_name || "Пользователь")}\n`;
    text += `<b>ID:</b> <code>${escapeHtml(thread.user_email || thread.user_id || thread.guest_id || "Гость")}</code>\n`;
    text += `<b>Версия Luma:</b> ${escapeHtml(thread.app_version || "—")}\n`;

    text += `\n<b>История сообщений (${(msgs ?? []).length}):</b>\n`;
    const recent = (msgs ?? []).slice(-12);
    for (const m of recent) {
      const time = new Date(m.created_at).toLocaleTimeString("ru-RU", { hour: "2-digit", minute: "2-digit" });
      const author = m.sender_type === "admin" ? "dakower" : escapeHtml(m.sender_name || thread.display_name || "Пользователь");
      text += `• <b>${time}</b> [${author}]: ${escapeHtml(clean(m.body, 180))}\n`;
    }

    const keyboard = [
      [{ text: isActiveChat ? "🟢 В диалоге (активен)" : "🟢 Принять чат", callback_data: `support_accept:${threadId}` }],
      [{ text: "🔴 Завершить чат", callback_data: `support_leave:${threadId}` }]
    ];

    await telegram(token, "editMessageText", {
      chat_id: chatId,
      message_id: thread.telegram_message_id,
      text,
      parse_mode: "HTML",
      reply_markup: { inline_keyboard: keyboard },
    });
  } catch (e) {
    console.error("updateSupportCard error", e);
  }
}

// Update test center report card
async function updateReportCard(admin: any, token: string, chatId: string, reportId: string, isActiveChat: boolean) {
  try {
    const { data: report } = await admin.from("feedback_reports").select("*").eq("id", reportId).maybeSingle();
    if (!report || !report.telegram_message_id) return;

    const { data: profile } = await admin.from("profiles").select("display_name").eq("id", report.user_id).maybeSingle();
    const testerName = profile?.display_name || "Тестер";
    const shortId = reportId.slice(0, 8).toUpperCase();
    const icon = report.category === "crash" ? "💥" : report.category === "idea" ? "💡" : "🐞";

    const { data: msgs } = await admin
      .from("feedback_messages")
      .select("sender_type,sender_name,body,created_at")
      .eq("report_id", reportId)
      .order("created_at");

    let text = `${icon} <b>Отчёт Тест-Центра #${shortId}</b>\n`;
    text += `<b>Тестер:</b> ${escapeHtml(testerName)}\n`;
    text += `<b>UID:</b> <code>${report.user_id}</code>\n`;
    text += `<b>Версия:</b> ${escapeHtml(clean(report.diagnostics?.version, 30) || "—")}\n`;
    text += `<b>Категория:</b> ${escapeHtml(report.category)} · <b>Тема:</b> ${escapeHtml(report.subject)}\n`;

    text += `\n<b>История сообщений (${(msgs ?? []).length}):</b>\n`;
    const recent = (msgs ?? []).slice(-10);
    for (const m of recent) {
      const time = new Date(m.created_at).toLocaleTimeString("ru-RU", { hour: "2-digit", minute: "2-digit" });
      const author = m.sender_type === "admin" ? "dakower" : escapeHtml(m.sender_name || testerName);
      text += `• <b>${time}</b> [${author}]: ${escapeHtml(clean(m.body, 180))}\n`;
    }

    const keyboard = [
      [{ text: isActiveChat ? "🟢 В диалоге (активен)" : "🟢 Принять чат", callback_data: `accept:${reportId}` }],
      [
        { text: "👀 Проверяется", callback_data: `status:${reportId}:checking` },
        { text: "❓ Нужна инфа", callback_data: `status:${reportId}:need_info` },
      ],
      [
        { text: "🛠 Исправляется", callback_data: `status:${reportId}:fixing` },
        { text: "✅ Исправлено", callback_data: `status:${reportId}:fixed` },
      ],
    ];

    await telegram(token, "editMessageText", {
      chat_id: chatId,
      message_id: report.telegram_message_id,
      text,
      parse_mode: "HTML",
      reply_markup: { inline_keyboard: keyboard },
    });
  } catch (e) {
    console.error("updateReportCard error", e);
  }
}

Deno.serve(async (request) => {
  if (request.method !== "POST") return answer({ error: "method_not_allowed" }, 405);

  const secret = Deno.env.get("TELEGRAM_WEBHOOK_SECRET") ?? "";
  if (!secret || request.headers.get("X-Telegram-Bot-Api-Secret-Token") !== secret) {
    return answer({ error: "forbidden" }, 403);
  }

  const url = Deno.env.get("SUPABASE_URL") ?? "";
  const key = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY") ?? "";
  const token = Deno.env.get("TELEGRAM_BOT_TOKEN") ?? "";
  const adminId = Deno.env.get("TELEGRAM_ADMIN_USER_ID") ?? "";
  if (!url || !key || !token || !adminId) return answer({ error: "configuration_incomplete" }, 503);

  let update: any;
  try { update = await request.json(); } catch { return answer({ error: "invalid_json" }, 400); }

  const sender = String(update?.message?.from?.id ?? update?.callback_query?.from?.id ?? "");
  if (sender !== adminId) return answer();

  const admin = createClient(url, key, { auth: { persistSession: false, autoRefreshToken: false } });

  try {
    const callback = update?.callback_query;
    if (callback) {
      const data = clean(callback.data, 120);

      // Support Chat: Accept
      if (data.startsWith("support_accept:")) {
        const threadId = data.replace("support_accept:", "").trim();
        if (!/^[0-9a-f-]{36}$/i.test(threadId)) return answer();

        // Deactivate any other sessions and activate this support thread
        await admin.from("support_active_sessions").upsert({
          admin_id: sender,
          thread_id: threadId,
          is_active: true,
          updated_at: new Date().toISOString(),
        });
        await admin.from("feedback_chat_sessions").update({ is_active: false }).eq("admin_id", sender);

        const { data: thread } = await admin.from("support_threads").select("display_name").eq("id", threadId).maybeSingle();
        const shortId = threadId.slice(0, 8).toUpperCase();
        const userName = thread?.display_name || "Пользователь";

        await telegram(token, "answerCallbackQuery", { callback_query_id: callback.id, text: `Чат #${shortId} принят!` });
        await telegram(token, "sendMessage", {
          chat_id: callback.message.chat.id,
          text: `🟢 <b>Вы вошли в чат с пользователем #${shortId}</b> (${escapeHtml(userName)})\n\nВсе ваши текстовые сообщения теперь будут напрямую отправляться ему в браузер без необходимости нажимать Reply.\n\nДля завершения нажмите кнопку ниже или введите <code>/exit</code>.`,
          parse_mode: "HTML",
          reply_markup: {
            inline_keyboard: [[{ text: "🔴 Завершить чат", callback_data: `support_leave:${threadId}` }]],
          },
        });

        await updateSupportCard(admin, token, callback.message.chat.id, threadId, true);
        return answer();
      }

      // Support Chat: Leave
      if (data.startsWith("support_leave:")) {
        const threadId = data.replace("support_leave:", "").trim();
        await admin.from("support_active_sessions").update({ is_active: false, updated_at: new Date().toISOString() }).eq("admin_id", sender);
        const shortId = threadId.slice(0, 8).toUpperCase();

        await telegram(token, "answerCallbackQuery", { callback_query_id: callback.id, text: "Чат завершён" });
        await telegram(token, "sendMessage", {
          chat_id: callback.message.chat.id,
          text: `🔴 <b>Чат #${shortId} завершён.</b> Вы вернулись в общий режим.`,
          parse_mode: "HTML",
        });

        await updateSupportCard(admin, token, callback.message.chat.id, threadId, false);
        return answer();
      }

      // Test Center Status updates
      if (data.startsWith("status:")) {
        const match = data.match(/^status:([0-9a-f-]{36}):(checking|need_info|fixing|fixed|cannot_reproduce|duplicate)$/i);
        if (!match) return answer();
        const reportId = match[1], status = match[2];
        await admin.from("feedback_reports").update({ status, updated_at: new Date().toISOString() }).eq("id", reportId);
        await admin.from("feedback_messages").insert({
          report_id: reportId,
          sender_type: "system",
          sender_name: "dakower",
          body: `Статус изменён: ${labels[status]}`,
        });
        await telegram(token, "answerCallbackQuery", { callback_query_id: callback.id, text: labels[status] });
        await telegram(token, "sendMessage", {
          chat_id: callback.message.chat.id,
          reply_to_message_id: callback.message.message_id,
          text: `Статус #${reportId.slice(0, 8).toUpperCase()}: ${labels[status]}`,
        });
        await updateReportCard(admin, token, callback.message.chat.id, reportId, false);
        return answer();
      }

      // Test Center: Accept
      if (data.startsWith("accept:")) {
        const reportId = data.replace("accept:", "").trim();
        if (!/^[0-9a-f-]{36}$/i.test(reportId)) return answer();

        await admin.from("feedback_chat_sessions").upsert({
          admin_id: sender,
          report_id: reportId,
          is_active: true,
          updated_at: new Date().toISOString(),
        });
        await admin.from("support_active_sessions").update({ is_active: false }).eq("admin_id", sender);

        const shortId = reportId.slice(0, 8).toUpperCase();
        await telegram(token, "answerCallbackQuery", { callback_query_id: callback.id, text: `Тест-отчёт #${shortId} принят!` });
        await telegram(token, "sendMessage", {
          chat_id: callback.message.chat.id,
          text: `🟢 <b>Вы вошли в чат отчёта #${shortId}</b>\nДля выхода введите /exit или нажмите кнопку ниже.`,
          parse_mode: "HTML",
          reply_markup: {
            inline_keyboard: [[{ text: "🔴 Завершить / Выйти", callback_data: `leave:${reportId}` }]],
          },
        });
        await updateReportCard(admin, token, callback.message.chat.id, reportId, true);
        return answer();
      }

      // Test Center: Leave
      if (data.startsWith("leave:")) {
        const reportId = data.replace("leave:", "").trim();
        await admin.from("feedback_chat_sessions").update({ is_active: false, updated_at: new Date().toISOString() }).eq("admin_id", sender);
        const shortId = reportId.slice(0, 8).toUpperCase();
        await telegram(token, "answerCallbackQuery", { callback_query_id: callback.id, text: "Чат отчёта завершён" });
        await telegram(token, "sendMessage", {
          chat_id: callback.message.chat.id,
          text: `🔴 <b>Чат отчёта #${shortId} завершён.</b>`,
          parse_mode: "HTML",
        });
        await updateReportCard(admin, token, callback.message.chat.id, reportId, false);
        return answer();
      }

      return answer();
    }

    const message = update?.message;
    const text = clean(message?.text ?? message?.caption);
    if (!text) return answer();

    // Command to exit any active chat
    if (text === "/exit" || text === "/leave" || text === "/stop") {
      await admin.from("support_active_sessions").update({ is_active: false, updated_at: new Date().toISOString() }).eq("admin_id", sender);
      await admin.from("feedback_chat_sessions").update({ is_active: false, updated_at: new Date().toISOString() }).eq("admin_id", sender);
      await telegram(token, "sendMessage", {
        chat_id: message.chat.id,
        text: "🔴 <b>Вы вышли из активного диалога.</b>\nБот снова в общем режиме.",
        parse_mode: "HTML",
      });
      return answer();
    }

    const replyId = message?.reply_to_message?.message_id;

    // 1. Direct Reply routing:
    if (replyId) {
      // Check Support Chat messages mapping
      const supMatch = await admin.from("telegram_support_messages").select("thread_id").eq("telegram_message_id", replyId).maybeSingle();
      let supThreadId = supMatch?.data?.thread_id;
      if (!supThreadId) {
        const supThread = await admin.from("support_threads").select("id").eq("telegram_message_id", replyId).maybeSingle();
        if (supThread?.data) supThreadId = supThread.data.id;
      }

      if (supThreadId) {
        await admin.from("support_messages").insert({
          thread_id: supThreadId,
          sender_type: "admin",
          sender_name: "dakower",
          body: text,
        });
        await admin.from("support_threads").update({ status: "active", updated_at: new Date().toISOString() }).eq("id", supThreadId);

        await telegram(token, "sendMessage", {
          chat_id: message.chat.id,
          reply_to_message_id: message.message_id,
          text: `✓ Ответ отправлен в чат поддержки · #${supThreadId.slice(0, 8).toUpperCase()}`,
        });
        await updateSupportCard(admin, token, message.chat.id, supThreadId, false);
        return answer();
      }

      // Check Test Center messages mapping
      const tcMatch = await admin.from("telegram_report_messages").select("report_id").eq("telegram_message_id", replyId).maybeSingle();
      let tcReportId = tcMatch?.data?.report_id;
      if (!tcReportId) {
        const tcRep = await admin.from("feedback_reports").select("id").eq("telegram_message_id", replyId).maybeSingle();
        if (tcRep?.data) tcReportId = tcRep.data.id;
      }

      if (tcReportId) {
        await admin.from("feedback_messages").insert({
          report_id: tcReportId,
          sender_type: "admin",
          sender_name: "dakower",
          body: text,
        });
        await admin.from("feedback_reports").update({ status: "checking", updated_at: new Date().toISOString() }).eq("id", tcReportId);

        await telegram(token, "sendMessage", {
          chat_id: message.chat.id,
          reply_to_message_id: message.message_id,
          text: `✓ Ответ отправлен в Тест-Центр · #${tcReportId.slice(0, 8).toUpperCase()}`,
        });
        await updateReportCard(admin, token, message.chat.id, tcReportId, false);
        return answer();
      }
    }

    // 2. Active Session routing (plain text without reply):
    // Check Support Chat active session first
    const activeSupport = await admin
      .from("support_active_sessions")
      .select("thread_id")
      .eq("admin_id", sender)
      .eq("is_active", true)
      .maybeSingle();

    if (activeSupport?.data) {
      const threadId = activeSupport.data.thread_id;
      await admin.from("support_messages").insert({
        thread_id: threadId,
        sender_type: "admin",
        sender_name: "dakower",
        body: text,
      });
      await admin.from("support_threads").update({ status: "active", updated_at: new Date().toISOString() }).eq("id", threadId);

      await telegram(token, "sendMessage", {
        chat_id: message.chat.id,
        text: `✓ Доставлено в #${threadId.slice(0, 8).toUpperCase()}`,
      });
      await updateSupportCard(admin, token, message.chat.id, threadId, true);
      return answer();
    }

    // Check Test Center active session
    const activeTc = await admin
      .from("feedback_chat_sessions")
      .select("report_id")
      .eq("admin_id", sender)
      .eq("is_active", true)
      .maybeSingle();

    if (activeTc?.data) {
      const reportId = activeTc.data.report_id;
      await admin.from("feedback_messages").insert({
        report_id: reportId,
        sender_type: "admin",
        sender_name: "dakower",
        body: text,
      });
      await admin.from("feedback_reports").update({ status: "checking", updated_at: new Date().toISOString() }).eq("id", reportId);

      await telegram(token, "sendMessage", {
        chat_id: message.chat.id,
        text: `✓ Доставлено в Тест-Центр #${reportId.slice(0, 8).toUpperCase()}`,
      });
      await updateReportCard(admin, token, message.chat.id, reportId, true);
      return answer();
    }

    // No active chat and not a reply
    await telegram(token, "sendMessage", {
      chat_id: message.chat.id,
      text: "ℹ️ <b>Нет активного диалога.</b>\nНажмите «🟢 Принять чат» на нужном обращении или ответьте на него через <b>Reply</b>.",
      parse_mode: "HTML",
    });

    return answer();
  } catch (error) {
    console.error("luma-feedback-telegram error", error);
    return answer({ error: "webhook_failed" }, 500);
  }
});
