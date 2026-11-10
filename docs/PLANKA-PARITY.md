# PLANKA reference audit and independent Zurvan implementation matrix

Source reviewed: https://github.com/plankanban/planka (default branch `master`, package version 2.3.1, inspected November 2026). Reference tree contains 1,648 tracked file entries, including approximately 1,066 `client/` and 525 `server/` entries. Source structure was inventoried; **do not claim every source line was read or that visual/behavioral parity is complete**.

## Licensing boundary
PLANKA Community 1.1 is **source available under Fair Use License**, not an unrestricted MIT/Apache licensed codebase. Its license restricts third-party commercial services and integration into paid products, and Pro/Enterprise source has a separate license. No PLANKA code, artwork, app branding, or proprietary assets are to be copied into Zurvan absent an appropriate license. The Zurvan module is independently authored, with PLANKA used as a behavioral/structural reference.

## Structural map of the reference

| Reference PLANKA area | Code areas identified | Zurvan status |
| --- | --- | --- |
| Projects and boards | `client/src/components/projects/`, `boards/`; `server/api/models/Project.js`, `Board.js` | **Partial:** multiple personal boards; separate project->multiple-board hierarchy not implemented |
| Kanban lists/cards | `client/src/components/lists/`, `cards/`, `boards/Board/KanbanContent/`; `server/api/models/List.js`, `Card.js` | **Partial:** local column/card CRUD and sorting; Android gesture and Windows drag/drop still require practical verification |
| Card types, checklists | `Card.js`, `Task.js`, `TaskList.js` and card components | **Partial:** Android types task/story/project, a checklist per card; Windows types added; multiple task lists incomplete |
| Labels and card custom fields | `labels/`, `custom-fields/`, `custom-field-groups/`, `server/api/models/CustomField.js`, `CustomFieldValue.js` | **Partial:** comma-separated labels and per-card key/value fields; no board-wide typed field definitions |
| Card activity and comments | `activities/`, `comments/`, `Action.js`, `Comment.js` | **Partial:** local timeline/comments; no full event log taxonomy or threaded discussions |
| Attachments and previews | `attachments/`, `Attachment.js`, `UploadedFile.js` | **Partial:** private stored files and open/delete; no gallery, thumbnail, Markdown file embeds, or complete Windows portable ZIP |
| Board views and filters | `boards/Board/KanbanContent/`, `Board/ListView.jsx`, `Board/GridView.jsx`, `BoardActions/Filters.jsx` | **Partial:** Kanban and filtering only; alternative views/saved filters incomplete |
| Team memberships and authorization | `board-memberships/`, `users/`, `server/api/models/BoardMembership.js`, `User.js` | **Not yet implemented in redesigned Kanban:** team panel is intentionally empty pending team specification |
| Realtime sync and backend | Socket.IO, Sails, PostgreSQL models, services and routes | **Not implemented:** Android and Windows private boards currently store data independently |
| Notifications, subscriptions, webhooks | `notifications/`, `notification-services/`, `webhooks/`, and backend models | **Not implemented** |
| Project media, backgrounds, and customization | `projects/`, `boards/`, `BackgroundImage.js`, assorted appearance components | **Not implemented** for Kanban |
| Security/accounts | Server auth, policies, sessions, trusted devices | **Separate Zurvan account infrastructure exists**; Kanban-specific ACLs not implemented |
| PLANKA Pro features | README-marked Pro/Enterprise capabilities | **Out of scope without appropriate rights**; independent alternative features may be designed |

## Recent independently authored features

- Android `ZurvanKanbanStore.kt` now serializes optional `cardType` and arbitrary `customFields` without breaking legacy cards that lack these fields.
- Android `ZurvanPersonalKanban.kt` edits card type and simple per-card custom values.
- Windows `MainWindow.Kanban.cs` now stores card type and user-defined key/value fields in its local JSON.
- Prior Kanban refactor added board/column/card CRUD, priorities, due dates, comments, attachments, backups, activity feed and search.
- Existing calendar app data is retained; no automatic deletion or forced migration of old plans.

## Release blockers

1. **Build not approved for this iteration.** Do not claim new code compiles or has a downloadable APK/EXE.
2. Execute Android Gradle / Windows WinUI builds only after explicit user approval.
3. Test real drag/drop, RTL and responsive behavior on both platforms.
4. Test import/export with malformed data, duplicated IDs, absent attachments, null fields, permission failures and storage exhaustion.
5. Design reusable shared Kanban data schema, sync transport, conflict handling and end-to-end authorization before releasing team boards.
6. Build design-system tokens from user-approved visuals to get closer to reference **without copying protected assets**.

## Next implementation sequence

A. Test/fix existing implementation after approved build; increase persistence robustness and regression test coverage.
B. Native project->boards hierarchy, list/card creation and precise drag/drop placement.
C. Board-wide typed custom fields, card task-list groups, labels palette, keyboard shortcuts, image previews and editable descriptions.
D. Parity for filtering, saved views, audit activity and notifications.
E. Account-aware team models, access control, server sync, safe conflict resolution and board invites.
F. Pixel/detail review against reference screenshots and interactive UI regression testing.

**Parity standard:** independently implement similar workflows; never falsely assert byte-for-byte or pixel-perfect sameness.
