# Luma 2.0.9 stable release

## Prerequisites

- Windows 10/11 x64 with .NET 8 SDK and Windows SDK.
- ECDSA update private key stored outside the repository.
- Authenticode certificate installed in the Windows certificate store.
- Production Supabase and Cloudflare R2 secrets.

## Build a signed local release

```powershell
$env:LUMA_CODESIGN_THUMBPRINT='CERTIFICATE_THUMBPRINT'
.\tools\release-preflight.ps1 -SourceOnly
.\release.ps1 -PrivateKeyPath 'D:\Luma-Secrets\luma-update-private.pem'
```

## Build, sign and upload the update

```powershell
$env:LUMA_CODESIGN_THUMBPRINT='CERTIFICATE_THUMBPRINT'
.\release.ps1 -PrivateKeyPath 'D:\Luma-Secrets\luma-update-private.pem' -Upload
```

`-Upload` uploads the update ZIP first and the signed manifest last. The public installer and `SHA256SUMS.txt` are produced in `dist\release\installer`. Upload them to the official HTTPS download location only after completing `RELEASE-CHECKLIST.md`.

Unsigned output is blocked for stable releases. `-AllowUnsigned` exists only for private testing and must never be used for the public release.
