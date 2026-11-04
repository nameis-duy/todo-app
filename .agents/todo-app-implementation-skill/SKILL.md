---
name: todo-app-implementation-skill
description: >-
    Implement and extend this Todo application with Angular, .NET 10 Web API,
    EF Core, SQL Server in Podman, and optional JSON storage. Use for task CRUD,
    purpose-based task grouping, migrations, storage configuration, API and UI
    integration, local account authentication, private ownership, task sharing,
    server-backed guest sessions, and consent-based guest sync.
    Automated testing remains deferred.
---

# Todo App Implementation Skill
1. Role and objective
Act as a senior full-stack engineer and pragmatic software architect. Help implement a reliable, maintainable Todo application incrementally. Prefer small, working vertical slices over large speculative implementations. Automated testing is temporarily out of scope.

The default stack is:
- Frontend: Angular with TypeScript
- Backend: ASP.NET Core Web API on .NET 10
- Database: SQL Server managed by Podman Compose with a persistent named volume
- Optional local storage: existing JSON provider selected by Storage:UseJsonFile
- ORM: Entity Framework Core
- Architecture: Clean Architecture principles in a modular monolith
- Validation: FluentValidation where appropriate
- Authentication/authorization: ASP.NET Core Identity, cookie sessions, private owners and task view/edit grants
- Testing: intentionally excluded for the current development phase
- DevOps: Podman Compose for local SQL Server; CI/CD and cloud deployment only when requested
First inspect the repository and follow its established conventions. Do not replace working technology or restructure the repository without a clear reason. If the user specifies a different stack, follow the user's stack.
2. Product objective
Build a Todo application that can grow from a personal task manager into a team productivity application. Start with a useful MVP and add advanced features in deliberate phases.
MVP features
1. Create, view, edit, complete/reopen, and delete tasks.
2. Support task title, description, status, priority, start date, due date, and timestamps.
3. Organize tasks by optional Purpose (a goal, project, or initiative), with progress summaries. Additional lists and tags are later features.
4. Search, filter, sort, and paginate tasks.
5. Provide a responsive Angular interface with task list and task-detail views.
6. Require a local account or private guest session for task/project data and enforce owner/shared access.
7. Validate input and return consistent, understandable API errors.
8. Include database migrations and local setup instructions; do not create automated tests in this phase.
Later feature groups
Implement these only after the MVP is stable, or when explicitly requested:
- Subtasks, task dependencies, templates, bulk actions, archive/restore, undo.
- Recurring tasks, reminders, calendar views, and time-zone-aware scheduling.
- Kanban, saved views, advanced search, and dashboards.
- Teams, workspaces, assignments, comments, mentions, attachments, and activity history.
- In-app/email/push notifications and notification preferences.
- Time tracking, goals, reports, exports, and productivity analytics.
- Automation rules, webhooks, calendar/email/chat integrations, and public API.
- AI task breakdown and planning.
- Observability, caching, rate limiting, backups, accessibility, localization, and cloud deployment.
Do not claim a feature is implemented until its code and relevant UI/API integration exist. Automated tests are out of scope for now.
3. Engineering principles
- Make small, coherent changes and keep the application buildable after each phase.
- Inspect existing files, project configuration, and tests before editing.
- Reuse existing patterns and dependencies when they are sound.
- Avoid unnecessary abstractions, generic frameworks, and premature microservices.
- Use a modular monolith by default; introduce distributed systems only for a justified requirement.
- Follow SOLID principles pragmatically. Prefer clear code over pattern-heavy code.
- Keep business rules out of controllers and Angular components.
- Use asynchronous APIs for I/O and pass cancellation tokens through backend I/O where supported.
- Use UTC for persisted instants; convert for display using the user's configured time zone. Be explicit about date-only values versus timestamps.
- Never commit secrets, credentials, access tokens, or production data.
- Do not silently change public API contracts or database schemas.
- Authentication was explicitly enabled. Preserve Identity cookie sessions, CSRF, private ownership and task sharing. Do not add roles, teams or external providers without a request.
- Do not invent package versions, commands, files, or test results. Inspect the repository and verify where possible.
- When a requirement is ambiguous and materially affects architecture or data, ask one focused question. Otherwise state a reasonable assumption and proceed.
4. Suggested solution structure
Adapt this to the existing repository rather than imposing it blindly.
todo-app/
├── todo-app-be/
│   ├── src/
│   │   ├── Todo.Api/             # HTTP endpoints, middleware
│   │   ├── Todo.Application/     # use cases, DTOs, validation, abstractions
│   │   ├── Todo.Domain/          # entities and domain rules
│   │   └── Todo.Infrastructure/  # EF Core SQL Server and JSON persistence
│   ├── compose.yaml              # SQL Server container configuration
│   ├── README.md
│   └── docs/architecture.md
├── todo-app-fe/                  # Angular application
└── .agents/skills/               # canonical workspace skills
Keep domain and application logic independent of ASP.NET Core, EF Core, and Angular. The API should depend on application use cases; infrastructure should implement application interfaces. Avoid circular project references. Do not create test projects during the current phase.
CQRS guidance
CQRS is optional, not a requirement for every feature. If adopted:
- Commands represent state-changing use cases.
- Queries read data and should not mutate state.
- Handlers should be focused and testable.
- Use MediatR only if it adds value or is already part of the repository.
- Do not introduce event sourcing merely because CQRS is used.
- Use explicit transactions for operations that must be atomic.
- Add an outbox only when reliable integration-event delivery is required.
5. Domain and data design
Start with the simplest data model that supports the agreed requirements. A possible MVP model:
- Task: Id, Title, Description, Status, Priority, StartDate, DueDate, CompletedAt, CreatedAt, UpdatedAt, Version, nullable OwnerId, and optional PurposeId.
- Purpose: Id, Title, Description, CreatedAt, UpdatedAt, Version, nullable OwnerId.
- Additional task lists and tags are deferred beyond the current Purpose grouping feature.
Adapt naming to the existing conventions. Add fields only when requirements justify them.

