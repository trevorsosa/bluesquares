# BlueSquares Go-Live Guide

This guide turns the current BlueSquares app into a production launch runbook.

Launch markets: **South Africa (ZA)**, **United Kingdom (GB)**, **Ireland (IE)**.

Replace `https://YOUR_DOMAIN` with your real live domain everywhere below, for example `https://squares.blue`.

---

## 1. Decide the production URL

Before touching infrastructure or third-party providers, lock down the exact public URL.

Choose:

- your primary domain, for example `https://squares.blue`
- whether `https://www.squares.blue` should also work
- which host should be the canonical one that users always end up on

Why this matters:

- `AppSettings:BaseUrl` is used throughout the app for links, payment redirects, emails, and WhatsApp messages
- webhook providers must point at the final live domain
- Firebase auth and accounting OAuth callbacks must match the live domain exactly

If you are not using `squares.blue` and `www.squares.blue`, review `Program.cs` before launch because CORS currently allows:

- the host in `AppSettings:BaseUrl`
- `https://www.squares.blue`

If your live hostnames are different, update that list before going live.

---

## 2. Prepare production infrastructure

Provision the production environment first so all later setup uses the real host and real database.

You need:

- a production server or app host for the ASP.NET app
- PostgreSQL for the main application database
- DNS for the chosen domain
- HTTPS certificate support
- a secure place for production secrets

Recommended preparation steps:

1. Create the production PostgreSQL database.
2. Create a dedicated database user with the minimum required permissions.
3. Provision the application host.
4. Point DNS to the production host.
5. Enable HTTPS and confirm certificate issuance works.
6. Decide where secrets will live in production:
   - environment variables
   - server secret store
   - host-managed configuration

Do not rely on production values living only in `appsettings.json`.

---

## 3. Set all required production configuration

BlueSquares will not run correctly in production until the required config values are set.

Use environment variables or a secure configuration store for these keys:

| Area | Required keys |
|------|---------------|
| App | `ConnectionStrings:DefaultConnection`, `AppSettings:BaseUrl`, `AllowedHosts` |
| Firebase | `Firebase:CredentialsPath` |
| Email | `SendPulse:Id`, `SendPulse:Secret`, `SendPulse:FromEmail`, `SendPulse:FromName` |
| WhatsApp | `WhatsApp:AccessToken`, `WhatsApp:PhoneNumberId`, `WhatsApp:VerifyToken`, `WhatsApp:AppSecret` |
| SaaS billing | `Paystack:SecretKey`, `Paystack:PlanCode:ZA:Monthly`, `Paystack:PlanCode:ZA:Annual`, `PayPal:ClientId`, `PayPal:ClientSecret`, `PayPal:PlanId:GB:Monthly`, `PayPal:PlanId:GB:Annual`, `PayPal:PlanId:IE:Monthly`, `PayPal:PlanId:IE:Annual` |
| Invoice payment gateways | provider-specific merchant credentials are entered per merchant inside the app after onboarding |
| Accounting | `AccountingIntegrations:Xero:ClientId`, `AccountingIntegrations:Xero:ClientSecret`, `AccountingIntegrations:Xero:RedirectUri`, `AccountingIntegrations:QuickBooks:ClientId`, `AccountingIntegrations:QuickBooks:ClientSecret`, `AccountingIntegrations:QuickBooks:RedirectUri`, `AccountingIntegrations:QuickBooks:Environment` |
| Stripe | `Stripe:WebhookSecret` |

Minimum configuration checks:

1. Set `ConnectionStrings:DefaultConnection` to the real PostgreSQL connection string.
2. Set `AppSettings:BaseUrl` to the exact live URL, including `https://`.
3. Set `AllowedHosts` to the production hosts you want accepted.
4. Move the Firebase service account JSON outside the repo and point `Firebase:CredentialsPath` at that file.
5. Replace all placeholder values such as `YOUR_*` before starting the app.

Important production notes:

- the app throws at startup if `ConnectionStrings:DefaultConnection` is missing or still contains the placeholder value
- recurring invoice generation and reminder sending run as hosted background services, so the web app must stay running continuously in production
- `QuickBooks:Environment` should be changed from `sandbox` to the live environment when you are ready for real usage

---

## 4. Protect secrets before deployment

Do this before uploading or publishing anything.

