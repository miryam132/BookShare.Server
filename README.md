# BookShare Server

Peer-to-peer lending of physical books. Scope: [PLAN.md](PLAN.md). React client
lives in its own repository, `../BookShare.Client`. Stage 9 implementation is
prepared; deployment acceptance is still pending. See [DEPLOYMENT.md](DEPLOYMENT.md)
for the concrete Render proposal, costs, secrets and controlled release steps.

The user subsequently requested Render Free only. `render.yaml` now explicitly
uses `plan: free`; paid resources/upgrades are not authorized. Free Postgres
expires after 30 days, and the API sleeps after 15 idle minutes. Free services
have no shell/pre-deploy; DEPLOYMENT.md documents controlled local Docker
maintenance against the separately created cloud demo database. The account
connection, resource creation and deployed acceptance have not yet occurred.

## Requirements and architecture

.NET SDK 10.0.400 (latest patch), PostgreSQL 18 (verified locally with 18.6), Git,
PowerShell on Windows. Client: Node 22.12+ and npm; browser tests: installed Google
Chrome and free port 5173. No package was added for stage 9.

- Domain: entities/enums, no EF or HTTP dependency.
- Application: services, DTOs, AutoMapper profiles and persistence interfaces.
- Infrastructure: EF Core/Npgsql, repositories, transactions, JWT/password hashing
  and explicit demo provisioning.
- API: short controllers, DI, middleware, authentication/authorization and NLog.

Controller -> Application service -> Repository -> PostgreSQL. Members manage
books they own; Admin manages categories and may assist with all books/loans.
Registration always creates Member. Contact details require a dedicated authorized
request. JWT is kept only in client memory; refresh requires another login.

## First run from an empty database

Create a dedicated PostgreSQL login and database owned by it (non-superuser).
Choose and retain the database password privately. Run from this repository root:

```powershell
dotnet restore BookShare.slnx
dotnet build BookShare.slnx --no-restore
# Explicit database change: inspect both migration files first.
./scripts/Start-Local.ps1 -UpdateDatabase
./scripts/Start-Local.ps1 -Seed
./scripts/Start-Local.ps1
```

All three commands prompt for the database password without echoing it. Use
`-DatabaseHost`, `-DatabasePort`, `-DatabaseName`, `-DatabaseUser` for another target.
Migration/seed additionally require typing the exact target database name. They
are separate operations and cannot be combined. Do not point test commands at
this database. The application database's second migration was NOT applied during
stage-9 verification; an existing application database requires separate approval.

Open http://localhost:5213/swagger (Development only). Health: `/api/health` and
`/api/health/database` (200/503). Start the client separately per its README.
Normal API startup never migrates or seeds. Migrations are InitialCreate and
AddLoansAndBookVersion; files are in `src/BookShare.Infrastructure/Migrations`.
Design-time EF commands read `ConnectionStrings__BookShare`; restore the local EF
tool with `dotnet tool restore` if needed. Prefer controlled maintenance commands
for application, rather than manually executing the historical SQL snapshots.

## Secrets and demo

Never put passwords, signing keys, JWTs or connection strings in Git, chat, shell
command arguments/history, screenshots or logs. `.env` is not automatically read.
The local script supplies process environment variables and restores previous
values in finally. Seed password prompts are hidden. Retain the four different
passwords in a password manager and share privately with the examiner; no real
password is documented here. Ctrl+C/forced process termination may bypass cleanup;
close that shell if interrupted. Secret strings necessarily exist in process memory.

For IDE/hosting configure secrets privately:

- `ConnectionStrings__BookShare`: Npgsql key/value string (not a PostgreSQL URI).
- `Jwt__SigningKey`: Base64 of at least 32 random bytes, stable across instances.
  The local script generates a temporary key if absent; restarting invalidates tokens.
- `Jwt__Issuer`, `Jwt__Audience`, `Jwt__LifetimeMinutes`: appsettings defaults or
  explicit overrides; signature, issuer, audience and expiry are validated.
- `Loans__DurationDays`: 14 by default (validated 1-365).
- `Cors__AllowedOrigins__0`: exact public client origin when hosting separately.
  No origin is allowed by default; local Vite proxy needs no CORS permission.

Seed requires `DemoSeed__Enabled=true`, exact `Maintenance__Database` confirmation,
and `DemoSeed__AdminPassword`, `DemoSeed__OwnerPassword`,
`DemoSeed__Borrower1Password`, `DemoSeed__Borrower2Password`. Four distinct secrets,
16-128 characters each; no defaults. CLI, after privately supplying settings:

```powershell
dotnet run --project src/BookShare.API --no-launch-profile -- --maintenance-migrate
dotnet run --project src/BookShare.API --no-launch-profile -- --maintenance-seed
```

