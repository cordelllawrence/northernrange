# northernrange — Requirements

**Version:** 0.1.0-draft
**Date:** 2026-03-02
**Status:** Phase 1 — Read-Only Gmail Client

---

## Table of Contents

1. [Overview](#1-overview)
2. [Technology Stack](#2-technology-stack)
3. [Authentication](#3-authentication)
4. [CLI Command Structure](#4-cli-command-structure)
5. [Functional Requirements](#5-functional-requirements)
6. [Output Formats](#6-output-formats)
7. [Logging](#7-logging)
8. [Configuration](#8-configuration)
9. [Error Handling](#9-error-handling)
10. [Non-Functional Requirements](#10-non-functional-requirements)
11. [Out of Scope — Phase 1](#11-out-of-scope--phase-1)
12. [Appendices](#12-appendices)

---

## 1. Overview

### 1.1 Purpose

**northernrange** (`nr`) is a focused, read-only command-line interface for Gmail. It is designed to be consumed programmatically by AI agents and automated pipelines as its primary use case, while remaining ergonomic for direct human use at a terminal.

### 1.2 Philosophy

- **Headless-first.** Every command produces stable, machine-parseable output when the `--json` flag is supplied. JSON output is the contract surface that callers (agents, scripts, CI systems) depend on. Human-readable output is a convenience, not a stability guarantee.
- **Read-only.** Phase 1 is intentionally limited to reading, listing, and downloading. No mutations to mailbox state occur. This makes the tool safe to embed in automated pipelines without risk of accidental sends or deletes.
- **Minimal local footprint.** The tool does not cache, index, or mirror email data locally. The only persistent local state is the OAuth2 token and log files.
- **Composable.** Output is designed to be piped. List commands return a JSON object with an array and a `nextPageToken` when `--json` is active.
- **Explicit over implicit.** Required arguments are positional. Optional modifiers are flags. No hidden defaults that change behavior based on environment variables (except those explicitly documented in §8).

### 1.3 Target Use Cases

- AI agent reading a user's Gmail inbox as part of a larger workflow.
- Developers scripting Gmail interactions in shell pipelines.
- Engineers auditing email metadata and label structure without writing application code.
- Power users who prefer a terminal to the Gmail web UI.

### 1.4 Binary Name

The compiled executable is named `nr`. All command examples in this document use `nr` as the top-level invocation.

---

## 2. Technology Stack

| Package | Version | Role |
|---|---|---|
| .NET | 10.0 | Runtime (`net10.0` TFM, `OutputType=Exe`) |
| Cocona | 2.2.0 | CLI framework — class/attribute-based subcommands |
| Google.Apis.Gmail.v1 | 1.73.0.3987+ | Gmail REST API client (auto-generated) |
| Google.Apis.Auth | 1.73.0+ | OAuth2 via `GoogleWebAuthorizationBroker` / `FileDataStore` |
| MimeKit | 4.x | RFC-compliant MIME parsing for message bodies and attachments |
| Spectre.Console | latest stable | Rich terminal output (tables, colors); TTY-guarded |
| Microsoft.Extensions.Logging | (inbox with .NET 10) | `ILogger<T>` abstraction used throughout the codebase |
| Serilog | latest stable | Logging implementation / provider |
| Serilog.Extensions.Logging | latest stable | Bridge between Serilog and `Microsoft.Extensions.Logging` |
| Serilog.Sinks.Console | latest stable | Console log sink (stderr) |
| Serilog.Sinks.File | latest stable | Rolling daily file log sink |
| System.Text.Json | (inbox with .NET 10) | JSON serialization for `--json` output mode |

> **Note on Cocona:** Cocona was archived by its author in December 2025. It remains feature-complete and its 2.2.0 release is compatible with .NET 10.0 via its .NET 6 / .NET Standard 2.0 targets. If runtime incompatibilities emerge on future .NET versions, the migration path is `System.CommandLine` (Microsoft) or `Spectre.Console.Cli` (Spectre). The command contract (names, flags, arguments) defined in this document must not change as a consequence of a framework swap.

> **Note on Spectre.Console:** Spectre.Console may be used for tables, colors, and progress bars in human-readable output mode. It must be conditionally activated: if `--json` is present, or if `Console.IsOutputRedirected == true`, Spectre rendering must be completely disabled. A plain-text fallback must exist for all Spectre output paths.

---

## 3. Authentication

### 3.1 OAuth2 Flow

northernrange uses the **OAuth2 installed application flow** (Authorization Code) via `GoogleWebAuthorizationBroker.AuthorizeAsync`. This flow is designed for desktop/CLI apps where the user can interact with a local browser.

Flow at a high level:

1. User runs `nr auth login`.
2. northernrange opens the system default browser to Google's OAuth2 consent screen.
3. Google redirects to a local loopback listener (`http://localhost:<ephemeral-port>/`).
4. northernrange exchanges the authorization code for an access token and refresh token.
5. The refresh token is persisted to the token store (see §3.3).
6. Future commands silently refresh the access token using the stored refresh token; no browser interaction is required.

### 3.2 OAuth2 Scope

Phase 1 requests a single scope:

```
https://www.googleapis.com/auth/gmail.readonly
```

No additional scopes are requested in Phase 1.

### 3.3 Credential and Token Storage

The user must supply a Google Cloud project's **OAuth 2.0 Client ID** as a `client_secrets.json` file (desktop application type), downloaded from the Google Cloud Console.

| Platform | `client_secrets.json` | Token store |
|---|---|---|
| Windows | `%APPDATA%\northernrange\client_secrets.json` | `%APPDATA%\northernrange\tokens\` |
| macOS / Linux | `~/.config/northernrange/client_secrets.json` | `~/.config/northernrange/tokens/` |

- The `client_secrets.json` path can be overridden via `--credentials` global flag or `NR_CREDENTIALS` environment variable.
- The token store is a `FileDataStore` scoped to the tokens folder. The token file within is named `TokenResponse-user`.
- The config directory is created on first run if absent.
- On Linux/macOS, the config directory must be created with permissions `0700`. northernrange warns on stderr if permissions cannot be set but does not fail.

### 3.4 First-Run Behavior

When no token is found in the token store, any command requiring an authenticated Gmail session must:

1. Print to stderr: `No credentials found. Run 'nr auth login' to authenticate.`
2. Exit with code **3**.

Unauthenticated commands must never silently launch a browser. `nr auth login` is the sole entry point to the browser flow.

### 3.5 Commands

#### `nr auth login [--force]`

| Flag | Description |
|---|---|
| `--force` | Re-runs the browser flow even if a valid token exists. Overwrites stored token. |

**Exit codes:** 0 = success, 1 = user cancelled or browser flow failed.

#### `nr auth logout`

Revokes the stored token via the Google revocation endpoint and deletes the local token file. If revocation fails due to a network error, the local token file is still deleted and the exit code is 0; the error is reported to stderr.

#### `nr auth status`

Reports authentication state without triggering any browser flow or non-trivial network requests.

**Exit codes:** 0 = authenticated, 3 = not authenticated.

### 3.6 Token Refresh

`Google.Apis.Auth` handles silent token refresh automatically before each API call. If a refresh fails (revoked token, network error), northernrange emits a descriptive error to stderr and exits with code **3**.

---

## 4. CLI Command Structure

### 4.1 Global Flags

These flags are accepted by every command:

| Flag | Short | Type | Default | Description |
|---|---|---|---|---|
| `--json` | | bool | false | Machine-readable JSON to stdout. Disables all Spectre.Console rendering. |
| `--ui` | | bool | false | Enable Spectre.Console rich rendering. Ignored if `--json` is set or stdout is redirected. |
| `--credentials <path>` | | string | see §3.3 | Path to `client_secrets.json`. |
| `--config <path>` | | string | see §8.1 | Path to northernrange config file. |
| `--verbose` | `-v` | bool | false | Emit additional diagnostic output to stderr via the logging system. |
| `--help` | `-h` | bool | — | Show help for this command and exit. |

### 4.2 Command Hierarchy

```
nr
├── auth
│   ├── login   [--force]
│   ├── logout
│   └── status
│
├── messages
│   ├── list    [--label <id>] [--query <q>] [--max <n>] [--page-token <token>] [--format minimal|metadata]
│   └── read    <id>  [--format full|raw|metadata] [--include-headers <names>]
│
├── threads
│   ├── list    [--label <id>] [--query <q>] [--max <n>] [--page-token <token>]
│   └── read    <id>  [--format full|metadata|minimal]
│
├── labels
│   ├── list
│   └── info    <id>
│
└── attachments
    ├── list      <message-id>
    └── download  <message-id> <attachment-id>  [--output <path>] [--force]
```

### 4.3 Auth Commands

#### `nr auth login`

**Human stdout:** `Authenticated successfully as user@gmail.com`
**JSON stdout:** `{"status":"authenticated","email":"user@gmail.com"}`

#### `nr auth logout`

**Human stdout:** `Logged out. Token revoked.`
**JSON stdout:** `{"status":"logged_out"}`

#### `nr auth status`

**Human stdout:**
```
Authenticated: true
Account:       user@gmail.com
Token expires: 2026-03-02T14:23:00Z (valid)
```
**JSON stdout:**
```json
{
  "authenticated": true,
  "email": "user@gmail.com",
  "tokenExpiry": "2026-03-02T14:23:00Z",
  "tokenValid": true
}
```

### 4.4 Messages Commands

#### `nr messages list`

```
nr messages list [--label <id>] [--query <q>] [--max <n>] [--page-token <token>] [--format minimal|metadata]
```

| Flag | Short | Default | Description |
|---|---|---|---|
| `--label <id>` | `-l` | `INBOX` | Filter by label ID or name. System labels: `INBOX`, `SENT`, `UNREAD`, `SPAM`, `TRASH`, etc. |
| `--query <q>` | `-q` | (none) | Gmail search query (same syntax as the Gmail search box). Passed unmodified to the API. |
| `--max <n>` | `-n` | 25 | Maximum messages to return. Range: 1–500. |
| `--page-token <token>` | | (none) | Token from a previous `list` response for pagination. |
| `--format <f>` | | `metadata` | `minimal` = ID + threadId only. `metadata` = adds From, To, Subject, Date, snippet. |

**Human stdout:** Table with columns: ID, From, Subject, Date, Snippet (truncated to 60 chars).

**JSON stdout:**
```json
{
  "messages": [
    {
      "id": "18e4f...",
      "threadId": "18e4f...",
      "from": "Alice <alice@example.com>",
      "to": "user@gmail.com",
      "subject": "Meeting tomorrow",
      "date": "2026-03-01T10:00:00Z",
      "snippet": "Hi, just confirming our meeting..."
    }
  ],
  "nextPageToken": "abc123",
  "resultSizeEstimate": 142
}
```

#### `nr messages read <id>`

```
nr messages read <id> [--format full|raw|metadata] [--include-headers <names>]
```

| Argument / Flag | Description |
|---|---|
| `<id>` | Gmail message ID. Required. |
| `--format <f>` | `full` (default) = decoded body + attachment list. `metadata` = headers only. `raw` = RFC 2822 bytes decoded from base64url, written to stdout as UTF-8. |
| `--include-headers <names>` | Comma-separated header names for `metadata` format. Default: `From,To,Cc,Subject,Date,Message-ID`. Case-insensitive. |

In `full` format, northernrange uses MimeKit to parse the body. It prefers `text/plain` parts. If only `text/html` is present, it strips HTML tags to produce readable plain text.

**Human stdout:**
```
From:    Alice <alice@example.com>
To:      user@gmail.com
Subject: Meeting tomorrow
Date:    Sat, 01 Mar 2026 10:00:00 +0000

Hi, just confirming our meeting is still on for tomorrow at 2pm.

Best,
Alice

--- Attachments ---
[1] agenda.pdf (application/pdf, 42 KB) — attachment-id: ANGjdJ...
```

**JSON stdout:**
```json
{
  "id": "18e4f...",
  "threadId": "18e4f...",
  "labelIds": ["INBOX", "UNREAD"],
  "headers": {
    "From": "Alice <alice@example.com>",
    "To": "user@gmail.com",
    "Subject": "Meeting tomorrow",
    "Date": "Sat, 01 Mar 2026 10:00:00 +0000",
    "Message-ID": "<abc@mail.gmail.com>"
  },
  "snippet": "Hi, just confirming...",
  "body": {
    "mimeType": "text/plain",
    "text": "Hi, just confirming our meeting is still on for tomorrow at 2pm.\n\nBest,\nAlice"
  },
  "attachments": [
    {
      "attachmentId": "ANGjdJ...",
      "filename": "agenda.pdf",
      "mimeType": "application/pdf",
      "size": 43008
    }
  ],
  "internalDate": "2026-03-01T10:00:00Z",
  "sizeEstimate": 45312
}
```

### 4.5 Threads Commands

#### `nr threads list`

```
nr threads list [--label <id>] [--query <q>] [--max <n>] [--page-token <token>]
```

Flags are identical in semantics to `messages list`.

**JSON stdout:**
```json
{
  "threads": [
    {
      "id": "18e4f...",
      "snippet": "Hi, just confirming...",
      "messageCount": 3,
      "historyId": "1234567"
    }
  ],
  "nextPageToken": "abc123",
  "resultSizeEstimate": 54
}
```

#### `nr threads read <id>`

```
nr threads read <id> [--format full|metadata|minimal]
```

Returns all messages in the thread in chronological order (ascending `internalDate`).

**JSON stdout:**
```json
{
  "id": "18e4f...",
  "historyId": "1234567",
  "messages": [ /* array of message objects, same shape as messages read --json */ ]
}
```

### 4.6 Labels Commands

#### `nr labels list`

**Human stdout:** Table with columns: ID, Name, Type, Messages Total, Messages Unread.

**JSON stdout:**
```json
{
  "labels": [
    {
      "id": "INBOX",
      "name": "INBOX",
      "type": "system",
      "messagesTotal": 1500,
      "messagesUnread": 12,
      "threadsTotal": 800,
      "threadsUnread": 10,
      "color": null
    },
    {
      "id": "Label_1234567",
      "name": "Work/Projects",
      "type": "user",
      "messagesTotal": 42,
      "messagesUnread": 0,
      "threadsTotal": 15,
      "threadsUnread": 0,
      "color": {
        "textColor": "#ffffff",
        "backgroundColor": "#16a765"
      }
    }
  ]
}
```

#### `nr labels info <id>`

Accepts either label ID (`Label_1234567`) or display name (`Work/Projects`). If a name is given, northernrange calls `users.labels.list` to resolve it to an ID first. If multiple labels share the same name, exits with code 2 (ambiguity error).

**JSON stdout:** Same shape as a single element from `labels list`, with all fields populated.

### 4.7 Attachments Commands

#### `nr attachments list <message-id>`

Retrieves the message with `format=full` and enumerates MIME parts with a non-empty `filename` or `Content-Disposition: attachment`. Does **not** call `users.messages.attachments.get`.

**Human stdout:**
```
Attachments for message 18e4f...

Index  Filename       MIME Type          Size
1      agenda.pdf     application/pdf    42 KB
2      photo.jpg      image/jpeg         1.2 MB
```

**JSON stdout:**
```json
{
  "messageId": "18e4f...",
  "attachments": [
    {
      "attachmentId": "ANGjdJ...",
      "filename": "agenda.pdf",
      "mimeType": "application/pdf",
      "size": 43008
    }
  ]
}
```

#### `nr attachments download <message-id> <attachment-id>`

```
nr attachments download <message-id> <attachment-id> [--output <path>] [--force]
```

| Argument / Flag | Description |
|---|---|
| `<message-id>` | Gmail message ID. Required. |
| `<attachment-id>` | Attachment part ID from `attachments list`. Required. |
| `--output <path>` | Destination file or directory path. If a directory, the original filename is used. If omitted, writes to the current working directory using the original filename. |
| `--force` | Overwrite `--output` path if it already exists as a file. Without this flag, northernrange exits with code 6 if the file exists. |

The `data` field from `users.messages.attachments.get` is base64url-encoded. northernrange converts base64url to standard base64 (replacing `-` with `+` and `_` with `/`, padding as needed) before decoding with `Convert.FromBase64String`, then writes the raw bytes to the output path.

**Human stdout:** `Downloaded agenda.pdf (42 KB) to /home/user/downloads/agenda.pdf`

**JSON stdout:**
```json
{
  "messageId": "18e4f...",
  "attachmentId": "ANGjdJ...",
  "filename": "agenda.pdf",
  "mimeType": "application/pdf",
  "size": 43008,
  "outputPath": "/home/user/downloads/agenda.pdf"
}
```

---

## 5. Functional Requirements

### 5.1 Authentication (FR-AUTH)

**FR-AUTH-001:** The application must not initiate any browser flow unless the user explicitly runs `nr auth login`. All other commands must check for a stored token and exit with code 3 if none is found.

**FR-AUTH-002:** `nr auth login` must open the system default browser to the Google OAuth2 consent URL and listen on a loopback address for the authorization code redirect. The loopback port must be OS-assigned (ephemeral).

**FR-AUTH-003:** `nr auth login` must request exactly one OAuth2 scope: `https://www.googleapis.com/auth/gmail.readonly`. No additional scopes may be requested.

**FR-AUTH-004:** The OAuth2 refresh token must be stored in the OS-appropriate user data directory using a `FileDataStore`. It must not be written to the current working directory, a temp directory, or any project directory.

**FR-AUTH-005:** `nr auth logout` must call the Google token revocation endpoint (`https://oauth2.googleapis.com/revoke`) before deleting the local token file. If revocation fails due to a network error, the local token file must still be deleted; exit code must be 0; the network error must be reported to stderr.

**FR-AUTH-006:** `nr auth status` must report authentication state without initiating the OAuth2 browser flow.

**FR-AUTH-007:** If the stored refresh token has been externally revoked and a subsequent token refresh fails, northernrange must exit with code 3 and emit: `Stored token is no longer valid. Run 'nr auth login' to re-authenticate.`

**FR-AUTH-008:** The `client_secrets.json` file must never be modified by northernrange.

### 5.2 Messages (FR-MSG)

**FR-MSG-001:** `nr messages list` without any flags must list up to 25 messages from `INBOX` using the `metadata` format, showing From, Subject, Date, and a truncated snippet.

**FR-MSG-002:** `nr messages list --query` must pass the query string unmodified to the Gmail API `q` parameter. northernrange must not interpret, validate, or transform the query.

**FR-MSG-003:** `nr messages list --max` must accept values between 1 and 500 inclusive. Values outside this range must produce a validation error to stderr and exit with code 2 before any API call.

**FR-MSG-004:** `nr messages list` must include `nextPageToken` in `--json` output when returned by the API (`null` when absent), enabling callers to paginate via `--page-token`.

**FR-MSG-005:** `nr messages read <id> --format full` must parse the message body using MimeKit. It must prefer `text/plain` parts. If no `text/plain` part exists, it must fall back to stripping HTML tags from the `text/html` part.

**FR-MSG-006:** `nr messages read <id> --format full` must include a list of attachment metadata (filename, mimeType, size, attachmentId) without calling `users.messages.attachments.get` for attachment data.

**FR-MSG-007:** `nr messages read <id> --format raw` must output the RFC 2822 message as base64url-decoded bytes written to stdout as UTF-8 text, enabling piping to other tools.

**FR-MSG-008:** If a message ID does not exist or is inaccessible, northernrange must exit with code 5 and print a descriptive error to stderr. It must not print a partial JSON object to stdout.

**FR-MSG-009:** `nr messages read --format metadata` must include only the headers specified by `--include-headers` (defaulting to `From`, `To`, `Cc`, `Subject`, `Date`, `Message-ID`). Header names are case-insensitive.

### 5.3 Threads (FR-THR)

**FR-THR-001:** `nr threads list` must accept the same `--label`, `--query`, `--max`, and `--page-token` flags as `nr messages list`. Pagination must work identically.

**FR-THR-002:** `nr threads read <id>` must return all messages in the thread in chronological order (ascending `internalDate`).

**FR-THR-003:** `nr threads read <id> --format full` must parse each message body using MimeKit with the same `text/plain`-first fallback as `nr messages read`.

**FR-THR-004:** If a thread ID does not exist, northernrange must exit with code 5.

### 5.4 Labels (FR-LBL)

**FR-LBL-001:** `nr labels list` must return both system labels (e.g., `INBOX`, `SENT`, `SPAM`) and all user-created labels.

**FR-LBL-002:** `nr labels list` output must include `messagesTotal`, `messagesUnread`, `threadsTotal`, and `threadsUnread` counts. If a count is unavailable for a label, that field must be `null` (not omitted).

**FR-LBL-003:** `nr labels info <id>` must call `users.labels.get` to retrieve full label detail including color fields for user labels. System labels have no color; color fields must be `null`.

**FR-LBL-004:** `nr labels info <id>` must accept both label ID and display name. If a name is given, northernrange calls `users.labels.list` to resolve it. If multiple labels share the same name, northernrange must report an ambiguity error and exit with code 2.

### 5.5 Attachments (FR-ATT)

**FR-ATT-001:** `nr attachments list <message-id>` must enumerate all MIME parts with a non-empty `filename` or `Content-Disposition: attachment`. Inline parts without a filename (e.g., inline images with only a `Content-ID`) must not be listed.

**FR-ATT-002:** `nr attachments download` must decode the base64url `data` field by converting the URL-safe alphabet (`-` → `+`, `_` → `/`) and adding padding before decoding.

**FR-ATT-003:** `nr attachments download` must not hold the full decoded attachment in memory simultaneously with any other full copy. The implementation must write to the output file as the data is decoded.

**FR-ATT-004:** If the output path already exists as a file, northernrange must not overwrite it silently. It must print a warning to stderr and exit with code 6 unless `--force` is also passed.

**FR-ATT-005:** The `--output` flag must accept both file paths and directory paths. If an existing directory is given, the file is written into it using the original attachment filename.

---

## 6. Output Formats

### 6.1 Output Mode Selection

Precedence (highest wins):

1. `--json` present → **JSON mode**
2. `--ui` present AND `Console.IsOutputRedirected == false` → **Rich UI mode**
3. stdout redirected (`Console.IsOutputRedirected == true`) → **Plain text mode** (Spectre disabled)
4. Default (TTY, no flags) → **Plain text mode**

### 6.2 JSON Mode

- Activated by `--json` (or `NR_JSON=1`).
- All output written to stdout as valid, complete JSON.
- No ANSI escape codes, no color, no table-drawing characters.
- All errors written to stderr as plain text only. Callers must not parse stderr.
- Dates are always ISO 8601 UTC strings (e.g., `"2026-03-01T10:00:00Z"`).
- Byte sizes are always integers (bytes), never formatted strings.
- Pagination state (`nextPageToken`) is always included in list responses, with value `null` when absent.
- Schema is a stable API contract. Breaking changes require a document version increment.

### 6.3 Plain Text Mode (Default)

- Human-readable, aligned, UTF-8 encoded.
- No ANSI codes unless `--ui` is active.
- Tables use ASCII box-drawing (`+`, `-`, `|`) for universal terminal compatibility.
- When a `nextPageToken` is available: `Next page: nr <command> --page-token <token>` printed at the end.

### 6.4 Rich UI Mode (`--ui`)

- Only active when `--ui` is passed AND `Console.IsOutputRedirected == false`.
- Uses Spectre.Console for colored tables, progress indicators, and styled text.
- If `--ui` is passed but stdout is redirected, northernrange warns on stderr and silently falls back to plain text.
- No Spectre type may be instantiated in any code path where `--json` is active.

### 6.5 Stderr

- All diagnostic messages, warnings, and errors go to stderr regardless of output mode.
- stderr is always plain text and never JSON.

---

## 7. Logging

### 7.1 Architecture

northernrange uses `Microsoft.Extensions.Logging` (`ILogger<T>`) as its logging abstraction throughout the entire codebase. Direct `Console.Write` calls must not be used for diagnostic output. Serilog is configured as the backing provider at application startup and wired via `Serilog.Extensions.Logging`.

### 7.2 Sinks

#### Console Sink (`Serilog.Sinks.Console`)

- **Output stream:** stderr exclusively. Never stdout.
- **Active when:** `--verbose` (`-v`) flag is present.
- **Minimum level when active:** `Debug`.
- **Minimum level when inactive:** `Warning` (only warnings and errors surface to the terminal without `--verbose`).
- **Format:** `[{Level:u3}] {Message:lj}{NewLine}{Exception}`
- **In JSON mode:** The console sink must be fully suppressed (set to minimum level `Fatal+1` / `Off`) to ensure stdout contains only the JSON payload.

#### File Sink (`Serilog.Sinks.File`)

- **Path:**
  - Windows: `%APPDATA%\northernrange\logs\nr-.log` (rolling by date → `nr-20260302.log`)
  - macOS / Linux: `~/.config/northernrange/logs/nr-.log`
- **Always active:** regardless of `--verbose` or `--json` flags.
- **Minimum level:** `Information`.
- **Rolling interval:** Daily.
- **Retention:** 7 days (files older than 7 days are automatically deleted by Serilog).
- **Format:** `{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}{NewLine}{Properties:j}`

### 7.3 Structured Logging Requirements

All log entries must use structured Serilog message templates with named properties rather than string interpolation. Command context properties must be captured using `LogContext.PushProperty` or equivalent at the command entry point.

**Required context properties (where applicable):**

| Property | Type | Description |
|---|---|---|
| `Command` | string | Top-level command name (e.g., `messages.read`) |
| `MessageId` | string | Gmail message ID when operating on a message |
| `ThreadId` | string | Gmail thread ID when operating on a thread |
| `LabelId` | string | Label ID when operating on a label |
| `AttachmentId` | string | Attachment ID when operating on an attachment |

**Example log entries:**
```
[INF] northernrange.Commands.MessagesCommands Retrieving message {MessageId} with format {Format}
[DBG] northernrange.Auth.AuthService Token refresh succeeded; expires {TokenExpiry}
[WRN] northernrange.GmailService API rate limit hit; retrying in {DelaySeconds}s (attempt {AttemptNumber}/{MaxAttempts})
[ERR] northernrange.Commands.AttachmentsCommands Failed to write attachment to {OutputPath}
```

### 7.4 Security Constraints on Logging

**LOG-SEC-001:** OAuth2 access token values must never appear as log property values or in message templates under any circumstances, including `Debug` level.

**LOG-SEC-002:** OAuth2 refresh token values must never appear in any log output.

**LOG-SEC-003:** The OAuth2 client secret value must never appear in any log output. The client ID value may appear (e.g., at startup when resolving credentials path).

**LOG-SEC-004:** Email body text and attachment binary data must not be written to log output.

**LOG-SEC-005:** Recipient email addresses may appear in logs at `Debug` level when `--verbose` is active, but must be truncated or omitted in `Information`-level file log entries.

### 7.5 Startup Logging

At startup (before any command executes), northernrange must log at `Information` level:

- Resolved config file path (or `default` if using built-in defaults).
- Resolved `client_secrets.json` path.
- Active output mode (json / plain / ui).
- `--verbose` state.

This information is written to the file log only (not the console sink unless `--verbose` is active).

---

## 8. Configuration

### 8.1 Config File Location

| Platform | Default path |
|---|---|
| Windows | `%APPDATA%\northernrange\config.json` |
| macOS / Linux | `~/.config/northernrange/config.json` |

Override via `--config <path>` flag or `NR_CONFIG` environment variable.

### 8.2 Config File Format

All settings are optional; missing settings use the documented defaults.

```json
{
  "defaultLabel": "INBOX",
  "defaultMaxResults": 25,
  "defaultOutputFormat": "text",
  "dateFormat": "iso8601",
  "credentialsPath": null,
  "httpTimeoutSeconds": 30
}
```

### 8.3 Configuration Settings

| Key | Type | Default | Description |
|---|---|---|---|
| `defaultLabel` | string | `"INBOX"` | Default label for `messages list` / `threads list` when `--label` is not specified. |
| `defaultMaxResults` | integer | `25` | Default value for `--max` on list commands. Must be 1–500. |
| `defaultOutputFormat` | string | `"text"` | `"text"` or `"json"`. If `"json"`, behaves as if `--json` was always passed. |
| `dateFormat` | string | `"iso8601"` | `"iso8601"` for UTC ISO 8601 strings, `"local"` for local timezone in plain text output. JSON output always uses ISO 8601 regardless. |
| `credentialsPath` | string \| null | null | Absolute path to `client_secrets.json`. Lower precedence than `--credentials` flag. |
| `httpTimeoutSeconds` | integer | `30` | Timeout in seconds for individual HTTP requests to the Gmail API. |

### 8.4 Precedence

Settings are applied in this order (later overrides earlier):

1. Built-in defaults
2. `config.json`
3. Environment variables
4. Command-line flags

### 8.5 Environment Variables

| Variable | Equivalent | Description |
|---|---|---|
| `NR_CREDENTIALS` | `credentialsPath` | Path to `client_secrets.json` |
| `NR_CONFIG` | `--config` flag | Path to config file |
| `NR_DEFAULT_LABEL` | `defaultLabel` | Default label for list commands |
| `NR_MAX_RESULTS` | `defaultMaxResults` | Default max results |
| `NR_JSON` | `--json` flag | Set to `1` to enable JSON output globally |

---

## 9. Error Handling

### 9.1 Exit Codes

| Code | Name | Meaning |
|---|---|---|
| 0 | Success | Command completed successfully |
| 1 | GeneralError | Unexpected error not covered by a specific code |
| 2 | InvalidArguments | Bad argument value or missing required argument |
| 3 | AuthRequired | Not authenticated, or token invalid / revoked |
| 4 | ApiError | Gmail API returned an error response |
| 5 | NotFound | Requested resource (message, thread, label) does not exist |
| 6 | FileError | Cannot write output file (permissions, disk full, file exists without `--force`) |

### 9.2 Authentication Errors

| Condition | Exit code | Message (stderr) |
|---|---|---|
| No token stored | 3 | `No credentials found. Run 'nr auth login' to authenticate.` |
| Token refresh failed (revoked) | 3 | `Stored token is no longer valid. Run 'nr auth login' to re-authenticate.` |
| Token refresh failed (network) | 4 | `Network error during token refresh: <detail>. Check your internet connection.` |
| `client_secrets.json` not found | 1 | `Client secrets file not found at <path>. Download it from the Google Cloud Console.` |

### 9.3 Gmail API Errors

| HTTP Status | Treatment |
|---|---|
| 400 Bad Request | Exit code 2. Print API error reason to stderr. |
| 401 Unauthorized | Exit code 3. Treat as auth required. |
| 403 Forbidden | Exit code 4. Print API error reason (may indicate insufficient scope). |
| 404 Not Found | Exit code 5. |
| 429 Too Many Requests | Apply exponential backoff (see §9.4). |
| 500 / 503 Server Error | Apply exponential backoff (see §9.4). |

### 9.4 Rate Limiting and Exponential Backoff

When a `429` or `503` response is received:

1. Initial delay: 1 second.
2. Retry the request.
3. On each subsequent failure, double the delay: 2s, 4s, 8s, 16s, 32s max.
4. Maximum 5 total attempts (1 original + 4 retries).
5. After 5 failed attempts, exit with code 4.
6. Log a warning for each retry: `API rate limit hit. Retrying in <n>s (attempt <x>/5)...`

Use the `ConfigurableBackOff` provided by `Google.Apis` rather than implementing custom retry logic.

### 9.5 Network Errors

- **Timeout:** Exit code 4. Message: `Request timed out after <n>s. Check your internet connection.`
- **DNS / no network:** Exit code 4. Message: `Network error: <detail>.`
- Underlying exception detail is logged at `Debug` level and written to stderr when `--verbose` is active.

### 9.6 Argument Validation

- All validation errors exit with code 2.
- Validation is performed before any API call.
- All validation errors append: `Run 'nr <command> --help' for usage.`

---

## 10. Non-Functional Requirements

### 10.1 Performance

**NFR-PERF-001:** `nr messages list` with default options must return output within 3 seconds on a connection with ≥ 10 Mbps and ≤ 50ms RTT to Google APIs. This is a target, not a hard command timeout.

**NFR-PERF-002:** `nr auth status` must complete without any network request and return within 100ms.

**NFR-PERF-003:** `nr attachments download` must not hold the full decoded attachment in memory simultaneously. The Gmail API returns base64url data as a single string; northernrange must decode and write in chunks where the API response permits.

### 10.2 Security

**NFR-SEC-001:** No email content, message body text, or attachment data may be written to disk except when explicitly requested (`nr attachments download`).

**NFR-SEC-002:** OAuth2 access tokens, refresh tokens, and client secret values must never appear in stdout, stderr, log files, or any other output channel.

**NFR-SEC-003:** The OAuth2 refresh token must be stored only in the OS-appropriate user data directory. It must not be written to the current working directory, a temp directory, or any project directory.

**NFR-SEC-004:** northernrange must not write any email content to temp files as an intermediate processing step. MimeKit must parse the API response in memory.

**NFR-SEC-005:** On Linux/macOS, the config directory must be created with permissions `0700`. northernrange warns on stderr if permissions cannot be set but does not fail.

### 10.3 Correctness

**NFR-COR-001:** The Gmail API `internalDate` field (milliseconds since epoch, as a string) must be correctly converted to ISO 8601 UTC strings in all output modes.

**NFR-COR-002:** Base64url decoding for attachment data must correctly handle the URL-safe alphabet (`-` and `_`) and missing padding. Any valid Gmail attachment that fails to decode is a bug.

**NFR-COR-003:** The `--json` output schema must be self-consistent across invocations. Fields documented as part of the schema must always be present, with `null` values when not populated — never omitted.

### 10.4 Compatibility

**NFR-COMPAT-001:** northernrange must run on Windows 10+, macOS 13+, and Ubuntu 22.04+ without platform-specific installation steps. The published binary must be fully self-contained — no .NET runtime installation is required on the target machine.

**NFR-COMPAT-002:** `dotnet publish` must produce a **single-file, self-contained** executable via `PublishSingleFile=true` and `SelfContained=true`. Required RIDs: `win-x64`, `osx-x64`, `osx-arm64`, `linux-x64`. The following MSBuild properties must be set in the project file or a publish profile:

```xml
<PublishSingleFile>true</PublishSingleFile>
<SelfContained>true</SelfContained>
<PublishTrimmed>true</PublishTrimmed>
<TrimMode>partial</TrimMode>
<PublishReadyToRun>true</PublishReadyToRun>
<IncludeNativeLibrariesForSelfExtract>true</IncludeNativeLibrariesForSelfExtract>
```

> **Trimming caveat:** `Google.Apis.Gmail.v1` and `Cocona` both use reflection at runtime (API client dispatch, command discovery). `TrimMode=partial` trims only framework assemblies, leaving third-party NuGet packages untrimmed. This avoids `MissingMethodException` / `TypeLoadException` at runtime while still meaningfully reducing binary size. `TrimMode=full` must not be used without a trimming compatibility audit of all dependencies.

> **Native AOT:** `PublishAot=true` is not viable for Phase 1. The Google API client library, MimeKit, and Cocona all rely on runtime reflection incompatible with AOT without significant annotation work. This may be revisited in a future phase.

> **Expected output sizes (approximate):** `win-x64` ~80–110 MB, `linux-x64` ~75–100 MB, `osx-arm64` ~70–95 MB. Sizes vary with the number of NuGet dependencies added.

**NFR-COMPAT-003:** All stdout output must be UTF-8 encoded. `Console.OutputEncoding` must be set to `Encoding.UTF8` at startup before any output is written.

### 10.5 Observability

**NFR-OBS-001:** In `--verbose` mode, northernrange must log to stderr (via the logging system): the Gmail API endpoint being called, the HTTP method, and the HTTP response status code. Request and response bodies must not be logged.

**NFR-OBS-002:** In `--verbose` mode, northernrange must log the resolved config file path and resolved `client_secrets.json` path at startup.

---

## 11. Out of Scope — Phase 1

The following features are explicitly deferred. No code, flags, or stubs for these features should appear in the Phase 1 codebase.

| Feature | Reason deferred |
|---|---|
| Sending email (`nr messages send`) | Requires `gmail.send` scope; mutates mailbox state |
| Composing and saving drafts | Requires `gmail.compose` scope |
| Replying and forwarding | Requires send capability |
| Moving messages between labels | Requires `gmail.modify` scope |
| Archiving and deleting messages | Requires `gmail.modify` scope |
| Marking messages read / unread | Requires `gmail.modify` scope |
| Managing labels (create, rename, delete) | Requires `gmail.modify` scope |
| Gmail Settings API (filters, vacation, forwarding) | Separate API surface and scope |
| Push notifications / watch | Requires server infrastructure (Google Cloud Pub/Sub) |
| Multi-account support | Single Google account per config directory in Phase 1 |
| Offline mode / local message cache | Contradicts minimal local footprint philosophy |
| S/MIME or PGP decryption | Complex key management; separate scope of work |
| Interactive TUI (curses-style browsing) | Headless-first is the priority |
| Email export to mbox format | Not required for read-only agent use case |

---

## 12. Appendices

### Appendix A: Gmail API Endpoints Used

| Command | API Endpoint | Format Parameter |
|---|---|---|
| `messages list` | `GET /gmail/v1/users/me/messages` | `minimal` or `metadata` |
| `messages read` | `GET /gmail/v1/users/me/messages/{id}` | `metadata`, `full`, or `raw` |
| `threads list` | `GET /gmail/v1/users/me/threads` | — |
| `threads read` | `GET /gmail/v1/users/me/threads/{id}` | `metadata`, `full`, or `minimal` |
| `labels list` | `GET /gmail/v1/users/me/labels` | — |
| `labels info` | `GET /gmail/v1/users/me/labels/{id}` | — |
| `attachments list` | `GET /gmail/v1/users/me/messages/{id}` | `full` (parsed locally) |
| `attachments download` | `GET /gmail/v1/users/me/messages/{id}/attachments/{attachmentId}` | — |
| `auth logout (revoke)` | `POST https://oauth2.googleapis.com/revoke` | — |

### Appendix B: Gmail Search Query Examples

The `--query` flag accepts any expression valid in the Gmail search box. Examples:

| Query | Meaning |
|---|---|
| `from:alice@example.com` | Messages from Alice |
| `subject:meeting` | Messages with "meeting" in subject |
| `has:attachment` | Messages with attachments |
| `is:unread` | Unread messages |
| `after:2026/01/01 before:2026/02/01` | Messages in January 2026 |
| `label:Work/Projects` | Messages with a specific user label |
| `larger:5M` | Messages larger than 5 MB |
| `filename:pdf` | Messages with PDF attachments |

### Appendix C: Cocona Command Structure Pattern

The following illustrates how command groups map to Cocona's class-based model:

```csharp
// Program.cs — entry point
CoconaApp.CreateHostBuilder(args)
    .UseSerilog()  // Serilog as ILogger provider
    .ConfigureServices((ctx, services) =>
    {
        services.AddSingleton<GlobalOptions>();
        services.AddSingleton<GmailServiceFactory>();
        // ... other registrations
    })
    .Run<NorthernRangeApp>();

// Top-level command class
[HasSubCommands(typeof(AuthCommands),        "auth")]
[HasSubCommands(typeof(MessagesCommands),    "messages")]
[HasSubCommands(typeof(ThreadsCommands),     "threads")]
[HasSubCommands(typeof(LabelsCommands),      "labels")]
[HasSubCommands(typeof(AttachmentsCommands), "attachments")]
public class NorthernRangeApp { }

// Example sub-command class
public class MessagesCommands
{
    private readonly ILogger<MessagesCommands> _logger;
    private readonly GlobalOptions _globals;

    public MessagesCommands(ILogger<MessagesCommands> logger, GlobalOptions globals)
    {
        _logger = logger;
        _globals = globals;
    }

    [Command("list")]
    public async Task ListAsync(
        [Option('l', Description = "Label ID or name")] string label = "INBOX",
        [Option('q', Description = "Gmail search query")] string? query = null,
        [Option('n', Description = "Max results (1-500)")] int max = 25,
        [Option(Description = "Page token for pagination")] string? pageToken = null,
        [Option(Description = "API message format")] string format = "metadata")
    {
        using var _ = LogContext.PushProperty("Command", "messages.list");
        _logger.LogInformation("Listing messages in label {LabelId}", label);
        // ...
    }

    [Command("read")]
    public async Task ReadAsync(
        [Argument(Description = "Message ID")] string id,
        [Option(Description = "Message format")] string format = "full",
        [Option(Description = "Comma-separated header names")] string? includeHeaders = null)
    {
        using var _ = LogContext.PushProperty("MessageId", id);
        // ...
    }
}
```

### Appendix D: Serilog Bootstrap Pattern

```csharp
// Serilog configured before host build so startup errors are captured
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Google", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.File(
        path: GetLogFilePath(),           // OS-appropriate path
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 7,
        restrictedToMinimumLevel: LogEventLevel.Information,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}{NewLine}{Properties:j}{NewLine}")
    .WriteTo.Conditional(
        condition: _ => isVerbose && !isJsonMode,
        configureSink: wt => wt.Console(
            standardErrorFromLevel: LogEventLevel.Verbose,
            restrictedToMinimumLevel: LogEventLevel.Debug,
            outputTemplate: "[{Level:u3}] {Message:lj}{NewLine}{Exception}"))
    .CreateLogger();

// Host builder integrates Serilog with ILogger<T>
builder.UseSerilog();
```
