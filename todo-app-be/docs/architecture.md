# Backend architecture

## Current slice

The backend is a modular monolith with four .NET 10 projects:

- `Todo.Domain`: task/purpose state, transitions, date/title invariants; no framework dependencies.
- `Todo.Application`: immutable contracts, validators, use cases, persistence interfaces;
  references Domain and FluentValidation only.
- `Todo.Infrastructure`: EF Core SQL Server mappings, projected queries, migrations,
  and JSON/SQL implementations of task and purpose persistence interfaces.
- `Todo.Api`: minimal HTTP endpoints, dependency composition, error handling,
  configuration, and development OpenAPI; no task business rules.

Application depends on abstractions rather than Infrastructure. API is the
composition root. No generic repository framework, CQRS mediator, event sourcing,
background workers, or distributed infrastructure is needed.

## Identity and authorization

ASP.NET Core Identity accounts and task grants always live in SQL Server, including
when task/project storage is JSON. Infrastructure owns the Identity EF store; API
owns cookie authentication, CSRF, lockout/throttling and account endpoints. Application
depends only on `ICurrentUser` and `ITaskGrants`, not HTTP/Identity framework types.
The fallback authorization policy requires sign-in. Only account initialization,
registration/login, guest-session creation, health and development documentation are anonymous.

Guest mode uses passwordless Identity users marked with the `todo:guest` claim,
not anonymous access to shared records. `GuestSession` issues the normal session
and a separate eight-hour time-limited Data Protection recovery cookie. Account
responses include `isGuest` and `hasGuestData`; signing in never implicitly transfers
ownership. Registered-account policies exclude guests from sharing and sync targets;
grant recipients must also be registered users. CSRF and sign-in rate limits protect
guest creation. Recovery IDs are never accepted from request bodies.

`IGuestData` keeps the transfer contract in Application. Infrastructure validates
the source guest and target account, then moves only source-owned records using a
serializable SQL transaction or one atomic locked JSON replacement. Both collections
retain IDs, associations and created/completed timestamps while update timestamps
and versions change. Existing target data and ownerless legacy data are untouched.
The recovery cookie is deleted only after a successful transfer. Retrying never
duplicates rows, though a cleared/expired cookie returns 403. Not now retains the
cookie, and the workspace offers a later explicit sync action. This applies only to
the active storage provider; there is no SQL/JSON cross-store synchronization.
Cookie expiry is not data deletion; automatic abandoned-guest cleanup and cancellation
of already in-flight guest requests are not implemented. See README for recovery,
shared-browser and retention limits. No guest schema migration is required.

Entities have nullable immutable owner IDs; new records receive the actor's ID.
Legacy null owners remain hidden until an administrator explicitly assigns them.
Repositories apply ownership/sharing scope before filtering, counting or pagination,
including direct-ID reads. Projects are owner-only. Services require owners for
deletion and grants, and owner/edit grants for task changes. Shared editors must
retain the task's project ID; shared responses do not reveal its project name.
Grants are namespaced by storage provider and rechecked on requests. SQL shares have
no task FK because a grant may reference a JSON task. Deletion clears grants before
removing a task, failing closed if later persistence fails. Concurrent requests may
finish after revocation; JSON/SQL changes cannot share a database transaction.

The HttpOnly session cookie expires after eight hours; the readable XSRF cookie is
only an antiforgery request token, not an authentication credential. Angular refreshes
CSRF after sign-in and during authenticated initialization, and relies on its default
same-origin XSRF header support. No password or bearer token is stored in browser
storage. Deployment requires HTTPS and a persistent protected Data Protection key ring.
Password recovery/verification/MFA and server-side session revocation remain absent;
see README for operational limits.

## Persistence and concurrency

`Storage:UseJsonFile` selects repositories at startup. SQL Server is the default;
setting true enables JSON for both tasks and purposes.
Application use cases and HTTP contracts do not depend on the selected provider.
The DbContext is also used for accounts and sharing grants in file mode.
Switching providers does not migrate data or sharing permissions.
Explicit ImportJson copies only into an empty, migrated SQL database, in one
serializable transaction, preserving IDs/versions/timestamps and the source file.

