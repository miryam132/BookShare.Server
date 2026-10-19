# Render deployment review — stage 9

Prepared 2026-10-19. Nothing in this document authorizes resource creation,
publication, billing or application database changes. No deployment has occurred.

## Proposed resources and costs

Two separate Git repositories, two separate Render services, one Render Postgres
database. Proposed region: Frankfurt for API and database. Client is a static site.
The API blueprint proposes Starter compute; it does not create a database.
Select PostgreSQL major version 18 explicitly in the dashboard (Render currently
offers 13-18; local checks use 18.6). Record the actual managed patch version.
Database proposal: Basic-256mb, database/user `bookshare_demo`, 1 GB initial storage,
storage autoscaling disabled, no replicas or HA. Review these values and the
dashboard's current price before creation; scale only with separate approval.

Render's published guidance gives an indicative $13/month for Starter API plus
Basic-256mb Postgres, before database storage, bandwidth, build overages and taxes.
This is an estimate, not a spending cap. Confirm the exact selected plans, storage,
workspace charges, region and usage controls in the dashboard before approval.
Static hosting has no compute charge but consumes bandwidth/build allowances.
Free API instances sleep after 15 idle minutes; Free Postgres expires after 30 days,
has 1 GB storage and no backups. Free API also lacks shell/one-off jobs. This plan
uses paid API compute for controlled maintenance; no free tier is assumed.

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

## Controlled release and migrations

1. Review separate repository content and ignores, AutoMapper license terms,
   exact dashboard estimate and desired database version. No commits or remote
   publishing have been performed by this stage. Obtain explicit publication /
   resource / billing approval for the selected repositories, account and plans.
2. Create the approved database and service(s). Set secrets privately. First API
   health checks may fail until schema initialization. Never solve this by enabling
   migrations at normal startup. Record the immutable source revision/image.
3. Back up an existing target database and review both migration files. For an
   existing application database, applying migration 2 requires its own controlled
   approval. Stop API writes while changing schema. Set `Maintenance__Database`
   to the exact approved target name.
4. In a paid Render shell for the same image/environment, run
   `dotnet BookShare.API.dll --maintenance-migrate`. It exits nonzero on failure
   without printing connection/provider messages. On a fresh database it applies
   both migrations. Verify health and migration history before enabling writes.
   An initial deployment that cannot become healthy can instead use a temporary
   approved pre-deploy command with this exact command; remove it after that
   controlled release. Pre-deploy is available on paid compute. Never put the seed
   command in the start command or permanent pre-deploy command.
5. Set the four demo passwords privately, enable the seed gate, run
   `dotnet BookShare.API.dll --maintenance-seed` once and repeat to confirm it exits
   successfully. Disable/remove seed settings and target confirmation afterward.
   Save passwords only in a private password manager and give the examiner access
   through a private channel. They are not returned by the command.
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
