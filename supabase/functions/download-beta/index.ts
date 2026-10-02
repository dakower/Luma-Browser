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
    try {
      if (request.method === "POST") {
        const body = await request.json().catch(() => ({}));
        if (body?.platform) platform = String(body.platform).toLowerCase();
      } else {
        const u = new URL(request.url);
        const p = u.searchParams.get("platform");
        if (p) platform = p.toLowerCase();
      }
    } catch {
      // default to windows
    }

    // Android Beta is public! Anyone visiting the website can download the APK
    if (platform === "android" || platform === "apk") {
      const file = Deno.env.get("BETA_ANDROID_PATH") ?? "android/Luma-2.1.4-Android.apk";
      const { data, error } = await admin.storage.from("beta-installers").createSignedUrl(file, 3600, { download: "Luma-2.1.4-Android.apk" });
      if (error) throw error;
      return new Response(JSON.stringify({ downloadUrl: data.signedUrl, platform: "android", file }), {
        headers: { ...cors, "Content-Type": "application/json", "Cache-Control": "no-store" }
      });
    }

    // Windows Beta requires authorization & approved beta access
    const { data: { user } } = await anon.auth.getUser();
    if (!user) {
      return new Response(JSON.stringify({ error: "unauthorized" }), {
        status: 401,
        headers: { ...cors, "Content-Type": "application/json" }
      });
    }

    const { data: profile } = await admin.from("profiles").select("role").eq("id", user.id).maybeSingle();
    const { data: access } = await admin.from("beta_access").select("status").eq("user_id", user.id).maybeSingle();

    if (profile?.role !== "admin" && access?.status !== "approved") {
      return new Response(JSON.stringify({ error: "beta_access_required" }), {
        status: 403,
        headers: { ...cors, "Content-Type": "application/json" }
      });
    }

    // Windows platform: Try R2 first, then fallback to Supabase Storage
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
        const packageUrl = await getSignedUrl(r2, new GetObjectCommand({ Bucket: r2Bucket, Key: r2File }), { expiresIn: 300 });
        return new Response(JSON.stringify({ downloadUrl: packageUrl, platform: "windows", file: r2File }), {
          headers: { ...cors, "Content-Type": "application/json", "Cache-Control": "no-store" }
        });
      } catch (r2Err) {
        console.warn("R2 presign failed, falling back to Supabase storage", r2Err);
      }
    }

    const file = Deno.env.get("BETA_INSTALLER_PATH") ?? "windows/LumaSetup-Beta-x64.exe";
    const { data, error } = await admin.storage.from("beta-installers").createSignedUrl(file, 300, { download: "LumaSetup-Beta-x64.exe" });
    if (error) throw error;
    return new Response(JSON.stringify({ downloadUrl: data.signedUrl, platform: "windows", file }), {
      headers: { ...cors, "Content-Type": "application/json", "Cache-Control": "no-store" }
    });
  } catch (error) {
    return new Response(JSON.stringify({ error: String(error) }), {
      status: 500,
      headers: { ...cors, "Content-Type": "application/json" }
    });
  }
});
