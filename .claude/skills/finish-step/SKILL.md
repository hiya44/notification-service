---
name: finish-step
description: Wrap up a piece of work for the developer's review - check the diff, run the right tests, check that docs are current, and report with a suggested commit message. Use at the end of every change, before handing it over.
---

# Finish a step

The developer reviews and commits every change; this skill prepares it for that review. Never commit, push or discard changes.

## 1. Check the change

- `git status` and `git diff` (plus new untracked files): does the change contain only what was asked for? Look for leftover debug
  code, temporary files, commented-out code, unrelated edits, and files the developer changed that you did not expect.
- If the change is large or mixes unrelated concerns, suggest how to split it into separate commits.

## 2. Verify

- `dotnet build` (warnings are errors).
- Unit tests: `dotnet test` on `tests/NotificationService.Domain.Tests`, `tests/NotificationService.Application.Tests` and
  `tests/NotificationService.Infrastructure.Tests`.
- **Integration tests** (`dotnet test tests/NotificationService.IntegrationTests`, Docker required) when the change touches persistence,
  migrations, providers, the dispatch worker, the API, configuration or DI registration. If Docker is not running, say so; do not skip silently.
- New or changed end-to-end tests: run them several times to catch flakiness.
- For behaviour that tests do not cover (e.g. configuration reload, the demo), run the application and check it, or say that it was not checked.

## 3. Review

For changes to production code, use the reviewer subagents that fit and address what they find:
**privacy-reviewer** (always, for code under `src/`), **domain-reviewer** (Domain or Application changes), **test-reviewer** (new behaviour or new tests).

## 4. Check the docs

- README: behaviour, configuration, API or provider changes documented in the relevant section.
- `docs/adr/`: a new record for a new significant decision; a superseding record if an accepted decision changed.
- `CLAUDE.md` files: new conventions, commands or pitfalls learned during the work.
- `src/NotificationService.Api/NotificationService.Api.http`: endpoint or contract changes.

## 5. Report

Keep it short and factual:

- **What changed:** files or areas, grouped, one line each.
- **Verification:** what ran and the results (test counts), plus anything **not** verified and why.
- **Found along the way:** bugs, risks or follow-ups, including anything outside the requested scope that the developer should know.
- **Suggested commit message(s):** conventional commits (`feat(api): ...`, `fix(infra): ...`, `test: ...`, `docs: ...`, `chore: ...`),
  one per commit if you suggested a split, with the files for each.