Database rules:
- Use primary keys, foreign keys, appropriate unique constraints, and indexes.
- Index common query patterns, such as status plus due date, based on actual access patterns.
- Enforce owner and relationship integrity on the server; assign the actor on creation, never from request bodies.
- Use EF Core migrations for schema changes and document how to apply them.
- Avoid N+1 queries; project to DTOs and use pagination for collections.
- Use optimistic concurrency where concurrent edits could overwrite data.
- If soft deletion is used, define restore and retention behavior explicitly.
- Store date-only concepts as date-only values where appropriate; do not treat a date-only deadline as an arbitrary UTC instant.

Purpose-based task grouping (current feature):
- Preserve the confirmed Angular + .NET 10 API architecture, not MVC/Razor, SQLite, or Bootstrap from an older feature draft.
- Task PurposeId is nullable: zero or one purpose per task, with existing tasks initially uncategorized.
- Purpose title is trimmed, required, max 200; description optional, max 4000. Use UTC audit instants and optimistic concurrency versions.
- Implement Purpose CRUD with live total tasks, completed tasks, and completion percentage. Percentage is completed / total * 100, rounded to one decimal; zero tasks means 0%.
- Compute summaries in SQL projections/aggregations, never persisted counters, N+1 queries, or unbounded task loading.
- Purpose details show related paged tasks and progress in the filtered task workspace.
- Use Project/No project in the combined Angular workspace, while preserving Purpose entities and API routes. Owners can assign/reassign/remove projects; shared editors must retain the existing association. Hide private project names from recipients.
- Support purpose and uncategorized filters, rejecting their combination. Validate nonempty IDs and reference existence; enforce a database FK.
- Purpose deletion must keep tasks, clear references, refresh affected task versions/audit times, and version-check/delete the purpose atomically. Confirm these semantics in the UI.
- Add Purposes, nullable Tasks.PurposeId, its index, and ON DELETE SET NULL FK via additive migrations. Never drop/recreate Tasks or reset data to add grouping.
- Maintain SQL Server and JSON providers. Existing schemaVersion 1 documents without purposes still load; all file mutations retain both collections under the same lock and atomic replacement.
- Use Persistence/SqlServerMigrations for new migrations. Historical PostgreSQL InitialTasks source is excluded from compilation; the active snapshot is SQL Server. Never apply PostgreSQL SQL to SQL Server.
- Use todo-app-be/compose.yaml for SQL Server container lifecycle and the external todo-sqlserver-data volume. Never delete the existing database volume when replacing a container.
- Require an externally supplied MSSQL_SA_PASSWORD; do not generate credentials, automate .NET user secrets, handle Windows-specific networking, or apply EF migrations through container startup. API configuration, database/login setup, and migrations are separate manual responsibilities.
- Port 14333 is published on host interfaces as explicitly approved for local SQL. Document potential LAN exposure; keep development servers on loopback and use non-sensitive data.
- Provider switching never copies data automatically. Explicit --ImportJson=true imports only into an empty migrated SQL database, preserves IDs/versions/timestamps, and leaves source JSON untouched.
6. API design
Use resource-oriented, consistent endpoints. Confirm exact routes with the repository before creating them. A possible starting API:
GET    /api/tasks
POST   /api/tasks
GET    /api/tasks/{id}
PUT    /api/tasks/{id}
PATCH  /api/tasks/{id}/completion
DELETE /api/tasks/{id}

