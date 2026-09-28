# Roadmap Documentation Index

Forward-looking briefs for work that is planned or designed but not yet landed on `main`.

## Who these are for

A developer or AI agent who has **not** seen the work being described and has no access to the
conversation that produced it. The reader has the repository and this document, and nothing else.

The point of writing these carefully is leverage: a brief authored with full context should carry
enough detail that a **smaller, cheaper model can execute it** without rediscovering the decisions.
Every judgement left implicit is a judgement the implementer has to make again, usually worse and
usually without knowing it was a judgement at all.

## What separates this from the other categories

| Category | Answers |
|----------|---------|
| [project/](../project/INDEX.md) | *What* to build — requirements, acceptance criteria, PRDs |
| [architecture/](../architecture/INDEX.md) | *How it is structured* — designs, patterns, module boundaries |
| [rules/](../rules/INDEX.md) | *What the game rules say* — the authoritative BattleTech reference |
| **roadmap/** | *How the work gets built and lands* — order, sizing, constraints, traps |

A PRD says a record sheet should render. A roadmap brief says which twelve PRs that becomes, what
goes in each, which one will look wrong to a reviewer and what its description has to say about it.

## What every brief must contain

1. **Where the work is now.** Branch, commit, test state, or "not started".
2. **Exact paths.** Every file named in full from the repository root. Never "the composer" alone.
3. **Order and dependencies**, with the reason for each edge — a compile-time dependency and a
   reviewer-comfort preference are not the same constraint and should not read the same.
4. **Per-unit acceptance criteria** that can be checked without judgement.
5. **The verification command**, literally, so it can be run rather than inferred.
6. **Traps**, each stated as the wrong thing someone will otherwise do, and why it is wrong. A
   decision recorded without its rejected alternative gets re-litigated.
7. **Open questions**, with who owns each. An agent must be able to tell what it may decide from
   what it must escalate.

## Conventions

- Date the filename (`YYYY-MM-DD-topic.md`) so staleness is visible without opening it.
- Update the brief when reality diverges; delete or archive it once the work has landed.
- Rules calls belong to the maintainer. A brief cites [rules/](../rules/INDEX.md) and never invents
  a ruling of its own.

## Documents

| Document | Summary |
|----------|---------|
| [2026-09-28-record-sheet-paper-doll.md](2026-09-28-record-sheet-paper-doll.md) | Landing the record-sheet paper doll: twelve PRs, dependency order, the two oversized layers, and the five traps that cost time the first time round |
| [2026-09-28-ui-overhaul-remainder.md](2026-09-28-ui-overhaul-remainder.md) | Layers 5, 7 and 8 of epic #1515: which source branches are cumulative and must be re-cut, where the layer 8 views already exist, and what order to send them in |
