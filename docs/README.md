# docs/ — the dated journal

**Charter.** Per-topic **dated records**: `YYYY-MM-DD-<slug>.md`, one file per substantial standalone
record — a design discussion, a review, an investigation, a decision and its rationale. Written once,
dated, and left as history; not edited in place the way the reference docs are.

**This directory is addressed by convention, never by an enumerated list.** To find something: `glob
docs/*.md` then grep. Nothing routes to individual files here except where a reference doc cites one
by name.

## What goes where

| It is… | Home |
|---|---|
| A small empirical finding from doing the work (a measurement, a surprise, a rejected approach) | `NOTEBOOK.md` |
| A shipped unit of work | `CHANGELOG.md` |
| A substantial standalone record (design / review / investigation / decision) | **here**, as `YYYY-MM-DD-<slug>.md` |
| Current truth about how something works or where it's going | the reference set — `ARCHITECTURE.md`, `ROADMAP.md`, `DOMAIN.md`, `VERIFICATION.md`, `CONSUMERS.md` |

A standing truth that emerges from a note here **graduates up** into the reference set; the dated note
stays as the record of how it was reached. Records that are no longer current-design-relevant move to
`archive/`.

_No entries yet — the completed/superseded records that predate this directory live in `archive/`._