GET    /api/purposes
POST   /api/purposes
GET    /api/purposes/{id}
PUT    /api/purposes/{id}
DELETE /api/purposes/{id}?version={version}

GET    /api/tasks?purposeId={id}
GET    /api/tasks?uncategorized=true
Guidelines:
- Use request and response DTOs; do not expose EF entities directly.
- Validate required fields, lengths, enums, dates, relationships and owner/grant permissions.
- Use 200/201/204 for success, 400 for invalid input/CSRF, 401 for missing sign-in, 403 for forbidden mutations, 404 for missing/inaccessible records, 409 for conflicts, and 429 for sign-in throttling.
- Prefer Problem Details or another consistent error contract.
- Support bounded pagination and allowlisted sorting/filter fields.
- Avoid returning stack traces or internal exception details to clients.
- Keep OpenAPI/Swagger documentation accurate.
- Add idempotency support only for operations where retries could cause harmful duplicate effects.
7. Authentication and authorization (enabled)
- Local email/password accounts use ASP.NET Core Identity in SQL Server. Accounts and per-task grants require SQL even when task/project data uses JSON.
- Use HttpOnly cookie sessions, no localStorage/sessionStorage bearer tokens or passwords. Require HTTPS/Secure cookies outside Development and persist/protect Data Protection keys in deployment.
- Initialize GET /api/auth/csrf and send X-XSRF-TOKEN for all unsafe API requests, including login/register/logout. Refresh tokens after identity changes. Keep Angular/API same-origin through the existing proxy.
- Registration/login are anonymous but CSRF-protected and rate limited. Preserve strong password rules and account lockout. All task/project endpoints require authentication.
- Guests use private passwordless Identity users with the todo:guest claim, not browser-only data or anonymous shared access. Guest creation is CSRF-protected/rate limited; SQL accounts remain required in JSON mode. Exclude guests from sharing, grant recipients and sync targets.
- Preserve the protected eight-hour HttpOnly guest recovery cookie across sign-in/sign-out. Ask before transferring guest tasks/projects into the registered account; Not now retains a later sync action. Never merge automatically or accept source/target ownership IDs from a request body.
- Guest sync moves only source-owned records in the active provider using one SQL transaction or atomic JSON write. Preserve IDs, content, created/completed times and project links; refresh update times/versions. Never replace existing account data or claim legacy ownerless data. Clear the recovery cookie only on success and avoid duplicate records on retry.
- Document guest expiry, shared-browser recovery, SQL/server dependence, active-provider scope, in-flight request limits and retained abandoned server data. Automatic cleanup, offline use and cross-device guest recovery are not implemented.
- New records belong to the actor. Legacy nullable owners remain hidden/unassigned; never claim them for the first registered user. Ownership assignment is an explicit administrator operation, not a public endpoint.
- Scope both storage providers before counts/search/filter/pagination and on direct-ID reads. Projects are owner-private, including their names on shared-task responses.
- Only task owners may delete or manage registered-recipient grants by email. Default grants to view-only; edit recipients may change content/completion but not project assignment, deletion or sharing.
- Persist JSON owner IDs backward-compatibly. SQL grants are separated by provider. Delete grants before tasks; if later deletion fails, access remains revoked. Do not imply cross-store atomic transactions or instantaneous cancellation of in-flight requests.
- Keep ICurrentUser and ITaskGrants in Application; Identity/EF implementation stays in Infrastructure/API. Do not add roles, password recovery, verification, MFA or external providers without scope approval.
- Email verification, recovery, MFA and server-side session revocation are not implemented. Require security testing/deployment hardening before production. Automated testing remains deferred unless explicitly re-enabled.
8. Angular frontend guidelines
- Use the Angular version and patterns already present in the repository.
- Organize by feature/domain (tasks, purposes, shared UI).
- Keep API calls in services/data-access layers; keep components focused on presentation and user interaction.
- Use typed interfaces for API contracts and typed reactive forms where appropriate.
- Handle loading, empty, success, validation, network, and server-error states.
- Include confirmation for destructive actions and clear feedback for successful operations.
- Use server-side pagination/filtering for large collections.
- Avoid duplicating backend business rules in the frontend; client validation improves UX, while server validation remains authoritative.
- Support keyboard navigation, visible focus, accessible labels, sensible contrast, and responsive layouts.
- Avoid unsafe HTML rendering and sanitize untrusted content.
- Centralize environment configuration and do not put secrets in Angular bundles.
Suggested MVP screens:
1. Main task list with create, search, filter, sort, and completion actions.
2. Task create/edit form or detail page.
3. Purpose management with counts/progress, safe deletion, and related-task details.
4. Purpose assignment and purpose/uncategorized task filters.
5. Not-found and error states.
9. Testing (temporarily deferred)
Automated testing is intentionally out of scope for the current phase.
- Do not create unit-test, integration-test, architecture-test, or end-to-end-test projects/specifications.
- Do not add test packages or test infrastructure solely for this app at this stage.
- Do not spend implementation time writing or running automated tests unless the user explicitly asks to re-enable testing.
- Still build the application and run relevant compile/build checks when available. Report the actual commands run and results; never claim a build succeeded if it was not run.
- Keep code modular and readable so tests can be introduced later.
- When testing is re-enabled, add appropriate tests for domain rules, API behavior, persistence, UI flows, and security before production use.
10. Security, privacy, and reliability
- Validate and normalize untrusted input.
- Use parameterized database access; avoid raw SQL concatenation.
- Use least-privilege database and cloud identities.
- Authentication alone is not production readiness. Keep development servers private and require security review, automated security coverage and deployment hardening before public use.
- Keep secrets in environment variables or a secret manager, never source control.
- Configure CORS narrowly for the intended frontend origins.
- Limit upload size and validate file type/content if attachments are implemented.
- Log useful structured diagnostics without exposing sensitive information.
- Add health checks and telemetry when deploying or operating the application.
- Define backup/restore and data-retention expectations before production use.
- Use retries only for transient failures and only when operations are safe to retry.
- Add caching only after identifying a need; ensure user-specific data cannot leak across cache keys.
- Consider accessibility and privacy from the beginning, not as a final polish step.
11. Background jobs, notifications, and integrations
Only add these after core task workflows are stable.
- Use a scheduler or background worker for recurring task generation and reminders.
- Ensure recurring-task creation and notification delivery are idempotent.
- Persist scheduling rules and the time zone needed to interpret them.
- Avoid sending emails or external messages directly inside a database transaction.
- Use a durable queue when asynchronous work needs independent retries or scaling.
- Use an outbox when database changes and event publication must be coordinated reliably.
- Keep provider-specific integrations behind interfaces.
- Store notification delivery status and retry safely; avoid duplicate messages.
- Add dead-letter handling and operational visibility if a message broker is used.
Do not add Kafka, microservices, Redis, or a message broker solely to make the project appear advanced. Use the simplest solution that meets the requirement.
12. Implementation workflow
Follow this workflow for each task:
1. Understand: Restate the requested outcome and acceptance criteria.
2. Inspect: Read the relevant source files and project configuration.
3. Plan: Identify the smallest end-to-end change, affected layers, and any database migration.
4. Implement: Make focused changes in domain/application/infrastructure/API/frontend as needed.
5. Verify: Run relevant build/compile checks where practical; automated tests are deferred.
6. Review: Check authorization, CSRF, validation, error handling, performance, accessibility, and unintended API/schema changes. Do not add automated tests unless requested.
7. Summarize: Explain files changed, behavior delivered, build checks actually run, and any remaining work.
When the user asks for implementation, prioritize editing the repository over merely giving sample code, if repository-editing tools are available. Do not claim to have modified files or run tests unless that actually happened.

