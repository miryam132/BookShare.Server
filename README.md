# BookShare Server

Peer-to-peer lending of physical books. Each ad represents one copy. A borrower
reserves it, contacts its owner, and coordinates collection. The owner confirms
actual handover, starting the 14-day loan, and later confirms return.
Scope: [PLAN.md](PLAN.md). Release: [DEPLOYMENT.md](DEPLOYMENT.md).

## Architecture and requirements

Four .NET 10 projects in BookShare.slnx: Domain (entities/enums, no external
packages), Application (services, DTOs, interfaces, AutoMapper), Infrastructure
(EF Core/Npgsql, repositories, transactions and JWT/password security), API
(controllers, middleware, configuration and DI). Controllers call services and
repositories; DbContext/services/repositories are scoped.

Requirements: .NET SDK 10.0.400 or compatible patch, PostgreSQL 18 (tests verified
with 18.6), PowerShell, Git; client Node 22.12+ and npm. Browser tests require
Google Chrome and port 5173 free. No packages were added for the review fixes.

## Local run

From the server repository:

```powershell
dotnet restore BookShare.slnx
dotnet build BookShare.slnx --no-restore -p:BaseOutputPath=bin/Stage7/
```

Install client dependencies with npm.cmd ci in the separate client repository.
For a disposable demo, with no manual database changes:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File ./scripts/Start-Preview.ps1
```

This creates a NEW temporary loopback PostgreSQL cluster, applies all three
migrations and seed, and serves http://127.0.0.1:5173. Requires PostgreSQL 18
binaries (default C:\Program Files\PostgreSQL\18\bin, override -PostgresBin).
Docker is not required. Keep the launcher running; Ctrl+C stops its services and
cluster. A fresh launch has new data/passwords. The printed temporary folder holds
logs and Windows DPAPI-encrypted demo credentials for the same Windows account.
Never publish that file. Application and Render databases are not used.

For a persistent local database, first create a dedicated PostgreSQL login and
database owned by it, then run these separately:

```powershell
./scripts/Start-Local.ps1 -UpdateDatabase
./scripts/Start-Local.ps1 -Seed
./scripts/Start-Local.ps1
```

Defaults: localhost:5432, database/login bookshare. Override -DatabaseHost,
-DatabasePort, -DatabaseName, -DatabaseUser. Passwords are prompted invisibly;
maintenance requires exact database-name confirmation. Normal API startup never
migrates or seeds. Do not run verification against the application database.

## Development User Secrets

API UserSecretsId is BookShare.API-development. Standard ASP.NET configuration
loads this store only in Development. To configure without values in shell history:

```powershell
./scripts/Set-DevelopmentSecrets.ps1
dotnet run --project src/BookShare.API --launch-profile http
```

The helper prompts for the local connection string invisibly and saves it and a
random Base64 JWT signing key outside Git using JSON on stdin. It changes no DB.
Running it again replaces the development key and invalidates existing tokens.
User Secrets are for development, not an encrypted production vault. Never publish
user-secrets list output. The Start-Local wrapper remains independently usable:
it temporarily supplies process environment settings and restores previous values.

In Render, use environment settings. Never use client variables or build args
for server secrets. The API does not automatically read .env files.

| Setting | Meaning |
|---|---|
| ConnectionStrings__BookShare | Npgsql key/value string, not a postgres URI |
| Jwt__SigningKey | Base64 of at least 32 random bytes; stable per deployment |
| Jwt__Issuer / Jwt__Audience | Defaults BookShare / BookShare.Client |
| Jwt__LifetimeMinutes | Default 60, validated 1-1440 |
| Loans__DurationDays | Default 14, validated 1-365 |
| Cors__AllowedOrigins__0 | Exact approved client origin, no path/trailing slash |
| Hosting__BehindTlsProxy | true only behind the Render HTTPS edge |

No secrets in Git, chat, screenshots or logs. JWT is client-memory only; refresh
requires login. Mutation requests are never automatically retried.

## Migrations and seed

Review all three migrations in src/BookShare.Infrastructure/Migrations:
InitialCreate, AddLoansAndBookVersion, AddBookReservation. The third adds the
reservation holder FK, index and status CHECK without deleting existing data.
Schema generation is offline with the design-time factory; dotnet tool restore
restores the pinned EF tool. Never drop/truncate or run database update 0 as a fix.

Maintenance CLI (privately configure settings first):

```powershell
dotnet run --project src/BookShare.API --no-launch-profile -- --maintenance-migrate
dotnet run --project src/BookShare.API --no-launch-profile -- --maintenance-seed
```

Both require Maintenance__Database to match the configured target exactly. Seed
also requires DemoSeed__Enabled=true and four distinct 16-128 character passwords:
DemoSeed__AdminPassword, DemoSeed__OwnerPassword, DemoSeed__Borrower1Password,
DemoSeed__Borrower2Password. Start-Local -Seed supplies these via hidden prompts.
Commands start no HTTP, return nonzero on failure and print fixed diagnostic
codes without provider details. Remove maintenance/seed settings after use.

Seed is atomic, additive and serializes concurrent seed processes. It preserves
hashes, edits, state, Versions and history, and rejects identity/role collisions.
It creates four categories, 12 ads and multi-category links. The race book is
ספר הדגמת תחרות. Demo accounts:

| Role | Email |
|---|---|
| Admin | admin@demo.bookshare.example |
| Owner | owner@demo.bookshare.example |
| Borrower 1 | borrower1@demo.bookshare.example |
| Borrower 2 | borrower2@demo.bookshare.example |

These addresses/phones are fictional. Give the examiner valid passwords privately
for the actual environment being examined. Preview credentials change on each
fresh launch and differ from cloud credentials. Never put real passwords here.

## Limited resource and concurrency

The scarce resource is one physical copy. Two borrowers read the same Available
book and Version and reserve concurrently: one gets 200, the other 409. One holder
is saved; no loan begins until actual handover. Book.Version is an EF concurrency
token refreshed in DbContext SaveChanges. A transaction starts BEFORE loading and
checking the book. UnitOfWork rolls back DbUpdateConcurrencyException and maps it
to 409. A partial unique index additionally prevents two active loans per book.

Demonstrate in independent browser profiles. Holder/owner/Admin can release a
reservation using current Version. No expiry or waiting list. Owner/Admin can
read requester name/email/phone through GET /api/books/{id}/reservation/contact;
this response is no-store and versioned, and the UI requires matching requester
details before handover. Public DTOs do not expose requester identity.

The owner confirms delivery to the holder and later return. Stale cancellation
and handover, duplicate return and editing/hiding/deleting reserved or loaned
books are rejected. History prevents deletion after return. Legacy owner/Admin
manual delivery by email remains supported for unreserved available books only;
it cannot override a reservation. Loans UI separately lists pending reservations
using authenticated GET /api/books?reservedByMe=true, paged in SQL and restricted
to the caller even for Admin. It cannot be combined with mine.

## Tests

From the server root:

```powershell
dotnet build BookShare.slnx --no-restore -p:BaseOutputPath=bin/Stage7/
dotnet test tests/BookShare.Application.Tests --no-restore -p:BaseOutputPath=bin/Stage7/
dotnet run --project tests/BookShare.IntegrationTests --no-restore -p:BaseOutputPath=bin/Stage7/
powershell -NoProfile -ExecutionPolicy Bypass -File ./scripts/Test-PostgreSql.ps1
powershell -NoProfile -ExecutionPolicy Bypass -File ./scripts/Test-PostgreSql.ps1 -Seed
powershell -NoProfile -ExecutionPolicy Bypass -File ./scripts/Test-PostgreSql.ps1 -Client
```

Each PG launcher creates a NEW empty temporary cluster and stops it in finally.
Only bookshare_test_ database names are allowed. Add -BaseOutputPath bin/ReviewFix/
to isolate builds from a running preview. Chrome workflow needs port 5173 free.
No Microsoft.AspNetCore.Mvc.Testing or new external test packages are installed.

There are 15 xUnit/Moq service tests. The default executable harness uses doubles;
explicit PG mode proves independent two-DbContext Version conflict, coordinated
borrower reservation and delivery/return races, SQL rollback, constraints and
permissions. Review regression checks verify thrown business error log status,
409 Warning, no-tracking reads, persisted password rehash and category updates.
Client build and six Chrome contract tests are documented in its README. Do not
mistake default offline harness passes for real PostgreSQL verification.

## API, logs and release status

Resources under /api: users, auth/tokens, books, books/{id}/reservation, categories,
loans and loans/{id}/return. Swagger: http://localhost:5213/swagger in Development
only. It is disabled in Production, including the production-mode preview.
Health: /api/health and /api/health/database (200/503). Errors contain status, code,
message, correlationId and optional validation errors. NLog records actual request
status, business conflicts at Warning and unexpected exceptions at Error; no
request bodies, JWTs or provider messages. Production files are ephemeral.

Separate GitHub repositories and Free Render API/static-client/PostgreSQL exist.
The deployed code still predates reservation changes: migration 3, coordinated
release and full deployed acceptance remain pending. NetFree previously blocked
public acceptance. Server auto-deploy was found enabled during review fixes;
do not push until cloud schema is ready or auto-deploy is disabled. See
DEPLOYMENT.md for exact targets, TLS/IP rules and Free limits/expiry (2026-11-18).
No paid upgrades are authorized. Stage 9 is incomplete until deployed acceptance.

Existing licenses are in package LICENSE files. AutoMapper 16.1.1 is RPL 1.5 or an
alternative Lucky Penny license; review applicable terms and supply a private
AUTOMAPPER_LICENSE_KEY if required. No new package was added for review fixes.
