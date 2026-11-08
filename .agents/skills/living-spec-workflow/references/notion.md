# Notion Roadmap

Read this before any mode that reads or writes roadmap items. The Notion
Roadmap database is the only backlog and status tracker. The repo keeps only
the public `docs/roadmap.md` (direction, shipped, planned) and the living
specs in `docs/specs/`.

## Where things live

| Content | Location |
| --- | --- |
| Status, priority, branch, PR, dependencies | Notion Roadmap row properties |
| Brief | Row page body, under `## Brief` |
| Delta spec | Row page body, under `## Delta spec` |
| Audit findings checked and rejected | Notion page "Considered and rejected" |
| Local copies of brief and delta spec | `docs/work/EMC-<n>-<slug>/brief.md`, `spec.md` (git-ignored) |
| Implementation plan | `docs/work/EMC-<n>-<slug>/plan.md` (git-ignored, never in Notion) |
| Accepted behavior | `docs/specs/` (committed, merged in the item's PR) |

Notion is the source of truth for the brief and the delta spec. When a local
copy and the page differ, Notion wins: pull again before you plan.

## IDs and connection

The Notion IDs live only in the git-ignored `AGENTS.local.md`, in its "Notion
tracking" section, with these labels:

- `Notion parent page (Expo Modules C#):`
- `Notion Roadmap database:`
- `Notion Roadmap data source:`
- `Notion Considered and rejected page:`
- `Notion Roadmap views:`

Never write a Notion ID or URL into a committed file.

If the labels are missing or no Notion MCP tool is available on this
machine, stop and tell the owner. Do not fall back to tracking in git. Do not
create a new parent page or database.

## Database

| Property | Type | Values |
| --- | --- | --- |
| Name | title | |
| ID | unique ID | `EMC-<n>`, set by Notion in creation order |
| Status | status | see below |
| Priority | select | `P1`, `P2`, `P3` |
| Effort | select | `S`, `M`, `L` (round a range up) |
| Milestone | select | `Bridge and authoring core`, `Authored modules`, `expo-desktop integration`, `Production hardening` |
| Area | select | `Modules and compatibility`, `Platform adapters`, `ABI and codecs`, `Runtime and tooling`, `CI` |
| Depends on | relation (paired with "Blocks") | other rows |
| Branch | text | `impl/EMC-<n>-<slug>` |
| PR | URL | GitHub PR |
| Spec approved | checkbox | owner approved the delta spec |

| Status | Meaning |
| --- | --- |
| Idea | No brief yet. |
| Backlog | Brief exists, not scheduled. |
| Todo | Brief exists, scheduled. |
| In progress | Branch exists. |
| In review | PR is open. |
| Blocked | Waits on something outside the item. |
| Done | PR merged. |
| Rejected | Decided not to build. |

A follow-up is a new row that depends on the old one. There is no "open with
follow-up" status.

Rows migrated from the old git roadmap start with `Former repo ID: NNN`. Old
three-digit IDs in briefs and notes refer to that line. To find the row, use
`notion-search` for `Former repo ID: NNN`. Former 041 matches two rows (the
call-path benchmark and the shared-object composites). Former IDs 001–021
have no row.

## Page body

```markdown
Former repo ID: NNN          (migrated rows only)

## Brief
...from references/brief-template.md, headings start at ###...

## Delta spec
...added in deliver mode, headings start at ###...
```

Local copies keep the Notion heading levels: in `brief.md` and `spec.md`,
the top headings are `###`, without the `## Brief` or `## Delta spec` line.
Write a new `spec.md` the same way, so `push-spec` copies it without changes.
Do not hard-wrap paragraphs or list items in these files: Notion turns every
line break into a hard break.

## The `notion-tracker` agent

Do large Notion reads and writes through the `notion-tracker` agent
(`.claude/agents/notion-tracker.md`, Haiku). It keeps page content out of the
main context and returns short summaries. Operations:

| Operation | Effect |
| --- | --- |
| `pull <EMC-n>` | Writes `docs/work/EMC-<n>-<slug>/brief.md` and, if present, `spec.md`. Notion wins. |
| `push-brief <EMC-n> <path>` | Replaces or adds the page's "Brief" part with the file. |
| `push-spec <EMC-n> <path>` | Replaces or adds the page's "Delta spec" part with the file. |
| `set <EMC-n> "<property>=<value>" ...` | Updates properties. Quote each pair. |
| `create <status> <title> [body-file]` | Creates a row, with the file under `## Brief` (or as the whole body for `Idea`); returns `EMC-<n>` and the URL. |
| `list <filter>` | One line per row: ID, title, Status, Branch, PR. |

If the agent type is not available (a new definition needs a Claude Code
restart), dispatch a general-purpose agent on the Haiku model and tell it to
read `.claude/agents/notion-tracker.md` and act as that agent. Small property
changes, and reads and one-line additions on the "Considered and rejected"
page, may use the Notion MCP tools directly (`notion-fetch`,
`notion-update-page` with `insert_content`).

## Two machines

One machine drives an item from start to PR. Its `docs/work/` folder holds
the plan. When the Mac drives and Windows only verifies, the Windows agent
gets its tasks by message and code through the `development` branch; it does
not need the plan. Moving an item to the other machine in the middle of
delivery is not supported: the new machine pulls the Notion page and writes a
new plan. A machine without the Notion MCP can still verify, but it cannot
run discover, brief, reconcile, or deliver.