1. Make sure no production keys are committed into the repo.
2. Keep `firebase-credentials.json` out of source control.
3. Store production secrets outside the project directory where possible.
4. Rotate any credential that was ever shared in email, chat, or a copied repo folder.
5. Restrict server access to only the people who need it.

Treat these as sensitive:

- database credentials
- Firebase service account JSON
- SendPulse credentials
- WhatsApp access token and app secret
- Paystack secret key
- PayPal client secret
- Stripe webhook secret
- Xero and QuickBooks client secrets

---

## 5. Configure Firebase for production

BlueSquares depends on Firebase auth, so this needs to be correct before user testing.

In the Firebase console:

1. Create or confirm the production Firebase project.
2. Enable the sign-in methods you actually use:
   - Email/Password
   - Google
   - Facebook
3. Add the live domain to authorized domains.
4. Configure the password reset template and any auth email templates.
5. Download the production service account JSON.
6. Place that JSON on the server outside the repo.
7. Point `Firebase:CredentialsPath` at that file.

Frontend check:

1. Open `wwwroot/js/app.js`.
2. Confirm the Firebase web config matches the production Firebase project.
3. Check any standalone auth pages that may also embed Firebase config.
4. Publish only after production Firebase values are in place.

---

## 6. Configure email delivery

BlueSquares sends emails through SendPulse, so validate email before launch.

1. Create or confirm the production SendPulse account.
2. Add and verify the sending domain if required by SendPulse.
3. Set:
   - `SendPulse:Id`
   - `SendPulse:Secret`
   - `SendPulse:FromEmail`
   - `SendPulse:FromName`
4. Confirm the sender address matches your real domain and branding.
5. Send a live test email from the production environment.

Verify:

- the email arrives
- links inside the email use the production `BaseUrl`
- spam filtering is acceptable

---

## 7. Configure WhatsApp for production

WhatsApp is a core product channel, so do not leave this until the last minute.

1. In Meta, create or confirm the production app and phone number.
2. Set the production values:
   - `WhatsApp:AccessToken`
   - `WhatsApp:PhoneNumberId`
   - `WhatsApp:VerifyToken`
   - `WhatsApp:AppSecret`
3. Create the templates BlueSquares needs in production, for example:
   - invoice messages
   - payment reminders
   - receipts
4. Submit templates for approval.
5. Register the live webhook URL:
   - `GET https://YOUR_DOMAIN/api/webhooks/whatsapp`
   - `POST https://YOUR_DOMAIN/api/webhooks/whatsapp`
6. Complete Meta webhook verification using the same value as `WhatsApp:VerifyToken`.
7. Send a real test message from production.
8. Reply to that message and confirm incoming webhook processing works.

Important:

- Meta template approval often takes longer than the rest of technical deployment
- do not promise a same-day launch if template approval is still pending

---

## 8. Configure SaaS subscription billing

BlueSquares has launch pricing for:

- South Africa through Paystack
- United Kingdom through PayPal
- Ireland through PayPal

### 8.1 Paystack SaaS setup for South Africa

1. Create the production Paystack account or switch to live mode.
2. Create the live monthly and annual subscription plans.
3. Save the real plan codes into:
   - `Paystack:PlanCode:ZA:Monthly`
   - `Paystack:PlanCode:ZA:Annual`
4. Set `Paystack:SecretKey` to the live secret key.
5. Register the live webhook:
   - `POST https://YOUR_DOMAIN/api/webhooks/paystack`
6. Run a real or controlled live subscription test.

### 8.2 PayPal SaaS setup for UK and Ireland

1. Create the production PayPal app.
2. Create the live monthly and annual billing plans for each supported country.
3. Set:
   - `PayPal:ClientId`
   - `PayPal:ClientSecret`
   - `PayPal:PlanId:GB:Monthly`
   - `PayPal:PlanId:GB:Annual`
   - `PayPal:PlanId:IE:Monthly`
   - `PayPal:PlanId:IE:Annual`
4. Make sure `PayPal:Sandbox` is `false` in production.
5. Register the live webhook:
   - `POST https://YOUR_DOMAIN/api/webhooks/paypal`
6. Complete one live end-to-end subscription test for UK and one for Ireland if possible.

---

## 9. Configure merchant payment gateways

These are separate from BlueSquares' own SaaS billing.

Merchant invoice payment gateways are configured per merchant inside the application after onboarding. That means your platform can go live even if not every merchant uses every provider on day one, but each provider you offer should still be tested.

Supported live webhook endpoints:

