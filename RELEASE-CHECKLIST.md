# Luma stable release checklist

## Required secrets and certificates

- [ ] ECDSA update private key is stored outside the repository.
- [ ] `src/Luma/update-public.pem` matches that private key.
- [ ] Authenticode code-signing certificate is installed in the Windows certificate store.
- [ ] `LUMA_CODESIGN_THUMBPRINT` points to that certificate.
- [ ] R2 and Supabase production secrets are configured.

## Build

```powershell
$env:LUMA_CODESIGN_THUMBPRINT='CERTIFICATE_THUMBPRINT'
.\tools\release-preflight.ps1 -SourceOnly
.\release.ps1 -PrivateKeyPath 'D:\Luma-Secrets\luma-update-private.pem'
```

- [ ] Core tests pass.
- [ ] Browser and installer compile in Release for win-x64.
- [ ] `Luma.exe` and `LumaSetup-<version>-x64.exe` have valid timestamped signatures.
- [ ] Installer SHA-256 matches `SHA256SUMS.txt`.
- [ ] Update manifest signature verifies with the embedded public key.

## Clean-machine tests

- [ ] Windows 10 x64 current supported build.
- [ ] Windows 11 x64 current supported build.
- [ ] Current-user install and Program Files install.
- [ ] WebView2 already installed and WebView2 recovery path.
- [ ] Upgrade from the previous stable version preserves profile, tabs and account.
- [ ] Uninstall with profile preservation and full profile deletion.
- [ ] Browser registration, shortcuts and default-browser prompt.

## Regression

- [ ] Luma Search, result opening, Back and custom network error page.
- [ ] Account restoration, sign-in, sign-out and expired-token refresh.
- [ ] Downloads start, progress, cancel, finish, open and reveal.
- [ ] YouTube Music, Spotify and SoundCloud media widgets.
- [ ] Floating music and fullscreen floating video after Alt+Tab.
- [ ] RU, EN and UK interfaces contain no mixed-language critical screens.
- [ ] Themes, scale, private mode, history, import and crash reporting.
- [ ] Signed-out users can check and install updates.
- [ ] Failed update rolls back to the previous working installation.

## Production publication

- [ ] Deploy `luma-update` with JWT verification disabled.
- [ ] Upload update ZIP first and signed manifest last.
- [ ] Publish signed installer and SHA-256 on the official HTTPS download page.
- [ ] Publish release notes, privacy policy, security contact and known issues.
- [ ] Keep the previous stable installer and update package available for rollback.