### JSON-file mode

The API creates the configured directory on the first storage operation. The
versioned JSON document stores task/purpose snapshots including IDs, version tokens,
calendar dates, nullable owner IDs, and UTC audit timestamps. The domain's Restore factory validates
stored state without changing its identifiers or timestamps. The local snapshot
currently reuses TaskResponse fields; changes to that contract must account for
existing file schema compatibility before changing schemaVersion. Version 1 files
without purposes still load. References, duplicate IDs, and metadata are validated.

One singleton store owns the path and semaphore; scoped repositories track the
loaded task and original version. Commit reloads the file under the same lock,
compares versions, and writes through a temporary file followed by replacement.
An exclusive sidecar file lock prevents cooperating processes from overlapping
read/modify/write operations; contention returns 503 rather than retrying a write.
The sidecar remains on disk, but the OS handle owns the lock and releases it on
process exit. A malformed document is never silently replaced with empty data.
Purpose deletion and task detachment are one atomic document write under this lock.

This provider is for small local/demo data, not multi-instance or production use.
Each operation reads the entire document, and there is no automatic expiry,
backup, disaster recovery, or file-to-database synchronization.

### SQL Server mode

Tasks use UUID primary keys and application-generated UUID concurrency tokens.
The service checks the supplied token before changes; EF includes the originally
loaded token in UPDATE/DELETE predicates to also catch racing database writes.
A zero-row concurrency write maps to 409. All writes use one SaveChanges call
and its EF transaction. Dates are SQL `date`; timestamps are
`datetimeoffset` with UTC values. Database checks enforce core field,
date, enum, and completion consistency. Indexes cover status/due-date and default
created-time ordering. Search uses parameterized EF expressions, not raw SQL.

Paged queries use no tracking and project to response DTOs in SQL. Offset
pagination and substring search suit an initial local app; evaluate keyset
pagination and specialized search indexes only when real usage calls for them.
No automatic retry policy is used for writes that could duplicate effects.

`compose.yaml` declares the SQL Server container, health check, port, restart policy,
and external `todo-sqlserver-data` volume. Compose manages container lifecycle only;
credentials are supplied through the environment, and connection configuration,
database/login setup, networking, and migrations are managed separately. Existing
database files and local connection settings are not changed by this configuration.
Port 14333 is published on host interfaces under explicit user approval; consider
LAN exposure. Keep development servers on loopback.

Historical PostgreSQL InitialTasks source is excluded from compilation. SQL Server
has its own baseline, AddPurposes, and AddAccountsAndTaskSharing migrations; the active
snapshot is SQL Server. Existing Tasks are not dropped/recreated to add these features.

### Purpose integrity

Each task has zero or one Purpose. API use cases validate references; the SQL FK
handles deletion races before SaveChanges. Purpose summaries project live task
counts in SQL, never N+1 requests or persisted counters. Percentage is rounded to
one decimal; an empty purpose is 0%. Task projections include purpose titles.

Purpose deletion runs a transaction: detach matching tasks, refresh their UpdatedAt
and Version, then version-check/delete the purpose. Conflicts roll back detachment.
The FK also has ON DELETE SET NULL; direct SQL deletion does not execute application
audit/version-refresh logic. JSON performs the equivalent changes atomically.

Angular has typed Purpose API access, reactive CRUD forms and progress rows. Purpose
links open the paged, filtered task workspace as details. Tasks support assignment,
reassignment/removal and uncategorized filtering. Both editors handle stale versions
and explicitly confirm destructive changes. No Razor/SQLite/Bootstrap was added.

## Next slices

1. Keep builds and manual API/browser checks covering Purpose CRUD, progress,
   assignment, stale changes, task-preserving deletion, and data persistence.
2. Add additional lists/tags only as separate requested features.

Automated tests remain deferred. Authentication/ownership are implemented, but require
security tests and deployment hardening before multi-user production use.