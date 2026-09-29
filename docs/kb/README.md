# Support knowledge base

What the in-app support assistant — and a human answering support — knows
about problems people actually hit in AltomateHR. Built from the testing pass
of 21–28 Sep 2026 and meant to grow with every one after it.

One file per problem. Each is written for the person asking — an HR admin, a
supervisor or an employee — in the words they would use, and says what *they*
can do about it.

## How the assistant should use this

1. **Always load [`_assistant-rules.md`](_assistant-rules.md)** as standing
   instructions, whatever the question.
2. **Retrieve entries** by matching the question against `title`, `symptoms`,
   `keywords` and the body. The `symptoms` are there for exactly this — they
   are how people phrase the problem, in English and Malay.
3. **Filter by the asker's role** using `audience`. An employee cannot change
   company settings, so an admin-only fix should reach them as "ask your HR
   admin to…".
4. **Respect `status`.** For `open`, give the workaround. For `fixed`, the
   problem should not happen; if the person still sees it, hand off to support.
5. **Hand off when `escalate: true`** and the steps don't resolve it — above
   all where money has already moved.

## Entry format

```yaml
---
id: approvals-stuck-pending          # = the file name, kebab-case
title: A claim or leave request is stuck waiting for approval
module: approvals                     # see list below
audience: [employee, supervisor, admin]
kind: known-issue                     # known-issue | error-message | how-to | expected-behaviour
status: open                          # open | fixed | by-design
severity: high                        # user impact: high | medium | low
escalate: true                        # hand to a human if the steps don't resolve it
verified: 2026-09-28                  # when this was last confirmed by reproducing it
symptoms:                             # how people ask — English and Malay
  - My supervisor can't approve my claim
  - Tuntutan saya masih belum diluluskan
keywords: [approve, pending, stuck, lulus]
related: [leave-applied-twice]        # other entry ids
internal_ref: "runbook §1, issue 1"         # where support staff find the engineering detail
---
```

Modules: `account`, `approvals`, `attendance`, `claims`, `employees`, `leave`,
`overtime`, `payroll`, `payroll-bank`, `payroll-import`, `settings`.

The body always has the same three headings, so every chunk stands on its own:

```markdown
## What's happening
## What you can do
## When to contact support
```

## Writing rules

- **One problem per file**, readable on its own. Retrieval returns single
  entries, so never write "see above".
- **The asker's words, not ours.** "My claim is stuck", not "approval chain
  role mismatch". Menu names exactly as the app shows them.
- **No internal detail in the body** — no file names, commits, database terms
  or root causes. The assistant may quote anything in the body to a customer.
  Point to the engineering record with `internal_ref` instead.
- **No figures the app doesn't show.** Explain how AltomateHR works something
  out; don't hand out tax or legal advice.
- **Date every claim.** `verified` is when someone last reproduced it, not when
  it was written.

## Checking before you commit

Every entry is machine-read, and one malformed header makes a pipeline skip it
silently. From `docs/kb`, run:

```bash
LC_ALL=en_US.UTF-8 ruby -E UTF-8 validate.rb
```

It checks that headers parse, ids match file names, required fields are
present, each entry has at least four symptoms including a Malay one, and every
`related` link resolves.

## Keeping it current

- **A bug is fixed and deployed** → set `status: fixed`, update `verified`,
  and rewrite "What you can do" for the fixed behaviour. Move its row in
  [the runbook](../08-launch-runbook.md) to "Fixed".
- **Testing finds something new** → add an entry here *and* a row in the
  runbook. The entry is what users are told; the runbook is why.
- **Malay and Chinese phrasings** can be added to `symptoms` at any time. More
  ways of asking means better matching.
