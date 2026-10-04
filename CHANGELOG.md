# Changelog

All notable changes to `nr` land here. Dates are the day a tag was cut;
unreleased work sits under `[Unreleased]`.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and `nr` follows [Semantic Versioning](https://semver.org/spec/v2.0.0.html).
Pre-1.0 releases may still change CLI grammar or JSON shapes; breaking changes
are called out explicitly in each entry.

## [0.2.0-beta] — 2026-10-04

A broad sweep across the CLI surface, the JSON/exit-code contract, the Gmail
send path, and the project's shipping gates. The CLI grammar changes are
breaking; everything else is additive or a bug fix.

### Breaking — CLI grammar

- Unified verbs and nouns across every command group. `messages`, `threads`,
  `drafts`, and `labels` all use the same `read` / `show` / `list` shapes;
  `send` is now a verb at the right level in the tree.
- Short-flag inventory audited for clashes; long flags are all kebab-case.
- `--max` range unified at 1–500 across list commands.
- `--log-flat` retired in favour of `--log-format jsonl|text`.
- `--to` and `--subject` are now enforced at the parser, so `--help` and the
  `--llm` schema show them as required.

### Added — contracts for agents

- Error envelope in `--json` mode: errors go to stderr as
  `{"error": {"code", "message"}}`, so stdout stays empty on failure.
- Global options are accepted before or after the subcommand
  (`nr --json messages list` now works, not just the trailing form).
- `--llm` response-type map is derived from the live command tree; a test
  fails if a command is added without an entry.
- `.github/workflows/ci.yml` runs on push and PR across Ubuntu, Windows,
  and macOS: build with `-warnaserror`, Release tests, and
  `dotnet format --verify-no-changes`.

### Added — correctness

- `Reply-To` is honoured when composing a reply (RFC 5322 §3.6.2 — mailing
  lists use `Reply-To` to route replies back to the list, not the sender).
- Reply-all deduplicates the primary recipient out of Cc, comparing bare
  email addresses so display-name variations collapse.
- Address validation: `--to`, `--cc`, and `--bcc` with "not an email" now
  exit **2** with a message naming the flag and the bad entry, instead of
  the previous unhandled `MimeKit.ParseException` → exit 1.
- Attachment size guard: cumulative on-disk size is checked against a 20 MiB
  cap before upload begins (Gmail's 25 MB wire limit after base64).
- Drafts list preserves Gmail's order across page boundaries (previous
  client-side sort could interleave page edges).

### Fixed — exit codes and output

- Parse errors (unknown option, non-numeric `--max`, `--log-file=path`) now
  return the documented **2** instead of Cocona's `1` or `129`.
- `--help` exits 0; `Ctrl+C` exits 130; `auth status` with no account exits 3.
- Pagination: the "Next page:" hint now echoes `--label`, `--query`, `--max`,
  `--format`, `--account` so page 2 continues the same filter instead of
  silently reverting to defaults.
- Empty-page-with-token no longer ends paging silently — the hint prints
  whenever a token exists.
- `--ui` auto-disable on redirected stdout verified on all platforms.
- Snippets strip zero-width Unicode (Cf / Mn categories) before truncation,
  so table columns align with marketing-mail payloads.

### Changed — code structure

- Introduced `CommandPrelude` and `CommandSession`: the four-line prologue
  (resolve account, pick output mode, open log scope, build Gmail client)
  that every command repeated is now one line.
- `FlagNames` is the single source of truth for global-option spellings;
  `GlobalOptions` attributes and `ArgPrescan`'s pre-host matcher share it.
- `GmailErrorMapper` audit: all 24 `catch(GoogleApiException)` sites route
  through it. No stragglers.
- `LabelService.IsLikelyLabelId` is now `internal` with documented
  behaviour; 15 cases pin the predicate.

### Changed — docs

- `README.md` and `docs/ARCHITECTURE.md` ledes reframed around "an
  open-source CLI that gives local AI agents and their harnesses direct
  access to Gmail".
- Docs re-homed into `docs/` and `docs/journal/`; only `README.md` and
  `CHANGELOG.md` remain in the repo root.

### Tests

- Suite grew from **87** to **244** tests.
- New coverage: output-mode selection, flag-name/reflection invariants,
  label-ID heuristic, Reply-To and reply-all dedup, address validation,
  attachment-size cap.

[0.2.0-beta]: https://github.com/cordelllawrence/northernrange/compare/v0.1.2-beta...v0.2.0-beta