The local `-Seed` wrapper configures these through hidden prompts. Hosting uses
`dotnet BookShare.API.dll` with the same maintenance argument. Commands do not
start HTTP and return nonzero on failure with no provider/secret detail. Remove
seed settings after provisioning. Never put seed in an automatic startup command.

| Demo role | Login email |
|---|---|
| Admin | admin@demo.bookshare.example |
| Owner | owner@demo.bookshare.example |
| Borrower 1 | borrower1@demo.bookshare.example |
| Borrower 2 | borrower2@demo.bookshare.example |

These reserved `.example` addresses and phone 0500000000 are fictional; there is
no mail/SMS service. Seed creates four categories, twelve Available owner ads and
18 category links. The first ad is `ספר הדגמת תחרות`, ID
`b0090000-0000-4000-8000-000000000100`. All ads represent one physical copy each.

Seed uses stable demo IDs, normalized email/category lookups, a database advisory
transaction lock and one atomic commit. Existing demo hashes, roles, book status,
Version, edits, category links and loan history are never overwritten. An email/ID
or role collision fails; it never elevates a previously registered Member. Repeats
add only missing fixture records; they do not reset a used race book to Available.
Return the active loan normally before repeating the demo. Supplying new secrets
on a repeat does not rotate existing passwords. Seed requires migrations first.
A previously deleted fixture may be recreated; seed is provisioning, not cleanup.

## Limited resource and race demonstration

The limited resource is the single physical book represented by an ad. Contact
access does not reserve it. Owner and Admin open the same available race book in
separate browser profiles and prepare deliveries to the two different borrowers.
Submit concurrently: one delivery succeeds (201), the other gets 409. One active
loan remains. The owner confirms return, making the book Available again; duplicate
return gets 409. Returned history prevents deletion but permits hiding.

Book.Version is an optimistic concurrency token changed on every book update.
Delivery and return each begin a transaction before the tracked read and commit
book status plus loan changes atomically. A unique partial index on active
Loan.BookId provides another guard. Only the specific concurrency/index/history
failures map to 409; other database failures remain errors. Stale edit Version is
also rejected. No automatic mutation/delivery/return retry exists.

## Tests (isolated database only)

```powershell
dotnet build BookShare.slnx --no-restore -p:BaseOutputPath=bin/Stage7/
dotnet test tests/BookShare.Application.Tests --no-restore -p:BaseOutputPath=bin/Stage7/
dotnet run --project tests/BookShare.IntegrationTests --no-restore -p:BaseOutputPath=bin/Stage7/
powershell -NoProfile -ExecutionPolicy Bypass -File ./scripts/Test-PostgreSql.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File ./scripts/Test-PostgreSql.ps1 -Seed
powershell -NoProfile -ExecutionPolicy Bypass -File ./scripts/Test-PostgreSql.ps1 -Client
powershell -NoProfile -ExecutionPolicy Bypass -File ./scripts/Test-Hosting.ps1
```

10 xUnit/Moq cases test service decisions; the executable harness runs 165 offline /
HTTP checks. Real PostgreSQL checks independently verify Version, synchronized
owner/Admin delivery and return races, real SQL rollback, constraints, authorization
and pagination. Seed checks cover empty-database migrations, disabled/missing
secrets, hashes, counts, repeated/concurrent seeds, preservation and role collisions.
Client mode runs Chrome against actual API services/repositories/PostgreSQL and
checks final persisted state. Install client dependencies first with `npm.cmd ci`.

Each PowerShell run initializes a new temporary cluster on loopback and a free
port, with a random password; no application service/database is contacted. It
stops the cluster in finally and retains artifacts under the printed Temp path.
Use `-PostgresBin` for an alternative installation. Abrupt termination can bypass
finally: stop only that cluster with `pg_ctl -D <printed-path>/data -m fast -w stop`.
Direct harness `--postgres` / `--seed` / `--client` requires a genuinely separate,
empty database named `bookshare_test_[a-z0-9_]+`; it refuses populated/other names.
Never rename the application database to bypass this check. No drops/truncation.

## API, errors and licensing

Routes: `/api/users`, `/api/auth/tokens`, `/api/users/me`, `/api/books`,
`/api/books/{id}/contact`, `/api/categories`, `/api/loans`,
`/api/loans/{id}/return`. Swagger documents request/response contracts. Catalog
supports search/category/city/availability/mine, title/date sorting and paging
(default 10, max 50). Loan lists respect owner/borrower/admin visibility.

Errors have status, code, message, correlationId (validation also errors).
NLog writes request Information, conflict Warning, exception Error; no bodies,
query strings, tokens or provider exception messages. Production files are ephemeral;
retain console logs through the host if necessary. CORS is before authentication.

