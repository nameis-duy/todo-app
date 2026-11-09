# Todo backend

A .NET 10 Web API with EF Core, FluentValidation, SQL Server in Podman by default,
and optional JSON-file storage. The Angular frontend is in `../todo-app-fe`.
The independent Expo/React Native client is documented in
[mobile setup](../todo-app-mobile/README.md).

Native cookie-session clients can initialize CSRF with `GET /api/auth/csrf-token`.
It returns `{ "requestToken": "..." }`, sets the existing antiforgery cookie and
disables caching. Send `X-XSRF-TOKEN` plus session cookies for unsafe requests and
fetch a new token after changing identity. The Angular `/api/auth/csrf` endpoint
is unchanged. Offline mobile imports add the `OfflineGuestImport` migration for SQL task storage.

## Implemented

- Task create, read, replace, complete/reopen, and permanent delete.
- Title, description, status, priority, start/due dates, and UTC audit timestamps.
- Search, status/priority/due-date filters, allowlisted sorting, and bounded pagination.
- Optimistic concurrency on update, completion, and deletion.
- Purpose CRUD, task assignment/removal, purpose/uncategorized filters, and live progress.
- Problem Details, development Swagger/OpenAPI, and additive EF Core migrations.
- A configuration toggle for local JSON-file or SQL Server storage.
- Local email/password accounts with ASP.NET Core Identity, cookie sessions, CSRF protection,
  sign-in throttling, and account lockout.
- Private tasks/projects, plus owner-managed task sharing with registered accounts by email.
- Private server-backed guest sessions with explicit, additive sync after account sign-in.
- Authenticated import of offline mobile guest batches with atomic, durable retry receipts.

Single-container Docker/Azure deployment is documented in
[the deployment runbook](../deploy/README.md).
Additional lists/tags and a full automated test suite are deferred.
Use locally with non-sensitive data until production security and deployment are reviewed.

## Requirements

- .NET 10 SDK (the SDK selection is in global.json).
- Podman with a running Linux machine and a Compose provider (`podman-compose`
	or `docker-compose`) for database mode.
- Node/npm for the Angular frontend.

## Local setup (PowerShell)

From the repository root, provide `MSSQL_SA_PASSWORD` in your shell environment,
then start SQL Server with Compose:

```powershell
podman machine start
podman volume create todo-sqlserver-data
podman compose -f todo-app-be/compose.yaml up -d
dotnet run --project todo-app-be/src/Todo.Api --launch-profile http
```

Skip machine start when already running. `compose.yaml` declares SQL Server 2022
Developer, its port, health check, restart policy, and persistent storage.
The external `todo-sqlserver-data` volume preserves existing database files and is
not removed by Compose teardown. Do not delete it to reset or repair a container.

For the existing data volume, use the existing SA password: setting the environment
variable does not reset a password already stored in SQL Server.
Compose only manages the container. Database/login setup, API connection
configuration, and EF migrations are separate manual responsibilities. It does not
generate credentials, write .NET user secrets, or configure Windows networking.

If the old script-created `todo-sqlserver` container still exists, stop and remove
only that container before the first Compose start (do not remove its volume):

```powershell
podman stop todo-sqlserver
podman rm todo-sqlserver
podman compose -f todo-app-be/compose.yaml up -d
```

To stop the Compose-managed container:

```powershell
podman compose -f todo-app-be/compose.yaml down
```

SQL port `14333:1433` is published on host interfaces, as explicitly approved.
Depending on host firewall/WSL networking, this can expose SQL to the LAN; credentials
are required. Prefer loopback publishing if your Podman forwarding supports it.
Compose does not change firewall rules or execution policies.

Start Angular in another terminal:

```powershell
npm --prefix todo-app-fe install
npm --prefix todo-app-fe start -- --host 127.0.0.1 --port 4200
```

Open `http://127.0.0.1:4200/` or `/purposes`. VS Code tasks also provide Compose
start/stop, builds, and dev servers. The Angular dev proxy forwards `/api` to the backend.

