# Zurvan Planning & Collaboration — implementation blueprint

Status: architecture baseline drafted; this document is not a claim that remote collaboration is implemented.

## Product scope

A workspace may enable any combination of:
- Agenda: time-based personal/team schedule.
- Kanban: workflow board with configurable columns and cards.
- School: academic timetable, lessons, homework, exams, attendance, grades and guardian visibility.

All modes share identity, workspace/group membership, conversations, files, notifications, audit history and permission evaluation. Mode checkboxes enable views/features on a workspace; they do not duplicate underlying tasks/events.

## Domain boundaries

### Identity
- UserId is an immutable server-issued Zurvan identifier. User-chosen local strings are not identity proof.
- Profile fields: display name, avatar reference, locale, timezone.
- A group member is always linked to a verified UserId.

### Group / workspace
- GroupProfile: id, slug, name, description, avatar, owner_user_id, created_at, archived_at.
- GroupMembership: group_id, user_id, role, status, joined_at, removed_at.
- Roles: owner, assistant, member, teacher, student, guardian (role is a starting preset, never the sole authorization check).
- Group invitations must be accepted by the target authenticated account. Adding an arbitrary identifier must never grant membership.
- Owner manages group and assistant assignment. Assistant permissions are explicitly delegated and cannot exceed owner authority.

### Permissions
Use server-enforced permissions, not UI visibility:
- group.view, group.manage, member.invite, member.remove, role.assign
- message.read, message.send, message.moderate
- report.read, report.submit, report.review
- agenda.read, agenda.create, agenda.update, agenda.delete
- kanban.read, kanban.card.create, kanban.card.update, kanban.column.manage
- school.timetable.manage, school.homework.assign, school.homework.submit,
  school.exam.manage, school.grade.write, school.attendance.write,
  school.student.read, school.guardian.read
Default presets are editable; explicit denies and scoped grants are evaluated by the API.

### Agenda
- ScheduleItem: id, workspace_id, owner_user_id, title, description, start_at, end_at,
  all_day, timezone, recurrence_rule, visibility, status, created_by, updated_at.
- Agenda view is a projection of ScheduleItems and linked Kanban cards / lessons.
- Time zone is stored with each timed event; recurring instances are expanded consistently.

### Kanban
- Board: id, workspace_id, name, enabled, created_by.
- BoardColumn: id, board_id, name, position, semantic_state, WIP_limit.
- WorkItem: id, board_id, column_id, title, description, assignee_user_id,
  reporter_user_id, priority, due_at, position, linked_schedule_item_id,
  linked_lesson_id, created_at, updated_at, archived_at.
- WorkItemComment and WorkItemAttachment are separate entities.
- Drag/drop updates column and position transactionally; audit event records old/new values.

### School
- AcademicYear, Term, ClassGroup, Subject, Room, TeacherAssignment.
- TimetableSlot: class_group_id, subject_id, teacher_id, room_id, weekday,
  start_time, end_time, effective_from, effective_to.
- LessonOccurrence: timetable_slot_id, starts_at, ends_at, status, notes.
- Homework: lesson/subject/class scope, title, instructions, due_at, attachments,
  created_by, grading rubric.
- HomeworkSubmission: homework_id, student_id, submitted_at, content/file refs,
  status, teacher_feedback, grade.
- Exam: class/subject scope, starts_at, duration, syllabus, maximum_score.
- GradeRecord: student_id, subject_id, term_id, assessment_id, score, feedback.
- AttendanceRecord: lesson_occurrence_id, student_id, state, note, recorded_by.
- Guardian links are verified and scoped to a student; guardians see only linked students.

## Messaging and reporting
- Conversation: group/direct/work-item context; ConversationMember.
- Message: sender_id, body, reply_to_id, created_at, edited_at, deleted_at.
- MessageAttachment and ReadReceipt.
- Reports are structured records, not ordinary chat messages: report type, author,
  subject, period, body/data, attachments, status, reviewer and review history.
- Chat send permission and report-submit permission are independent.

