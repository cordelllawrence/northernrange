# Phase 2 — Contract decisions

Date: 2026-09-16. Checklist source: `2026-09-16-review-plan.md`, Phase 2.

The contract is: stdout carries only the command's output; stderr carries one
error line; the exit code says what kind of failure it was. This phase makes
that true on every path, including the ones Cocona owns before our code runs.

## Exit codes

| Situation | Before | After | How |
| --- | --- | --- | --- |
| Unknown option (`--bogus`) | 129, Cocona text | 2 | `NrDispatchPipeline` wraps Cocona's dispatch pipeline and rejects unknown options first. |
| Unknown command | 1, Cocona text | 2 | Cocona prints its message before any command runs; `Program` remaps 1 to 2 when nothing was dispatched. |
| Value cannot be converted (`-n abc`) | 1, "requires Nullable\`1 value" | 2, "Invalid value for --max: expected an integer." | `ErrorHandlingFilter` catches `ParameterBinderException`. |
| Missing required option / argument | n/a (was manual validation) | 2, "Missing required option --to." | `--to` and `--subject` are now required at the parser, so `--help` and `--llm` show them as required. |
| Ctrl+C | unhandled | 130 | Filter and `Program` both catch `OperationCanceledException`. 128 + SIGINT is the shell convention. |
| `--help` / `-h` at any level | 129 | 0 | Cocona exits 129 after printing requested help. `Program` remaps it; help is a success. |
| Global option before the command (`nr --json messages list`) | 129, "Unknown option" | works | Cocona binds globals per command, so it only accepted them after the subcommand. `ArgPrescan.HoistGlobalOptions` moves leading globals behind the command. |
| `auth status` with nothing authenticated | 3 | 3, unchanged | Deliberate: scripts branch on it (`nr auth status && …`). Documented as a query result, not a failure. |

`Program` no longer calls `Environment.Exit` inside its try/finally; it sets
`Environment.ExitCode` so the log flush in `finally` always runs.

## JSON contract

- Errors in JSON mode are a JSON envelope on **stderr**:
  `{"error":{"code":N,"message":"…"}}`. stdout stays empty on failure. Plain
  text mode is unchanged. JSON mode is known at startup from `--json` /
  `NR_JSON`, and `OutputWriter.DetermineMode` also flips it when config chooses
  JSON. Rejected alternative: writing the envelope to stdout. That would make
  "parse stdout only on exit 0" the rule instead of "stdout is always the
  result", and the exit code already tells the agent to look at stderr.
- Delete results are a record, `DeleteResult { deleted, id }`, for both
  `labels delete` and `drafts delete`. The drafts field was `draftId`; it is
  now `id`, matching labels. Records let the `--llm` schema describe them.
- Tests guard the two drift paths: every command in the `--llm --json` output
  must have a response schema, and the exit-code table in the schema must equal
  the `ExitCodes` class. A schema/serializer test checks property names agree.

## Logging and local state

- The always-on daily log under the config directory is gone. `README.md`
  promised "no local state" and the log recorded queries, subjects and
  addresses at Information level on every run, including `--help`. Nothing is
  written to disk unless `--log` or `--log-file` is given. Warnings still reach
  stderr.
- Information-level lines keep subjects, addresses and queries. That is what
  someone turning on `--log` to debug an agent wants to see. The `--log` help
  text and `docs/USAGE.md` now say so. Bodies, tokens and secrets are never
  logged (checked by grep in `Auth/` and `Gmail/`).
- `--ui` help text now says it is partial: tables and key-value blocks are
  rich, body text stays plain.

## Verification

`tests/ExitCodeE2ETests.cs` runs the real binary from the test output
directory with an isolated `--config` path and asserts exit code, stdout, and
stderr for every failure that happens before a network call. This is the layer
that would have caught the 129 and the leaked CLR type name.