## Storage toggle

The configuration in `src/Todo.Api/appsettings.json` selects the provider:

```json
"Storage": {
	"UseJsonFile": false,
	"JsonDirectory": "App_Data"
}
```

- `true`: task/project data uses JSON-file storage; **SQL Server is still required for accounts and task-sharing grants**.
- `false`: task and purpose requests use EF Core/SQL Server (default).
- `JsonDirectory` is resolved relative to the API content root; an absolute path
	is also accepted. Custom data directories must also be excluded from Git and
	kept outside any publicly served directory.

Environment overrides are `Storage__UseJsonFile` and `Storage__JsonDirectory`.
Restart the API after changing the provider. The stores are independent: switching
does not copy, merge, or delete either provider's data. To switch for one run:

```powershell
dotnet run --project todo-app-be/src/Todo.Api --launch-profile http -- --Storage:UseJsonFile=true
```

The JSON document has `schemaVersion: 1`, tasks, and optional purposes; older files
without purposes still load. Data lives under `src/Todo.Api/App_Data`, ignored by
Git and not served publicly. A scoped repository
tracks each request's changes; the shared store rechecks the persisted version
while holding a lock before committing. Writes use a same-directory temporary
file and replacement, not an in-place rewrite. A process-wide semaphore serializes
requests, and an exclusive lock file prevents simultaneous cooperating processes
from overwriting each other (a competing process receives 503; no automatic retry).
Malformed, unreadable, or unsupported files return a safe 503 without resetting
the original data. Check server logs and restore a known-good copy manually.

File mode loads the whole document for every operation and is intended for one
local API instance and small datasets. It is not a database, a backup strategy,
or a production storage option. Do not edit the file while the API is running.
Use SQL Server for larger datasets or multi-instance deployment.

## SQL Server connections and existing data

Configure the API's `ConnectionStrings:Todo` separately, for example through
`ConnectionStrings__Todo` in the process environment. The checked-in connection has
no password. Existing local configuration is not modified by Compose. Do not commit
credentials or send secrets to an AI assistant or logs.

To copy existing JSON into an **empty, migrated SQL database**, stop the API,
back up the source, and run from the workspace root:

```powershell
dotnet run --project todo-app-be/src/Todo.Api --launch-profile http -- --ImportJson=true
```

Import exits after copying, preserves IDs, versions, dates and audit timestamps,
leaves source JSON untouched, and refuses a nonempty SQL target. It never merges
stores or converts PostgreSQL databases. Historical PostgreSQL InitialTasks source
is retained but excluded from compilation. The active SQL Server baseline and
additive AddPurposes migration are in `Persistence/SqlServerMigrations`; the active
model snapshot describes SQL Server. Adding purposes does not recreate Tasks.

## Guest sessions and sync

Choose **Continue as guest** at `/login` to use tasks and projects before creating
an account. Guests receive a private, passwordless Identity record and the same
HttpOnly session/CSRF protection as registered accounts. SQL Server remains required,
including in JSON mode. This is server storage, not an offline/browser-only mode.
Guests cannot share tasks, receive sharing grants, or sync into a guest account.

After registration or sign-in, the account response exposes `isGuest: false` and
`hasGuestData: true` when this browser has pending records in the active provider.
Nothing transfers until **Sync guest data** is selected. **Not now** retains the
pending data and exposes a header sync action. Sync moves only this guest's owned
tasks/projects into the signed-in account; existing account records and legacy
ownerless records are untouched. IDs, content, created/completed timestamps and
project links survive; audit update times and concurrency versions refresh.

`Todo.GuestDraft` is an HttpOnly, SameSite protected recovery cookie valid for eight
hours from starting/resuming a guest session. The source guest ID is protected by
Data Protection and is never accepted from a sync request body. Keep the same
browser/hostname and sign in/sync before it expires. Clearing cookies or losing the
Data Protection keys can make unmerged data inaccessible. Sign-out and **Not now**
do not discard the recovery cookie; another person using the same browser can resume
that guest session. Successful sync clears it. An already-completed sync is safe to
retry without duplicating records; a request after cookie deletion returns 403.

