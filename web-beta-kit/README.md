# Current-site beta integration kit

The current website source was not included, so this folder contains the secure backend/client contract to merge into it.

- Registration modal: use the existing Supabase `signUp`, with an optional beta code field. If email confirmation is enabled, redeem the code after the user's first confirmed login.
- Account menu: call `get_my_beta_access`. Display `Пользователь` for a normal account and `Тестер` when `has_beta` is true. Display `Администратор` only when `is_admin` is true.
- Download button: only render for `has_beta`/`is_admin`, and call the `download-beta` Edge Function. Never expose a public installer URL.
- Upload the installer to private bucket `beta-installers` at `windows/LumaSetup-Beta-x64.exe`, or set Edge Function secret `BETA_INSTALLER_PATH` to another object path.