For a multi-step feature, deliver a small working slice first. Avoid dumping an entire application in one response without integrating or verifying it.
13. Suggested project roadmap
Phase 0 — Repository and design
- Inspect the repository and identify existing stack and conventions.
- Confirm MVP requirements and core user journeys.
- Create or update requirements and architecture notes.
- Establish build, formatting, and local-run commands. Do not set up test tooling yet.
Phase 1 — Foundation
- Create/align projects and dependency boundaries.
- Configure SQL Server in Podman, EF Core migrations, configuration, logging, and health checks; retain the JSON toggle.
- Add consistent error handling and API documentation.
- Establish a build workflow if needed; do not add test automation yet.
Phase 2 — Task MVP
- Implement task domain model and rules.
- Implement task CRUD, completion, filtering, sorting, and pagination.
- Add database migration.
- Implement Angular task list, forms, detail view, and loading/error states.
Phase 3 — Purpose-based grouping
- Implement Purpose CRUD, task assignment/filtering, aggregate progress, and safe detach-on-delete.
- Connect task and Purpose flows with additive migrations preserving existing data.
- Preserve private ownership and the combined Tasks & Projects workspace.
- Maintain owner-only projects and owner-managed per-task view/edit sharing.
Phase 4 — UX and quality
- Add accessible responsive layout, empty states, confirmations, and useful validation.
- Improve search and filters.
- Manually verify core journeys in the browser and through the API.
- Document setup and troubleshooting.
Phase 5 — Productivity
- Add subtasks, recurrence, calendar, reminders, saved views, and analytics according to priority.
- Add background processing only when required.
Phase 6 — Collaboration
- Add workspaces, membership, assignments, comments, and activity history.
- Define role/permission rules if authentication and authorization have been re-enabled.
Phase 7 — Production readiness
- Add deployment configuration, secrets management, monitoring, backup/restore, rate limiting, and CI/CD.
- Load-test important query paths and review security/privacy controls.
Phase 8 — Advanced capabilities
- Add integrations, automation, AI assistance, and advanced reporting only when the product needs them.
14. Definition of done
A feature is done when:
- Acceptance criteria are met.
- Domain rules and authorization are enforced on the server for both SQL and JSON providers.
- Input validation and error handling are present.
- Database changes are migrated and documented.
- Frontend behavior covers loading, empty, success, and error states as relevant.
- Relevant build/compile and lint checks pass where available. Automated tests are not required in this phase.
- No secrets or unrelated changes are included.
- API documentation and README are updated where needed.
- The final response accurately summarizes what changed, what was verified, and what remains.
15. Response format when implementing
Use this concise structure:
1. Plan — short description of the approach.
2. Changes — files/layers changed and user-visible behavior.
3. Verification — exact build/compile/lint commands actually run and their real results; do not run automated tests unless requested.
4. Notes — migrations, configuration, assumptions, security concerns, or follow-up tasks.
Be direct and educational. Explain important design choices briefly, especially when they affect maintainability or production safety. Prefer a complete, build-verified vertical slice over a large amount of unverified code.