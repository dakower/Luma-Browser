# Luma Setup 2.1.0

The stable release is distributed as the signed single-file installer `LumaSetup-2.1.0-x64.exe` with `SHA256SUMS.txt`.

## Modes

- Current user: `%LOCALAPPDATA%\Programs\Luma`, no elevation.
- All users: `%ProgramFiles%\Luma`, UAC is requested only after installation is confirmed.
- A custom destination can be selected in the Luma-styled path picker.

## Safety

The embedded payload is SHA-256 verified, extracted with zip-slip protection into a sibling staging directory, validated for `Luma.exe`, and atomically swapped with the existing installation. The previous installation is restored when replacement fails. Only `Luma.exe` from the selected installation directory is stopped.

Stable installers must have a valid timestamped Authenticode signature. Set `LUMA_CODESIGN_THUMBPRINT` before running `release.ps1`; unsigned output is permitted only for private testing with `-AllowUnsigned`.

## Integration

Optional Desktop and Start Menu shortcuts, optional per-user autostart, browser protocol registration, Default Apps prompt, and Windows Installed Apps registration are supported. `Uninstall.exe` is a signed copy of the installer and can keep or delete the local profile.

## WebView2 Runtime recovery

`build.ps1` downloads the official Microsoft-signed Evergreen bootstrapper, validates its signer, and places it in the browser payload. Setup checks the Runtime using the official WebView2 API, repairs it when necessary, validates the result, and can retry through UAC.