| Provider | Live webhook URL |
|----------|------------------|
| Paystack | `POST https://YOUR_DOMAIN/api/webhooks/paystack` |
| PayFast | `POST https://YOUR_DOMAIN/api/webhooks/payfast` |
| Ozow | `POST https://YOUR_DOMAIN/api/webhooks/ozow` |
| Stripe | `POST https://YOUR_DOMAIN/api/webhooks/stripe` |
| PayPal | `POST https://YOUR_DOMAIN/api/webhooks/paypal` |

Provider-specific launch notes:

- `Stripe:WebhookSecret` must be set correctly or Stripe webhook validation will fail
- PayFast and Ozow should point their notify/webhook URLs to the production domain
- PayPal is used for both invoice payments and subscription events, so validate both paths carefully
- if you only plan to support certain gateways at launch, disable or avoid marketing the others until tested

Recommended per-provider test:

1. Create a merchant in the correct country.
2. Enter the provider credentials for that merchant.
3. Create an invoice.
4. Open the public invoice payment page.
5. Complete a payment with the provider's live or approved test flow.
6. Confirm invoice status updates correctly.
7. Confirm the webhook is received by production.

---

## 10. Configure accounting integrations

If you are launching Xero and QuickBooks support, the redirect URIs must match exactly.

Use these live callback URLs:

- `https://YOUR_DOMAIN/api/accounting-integrations/xero/callback`
- `https://YOUR_DOMAIN/api/accounting-integrations/quickbooks/callback`

Set:

- `AccountingIntegrations:Xero:ClientId`
- `AccountingIntegrations:Xero:ClientSecret`
- `AccountingIntegrations:Xero:RedirectUri`
- `AccountingIntegrations:QuickBooks:ClientId`
- `AccountingIntegrations:QuickBooks:ClientSecret`
- `AccountingIntegrations:QuickBooks:RedirectUri`
- `AccountingIntegrations:QuickBooks:Environment`

Launch steps:

1. Register the BlueSquares production app in Xero.
2. Register the BlueSquares production app in QuickBooks.
3. Paste the exact live callback URLs into each provider.
4. Update the corresponding production config values.
5. Connect one test merchant account to Xero.
6. Connect one test merchant account to QuickBooks.
7. Export one invoice to each provider from production.

If you are not ready to support accounting at launch, postpone public rollout of that feature instead of launching with broken callbacks.

---

## 11. Deploy the production build

Once configuration and third-party accounts are ready, publish the app to the production server.

Suggested deployment flow:

1. Take a backup or snapshot of the production database if this is not a first launch.
2. Publish the application build.
3. Put production configuration and secrets in place.
4. Make sure the process manager or host is set to keep the app running.
5. Start the application.
6. Confirm the app starts without configuration errors.
7. Confirm the homepage loads over HTTPS.

Important runtime behavior:

- the app serves the frontend from static files
- `/health` is available as an unauthenticated health endpoint
- background services for reminders and recurring invoices only run while the app process is alive

After deployment, immediately test:

- `https://YOUR_DOMAIN/`
- `https://YOUR_DOMAIN/health`
- login and signup pages
- dashboard access after authentication

---

## 12. Run database migrations

Run the Entity Framework migrations against the production database before inviting live users.

Command:

```bash
dotnet ef database update
```

Production migration checklist:

1. Confirm the command points at the production database.
2. Run the migration.
3. Verify it completes successfully.
4. Confirm the app can connect after migration.
5. Keep a backup plan in case a rollback is needed.

Do not skip this step. The app depends on the live schema matching the current code.

---

## 13. Verify domain, HTTPS, and redirects

Before provider testing, make sure the live site itself behaves correctly.

1. Open the root domain in a browser.
2. Confirm HTTPS is active and certificate warnings are gone.
3. Check whether `www` and non-`www` behavior matches your chosen canonical domain.
4. Confirm internal links and payment links use the production domain.
5. Confirm public pages such as invoice payment pages open successfully.

If you use a hostname other than the base domain and `www`, update CORS rules in `Program.cs` before launch.

---

## 14. Run the full smoke test

This is the minimum end-to-end production validation before announcing launch.

### 14.1 Merchant onboarding flow

1. Sign up a new merchant account.
2. Complete merchant profile setup.
3. Confirm login, logout, and session behavior.

### 14.2 Core invoicing flow