Existing package licenses: EF Core, JWT, Swagger, DI and cryptography pins MIT;
Npgsql PostgreSQL license; NLog BSD-3-Clause; xUnit/runner Apache-2.0; Moq BSD-3-Clause;
Test SDK MIT. No Microsoft.AspNetCore.Mvc.Testing was installed. AutoMapper 16.1.1
is RPL 1.5 or an alternative Lucky Penny license, not MIT: review applicable terms
before publication and set `AUTOMAPPER_LICENSE_KEY` privately if applicable.
[AutoMapper license](https://github.com/LuckyPennySoftware/AutoMapper/blob/v16.1.1/LICENSE.md),
[configuration](https://docs.automapper.io/en/stable/License-configuration.html).
The Docker images are official .NET 10 SDK/runtime images; no additional packages.
Docker build and deployed acceptance must be explicitly recorded, not inferred
from a successful local dotnet build.

## Stage 9 verification record — 2026-10-19

Passed in this workspace:

- The documented Stage7 solution build: zero warnings/errors; 10 xUnit/Moq tests
  and all 165 executable harness checks passed.
- `npm.cmd ci`, `npm.cmd run build`, `npm.cmd test -- --project=contracts`:
  client build and all four Chrome contract tests passed.
- `Test-PostgreSql.ps1`: original actual PostgreSQL concurrency/rollback/constraint
  suite passed in a new isolated cluster.
- `Test-PostgreSql.ps1 -Seed`: actual API maintenance CLI refused wrong target,
  applied both migrations from empty database and repeated migration successfully.
  Four accounts, valid hashes, 12 ads, 4 categories, 18 links and an Available race
  book verified. Repeated/concurrent seed and CLI seed preserved credentials,
  edited title, Hidden state and Version; role collision rejected. Secret values
  were checked absent from child stdout/stderr. All seed checks passed.
- `Test-PostgreSql.ps1 -Client`: full real API/PostgreSQL Chrome workflow and final
  persisted-state checks passed. Each database run used a separate temporary
  PostgreSQL 18.6 cluster; all clusters were stopped afterward.
- `Test-Hosting.ps1`: actual production Program entry point passed exact-origin
  preflight, unrelated-origin rejection, protected 401 with CORS, disabled Swagger
  and TLS-edge HTTP hosting checks. It used an unreachable dummy connection and
  never queried a database.
- `dotnet publish src/BookShare.API/BookShare.API.csproj --no-restore -c Release
  -o bin/Stage9/publish` passed, producing a local release artifact.
- PowerShell parser validation of the updated local launcher passed. Its
  interactive maintenance prompts were not run against the application database.

Initial sandbox attempts blocked initdb/esbuild; reported final database/browser
runs used authorized execution and passed. No package was added. Docker build and
container verification were completed after Docker Desktop was started; see below.
Render settings were reviewed against current official docs;
no Render-side blueprint validation, resource creation, billing or deployment was
performed. No remote repository publishing or commits were performed. Existing
untracked files were preserved. The application database was never modified.

Next required work: review and approval of the actual Render account/plans/costs and repository publication,
controlled deployment/migrations/seed, then the complete deployed acceptance in
DEPLOYMENT.md. Stage 9 is not complete until that deployed workflow passes.

### Docker verification follow-up

`docker build --tag bookshare-api:stage9 .` passed using the complete updated
Dockerfile, with .NET SDK 10.0.402 inside Linux. The runtime uses the non-root `app`
user. The Dockerfile explicitly creates `/app/logs` and gives `app` ownership so
NLog's file target can write there.

```powershell
docker build --tag bookshare-api:stage9 .
powershell -NoProfile -ExecutionPolicy Bypass -File ./scripts/Test-Hosting.ps1 -Docker
```

The Docker smoke test passed on the final image: HTTP on port 10000 (published only
to loopback on a temporary host port), non-root UID, writable log directory,
allowed-origin preflight, rejection of unrelated origins, protected 401 with CORS,
and disabled production Swagger. It uses a dummy unreachable database connection,
a fresh signing key passed by environment variable name rather than command-line
value, and stops/removes its temporary container in finally. It never accesses
the application database. The ordinary non-Docker hosting smoke test also passed.

Initial image downloads were slow; an intermediate rebuild failed with Docker DNS /
NuGet transport errors. An offline runtime-layer check passed, and after DNS
recovered the complete source Dockerfile rebuild and final smoke test passed.
Docker reports missing Git commit metadata because repository files remain
untracked; no commit, image push or deployment was performed. The locally built
image remains available as `bookshare-api:stage9`. These are local container checks,
not Render acceptance or a full database/browser workflow inside the container.
