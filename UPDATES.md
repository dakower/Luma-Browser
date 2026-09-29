# Подписанные обновления Luma

Начиная с 2.1.0, проверка критических и обычных обновлений не требует входа в Luma Account. Подлинность обеспечивается ECDSA-подписью манифеста, встроенным публичным ключом, HTTPS и SHA-256 пакета.

## Сервер

Разверните `supabase/functions/luma-update/index.ts` без JWT-проверки:

```powershell
supabase functions deploy luma-update --no-verify-jwt
```

Настройте секреты `R2_ACCOUNT_ID`, `R2_ACCESS_KEY_ID`, `R2_SECRET_ACCESS_KEY`, `R2_BUCKET`. При необходимости задайте `R2_UPDATE_MANIFEST_KEY` и `R2_BETA_UPDATE_MANIFEST_KEY`.

## Публикация

```powershell
$env:LUMA_CODESIGN_THUMBPRINT='CERTIFICATE_THUMBPRINT'
npx wrangler login
.\release.ps1 -PrivateKeyPath 'D:\Luma-Secrets\luma-update-private.pem' -Upload
```

Скрипт собирает один и тот же подписанный browser payload для установщика и автообновления, создаёт `Luma-2.1.0-x64.zip`, SHA-256 и подписанный `manifest.json`. ZIP загружается первым, манифест — последним. Профиль хранится в `%LOCALAPPDATA%\Luma`; updater создаёт backup и откатывается, если новая Luma не подтверждает запуск.
