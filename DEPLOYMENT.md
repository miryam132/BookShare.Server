# Render deployment review — stage 9

Updated 2026-10-19. The user authorized private GitHub publication and connection /
deployment on Render's free tier only. Paid resources, paid upgrades and charges
are not authorized. No Render deployment has occurred. The local application
database must remain untouched.

## Proposed resources and costs

Two separate Git repositories, two separate Render services, one Render Postgres
database. Proposed region: Frankfurt for API and database. Client is a static site.
The API blueprint explicitly selects `plan: free`; it does not create a database.
Select PostgreSQL major version 18 explicitly in the dashboard (Render currently
offers 13-18; local checks use 18.6). Record the actual managed patch version.
Database proposal: Free, database/user `bookshare_demo`, 1 GB storage,
no replicas, no HA and no paid scaling. Check for an existing free database first:
Render permits only one active free Postgres database per workspace.

API compute and Postgres must both be Free; the client is a free static site.
Confirm the workspace subscription and usage/billing controls before creating any
resource. Free hosting still has bandwidth/build quotas; accounts with payment
methods can incur usage overages. Do not add a payment method, enable paid usage,
upgrade or accept a charge without explicit approval. If the account cannot avoid
charges or Render requires a paid plan, stop and report the specific requirement.

Free API instances sleep after 15 idle minutes, may take about a minute to wake,
and share 750 free running hours per workspace/month. Free Postgres expires after
30 days, has 1 GB storage and no backups. After expiration there is a 14-day upgrade
grace period before deletion; do not upgrade automatically. Schedule the examiner's
demo within the active period and retain no irreplaceable data in this database.
Free API lacks shell, one-off jobs and pre-deploy commands; maintenance is performed
explicitly from the local machine against the new cloud demo database. Free tier
is suitable for a temporary course demo, not durable production hosting.

