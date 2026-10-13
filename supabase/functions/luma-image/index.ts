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

Deno.serve(async (request) => {
  if (request.method === "OPTIONS") return new Response("ok", { headers: cors });
  if (request.method !== "POST") return json({ error: "method_not_allowed" }, 405);

  const supabaseUrl = Deno.env.get("SUPABASE_URL") ?? "";
  const anonKey = Deno.env.get("SUPABASE_ANON_KEY") ?? "";
  const serviceKey = Deno.env.get("SUPABASE_SERVICE_ROLE_KEY") ?? "";

  const admin = createClient(supabaseUrl, serviceKey, {
    auth: { persistSession: false, autoRefreshToken: false },
  });

  // Verify user authorization (optional, fallback to service if anon)
  const authHeader = request.headers.get("Authorization") ?? "";
  const userClient = createClient(supabaseUrl, anonKey, {
    global: { headers: { Authorization: authHeader } },
    auth: { persistSession: false, autoRefreshToken: false },
  });
  const { data: { user } } = await userClient.auth.getUser();

  let body: any;
  try {
    body = await request.json();
  } catch {
    return json({ error: "invalid_json" }, 400);
  }

  const prompt = (body?.prompt || "").trim();
  if (!prompt) return json({ error: "empty_prompt", message: "Промпт не может быть пустым" }, 400);

  // Check 2 images/day quota for non-unlimited users
  const isUnlimited = user?.email && user.email.toLowerCase() === "dakowerr@gmail.com";
  if (!isUnlimited && user?.id) {
    try {
      const { data: quotaRes, error: qErr } = await userClient.rpc("consume_image_quota");
      if (!qErr && quotaRes && quotaRes.allowed === false) {
        return json({
          error: "daily_image_limit_exceeded",
          message: "Дневной лимит генерации изображений исчерпан (2 из 2 в день). Обычные ответы LumaAI остаются доступны."
        }, 429);
      }
    } catch (quotaErr) {
      console.warn("Quota check error, proceeding with generation:", quotaErr);
    }
  }

  // References: up to 3 images
  const images: Array<{ base64?: string; url?: string; mime?: string }> = Array.isArray(body?.images) ? body.images.slice(0, 3) : [];

  // 1. Read API Key and Endpoint from environment secrets first
  let apiKey = Deno.env.get("NANOBANANA_API_KEY") || "";
  let endpoint = Deno.env.get("NANOBANANA_ENDPOINT") || "";
  let model = Deno.env.get("NANOBANANA_MODEL") || "";

  // 2. If not found in secrets, read from app_config table in Supabase
  if (!apiKey || !endpoint || !model) {
    try {
      const { data: configs } = await admin.from("app_config").select("key, value");
      if (configs) {
        for (const row of configs) {
          if (!apiKey && (row.key === "nanobanana_api_key" || row.key === "nano_banana_api_key")) apiKey = (row.value || "").trim();
          if (!endpoint && (row.key === "nanobanana_endpoint" || row.key === "nano_banana_endpoint")) endpoint = (row.value || "").trim();
          if (!model && (row.key === "nanobanana_model" || row.key === "nano_banana_model")) model = (row.value || "").trim();
        }
      }
    } catch (dbErr) {
      console.error("Error reading app_config table:", dbErr);
    }
  }

  if (!endpoint) endpoint = "https://www.cun.ai";
  if (!model) model = endpoint.includes("cun.ai") || apiKey.startsWith("sk-") ? "gemini-3.1-flash-image-preview" : "nano-banana-2";

  if (!apiKey) {
    return json({
      error: "api_key_missing",
      message: "API ключ Nano Banana не установлен. Добавьте его в Supabase в таблицу app_config (строка nanobanana_api_key) или в секреты Edge Functions (NANOBANANA_API_KEY)."
    }, 400);
  }

  // Upload reference images to Supabase storage if they are base64
  const imageUrls: string[] = [];
  const userId = user?.id || "anon";
  for (let i = 0; i < images.length; i++) {
    const img = images[i];
    if (img.url && img.url.startsWith("http")) {
      imageUrls.push(img.url);
    } else if (img.base64) {
      try {
        const rawBytes = Uint8Array.from(atob(img.base64), (c) => c.charCodeAt(0));
        const mime = img.mime || "image/jpeg";
        const ext = mime.includes("png") ? "png" : mime.includes("webp") ? "webp" : "jpg";
        const fileName = `ref_${userId}_${Date.now()}_${i}.${ext}`;
        const { error: upErr } = await admin.storage
          .from("assistant-images")
          .upload(fileName, rawBytes, { contentType: mime, upsert: true });

        if (!upErr) {
          const { data: pubData } = admin.storage.from("assistant-images").getPublicUrl(fileName);
          if (pubData?.publicUrl) imageUrls.push(pubData.publicUrl);
        } else {
          imageUrls.push(`data:${mime};base64,${img.base64}`);
        }
      } catch (err) {
        console.error("Reference upload error", err);
        const mime = img.mime || "image/jpeg";
        imageUrls.push(`data:${mime};base64,${img.base64}`);
      }
    }
  }

  try {
    // 1. Kie.ai Task API
    if (endpoint.includes("kie.ai")) {
      const taskPayload = {
        model: model,
        input: {
          prompt: prompt,
          image_input: imageUrls,
          aspect_ratio: body?.aspect_ratio || "1:1",
        },
      };

      const res = await fetch(endpoint, {
        method: "POST",
        headers: {
          "Authorization": `Bearer ${apiKey}`,
          "Content-Type": "application/json",
        },
        body: JSON.stringify(taskPayload),
      });

      const resData = await res.json();
      if (!res.ok || (resData.code && resData.code !== 200)) {
        return json({
          error: "generation_failed",
          message: resData.msg || resData.message || "Ошибка API Nano Banana: " + res.statusText,
        }, 502);
      }

      const taskId = resData.data?.taskId || resData.taskId;
      if (!taskId) {
        const directUrl = resData.data?.imageUrl || resData.data?.url || resData.url;
        if (directUrl) return json({ success: true, imageUrl: directUrl, prompt });
        return json({ error: "missing_task_id", message: "Не получен ID задачи от Nano Banana" }, 502);
      }

      // Poll for task completion (up to 45 seconds)
      const recordUrl = endpoint.replace("/createTask", "/recordInfo") + `?taskId=${taskId}`;
      let finalImageUrl = "";
      for (let attempt = 0; attempt < 30; attempt++) {
        await new Promise((r) => setTimeout(r, 1500));
        const checkRes = await fetch(recordUrl, {
          headers: { "Authorization": `Bearer ${apiKey}` },
        });
        if (!checkRes.ok) continue;
        const checkData = await checkRes.json();
        const state = checkData.data?.state || checkData.state;
        if (state === "success") {
          finalImageUrl = checkData.data?.resultUrl || checkData.data?.result?.image_url ||
                          checkData.data?.images?.[0] || checkData.data?.output?.[0] || "";
          break;
        } else if (state === "failed" || state === "error") {
          return json({
            error: "generation_failed",
            message: checkData.data?.error || "Генерация не удалась со стороны Nano Banana.",
          }, 502);
        }
      }

      if (!finalImageUrl) {
        return json({ error: "timeout", message: "Время ожидания генерации Nano Banana истекло." }, 504);
      }

      return json({ success: true, imageUrl: finalImageUrl, prompt });
    }

    // 2. cun.ai / NewAPI / OpenAI Chat Completions (e.g. gemini-3-pro-image-preview)
    if (endpoint.includes("cun.ai") || endpoint.includes("chat/completions") || apiKey.startsWith("sk-")) {
      const chatEndpoint = endpoint.includes("/v1/chat/completions")
        ? endpoint
        : endpoint.replace(/\/+$/, "") + (endpoint.includes("/v1") ? "/chat/completions" : "/v1/chat/completions");

      const userContent: any[] = [{ type: "text", text: prompt }];
      for (const imgUrl of imageUrls) {
        userContent.push({ type: "image_url", image_url: { url: imgUrl } });
      }

      const res = await fetch(chatEndpoint, {
        method: "POST",
        headers: {
          "Authorization": `Bearer ${apiKey}`,
          "Content-Type": "application/json",
        },
        body: JSON.stringify({
          model: model || "gemini-3.1-flash-image-preview",
          messages: [{ role: "user", content: userContent }],
        }),
      });

      const resData = await res.json();
      if (!res.ok) {
        return json({
          error: "generation_failed",
          message: resData.error?.message || resData.msg || "Ошибка API " + res.statusText,
        }, 502);
      }

      const content = resData.choices?.[0]?.message?.content || "";
      // Match markdown image ![alt](url) or data:image
      const mdMatch = content.match(/!\[.*?\]\((data:image\/[^;]+;base64,[^)]+|https?:\/\/[^\s)]+)\)/i);
      let foundUrl = mdMatch ? mdMatch[1] : "";
      if (!foundUrl) {
        const rawB64Match = content.match(/data:image\/[a-zA-Z0-9.+-]+;base64,[A-Za-z0-9+/=]+/);
        if (rawB64Match) foundUrl = rawB64Match[0];
        else {
          const httpMatch = content.match(/https?:\/\/[^\s"'>)]+/);
          if (httpMatch) foundUrl = httpMatch[0];
        }
      }

      if (foundUrl) {
        return json({ success: true, imageUrl: foundUrl, prompt, text: content });
      }

      if (content) {
        return json({ success: true, prompt, text: content });
      }

      return json({ error: "no_image_returned", message: "Модель не вернула изображение." }, 502);
    }

    // 3. OpenAI-compatible images/generations
    if (endpoint.includes("images/generations")) {
      const res = await fetch(endpoint, {
        method: "POST",
        headers: {
          "Authorization": `Bearer ${apiKey}`,
          "Content-Type": "application/json",
        },
        body: JSON.stringify({
          model: model,
          prompt: prompt,
          n: 1,
          size: "1024x1024",
          response_format: "url",
        }),
      });
      const data = await res.json();
      const imgUrl = data?.data?.[0]?.url || data?.data?.[0]?.b64_json;
      if (!imgUrl) return json({ error: "generation_failed", message: "Не удалось получить изображение." }, 502);
      return json({ success: true, imageUrl: imgUrl.startsWith("http") ? imgUrl : `data:image/png;base64,${imgUrl}`, prompt });
    }

    // 3. Fallback generic post
    const res = await fetch(endpoint, {
      method: "POST",
      headers: { "Authorization": `Bearer ${apiKey}`, "Content-Type": "application/json" },
      body: JSON.stringify({ prompt, images: imageUrls, model }),
    });
    const data = await res.json();
    const resultUrl = data.url || data.imageUrl || data.data?.url || data.result;
    if (resultUrl) return json({ success: true, imageUrl: resultUrl, prompt });
    return json({ error: "unsupported_endpoint", message: "Не удалось распознать формат ответа провайдера." }, 502);

  } catch (err: any) {
    console.error("luma-image error", err);
    return json({ error: "internal_error", message: err.message || "Внутренняя ошибка генерации" }, 500);
  }
});
