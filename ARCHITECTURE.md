# northernrange — Architecture & Current State

**Binary:** `nr`  **Version:** 0.1.2-beta  **Runtime:** .NET 10.0 (single-file, self-contained)

This document describes where the application actually is today — its command
surface, internal structure, and the design decisions behind it. It is the
"map" companion to the user-facing `README.md` / `USAGE.md` and the historical
spec in `requirements.md`.

> **Status note:** The project began (see `requirements.md`) as a *read-only,
> single-account* Phase 1 client scoped to `gmail.readonly`. It has since grown
> well past that. It now requests `gmail.modify`, supports multiple accounts,
> and can send mail, manage drafts, and mutate labels. `requirements.md` is kept
> as a historical record; this document reflects the shipped reality.

---

## 1. What it is

A Gmail command-line client whose **primary consumer is AI agents and scripts**,
with humans as a first-class secondary audience. The contract surface is the
`--json` output of every command; human-readable text and `--ui` (Spectre)
tables are conveniences layered on top.

Two features make the "agent-first" stance concrete:

- **`--json`** on every command → stable, camelCase JSON to stdout; diagnostics
  and errors go to stderr only, so stdout is always cleanly parseable.
- **`--llm` / `--llm-full` / `--llm --json`** → the tool documents *itself* by
  reflecting over its own Cocona command classes and response records, emitting
  Markdown or a JSON tool-schema an agent can ingest to learn the CLI.

---

## 2. Command surface

```
nr
├── auth         login [--force] · logout · status
├── messages     list · read <id> · label <id> [--add] [--remove]
├── threads      list · read <id>
├── labels       list · info <id> · create <name> · delete <id>
├── attachments  list <msg> · download <msg> <att> [--output] [--force]
├── send         new [-t -s --body …] · reply <id> [--reply-all]
└── drafts       list · send <id> · delete <id>
```

Plus process-level flags handled before the command pipeline:
`--llm` / `--llm-full` (self-documentation) and the logging flags
`--log` / `--log-flat` / `--log-file` / `--log-level`.

Global options (every command): `--json`, `--ui`, `-v/--verbose`,
`--credentials`, `--config`, `--account`, and the logging flags.

---

## 3. Layered structure

```
Program.cs                       Composition root: arg pre-scan, Serilog
                                 bootstrap, DI registration, Cocona host.
        │
        ▼
Commands/*                       Thin Cocona command classes. Parse flags,
  NorthernRangeApp (routing)     resolve the account, pick an output mode,
  GlobalOptions  (shared flags)  delegate to a service, render the result.
        │
        ▼
Gmail/*                          All Gmail REST logic. Each service wraps one
  MessageService  ThreadService  resource family, maps Google types → Models,
  LabelService    SendService    and translates GoogleApiException → NrException.
  AttachmentService
  GmailClientFactory (cached client)
        │
        ▼
Models/*                         Immutable record DTOs. These ARE the --json
                                 schema (serialized camelCase).

Cross-cutting:
  Auth/      OAuth2 login/logout/status, token store, headless code receiver.
  Config/    config.json + env + flag precedence, OS paths, multi-account.
  Mime/      MimeParser — base64url, header parse, body extraction, HTML strip.
  Output/    OutputWriter (json/plain/ui), renderers, help, LLM doc generators.
  Errors/    ExitCodes, NrException.
  Filters/   ErrorHandlingFilter (Cocona), ParamValidation.
```

### Dependency flow
Commands depend on services and `OutputWriter`; services depend on `MimeParser`
and `ILogger`; nothing in `Gmail/` depends on `Commands/` or `Output/`. Models
are leaf types with no dependencies. The boundary is clean — the Gmail layer
could be reused by a different front end without modification.

---

## 4. Request lifecycle (typical command)

1. **`Program.cs`** sets UTF-8 console encoding, pre-scans raw `args` for flags
   that must be known before the host exists (`--json`, `--verbose`, the `--log*`
   family, and `--llm*`). `--llm*` short-circuits here and never builds the host.
2. Serilog is configured: a rolling **file sink** (always on, `Information`+) plus
   **conditional console/JSONL sinks** driven by `--verbose` / `--log*`. In
   `--json` mode the console sink is suppressed so stdout stays pure.
3. The Cocona host builds, registers all services as singletons, and dispatches
   to a command method on the matching `*Commands` class.
4. **`[ErrorHandlingFilter]`** wraps execution: `NrException` → its message to
   stderr + its exit code; any other exception → generic message + exit 1.
5. The command calls **`AccountResolver.Resolve(globals)`** → a `ResolvedContext`
   (account name, credentials path, token-store path, loaded `AppConfig`) using
   precedence `--flag > env > config > default`.
6. **`OutputWriter.DetermineMode`** picks `Json` / `RichUi` / `PlainText`.
7. **`GmailClientFactory.GetServiceAsync`** returns a cached `GmailService`
   (building a `UserCredential` from the token store, refreshing if stale).
8. The relevant **`Gmail/*Service`** executes the API call, maps the response to a
   `Models` record, and throws `NrException` on API failure.
9. The command renders: `WriteJson(record)` in JSON mode, otherwise a table /
   key-value / plain-text view.

---

## 5. Key design decisions