SQL transfers run in one serializable transaction; JSON transfers replace both
collections in one locked atomic write. Only the active task provider is synced;
this is not synchronization between SQL and JSON. Finish guest edits in other tabs
before syncing: in-flight requests are not canceled. Cookie expiry is not a storage
purge. Unmerged guest identities/data remain on the server until an administrator
removes them; automatic retention cleanup and cross-device guest recovery are not
implemented. Use non-sensitive local data and define retention before production.
No additional migration is needed beyond the existing account/sharing migration.

## Offline guest imports

The Angular and mobile apps can keep guest tasks and purposes entirely on the device. They do
not create an Identity guest until a legacy server-guest flow explicitly requests
one. Login/registration and importing still require the API and SQL Identity.

`POST /api/auth/guest-import` requires a registered cookie session and CSRF token.
Each immutable batch supplies `batchId`, `workspaceId`, and at most 50 tasks and
purposes in total, within a 512 KiB body. Ownership is always the authenticated
actor. Account/workspace-scoped server IDs preserve purpose links without accepting
ownership claims or overwriting existing account records. Creation/completion
timestamps survive; update timestamps and concurrency versions are refreshed.

SQL commits records and `OfflineImportReceipts` in one serializable transaction.
JSON commits its additive `imports` receipt collection with tasks/purposes in the
same locked atomic replacement; schema-version 1 files remain backward compatible.
Repeating an identical batch returns the same acknowledgement even if its records
were subsequently edited/deleted. Changing a batch's content or colliding with an
existing record returns 409. Do not purge receipts independently of retry policy.
Do not switch storage providers while device imports are pending; receipts and
purpose references belong to the active provider, not a cross-provider merge.

Apply the additive migration explicitly before using SQL imports, with your
existing administrator connection configured outside source control:

```powershell
# From todo-app-be; never put the connection string or its password in this file.
dotnet ef database update --project src/Todo.Infrastructure --startup-project src/Todo.Api --connection "$env:TODO_MIGRATION_CONNECTION"
```

Nothing applies database migrations automatically. New Angular guests now use browser
storage; legacy server guests and their eight-hour recovery cookie remain supported.
See the frontend and mobile READMEs for device persistence, consent, retry and offline limitations. Manual request examples
are in `src/Todo.Api/Todo.Api.http`; automated tests remain deferred.

## Accounts and permissions

The combined Angular workspace uses **Project** in the UI; `/api/purposes` remains
unchanged for compatibility. Register or sign in at `/login`. Passwords require at
least 12 characters, uppercase/lowercase letters, a number and a symbol (max 128).
Passwords are hashed by Identity. Five failed attempts lock an account for 15 minutes;
login/registration share a 10-attempt/minute limit per client IP. Sessions expire
after eight hours and use an HttpOnly cookie, not browser-stored bearer tokens.

New tasks and projects belong to their creator. Ownership is assigned by the server,
never accepted from a request. All lists, counts, filters and direct-ID reads are
scoped to owned or explicitly shared records. Projects remain private. Owners can
invite an existing account by email with **Can view** (default) or **Can edit** access,
change that access, or revoke it in task details. No invitation email is sent.
Viewers can only read. Editors can update task fields/completion, but cannot change
its project, delete it, inspect its private project, or manage sharing. Project names
are hidden from recipients. Only owners can delete tasks or manage access.

`AddAccountsAndTaskSharing` adds Identity tables, sharing grants and nullable owner
columns without deleting/backfilling existing records. **Legacy ownerless data stays
unassigned and hidden**, including legacy JSON documents. There is no first-account
claim and no public ownership-assignment endpoint. Back up the data and assign chosen
records explicitly as an administrator after creating the intended account. For SQL,
update `Purposes.OwnerId` and associated `Tasks.OwnerId` consistently in one transaction;
for JSON, stop the API and update both collections' `ownerId` fields consistently.
Use an existing `AspNetUsers.Id`; do not assign records without confirming ownership.

