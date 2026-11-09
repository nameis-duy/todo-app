# Todo

<!-- impeccable:product-schema 1 -->

## Platform

web

## Users

Individuals managing personal and work projects. This audience was confirmed
during initialization on October 27, 2026. Collaboration is a priority within
this personal-workspace focus; a team-first product is not an established goal.

## Product Purpose

Help people capture tasks quickly, identify a clear next action, and organize
personal and work projects. A Project groups related tasks and shows their
completion progress. Sharing and collaboration are also confirmed product
priorities. Success means making everyday task management straightforward
without losing context about the work or who can access it.

## Operating Context

People manage projects and tasks in a combined browser workspace. Existing
workflows include task creation and editing, completion and reopening, project
organization, priorities, dates, search, filtering, sorting, and pagination.
Keep the working workspace central rather than substituting a marketing page.

The repository contains an Angular frontend in `todo-app-fe` and a .NET Web API
in `todo-app-be`. The frontend starts with `npm --prefix todo-app-fe start` from
the workspace root. Local frontend development uses a same-origin API proxy.

## Capabilities and Constraints

- Preserve the existing Angular/.NET stack and working task/project workflows.
- Use Project/Projects consistently across web and mobile titles, navigation,
  forms, filters, confirmation dialogs, sync messages, and product descriptions.
  Use No project for tasks without a project. Purpose/Purposes are internal
  entity and API names only, not product labels. Do not rename stored data,
  routes, or public API contracts solely to change presentation terminology.
- Use real API data. Do not invent tasks, totals, progress, or success feedback
  when the server is unavailable.
- Preserve existing authentication, ownership, access controls, and unsaved-edit
  protections. Verify the current implementation before extending a surface.
- Treat sharing and collaboration as priorities, not permission to assume that
  every collaboration feature or integration is already implemented.
- Keep development servers private. Do not claim production readiness, security
  certification, or deployment guarantees.

## Brand Commitments

The product name is Todo. Its working vocabulary uses tasks and Projects.
No additional visual identity or writing-voice commitments were established
during initialization. Existing visual implementation is evidence for later
design work, not a new product requirement introduced by this document.

## Evidence on Hand

- [Task workspace](todo-app-fe/src/app/tasks/task-workspace.html): current task,
  project, search, filtering, and editor presentation.
- [Frontend package](todo-app-fe/package.json): established frontend dependencies
  and development/build commands.
- The existing API and application code are the authority for current behavior;
  confirm integration before making capability claims.
- No customer testimonials, adoption figures, benchmarks, certifications, paid
  plans, or unique market-positioning claims were confirmed in this interview.

## Product Principles

1. Make fast capture and the next useful action easy to find.
2. Support collaboration without weakening privacy or access boundaries.
3. Keep task and project context together and terminology consistent.
4. Prefer truthful data and recoverable errors over simulated progress.
5. Preserve usability across screen sizes and input methods.

## Accessibility & Inclusion

Responsive layouts and keyboard-accessible controls are confirmed requirements.
Retain visible focus, meaningful labels, and non-color state cues. No specific
conformance level or completed accessibility audit was established.

## Open Decisions

Distinct market positioning, commercial plans, deployment targets, and additional
collaboration capabilities remain undecided. Do not infer them from this record.