## Realtime collaboration and synchronization — mandatory requirement
All group/workspace changes must be delivered to every currently authorized member online in realtime. This is a core product requirement, not an optional enhancement or a manual refresh feature.

- Use an authenticated realtime transport (SignalR/WebSockets) connected to server-authorized group/workspace channels.
- Server is authoritative: clients submit commands; the server validates identity, membership, permission and version, commits the change, then broadcasts the committed event.
- Broadcast events include create/update/archive/delete, assignment changes, due-date changes, Kanban column/order changes, timetable/homework/exam/attendance/grade changes, report lifecycle changes and chat messages.
- Never broadcast private messages, confidential student records, grades, reports or attachments to an entire group by default. Resolve recipients from the resource scope and each member's current permissions.
- Re-check membership and permissions when joining channels and before every subscription/broadcast. Revocation must disconnect or exclude the member immediately.
- Persist an event/outbox record in the same transaction as each successful mutation. Publish only committed outbox events; retry delivery safely and use stable event IDs for deduplication.
- Each event carries event_id, workspace_id, resource_type, resource_id, operation, actor_id, resource_version and server_timestamp. Clients apply only newer versions.
- Support online presence and connection state as advisory signals; do not treat presence as proof of delivery or authorization.
- On disconnect, clients keep a last-seen event cursor. On reconnect, fetch missed events from the durable event log before resuming live delivery.
- Offline edits are queued as commands/drafts and reconciled against server versions. Detect conflicts explicitly; never silently overwrite another member's change.
- Delivery semantics: durable at-least-once event publication with client deduplication; UI must distinguish sent, server-accepted, delivered and read where applicable.
- Chat supports message delivery/read receipts; work updates support realtime state refresh and targeted notifications.
- Push notifications are a fallback for offline/background devices, not a replacement for realtime synchronization.
- Use backpressure, bounded payloads, pagination for history, rate limits and per-user/group connection limits.

## API and security requirements
- Authenticated API with server-derived user identity; never trust user_id sent by client.
- Row-level authorization for every read/write; membership revocation takes effect immediately.
- Validate group scope, linked student/guardian scope, file ownership and attachment access.
- Audit log for membership, permission, task, grade, attendance, message moderation and report changes.
- Soft-delete/archive for historical records; retain attribution.
- Rate limits, pagination, idempotency keys, conflict/version checks.
- Offline cache is read/write only for explicitly supported drafts; server remains authority.
- Notifications are generated from committed server events; respect timezone and user preferences.

## Migration / compatibility
- Existing personal CalendarEvent and CalendarTask data remains intact.
- Existing local Android TeamPlanner profile/group data is treated as device-local legacy data.
  It must not be silently promoted to a real group or server membership.
- Migration offers profile claim and invitation acceptance after authenticated identity exists.
- Agenda projections can reference existing personal events/tasks without destructive conversion.
- Do not claim cross-device group sync until API, database policies, invitation handshake,
  sync conflict handling and push notifications are deployed and tested.

## Implementation sequence
1. Domain contracts and permission matrix (this document).
2. Authenticated identity and group API/database policies.
3. Group profile, invitations, membership and delegated permissions.
4. Shared workspace and Agenda API/sync.
5. Kanban boards/cards/comments/attachments and audit.
6. Messaging and structured reports.
7. School academic structure, timetable and attendance.
8. Homework/submissions, exams/grades and guardian access.
9. Desktop and Android UI parity, offline behavior, migration and end-to-end tests.

## Current repository facts
- Desktop currently renders a weekly personal planner from ICalendarRepository.
- Android currently has local planning views and a device-local TeamPlanner.
- The Android board view is a local date/status projection, not a collaborative Kanban board.
- Existing team planner documentation explicitly says arbitrary IDs/roles are local display data
  and cross-device synchronization is not implemented.
- Therefore, production group chat, verified membership, permission enforcement and School
  workflows require backend/API work before their UI can honestly be presented as functional.

## Build policy
No build is triggered by this architecture change. Build only when explicitly requested.