Apply migrations explicitly, using an admin connection supplied in your shell:

```powershell
# Run inside todo-app-be; TODO_MIGRATION_CONNECTION must be configured separately.
dotnet tool restore
dotnet ef database update --project src/Todo.Infrastructure --startup-project src/Todo.Api --connection "$env:TODO_MIGRATION_CONNECTION"
```

Runtime `ConnectionStrings:Todo` still needs only data read/write permissions on the
account, sharing, task and project tables. JSON mode also needs a migrated SQL database.
SQL and JSON grants are separate; switching providers does not copy permissions.
Deleting a task revokes its grants first. If deletion subsequently fails (including
a JSON write failure), the task may remain but recipients lose access; owners can
reload and share again. Concurrent requests already authorized may finish during
revocation. There is no distributed transaction across JSON and SQL.

For manual HTTP clients, retain cookies, GET `/api/auth/csrf`, and send the readable
`XSRF-TOKEN` cookie value in `X-XSRF-TOKEN` on every POST/PUT/PATCH/DELETE, including
registration/login/logout. Refresh the CSRF token after each identity change. Angular
does this automatically, and development Swagger refreshes it before unsafe requests.
Use the **same hostname** for login and subsequent requests; localhost and 127.0.0.1
have different cookie jars. Unauthorized requests return 401; forbidden mutations 403;
records outside your read scope return 404. Failed CSRF validation returns 400.

Production requires HTTPS (cookies are Secure outside Development), same-origin
frontend/API hosting, persisted/protected ASP.NET Core Data Protection keys, suitable
database privileges and a security review. Email verification, password recovery,
password changes, MFA, external providers, and server-side session revocation are not
implemented. Logout clears the current browser cookie; a stolen copy remains valid
until expiry/security-stamp invalidation. Do not treat this as production-ready auth.

## Local API URLs

The API is available at `http://localhost:5080`.
OpenAPI JSON: `http://localhost:5080/openapi/v1.json` (Development only).
Swagger UI: `http://localhost:5080/swagger` (Development only). Its pinned
Swagger UI assets load from jsDelivr and require internet access. The UI uses
the existing OpenAPI document and supports interactive API requests.
The documentation is public in Development only; data endpoints require an account or guest session.
Process liveness: `http://localhost:5080/health/live` (does not check the database).
Requests can be run manually from src/Todo.Api/Todo.Api.http using an HTTP client.

Migrations are not applied automatically on startup. To create a later migration,
run inside `todo-app-be`:

```powershell
dotnet tool restore
dotnet ef migrations add <MigrationName> --project src/Todo.Infrastructure --startup-project src/Todo.Api --output-dir Persistence/SqlServerMigrations
```

## API contract

| Method | Route | Behavior |
| --- | --- | --- |
| GET | `/api/auth/csrf` | Anonymous CSRF-cookie initialization |
| POST | `/api/auth/register` | Register and start a cookie session (201) |
| POST | `/api/auth/login` | Sign in and start a cookie session |
| POST | `/api/auth/guest` | Start/resume a private guest cookie session |
| POST | `/api/auth/guest-sync` | Explicitly transfer this browser's guest data to a registered account |
| POST | `/api/auth/guest-import` | Import an approved offline batch with account-scoped retry protection |
| GET | `/api/auth/me` | Current account or guest, including pending guest-data metadata |
| POST | `/api/auth/logout` | End the current browser session |
| GET | `/api/tasks` | Search/filter/sort/page |
| GET | `/api/tasks/{id}` | Read task |
| POST | `/api/tasks` | Create; returns 201 and Location |
| PUT | `/api/tasks/{id}` | Replace editable fields; requires body `version` |
| PATCH | `/api/tasks/{id}/completion` | Set `completed`; requires body `version` |
| DELETE | `/api/tasks/{id}?version={version}` | Permanently delete; requires current version |
| GET / POST | `/api/purposes` | Paged summaries / create purpose |
| GET / PUT | `/api/purposes/{id}` | Summary / edit title and description |
| DELETE | `/api/purposes/{id}?version={version}` | Keep tasks; clear their purpose references |
| GET | `/api/tasks/{id}/shares` | Owner-only recipient list |
| PUT | `/api/tasks/{id}/shares` | Owner-only grant/upsert `{ email, canEdit }` |
| DELETE | `/api/tasks/{id}/shares/{userId}` | Owner-only revoke |

