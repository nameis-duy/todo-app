# Todo app-screen review

## Authentication and Sharing Update (2026-10-26)

Preserved Achromatic Ledger, seed `1289257485`, Geist, neutral chrome and Lucide.
No redraws, sample production data, images or motion were added. Account question:
"Can I sign in?" Sharing question: "Who can access this task, and how?"

The 380px sign-in form is deliberately capped and centered, not a dashboard grid.
`inspect/` now contains the anonymous sign-in screen, not the archived task queue.
All six final fold/page captures were opened at 1440/768/390px. Inspector: zero
failures; accepted warnings are empty space below a short credential form and the
unused-canvas heuristic treating that narrow form as a working dashboard. Adding
filler or stretching password fields would not improve this screen. Lint has no
failures; its date-padding warning remains intentional.

The main action and labels are legible without a hero or explanatory marketing
copy. Mobile fields and account controls fit without horizontal page overflow.
Task access is server-derived, not a locally computed badge. Owners can invite,
switch view/edit grants and revoke; recipients cannot see sharing controls.
View-only task fields/completion are disabled and Save is absent. Shared access
is stated in the editor header. Project choices stay disabled for shared editors.

Manual connected checks covered registration, logout/login, task creation, sharing
and grant changes. API checks covered cross-account list/count/direct-ID isolation,
viewer/editor restrictions, revocation, logout, CSRF, JSON owner persistence and
hidden legacy records. These are manual checks, not an automated test suite.

Subjective review: design 8, usability 8, creativity 6, content 8 (overall 7.6).
Intentional tradeoffs: compact conventional credential form, native confirmation
prompts, and a scrollable task drawer with sharing after task fields. No claims of
screen-reader auditing or production security certification are made.

Reviewed 2026-10-23 using the design-taste-frontend app-screen workflow.

## Combined Projects and Tasks Update

This section supersedes the task-only layout and grouping limitations recorded
below. Internal API names remain `Purpose`; all grouping labels in the UI say
Project. Backend contracts, storage, authentication, and test scope are unchanged.

Direction: preserve Achromatic Ledger, seed `1289257485`, no redraws. The existing
Geist face, neutral palette, Lucide icons, and native editors remain in place.

Idea: a project's live completion ratio becomes the context for its working task
queue, with project management available without navigating to another screen.
Live thing: select a project, create its task, and complete it in place.

Layout: neutral project rail / scoped task queue / independent native dialogs.
At 1440px, `80 + (240 + 32 + 1008) + 80 = 1440`. At 390px, the rail folds above
the queue and `16 + 358 + 16 = 390`. No extra imagery, animations, or charts were
added to a working app screen.

### Final Combined Review

Seen in one second: Todo tasks with project context and a New task action.
Subjective scores: design 8.0, usability 8.0, creativity 6.0, content 8.0; overall
7.6. The familiar task-manager layout limits creativity, intentionally.

Seven app-screen checks:
1. The task queue remains the main working block; project selection takes one
    click on desktop and a disclosure plus selection on phone/tablet.
2. Rail, title, filters, list, and footer use a closed grid with equal gutters.
    Project-scope controls now align with the project identities below them.
3. Chrome is neutral; status, urgency, overdue dates, and feedback retain meaning.
4. Project counts and progress come from API responses, not local counters.
    Tasks remain server-paged; the project rail also keeps its existing paging.
5. Empty projects show true zero progress. New tasks inherit the selected
    project; tasks without an assignment explicitly say No project.
6. Thin progress tracks show actual completion, with counts and percentages.
    No decorative charts or fictional analytics are introduced.
7. Phone/tablet navigation folds, selections close it, and project dialogs remain
    visible even when the navigation list is collapsed. Task details remain a
    full-width phone dialog without horizontal overflow.

Top three changes from review: separated dialogs from collapsed navigation,
removed project scope from the task-filter badge, and left-aligned rail controls.
Remaining tradeoffs: phone sorting uses a second toolbar row; very large project
collections still use the existing assignment selector; native discard prompts
retain browser styling. No automated tests were written or run.

### Combined Verification

- Production build passed within unchanged bundle/style budgets (about 392kB
   initial JavaScript/CSS, about 98kB estimated transfer).
- Baseline capture: `combined/`, the prior instance on port 4200, zero failures
   and two warnings (empty space and unused canvas). It caught an initial loading
   state, so this is a layout baseline, not a populated-data comparison.
- Final workspace, populated project queue, and task editor: zero failures and
   zero warnings at 1440/768/390. Reports and fold/page images are in `merged/`,
   `merged-project/`, and `merged-editor/`. The editor allows covered-text because
   its native modal intentionally covers inert background content.
- Design lint: zero failures; one intentional warning for ISO-date zero-padding.
- Manual connected checks: created a temporary project, selected it, created a
   task with its assignment preselected, completed the task, observed 0% to 100%
   progress, and edited the project's description in the collapsed-nav layout.
- Confirmed project deletion names the project, preserves its task, switches to
   No project, and shows the refreshed task assignment and audit timestamp.
   The temporary task was then permanently deleted through its confirmation UI.
- Checked the project dialog at 390px and verified `/purposes?purpose=<id>`
   redirects to the unified root while preserving project selection.
- Project paging with multiple pages, all filter combinations, screen readers,
   and project-specific stale-version responses were not exhaustively exercised.
   Existing version checks and explicit conflict reload behavior were retained.
- Preview: `http://127.0.0.1:4201/`, API at `http://localhost:5080`.

