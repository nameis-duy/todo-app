# Todo frontend

A standalone Angular 22 workspace for projects and tasks, connected to the
existing ASP.NET Core API, with local email/password sign-in, private records and
per-task sharing. No seeded tasks or automated tests are included. Tags remain deferred.

## Requirements

- Node.js supported by Angular 22: `^22.22.3`, `^24.15.0`, or `>=26.0.0`.
	Prefer an LTS release. Node 25 on the implementation machine completed the
	build, but npm reports it outside Angular's supported engine range.
- npm and the .NET 10 backend in the sibling `todo-app-be` directory.

## Run locally

From the workspace root, start the API in one terminal:

```powershell
dotnet run --project todo-app-be/src/Todo.Api --launch-profile http
```

In another terminal:

```powershell
npm --prefix todo-app-fe ci
npm --prefix todo-app-fe start -- --host 127.0.0.1 --port 4200
```

Open `http://127.0.0.1:4200/`. The API uses its existing storage configuration
as configured by the backend; the frontend never falls back to simulated data.
If port 4200 is occupied, add a different `--port` to the start command.

The Angular development server proxies `/api/**` to `http://localhost:5080`
using `proxy.conf.json`. A 502 generally means that the backend is stopped or
unreachable. Restart the frontend server after changing proxy configuration.
The proxy keeps browser requests same-origin, including on an alternative port.

VS Code tasks are also available for serving the frontend/API, building,
dependency auditing, and running the design skill checks.

## Implemented workflows

- Manage projects and their tasks in one workspace. Project navigation sits
	beside the queue on desktop and collapses behind Projects on smaller screens.
- Register, sign in, and sign out using HttpOnly cookie sessions. Protected routes
	load the current account before rendering private data; expired sessions return to sign-in.
- Continue as guest to use private server-backed tasks/projects before sign-in.
	The header identifies the guest and provides Sign in; sharing is unavailable to guests.
- After sign-in/registration, choose Sync guest data or Not now. Sync adds guest
	records without replacing existing account data; Not now keeps a header sync action.
	Errors preserve the pending state for retry. Guest records are not copied automatically.
- Owners can share a saved task with registered users by email, select Can view or
	Can edit, update access, and revoke it. View-only fields/completion are disabled;
	shared editors cannot change projects, delete, or manage sharing. Projects stay private.
- Select All tasks, No project, or a project without leaving the task queue.
	New tasks inherit the selected project; the task editor supports reassignment
	or removing the assignment. Project progress refreshes after task mutations.
- Create, edit, and delete projects in the same workspace. Deleting a project
	preserves its tasks and, when that project is selected, switches to No project.
	The former `/purposes` page redirects to the combined workspace and preserves
	query parameters. The API's `Purpose` naming and contracts remain unchanged.
- View tasks and open a linkable detail/edit drawer with `?task=<id>`.
- Create, replace editable fields, complete/reopen, and permanently delete with
	confirmation. Closing a changed form asks before discarding its draft.
- Debounced server-side search, status/priority/date filters, all five supported
	sort fields, direction, and bounded server-side pagination.
- Title/description/date validation, API validation messages, loading/empty/error
	states, retry, and successful-operation feedback.
- Current version tokens on PUT, completion PATCH, and DELETE. A 409 keeps the
	edit draft and offers an explicit reload, with confirmation before discarding
	changes. No stale write is retried automatically.
- Keyboard-accessible native modal dialog, focus restoration, visible focus,
	accessible icon names, and responsive list/form layouts.

Calendar dates remain `YYYY-MM-DD` strings in requests; they are not converted
to UTC. UTC audit timestamps are displayed in the browser's local time zone.
Client validation is a convenience; backend validation remains authoritative.

## Structure and configuration

- `src/app/tasks/task.models.ts`: typed API request/response contracts.
- `src/app/tasks/task-api.ts`: HTTP operations only.
- `src/app/tasks/task-store.ts`: cancellable query state and API error mapping.
- `src/app/tasks/task-workspace.*`: task presentation and typed reactive form.
- `src/app/tasks/task-projects.css`: combined desktop/mobile workspace layout.
- `src/app/purposes/purpose-workspace.*`: reusable project navigation and editor.
- `src/app/purposes/purpose-api.ts`: existing grouping contracts and API client;
	internal names stay aligned with the backend rather than migrating stored data.
- `src/app/shared/icon.ts`: framework-independent Lucide icon adapter. The
	Angular wrapper currently excludes Angular 22 in its peer dependencies.
- `src/app/api.config.ts`: central `API_BASE_URL` injection token (default `/api`).
- `src/app/auth/`: session initialization, sign-in/registration form, route guard and 401 interceptor.
- `src/app/tasks/task-sharing.*`: owner-only per-task access management.

For production hosting, serve `dist/todo-web/browser`, configure SPA fallback to
`index.html`, and reverse-proxy `/api` to the backend. `proxy.conf.json` is only
for development. Keep frontend and API same-origin for cookie auth and Angular's
default XSRF cookie/header support. Merely changing `API_BASE_URL`/CORS is not enough
to support a separate API origin; credentialed requests and CSRF need an explicit design.
Never include secrets in frontend configuration.

SQL Server must be available and migrated even when tasks use JSON: accounts and
sharing grants always use SQL. Existing ownerless records remain hidden until explicitly
assigned by an administrator. See the backend README for migration and security limits.
No passwords or bearer tokens are saved to local/session storage. The XSRF cookie
is readable only for CSRF request headers; the session cookie stays HttpOnly.
Guest recovery uses an additional protected HttpOnly cookie lasting eight hours.
Use the same browser/hostname and sync before expiry; clearing cookies can lose
access to unmerged records. Not now and sign-out retain this recovery cookie.
Guest mode still requires the server and SQL, is not offline or cross-device sync,
and does not automatically purge abandoned server records. See the backend README
for retention and multi-tab limitations.
This application remains for local development with non-sensitive data. Do not
expose development servers publicly; email verification/recovery/MFA are not implemented.

## Build and visual checks

From the workspace root:

```powershell
npm --prefix todo-app-fe run build
node .agents/skills/design-taste-frontend/scripts/lint.mjs --app todo-app-fe/src
node .agents/skills/design-taste-frontend/scripts/inspect.mjs --app http://127.0.0.1:4200/ --out todo-app-fe/.taste-skill/inspect
```

The visual inspector needs an installed Chrome/Edge/Chromium. It checks 1440,
768, and 390px widths. These are design measurements, not an automated test suite.
The editor inspection task allows `covered-text` because a native modal
intentionally makes its background inert and covers it. No application-level
lint configuration exists; the lint command above checks the design skill rules.

Design: Achromatic Ledger, seed `1289257485`, no redraws. A cool neutral ground,
self-hosted Geist, and deadline/status marks give the personal queue its face.
No external photographs, fictional metrics, or demo datasets are shipped.
See `.taste-skill/review.md` for the visual review and documented exceptions.

## Dependency caveat

The implementation-time `npm audit` reports two findings (one high, one low)
through Angular's Vite development tooling. Its suggested forced fix downgrades
`@angular/build` to Angular 21, so it has not been applied. Keep the dev server
loopback-only and revisit a compatible upstream patch before deployment.
Automated testing remains deliberately deferred.
