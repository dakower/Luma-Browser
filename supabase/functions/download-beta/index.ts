import { createClient } from "https://esm.sh/@supabase/supabase-js@2";
const cors = { "Access-Control-Allow-Origin": "*", "Access-Control-Allow-Headers": "authorization, x-client-info, apikey, content-type" };
Deno.serve(async (request) => {
  if (request.method === "OPTIONS") return new Response("ok", { headers: cors });
  try {
    const authorization = request.headers.get("Authorization") ?? "";
    const url = Deno.env.get("SUPABASE_URL")!;
    const anon = createClient(url, Deno.env.get("SUPABASE_ANON_KEY")!, { global: { headers: { Authorization: authorization } } });
    const admin = createClient(url, Deno.env.get("SUPABASE_SERVICE_ROLE_KEY")!);
    const { data: { user } } = await anon.auth.getUser();
    if (!user) return new Response(JSON.stringify({ error: "unauthorized" }), { status: 401, headers: { ...cors, "Content-Type": "application/json" } });
    const { data: profile } = await admin.from("profiles").select("role").eq("id", user.id).maybeSingle();
    const { data: access } = await admin.from("beta_access").select("status").eq("user_id", user.id).maybeSingle();
    if (profile?.role !== "admin" && access?.status !== "approved") return new Response(JSON.stringify({ error: "beta_access_required" }), { status: 403, headers: { ...cors, "Content-Type": "application/json" } });
    const file = Deno.env.get("BETA_INSTALLER_PATH") ?? "windows/LumaSetup-Beta-x64.exe";
    const { data, error } = await admin.storage.from("beta-installers").createSignedUrl(file, 60, { download: true });
    if (error) throw error;
    return new Response(JSON.stringify({ downloadUrl: data.signedUrl }), { headers: { ...cors, "Content-Type": "application/json", "Cache-Control": "no-store" } });
  } catch (error) {
    return new Response(JSON.stringify({ error: String(error) }), { status: 500, headers: { ...cors, "Content-Type": "application/json" } });
  }
});
