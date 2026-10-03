import { createClient } from "https://esm.sh/@supabase/supabase-js@2";
import { GetObjectCommand, S3Client } from "npm:@aws-sdk/client-s3@3";
import { getSignedUrl } from "npm:@aws-sdk/s3-request-presigner@3";

const cors = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Headers": "authorization, x-client-info, apikey, content-type",
  "Access-Control-Allow-Methods": "GET, POST, OPTIONS"
};

Deno.serve(async (request) => {
  if (request.method === "OPTIONS") return new Response("ok", { headers: cors });

  try {
    const authorization = request.headers.get("Authorization") ?? "";
    const url = Deno.env.get("SUPABASE_URL")!;
    const anon = createClient(url, Deno.env.get("SUPABASE_ANON_KEY")!, {
      global: { headers: { Authorization: authorization } }
    });
    const admin = createClient(url, Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!);

    // Determine platform: "android" or "windows"
    let platform = "windows";
    let body: any = {};
    try {
      if (request.method === "POST") {
        body = await request.json().catch(() => ({}));
        if (body?.platform) platform = String(body.platform).toLowerCase();
      } else {
        const u = new URL(request.url);
        const p = u.searchParams.get("platform");
        if (p) platform = p.toLowerCase();
      }
    } catch {
      // default to windows
    }

    const reqUrl = new URL(request.url);
    const wantsJson = request.method === "POST" ||
      reqUrl.searchParams.get("format") === "json" ||
      (request.headers.get("accept")?.includes("application/json") ?? false);

    function respondWithUrl(downloadUrl: string, plat: string, file: string) {
      if (!wantsJson && request.method === "GET") {
        return new Response(null, {
          status: 302,
          headers: {
            ...cors,
            "Location": downloadUrl,
            "Cache-Control": "no-store"
          }
        });
      }
      return new Response(JSON.stringify({ downloadUrl, platform: plat, file }), {
        headers: { ...cors, "Content-Type": "application/json", "Cache-Control": "no-store" }
      });
    }

    // Android Beta is public! Anyone visiting the website can download the APK
    if (platform === "android" || platform === "apk") {
      const file = Deno.env.get("BETA_ANDROID_PATH") ?? "android/Luma-2.1.4-Android.apk";
      const { data, error } = await admin.storage.from("beta-installers").createSignedUrl(file, 7200, { download: "Luma-2.1.4-Android.apk" });
      if (error) throw error;
      return respondWithUrl(data.signedUrl, "android", file);
    }

    if (platform === "get_upload_url") {
      const target = body?.target || "android/Luma-2.1.4-Android.apk";
      const { data, error } = await admin.storage.from("beta-installers").createSignedUploadUrl(target, { upsert: true });
      return new Response(JSON.stringify({ uploadUrl: data?.signedUrl, error }), {
        headers: { ...cors, "Content-Type": "application/json", "Cache-Control": "no-store" }
      });
    }

    // Windows platform: Public download for all visitors (no registration required)
    // Try R2 first, then fallback to Supabase Storage
    const r2Id = Deno.env.get("R2_ACCOUNT_ID");
    const r2Key = Deno.env.get("R2_ACCESS_KEY_ID");
    const r2Secret = Deno.env.get("R2_SECRET_ACCESS_KEY");
    const r2Bucket = Deno.env.get("R2_BUCKET") || "luma-beta-installers";
    const r2File = Deno.env.get("R2_INSTALLER_KEY") || "windows/LumaSetup-Beta-x64.exe";

    if (r2Id && r2Key && r2Secret) {
      try {
        const r2 = new S3Client({
          region: "auto",
          endpoint: `https://${r2Id}.r2.cloudflarestorage.com`,
          forcePathStyle: true,
          credentials: { accessKeyId: r2Key, secretAccessKey: r2Secret }
        });
        const packageUrl = await getSignedUrl(r2, new GetObjectCommand({
          Bucket: r2Bucket,
          Key: r2File,
          ResponseContentDisposition: 'attachment; filename="LumaSetup-Beta-x64.exe"'
        }), { expiresIn: 7200 });
        return respondWithUrl(packageUrl, "windows", r2File);
      } catch (r2Err) {
        console.warn("R2 presign failed, falling back to Supabase storage", r2Err);
      }
    }

    const file = Deno.env.get("BETA_INSTALLER_PATH") ?? "windows/LumaSetup-Beta-x64.exe";
    const { data, error } = await admin.storage.from("beta-installers").createSignedUrl(file, 7200, { download: "LumaSetup-Beta-x64.exe" });
    if (error) throw error;
    return respondWithUrl(data.signedUrl, "windows", file);
  } catch (error) {
    return new Response(JSON.stringify({ error: String(error) }), {
      status: 500,
      headers: { ...cors, "Content-Type": "application/json" }
    });
  }
});
