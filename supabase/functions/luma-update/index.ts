import{GetObjectCommand,S3Client}from"npm:@aws-sdk/client-s3@3";import{getSignedUrl}from"npm:@aws-sdk/s3-request-presigner@3";
const origins=new Set(["https://lumabrowser.win","https://www.lumabrowser.win"]);function h(r:Request){const o=r.headers.get("Origin");return{"Access-Control-Allow-Origin":o&&origins.has(o)?o:"https://lumabrowser.win","Access-Control-Allow-Headers":"authorization, x-client-info, apikey, content-type","Access-Control-Allow-Methods":"POST, OPTIONS","Content-Type":"application/json","Vary":"Origin"}}function j(r:Request,b:unknown,s=200){return new Response(JSON.stringify(b),{status:s,headers:h(r)})}
Deno.serve(async r => {
  if (r.method === "OPTIONS") return new Response("ok", { headers: h(r) });
  if (r.method !== "POST") return j(r, { error: "Method not allowed" }, 405);
  try {
    let requested = "stable";
    try {
      const body = await r.json();
      if (body?.channel === "beta") requested = "beta";
    } catch {}

    const id = Deno.env.get("R2_ACCOUNT_ID");
    const key = Deno.env.get("R2_ACCESS_KEY_ID");
    const secret = Deno.env.get("R2_SECRET_ACCESS_KEY");
    const bucket = Deno.env.get("R2_BUCKET") || "luma-beta-installers";

    let text: string | undefined;
    let packageUrl: string | undefined;

    // 1. Try R2 if credentials present
    if (id && key && secret) {
      try {
        const configured = Deno.env.get(requested === "beta" ? "R2_BETA_UPDATE_MANIFEST_KEY" : "R2_UPDATE_MANIFEST_KEY");
        const manifestKey = configured || `updates/${requested}/manifest.json`;
        const r2 = new S3Client({
          region: "auto",
          endpoint: "https://" + id + ".r2.cloudflarestorage.com",
          forcePathStyle: true,
          credentials: { accessKeyId: key, secretAccessKey: secret }
        });
        const obj = await r2.send(new GetObjectCommand({ Bucket: bucket, Key: manifestKey }));
        text = await obj.Body?.transformToString();
        if (text) {
          const manifest = JSON.parse(text);
          if (manifest.packageKey && manifest.packageKey.startsWith(`updates/${requested}/`)) {
            packageUrl = await getSignedUrl(r2, new GetObjectCommand({ Bucket: bucket, Key: manifest.packageKey }), { expiresIn: 900 });
          }
        }
      } catch (e) {
        // R2 fetch failed or not found, fallback to GitHub
      }
    }

    // 2. Fetch from GitHub Release latest
    try {
      const ghRes = await fetch("https://github.com/dakower/Luma-Browser/releases/latest/download/manifest.json", {
        headers: { "User-Agent": "Luma-Updater" }
      });
      if (ghRes.ok) {
        const ghText = await ghRes.text();
        const ghManifest = JSON.parse(ghText);
        if (ghManifest.version) {
          let useGh = !text;
          if (text) {
            try {
              const r2Manifest = JSON.parse(text);
              const r2Parts = (r2Manifest.version || "0").split(".").map(Number);
              const ghParts = (ghManifest.version || "0").split(".").map(Number);
              for (let i = 0; i < Math.max(r2Parts.length, ghParts.length); i++) {
                const r = r2Parts[i] || 0;
                const g = ghParts[i] || 0;
                if (g > r) { useGh = true; break; }
                if (r > g) { break; }
              }
            } catch {}
          }
          if (useGh) {
            text = ghText;
            packageUrl = `https://github.com/dakower/Luma-Browser/releases/download/v${ghManifest.version}/Luma-${ghManifest.version}-x64.zip`;
          }
        }
      }
    } catch (err) {
      console.error("GitHub releases check error:", err);
    }

    if (!text || !packageUrl) return j(r, { noUpdate: true });
    if (text.length > 131072) throw Error("Invalid manifest");

    const manifest = JSON.parse(text);
    if (manifest.channel !== requested || typeof manifest.signature !== "string" || typeof manifest.packageKey !== "string" || !manifest.packageKey.startsWith(`updates/${requested}/`) || manifest.packageKey.includes("..") || manifest.packageKey.includes("\\")) {
      throw Error("Unsafe manifest");
    }

    return j(r, { manifest, packageUrl });
  } catch (e) {
    console.error("luma-update", e);
    return j(r, { error: "Unable to check updates" }, 500);
  }
});