1. Add a client.
2. Create a one-off invoice.
3. Open the public invoice page.
4. Verify totals, dates, and branding look correct.
5. Send the invoice by WhatsApp and email if both are enabled.

### 14.3 Payment flow

1. Pay the invoice using the target market's provider.
2. Confirm the user reaches the correct payment result page.
3. Confirm the webhook arrives.
4. Confirm invoice status updates to paid.
5. Confirm receipts and related messages are correct.

### 14.4 Reminder automation

1. Enable auto-reminders for a merchant.
2. Create or adjust a reminder rule that should match today.
3. Create an unpaid invoice with the matching due date.
4. Wait for the reminder background service cycle or inspect logs after it runs.
5. Confirm the reminder message is sent only once.

Note: reminder processing runs hourly.

### 14.5 Recurring invoice automation

1. Create a recurring invoice schedule due today.
2. Turn on auto-send if you want to test sending as well as generation.
3. Wait for the recurring invoice background service to process.
4. Confirm a new invoice is created.
5. Confirm `LastRunDate`, `NextRunDate`, and generated invoice behavior are correct.

Note: recurring invoice processing runs every 6 hours.

### 14.6 Accounting export

1. Connect Xero for one merchant.
2. Export one invoice.
3. Connect QuickBooks for one merchant.
4. Export one invoice.
5. Confirm the exported data appears correctly in each provider.

---

## 15. Validate logs and error handling

Do not rely only on browser success. Check production logs during testing.

Look for:

- startup exceptions
- database connection failures
- webhook validation errors
- WhatsApp send failures
- background service errors
- OAuth callback failures

A good launch signal is:

- app starts cleanly
- no repeated unhandled exceptions
- webhooks return success
- reminders and recurring jobs run without errors

---

## 16. Go / no-go checklist

Do not announce launch until every critical item below is true.

- [ ] Production domain is final and reachable over HTTPS
- [ ] `ConnectionStrings:DefaultConnection` points to the live PostgreSQL database
- [ ] `AppSettings:BaseUrl` is the real live URL
- [ ] `AllowedHosts` is correct for production
- [ ] Firebase production project is active and frontend config matches it
- [ ] Firebase credentials file is stored securely outside the repo
- [ ] SendPulse live sending works
- [ ] WhatsApp webhook verifies successfully
- [ ] Required WhatsApp templates are approved
- [ ] Paystack live SaaS plans are configured for ZA
- [ ] PayPal live SaaS plans are configured for GB and IE
- [ ] Stripe webhook secret is configured if Stripe is offered
- [ ] Production database migrations completed successfully
- [ ] At least one real end-to-end invoice payment test passed
- [ ] At least one recurring invoice test passed
- [ ] At least one reminder automation test passed
- [ ] Xero and QuickBooks callbacks are correct, or those features are withheld from launch
- [ ] No production secrets are stored in the repo or shared folders
- [ ] Production logs are being monitored

---

## 17. Launch day sequence

If all setup and testing is complete, use this order on launch day:

1. Confirm production config has not changed unexpectedly.
2. Confirm the app is healthy at `/health`.
3. Re-run one quick signup and invoice creation test.
4. Re-run one payment test on the main launch market.
5. Confirm logs are clean.
6. Open access to merchants or publish the live announcement.
7. Watch webhooks, signups, invoice creation, and payment results closely for the first several hours.

---

## 18. First 48 hours after launch

The first two days matter more than any documentation polish.

Watch closely for:

- failed signups or login issues
- webhook failures
- payment status mismatches
- reminder misfires
- recurring schedule timing issues
- WhatsApp template delivery problems
- merchant onboarding confusion around gateway credentials

Recommended operating routine:

1. Review logs several times during the first day.
2. Check the database for any failed or incomplete test records if something looks wrong.
3. Verify the first real merchants can complete the happy path without assistance.
4. Keep one person available for fast support responses.

---

## 19. Rough timing expectation

If all provider accounts already exist and live credentials are available, a realistic go-live window is usually **1 to 3 focused working days** for setup, deployment, and testing.

The most common delay is **WhatsApp template approval**, which can easily add **1 to 2 or more extra days**.

---

## 20. After launch improvements

After the initial launch is stable, the most valuable next improvements are:

- stronger WhatsApp webhook signing and validation
- more visibility into webhook delivery and retries
- better recurring invoice management UX
- accounting export retry and history
- analytics for invoice views, sends, payments, and reminder performance
