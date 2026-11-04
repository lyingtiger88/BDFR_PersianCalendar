# Zurvan Auth — Supabase client integration (development)

## Scope
- Android: email/password signup and login, encrypted access/refresh token cache backed by Android Keystore AES-GCM, refresh on demand, logout.
- Windows: same Supabase endpoints with credentials stored in Windows PasswordVault; settings panel for sign up, sign in and logout.
- Existing activation code handling remains unchanged. **Login does not confer a license.**
- Backend device enrollment / OAuth redirect / password recovery / license activation are **not implemented in this client change**. Keep these features disabled until end-to-end integration and testing.
- Never embed Supabase service_role / secret key / license private signing key in a client. The public Supabase publishable key is appropriate for client use only with correct RLS and Auth configuration.

## Configure Android build
Set Gradle properties (do not commit local secrets):

```properties
ZURVAN_SUPABASE_URL=https://YOUR_PROJECT.supabase.co
ZURVAN_SUPABASE_PUBLISHABLE_KEY=YOUR_PUBLIC_PUBLISHABLE_KEY
```

Use only the project's public/publishable key, not the server-side service-role key.
The existing app's INTERNET permission is reused.

## Configure Windows
Set `ZURVAN_SUPABASE_URL` and `ZURVAN_SUPABASE_PUBLISHABLE_KEY` environment variables before launching. They are read by `ZurvanAuthClient`. For a distributable build, move these non-secret values to a validated public configuration file. Do not require the user to configure them manually.

## Functional checklist
- [ ] Confirm Supabase email provider policy and redirect settings in the deployed project.
- [ ] Test Android email+password signup with email confirmation enabled and disabled.
- [ ] Test Android sign-in, app restart, expired access-token refresh, sign-out and network failure.
- [ ] Test Windows sign-in, restart, refresh, sign-out and network failure on supported OS.
- [ ] Verify auth data storage and Android auto-backup restore behavior.
- [ ] Add PKCE deep-link OAuth and password recovery only after redirect-host/domain configuration is selected.
- [ ] Wire device enrollment to Turnstile protected backend, verifying Ed25519 proof and account ownership.
- [ ] Wire signed license grants and status checks after server endpoints exist.
- [ ] Complete CI + physical-device testing before merge/release.

## Security properties / limitations
Session stores are OS-protected, but a compromised device can still inspect a running process. A failed remote sign-out clears local credentials but cannot guarantee server revocation if offline. There is no direct database write and the client does not store a payment or license private key. No licensing gates are changed by this patch.