Tasks optionally reference one purpose (`purposeId`, null means no purpose).
Responses include `purposeTitle` (owners only), `ownerId`, `isOwner` and `canEdit`. Purpose summaries derive total/completed counts
and percentage from current tasks; zero tasks means 0%. Purpose links open related
paged tasks/progress in Angular. Deletion atomically uncategorizes tasks and refreshes
their audit times/version tokens; it never deletes them. Invalid references return 400.
Purpose PUT/DELETE also require current versions. Titles/descriptions share task limits.

Statuses: `Pending`, `InProgress`, `Completed`. Priorities: `Low`, `Normal`,
`High`, `Urgent`. JSON enums are strings, not integers.

`startDate` and `dueDate` are optional calendar dates (`YYYY-MM-DD`), not UTC
instants. A due date cannot precede a start date. Titles are trimmed and required
(max 200 characters); descriptions are optional (max 4000 characters).
Audit/completion timestamps are server-generated UTC instants.

`PUT` replaces all editable fields: omitted optional fields (including purpose) are cleared and
omitted status/priority use `Pending`/`Normal`. Use completion PATCH for a
completion-only change. Reopening sets status to `Pending` and clears
`completedAt`. Every successful mutation issues a fresh version token. Use the
latest response token for the next mutation; stale tokens return 409. Reload
before resolving a conflicting edit. A resource already missing returns 404.

Query parameters: `search`, `status`, `priority`, `dueFrom`, `dueTo`, `sortBy`,
`descending`, `page`, `pageSize`, `purposeId`, `uncategorized`. Purpose and uncategorized
filters cannot be combined. Purpose queries support page/pageSize (default 100).
Search is a literal case-insensitive substring
of title/description. Sorting accepts `createdAt`, `updatedAt`, `title`, `dueDate`,
or `priority` (semantic priority order, not alphabetical). Defaults: createdAt
descending, page 1, pageSize 20. Page size is 1-100; page is 1-1,000,000.
Ties sort by ID. Both providers put null due dates last ascending and first
descending. Title ordering follows the provider's runtime/database collation.
In SQL Server mode, total count and page rows use separate queries and may differ
during concurrent writes. File mode filters and pages one loaded snapshot.

Errors use Problem Details: 400 invalid input/CSRF, 401 sign-in required, 403 forbidden,
404 missing/inaccessible resource, 409 stale
version, 429 too many sign-in attempts, 503 storage unavailable, 500 unexpected failure. Validation failures
include an `errors` dictionary. Database schema/configuration defects may return
503 or 500; consult server logs, not client error details.

CORS allows only `http://localhost:4200` in Development, without credentials.
Other environments have no allowed cross-origin clients by default. Configure
`Cors:AllowedOrigins` explicitly if needed. Cookie auth is designed for the existing
same-origin Angular proxy; changing only the API base URL does not enable credentialed
cross-origin authentication or Angular XSRF forwarding. The local launch profile uses HTTP on
loopback only; production TLS and deployment hardening are not configured.

## Build

```powershell
dotnet build todo-app-be/Todo.slnx --configuration Release
npm --prefix todo-app-fe run build
```

No automated test projects or test infrastructure are included, per current scope.
See docs/architecture.md for boundaries and next steps.