## Archived Task-Only Review

## Direction and intent

- Direction: Achromatic Ledger, seed `1289257485`. No redraws.
- Face: a cool neutral page ground, locally bundled Geist, and a Todo check mark.
- Signature: the task's completion control, current status, priority, and calendar
  deadline sit in one row; completion is available without opening the editor.
- Queue question: "What needs to get done?"
- Editor question: "What is the next step for this task?"
- Layout: centered 1120px column, equal outer margins, no sidebar. At 1440px,
  `160 + 1120 + 160 = 1440`. At 390px, `16 + 358 + 16 = 390`.
- Data: live API responses only. No seeded dataset, invented totals, people,
  charts, photography, or external images. No photograph credits are required.

## Final critique

ROUND final

Seen in one second: a personal task queue with New task as the primary action.

Idea (one sentence): the task's actual completion state and calendar deadline
become the organizing marks of a quiet working queue.

Live thing: create/edit and complete/reopen through the existing API.

Layout words by section: shell=centered utility bar / queue=status tabs and
aligned rows / editor=right-side native dialog, full-width on a phone.

Scores: design 8.0 · usability 8.0 · creativity 6.0 · content 7.0 → overall 7.5.
These are subjective self-review scores, not inspector measurements.

Caps applied: app-screen rules; marketing-page imagery and motion floors do not
apply. Familiar task-manager composition limits the creativity score.

Top three remaining tradeoffs, not measurement failures:
1. The identity is restrained rather than distinctive (inspect/desktop-fold.jpg).
   A future established brand can replace the mark without changing the layout.
2. Search and sorting occupy two toolbar lines on a phone
   (inspect/mobile-fold.jpg). This preserves readable labels and touch targets.
3. Native discard confirmations inherit the browser's appearance rather than
   the app's styling. No custom confirmation framework was added for this slice.

Earlier fixes: reduced empty-state spacing, enlarged mobile data/control labels,
and prevented task status from wrapping beside its description.

## App-screen checklist

1. The queue is the largest block; New task is present immediately. In the drawer,
   task fields and Create task/Save changes are the active working region.
2. Header, controls, list, and footer share the centered canvas edges. The list
   keeps its natural content height instead of inventing rows to fill the screen.
3. Chrome is neutral. Color is reserved for focus, task status, overdue/urgent
   data, and operation feedback. There are no decorative charts or colored tiles.
4. Totals and ranges use the server's page response. No independently maintained
   global task counts are displayed. Figures use tabular digits.
5. A genuine empty page shows zero. Unavailable API results do not show a false
   zero-result pagination footer. Missing deadlines say "No due date".
6. There are no charts or legends to reconcile. Priority labels remain explicit;
   color and marks are not the only way to identify task state.
7. The phone has no sidebar, no sideways page overflow, and a full-width editor.
   Status stays on one line; descriptions can truncate in rows but remain editable
   in full in the drawer.

All twelve final fold/page screenshots in `inspect/` and `editor/` were opened
and reviewed at desktop (1440px), tablet (768px), and phone (390px) widths.

## Verification actually performed

From the workspace root:

```powershell
npm.cmd --prefix todo-app-fe run build
node.exe todo-app-fe/.agents/skills/design-taste-frontend/scripts/lint.mjs --app todo-app-fe/src
node.exe todo-app-fe/.agents/skills/design-taste-frontend/scripts/inspect.mjs --app http://127.0.0.1:4200/ --out todo-app-fe/.taste-skill/inspect
node.exe todo-app-fe/.agents/skills/design-taste-frontend/scripts/inspect.mjs --app http://127.0.0.1:4200/?task=new --allow covered-text --out todo-app-fe/.taste-skill/editor
npm.cmd --prefix todo-app-fe audit
```

- Production build: passed, approximately 361kB initial JavaScript/CSS before
  compression, within the starter's unchanged bundle and stylesheet budgets.
- Design lint: zero failures, one warning for ISO-date zero-padding. This is date
  formatting, not decorative index numbering; the warning is intentional.
- Final queue inspector: zero failures, zero warnings at all three widths.
- Final editor inspector: zero failures, zero warnings at all three widths, with
  `covered-text` allowed because a real native modal intentionally covers its inert
  background. No content within the dialog is covered.
- Manual connected workflow: created a temporary task, opened its saved details,
  updated status/priority/start/due dates, completed it, reopened it, and confirmed
  permanent deletion through the UI. The temporary task was removed afterward.
- Manual stale-version check: changed the task through a separate API request
  while the editor retained its older version. Save returned HTTP 409, displayed
  the reload message, and disabled further stale mutations. Reload fetched the
  current state and reenabled the editor. Some browser actions used DOM event
  dispatch because the embedded browser could not physically click a background
  tab; no test files or test infrastructure were created.
- The unreachable-API state was also observed before starting the backend.
- Multi-page datasets, every filter combination, all server-validation responses,
  and screen-reader behavior were not exhaustively manually verified.
- No automated tests were written or run.

## Known environment and scope limits

- Node 25.5.0 completed the build but is outside Angular 22's declared supported
  engine ranges. Use a supported LTS version for normal development.
- `npm audit` reports two findings (one high, one low) in Angular's Vite development
  tooling. Its proposed forced fix downgrades Angular's build package to version
  21; it was not applied. Keep development servers loopback-only and review an
  upstream compatible patch before production work.
- Lists and tags need backend endpoints and are not presented as working features.
- No authentication, database migration, backend contract change, deployment, or
  automated testing infrastructure was introduced.