Sources checked 2026-10-19:
- [Pricing](https://render.com/pricing)
- [Render cost example](https://render.com/articles/how-much-does-cloud-application-hosting-cost-for-small-businesses)
- [Free limits](https://render.com/docs/free)
- [Deployment lifecycle](https://render.com/docs/deploys)
- [Docker](https://render.com/docs/docker)
- [Postgres connections](https://render.com/docs/postgresql-creating-connecting)
- [Blueprint schema](https://render.com/docs/blueprint-spec)

## Files and configuration to review

Server root: `Dockerfile`, `.dockerignore`, `render.yaml`. Client repository root:
its own `render.yaml`. Both disable automatic Git deploys. Also disable Blueprint
Auto Sync in the dashboard; a manual initial sync still creates/deploys resources.
Do not apply either blueprint before approval. Do not add database definitions to
an already approved blueprint without reviewing the extra cost.

Server secrets/settings (Render Environment; never in Git or build arguments):

| Setting | Value / purpose |
|---|---|
| `ConnectionStrings__BookShare` | Npgsql key/value connection string, not a postgres URI |
| `Jwt__SigningKey` | Stable Base64 of at least 32 cryptographically random bytes |
| `Jwt__Issuer`, `Jwt__Audience` | Defaults in appsettings, or explicit matching values |
| `Cors__AllowedOrigins__0` | Exact client HTTPS origin, no trailing slash or path |
| `ASPNETCORE_ENVIRONMENT` | `Production` |
| `Hosting__BehindTlsProxy` | `true` only behind Render's HTTPS edge |
| `ASPNETCORE_HTTP_PORTS` | Image defaults to `10000`; set to Render PORT if overridden |
| `Maintenance__Database` | Exact separately reviewed database name; only for maintenance |
| `DemoSeed__Enabled` | `true` only for a deliberate seed operation |
| `DemoSeed__AdminPassword`, `DemoSeed__OwnerPassword` | Separate secret demo credentials |
| `DemoSeed__Borrower1Password`, `DemoSeed__Borrower2Password` | Separate secret demo credentials |
| `AUTOMAPPER_LICENSE_KEY` | Only if required by your applicable license |

Render documents self-signed certificates on internal connections and explicitly
does not support VerifyFull there. To preserve certificate validation, this proposal
uses the database's full external hostname with managed TLS and Npgsql VerifyFull.
Example shape only: `Host=<full-external-hostname>;Port=5432;Database=<name>;Username=<user>;Password=<secret>;SSL Mode=VerifyFull`.
Allow external database access only from the approved API service's outbound IP
ranges in Render's database access rules (and a temporary examiner/admin IP only
if explicitly needed). Do not leave the default allow-all range. This may incur
network usage; confirm it in the cost review. Verify connectivity and certificate
validation before release; do not downgrade TLS or set Trust Server Certificate.
Use a least-privilege runtime identity when feasible. A future internal connection
requires a separately reviewed way to validate its certificate.

Client: set public `VITE_API_BASE_URL=https://<api-service>.onrender.com` before
`npm ci && npm run build`; never include `/api`. Publish `dist/`. No server secrets
belong in any `VITE_` variable. Updating this value requires a new client build.
CORS allows only configured exact origins, GET/POST/PUT/DELETE and Authorization /
Content-Type. No cookies or wildcard origins. Requests without Origin (server
clients) still require normal JWT authorization for protected endpoints.

The image runs as non-root `app`, listens on all interfaces on port 10000, and
uses console NLog output plus ephemeral files. Export console logs if retention
is required; do not rely on container files. Swagger is Development-only.
Render owns TLS redirection; `Hosting__BehindTlsProxy=true` disables application
redirection to avoid proxy loops. Do not expose that HTTP container directly.

## Controlled free-tier release and migrations

1. Private GitHub repositories have been published:
   [server](https://github.com/miryam132/BookShare.Server) and
   [client](https://github.com/miryam132/BookShare.Client). Review AutoMapper terms.
   Add/connect Render and authorize its GitHub integration for only these two
   repositories. Confirm free workspace/resource settings and usage controls.
2. Create the approved database and service(s). Set secrets privately. First API
   health checks may fail until schema initialization. Never solve this by enabling
   migrations at normal startup. Record the immutable source revision/image.
3. Back up an existing target database and review both migration files. For an
   existing application database, applying migration 2 requires its own controlled
   approval. Stop API writes while changing schema. Set `Maintenance__Database`
   to the exact approved target name.
4. Free services have no shell or pre-deploy command. Before deploying the API,
   use the already verified local Docker image with the new cloud demo database's
   external TLS connection and exact `Maintenance__Database`, set privately in
   process environment. Temporarily allow only the local machine's public IP in
   database access rules, in addition to the API's approved outbound IP ranges.
   Run the following local command; values are inherited, never command arguments:

   ```powershell
   docker run --rm --env ConnectionStrings__BookShare --env Maintenance__Database bookshare-api:stage9 --maintenance-migrate
   ```

   It applies both migrations to a fresh cloud database and exits nonzero on
   failure without sensitive provider output. Confirm database identity first;
   never reuse the local application connection. Verify migration history and
   health before enabling writes. Do not run tests against this application/demo
   database; production acceptance writes require the approved demo target.
5. Set four distinct demo passwords privately, enable the seed gate and run:

   ```powershell
   docker run --rm --env ConnectionStrings__BookShare --env Maintenance__Database --env DemoSeed__Enabled --env DemoSeed__AdminPassword --env DemoSeed__OwnerPassword --env DemoSeed__Borrower1Password --env DemoSeed__Borrower2Password bookshare-api:stage9 --maintenance-seed
   ```

   Repeat to confirm idempotence. Remove temporary local IP access, seed settings,
   maintenance confirmation and local process secrets afterward. Seed passwords
   need not be placed in the normal API service environment. Retain passwords
   only in a private password manager and share privately with the examiner.
6. Deploy the client with the API URL and set the server's exact client origin.
   Recheck both health endpoints, client loading and CORS preflight from that origin.
   Test an unrelated origin is denied. Do not send secret values in screenshots.

Rollback: stop writes, roll back the API image only if schema compatibility was
reviewed. Schema rollback is a separate reviewed operation; never automatically
run `database update 0` or drop tables. Restore a backup when required. No API retry
is enabled for delivery or return.

## Required deployed acceptance (still pending)

Use a separately approved deployed demo/test database. Local test commands must
never be redirected to the application database. Record URLs, revision, date,
selected Postgres version, and results without tokens/passwords.

1. Register/login; verify refresh loses login and browser storage contains no JWT.
2. Admin category create/edit, owner publishes/edits a book with multiple categories;
   borrower sees catalog, filtering/paging and contact details after login.
3. Open the seeded race book as owner and admin in independent browser profiles.
   Prepare delivery for borrower1 and borrower2, submit concurrently: one 201 and
   one 409, one active loan. A human click test may yield occupied-state 409;
   deterministic same-Version concurrency was separately proven by local gated tests.
4. Borrower cannot manage another user's book or confirm return (403); anonymous
   delivery fails 401; stale edit gets 409. Confirm no mutation retries in Network.
5. Owner confirms return, duplicate return gets 409, book becomes available. Hide
   returned book and verify history prevents deletion. Restore availability manually
   if needed for the examiner; rerunning seed deliberately does not reset state.
6. Verify final persisted state privately in the deployed demo database; ensure logs
   contain no passwords, authorization headers or tokens. Record pass/fail explicitly.

Stage 9 is incomplete until this workflow passes on an authorized deployment.

## Local Docker verification

The full source `docker build --tag bookshare-api:stage9 .` and
`./scripts/Test-Hosting.ps1 -Docker` have passed locally after Docker Desktop was
started. The image runs non-root, listens on 10000, has a writable `/app/logs`,
enforces restricted CORS and JWT 401, and disables Swagger in Production. Test
containers were stopped/removed. A transient Docker DNS/NuGet download failure
was followed by a successful complete rebuild and final runtime check. No image
was pushed; Render deployment and its database workflow remain pending approval.
