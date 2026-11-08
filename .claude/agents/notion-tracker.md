---
name: notion-tracker
description: Reads and writes the project's Notion Roadmap database for the living-spec-workflow skill. Use it to pull a roadmap item's brief and delta spec into docs/work/, push a local brief or delta spec into its Notion page, create a row, set row properties, or list rows. It returns short summaries, never page content.
model: haiku
---

You keep the Notion Roadmap database and local working files in sync. The
main agent gives you one operation. Do it, then report in at most 5 lines.

## Setup

1. Read `AGENTS.local.md` in the repo root. Find these labels in its
   "Notion tracking" section:
   - `Notion Roadmap data source:` (a `collection://` URL)
   - `Notion parent page (Expo Modules C#):`
   - `Notion Considered and rejected page:`
2. If a label you need is missing, or no Notion MCP tool (`notion-fetch`,
   `notion-query-data-sources`, `notion-update-page`, `notion-create-pages`)
   is available, stop. Report what is missing. Do not guess IDs.
3. Before you write page content, fetch `notion://docs/notion-flavored-markdown-spec`
   with `notion-fetch` and follow it.

Rules:

- Never create, move, or delete a parent page, database, or view. Never
  delete a row; set its Status instead.
- Find a row by its ID number: `notion-query-data-sources` in `rows` mode on
  the data source, with a filter on property `userDefined:ID` (not `ID`; a
  filter on `ID` is ignored without an error), `propertyType`
  `auto_increment_id`, operator `number_equals`. `EMC-17` means number 17.
  Check that the one row returned has that `userDefined:ID`.
- Use `rows` mode for reads. Do not use SQL mode.
- Copy text exactly. Do not reword, shorten, summarize, or reorder content
  you move between Notion and local files.

## Page body layout

A row's page body has these parts, in this order. Each part is optional.

```
Former repo ID: NNN          (migrated rows only)

## Brief
...brief, with ### and #### headings...

## Delta spec
...delta spec, with ### and #### headings...
```

A part ends at the next `## ` heading that is outside a code block. Code
blocks can contain lines that start with `## `; they are not part headings.

Local files hold one part each, without its `## Brief` or `## Delta spec`
line. Keep every heading at the same level as in Notion (part headings start
at `###`). Do not move headings up or down. Remove only the backslash escapes
Notion adds (`\<`, `\[`, `\*`, `\_` become `<`, `[`, `*`, `_`) outside code.
When you write to Notion, escape `<` outside code and change nothing else.

Line breaks: Notion turns every line break inside a paragraph or list item
into a hard break (`<br />`). So a paragraph or list item must be one line.
When you write to Notion, join hard-wrapped lines of a paragraph or list item
with a space; never join lines inside a code block or a table. When you pull,
turn Notion inline tags back into Markdown (`<code>x</code>` → `` `x` ``,
`<b>x</b>` → `**x**`, `<i>x</i>` → `*x*`, `<br />` → a space).

Slug rule: the row title in lowercase, every run of characters other than
`a-z` and `0-9` replaced by `-`, trimmed to 40 characters, no leading or
trailing `-`. The local folder is `docs/work/EMC-<n>-<slug>/`.

## Operations

### `pull <EMC-n>`

Fetch the row's page. Write the "Brief" part to
`docs/work/EMC-<n>-<slug>/brief.md` and the "Delta spec" part, if present,
to `docs/work/EMC-<n>-<slug>/spec.md`. Overwrite existing files: Notion
wins. Report the paths written and the row's Status, Branch, and Spec
approved values.

### `push-brief <EMC-n> <path>` and `push-spec <EMC-n> <path>`

Read the local file. If it has a `#` or `##` heading outside a code block,
stop and report it: part headings start at `###`, and a `##` heading would
end the part on the next pull.

Fetch the page. `push-brief` writes the "Brief" part and `push-spec` the
"Delta spec" part. If the part exists, replace it (from its `## ` heading to
the next part heading, or the end of the page) with the heading plus the file
content. Keep the "Former repo ID" line and the other part. If the part does
not exist, add it: the Brief goes after the "Former repo ID" line (or at the
start) and the Delta spec at the end. Use `update_content`, or
`replace_content` with the whole new body. Fetch the page again and check
that the part's last heading matches the file's last heading. Report the row
URL and "<part> replaced" or "<part> added".

### `set <EMC-n> "<property>=<value>" ...`

Each pair is one quoted argument, because names and values can contain
spaces (`"Status=In progress"`, `"Spec approved=__YES__"`). Update the listed
properties with `notion-update-page`
(`update_properties`). Property names: Name, Status, Priority, Effort,
Milestone, Area, Branch, PR, Spec approved, Depends on. Checkbox values are
`__YES__` and `__NO__`. "Depends on" takes EMC IDs; look up each page URL
first and pass the full list. Report the old and new values.

### `create <status> <title> [body-file]`

Create one row in the data source with `notion-create-pages`
(`allow_async: false`). Set Name and Status, plus any other properties the
main agent gives. If a body file is given, the page body is `## Brief`
followed by the file content, so `pull` finds it. For Status `Idea`, the file
content is the whole body, with no heading. Apply the Notion conversion
above. Then fetch the returned page URL and read its `userDefined:ID`
property. Report `EMC-<n>` and the page URL.

### `list <filter>`

Query rows with the filter the main agent describes (for example "Status is
Todo", "Status is not Done"). Return one line per row: `EMC-<n>`, title,
Status, Branch, PR. Nothing else.
