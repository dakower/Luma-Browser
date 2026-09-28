# Luma Beta access setup — 2.0.9

## 1. Apply Supabase migrations

Apply every migration in `supabase/migrations` in numeric order. Never expose the Supabase service-role key in the website or browser client.

## 2. Deploy protected beta download

```powershell
supabase functions deploy download-beta
supabase secrets set BETA_INSTALLER_PATH=windows/LumaSetup-2.0.9-x64.exe
```

## 3. Upload the signed installer

Build the signed release on Windows:

```powershell
$env:LUMA_CODESIGN_THUMBPRINT='CERTIFICATE_THUMBPRINT'
.\release.ps1 -PrivateKeyPath 'D:\Luma-Secrets\luma-update-private.pem'
```

Upload `dist\release\installer\LumaSetup-2.0.9-x64.exe` to the private `beta-installers/windows` path and verify its SHA-256 against `SHA256SUMS.txt`. The object path must match `BETA_INSTALLER_PATH`.

## 4. Beta access

Create beta codes from the Luma administrator panel. Only a hash is stored; a lost full code cannot be recovered. Confirm usage in `beta_codes` and granted access in `beta_access`.

## 5. Website

Use `web-beta-kit/client.js` as the integration reference. A website can download but cannot silently execute an EXE. Public stable installers must be Authenticode-signed; never instruct stable users to bypass SmartScreen.