| Decision | Rationale |
|---|---|
| **Cocona** for the CLI | Attribute/class-based subcommands, DI host integration. (Archived upstream Dec 2025 but feature-complete; migration path is System.CommandLine / Spectre.Console.Cli — the command contract must survive any swap.) |
| **Records as the JSON contract** | `Models/*` records serialize directly to the documented `--json` shape. One source of truth; no hand-written serializers. |
| **`gmail.modify` single scope** | Covers read, label mutation, and send in one consent. Permanent delete (which needs `mail.google.com`) is intentionally not supported. |
| **Stdout = data, stderr = everything else** | Lets agents/scripts parse stdout without filtering. Enforced by suppressing the console log sink in `--json` mode and routing all diagnostics to stderr. |
| **No local cache / no mailbox mirror** | Only the OAuth token, `user_info.json`, and logs touch disk (plus explicit `attachments download`). Keeps the tool stateless and safe to embed. |
| **Reflection-based self-documentation** | `--llm*` reads the live command tree, so docs can't drift from the actual flags. `IL2026/IL2104` trim warnings are suppressed because `TrimMode=partial` never trims the reflected NuGet/app assemblies. |
| **Per-account token subdirectories** | `tokens/<account>/`, auto-migrated from the old flat layout on first run, enables multiple Gmail accounts side by side. |
| **Centralized account resolution** | `AccountResolver` removes 3-line boilerplate from every command and keeps precedence rules in one place. |

---

## 6. Authentication

OAuth2 installed-app flow via `GoogleWebAuthorizationBroker` with a custom
**`ConsoleCodeReceiver`** that always prints the auth URL to stderr and binds an
OS-assigned ephemeral loopback port, so login works on headless servers (with an
SSH-port-forward hint). The refresh token lives in a `FileDataStore` under
`tokens/<account>/`; the user's email is cached in `user_info.json` so
`auth status` can report it with **no network call**. `auth logout` revokes at
Google's endpoint, then deletes the local token regardless of revocation outcome.

---

## 7. Output & logging

- **Output modes** (`OutputWriter.DetermineMode`): `--json`/`NR_JSON=1`/config →
  JSON; `--ui` on a TTY → Spectre tables; otherwise plain ASCII tables. `--ui`
  with redirected stdout warns and falls back to plain.
- **Plain tables** are rendered by `PlainTextRenderer`, which computes
  **display width** rune-by-rune (CJK/emoji count as 2 columns) for correct
  alignment.
- **Logging**: Serilog → a daily rolling file under the config dir
  (7-day retention) is always on at `Information`. `--verbose` adds a stderr
  console sink at `Debug`. `--log` / `--log-file` write agent-readable **JSONL**
  (`JsonlLogFormatter`); `--log-flat` writes structured text. Tokens, secrets,
  and message bodies are never logged.

---

## 8. Configuration & data paths

Precedence: **built-in defaults → `config.json` → environment variables →
command-line flags**.

| | Windows | macOS / Linux |
|---|---|---|
| Config | `%APPDATA%\northernrange\config.json` | `~/.config/northernrange/config.json` |
| Secrets | `…\client_secrets.json` | `…/client_secrets.json` |
| Tokens | `…\tokens\<account>\` | `…/tokens/<account>/` |
| Logs | `…\logs\` | `…/logs/` |

Config keys: `defaultAccount`, `accounts{}`, `defaultLabel`, `defaultMaxResults`,
`defaultOutputFormat`, `dateFormat`, `credentialsPath`, `httpTimeoutSeconds`.
Env vars: `NR_ACCOUNT`, `NR_CREDENTIALS`, `NR_CONFIG`, `NR_DEFAULT_LABEL`,
`NR_MAX_RESULTS`, `NR_JSON`.

---

## 9. Build & release

- `dotnet build` for local dev (`dotnet run -- …`).
- `build.sh [rid…]` publishes single-file self-contained binaries for
  `win-x64`, `osx-arm64`, `osx-x64`, `linux-x64` (partial trim, ReadyToRun).
- `.github/workflows/release.yml` builds the matrix on a `v*` tag push and
  attaches per-RID archives to a generated GitHub Release.

---

## 10. Tests

`tests/northernrange.Tests` (xUnit) covers the pure, API-independent logic — the
areas most prone to silent bugs:

- **MimeParser** — base64url decoding (padding + url-safe alphabet), header
  parsing, internal-date conversion, body extraction (plain/HTML/nested
  multipart), attachment enumeration.
- **PlainTextRenderer** — CJK/emoji/combining display widths, truncation,
  size/date formatting, table alignment.
- **GmailErrorMapper** — status-code → exit-code mapping.
- **SendService.BuildReplyFields** — Re: prefixing, reply-all CC, References chain.
- **Llm doc/schema generators**, **ConfigLoader/AccountResolver/ConfigPersister**
  precedence, and **ParamValidation**.

Run with `dotnet test`. The Gmail services themselves take a concrete
`GmailService` (no interface), so command/service flows are validated by manual
smoke tests rather than mocked unit tests.

## 11. Resolved review items & remaining gaps

The findings in `CODE-REVIEW.md` have been **addressed**: the class-level
`ErrorHandlingFilter` (which Cocona silently ignored for nested commands, so all
errors exited 1 with a stack trace) is now applied per-method; `threads list`
resolves label names; `messages read --format raw` is binary-safe; the API error
mapper is centralized; `httpTimeoutSeconds` and exponential backoff are wired in;
dead members were removed; and `drafts delete`/`drafts list` gained `--json` and
pagination.

Remaining, by design or deferred: 429 responses are not specially retried (only
503 + transient exceptions); Spectre `--ui` is best-effort; permanent mail
deletion is intentionally unsupported.
</invoke>
