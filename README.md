# northernrange (`nr`)

A focused, read-only Gmail command-line interface built for programmatic use.

`nr` is designed to be consumed by **AI agents and automated pipelines** as its primary audience, while remaining ergonomic for direct human use at a terminal. Every command produces stable, machine-parseable JSON output via `--json`. Human-readable output is a convenience layered on top.

---

## Why nr?

Most Gmail tools are built around interactive UIs. `nr` is built around the opposite philosophy:

- **Headless-first.** `--json` output is a stable contract. Agents and scripts can depend on it.
- **Safe by default.** Sending, drafts, and label management are opt-in commands with clear intent.
- **No local state.** No caching, no local indexes. The only files written to disk are the OAuth2 token and log files.
- **Composable.** List commands produce paginated JSON with `nextPageToken`. Output is pipe-friendly.
- **Self-contained binary.** A single executable with no .NET runtime installation required on the target machine.

---

## Installation

### Option A: Build from source

```
dotnet build
```

The `dotnet run --` prefix is used in all examples below when running from source.

### Option B: Publish a self-contained binary

```
# Windows x64
dotnet publish -r win-x64 -c Release

# macOS Apple Silicon
dotnet publish -r osx-arm64 -c Release

# macOS Intel
dotnet publish -r osx-x64 -c Release

# Linux x64
dotnet publish -r linux-x64 -c Release
```

The output is a single `nr` (or `nr.exe`) executable with no external dependencies. Copy it anywhere on your `PATH`.

---

## Setup

### Step 1: Get a Google Cloud OAuth2 credential

1. Go to [Google Cloud Console](https://console.cloud.google.com/) → **APIs & Services** → **Credentials**
2. Create an **OAuth 2.0 Client ID** of type **Desktop app**
3. Download the JSON file — this is your `client_secrets.json`
4. Enable the **Gmail API** for your project under **APIs & Services** → **Enabled APIs**

### Step 2: Place or reference the credential file

Default locations (no flag needed):

| Platform | Path |
|---|---|
| Windows | `%APPDATA%\northernrange\client_secrets.json` |
| macOS / Linux | `~/.config/northernrange/client_secrets.json` |

Or pass it explicitly on any command:

```
nr --credentials /path/to/client_secrets.json auth login
```

### Step 3: Authenticate (one time only)

```
nr auth login
```

This opens your default browser for Google's OAuth2 consent screen. After you approve, a refresh token is stored locally. All subsequent commands use the stored token silently — no browser required again.

---

## Authentication Commands

```
nr auth login [--account <name>] [--force]
nr auth logout [--account <name>]
nr auth status [--account <name>]
```

Multiple Gmail accounts are supported. Use `--account` to target a specific account. Without it, the default account is used (configurable via `defaultAccount` in `config.json` or `NR_ACCOUNT` env var).

#### `auth login`

Opens the OAuth2 browser flow. On headless servers the auth URL is printed to the terminal. The account is auto-created in `config.json` on success.

| Flag | Description |
|---|---|
| `--account <name>` | Account alias (e.g. `work`, `personal`). Default: `"default"`. |
| `--force` | Re-run the browser flow even if a valid token already exists |

```
nr auth login
nr auth login --account work
nr auth login --force
```

#### `auth logout`

Revokes the token with Google and deletes the local token file.

```
nr auth logout
nr auth logout --account work
```

#### `auth status`

Reports authentication state without any network call. Without `--account`, shows all configured accounts.

```
nr auth status
nr auth status --account work
```

```
Account:        default (default)
Authenticated:  True
Email:          user@gmail.com
Token expires:  2026-03-03 00:55:06Z (valid)

Account:        work
Authenticated:  True
Email:          user@company.com
Token expires:  2026-03-03 01:12:00Z (valid)
```

---

## Messages Commands

```
nr messages list   [--label <id>] [--query <q>] [--max <n>] [--page-token <token>] [--format minimal|metadata]
nr messages read   <id>  [--format full|raw|metadata] [--include-headers <names>]
nr messages label  <id>  [--add <label>]... [--remove <label>]...
```

#### `messages list`

| Flag | Short | Default | Description |
|---|---|---|---|
| `--label <id>` | `-l` | `INBOX` | Label ID or name to filter by. System labels: `INBOX`, `SENT`, `SPAM`, `TRASH`, `UNREAD` |
| `--query <q>` | `-q` | — | Gmail search query (same syntax as the Gmail search box) |
| `--max <n>` | `-n` | `25` | Number of messages to return (1–500) |
| `--page-token <token>` | | — | Token from a previous list response for pagination |
| `--format <f>` | | `metadata` | `minimal` = IDs only. `metadata` = adds From, Subject, Date, snippet |

```bash
# Default: 25 messages from INBOX
nr messages list

# Unread messages
nr messages list --query "is:unread"

# Messages with attachments, last 50
nr messages list --query "has:attachment" --max 50

# Search by sender
nr messages list --query "from:alice@example.com" --max 10

# Paginate
nr messages list --max 25
nr messages list --max 25 --page-token <token-from-previous-output>

# Machine-readable output
nr messages list --json
```

JSON output shape:
```json
{
  "messages": [
    {
      "id": "19cb08f9253d9482",
      "threadId": "19cb04f07919b8a7",
      "from": "Alice <alice@example.com>",
      "to": "user@gmail.com",
      "subject": "Meeting tomorrow",
      "date": "2026-03-02T21:58:43+00:00",
      "snippet": "Hi, just confirming our meeting..."
    }
  ],
  "nextPageToken": "07712902107382443779",
  "resultSizeEstimate": 201
}
```

#### `messages read`

| Argument / Flag | Description |
|---|---|
| `<id>` | Gmail message ID (from `messages list`) |
| `--format full` | Default. Decoded body text + attachment list |
| `--format metadata` | Headers only (no body fetch) |
| `--format raw` | Raw RFC 2822 bytes to stdout — pipe to other tools |
| `--include-headers <names>` | Comma-separated header names for `metadata` format |

```bash
# Read a message (full body)
nr messages read 19cb08f9253d9482

# Headers only
nr messages read 19cb08f9253d9482 --format metadata

# Specific headers
nr messages read 19cb08f9253d9482 --format metadata --include-headers "From,Subject,Date"

# Raw RFC 2822 output — pipe to mutt, save to .eml, etc.
nr messages read 19cb08f9253d9482 --format raw > message.eml

# Machine-readable
nr messages read 19cb08f9253d9482 --json
```

#### `messages label`

Add or remove labels on a message. Accepts label IDs or display names. At least one `--add` or `--remove` is required.

| Flag | Short | Description |
|---|---|---|
| `--add <label>` | `-a` | Label to add. Repeat for multiple. |
| `--remove <label>` | `-r` | Label to remove. Repeat for multiple. |

```bash
# Add a label
nr messages label 19cb08f9253d9482 --add "Work/Projects"

# Remove from inbox and add to a custom label
nr messages label 19cb08f9253d9482 --add "Archive" --remove INBOX

# Multiple labels
nr messages label 19cb08f9253d9482 -a "Urgent" -a "Needs Review"
```

---

## Threads Commands

```
nr threads list  [--label <id>] [--query <q>] [--max <n>] [--page-token <token>]
nr threads read  <id>  [--format full|metadata|minimal]
```

Threads group related messages (replies, forwards) together. Flags for `threads list` are identical to `messages list`.

```bash
# List threads
nr threads list

# Search
nr threads list --query "subject:invoice"

# Read all messages in a thread in chronological order
nr threads read 19cb04f07919b8a7

# JSON
nr threads read 19cb04f07919b8a7 --json
```

---

## Labels Commands

```
nr labels list
nr labels info    <id-or-name>
nr labels create  <name>  [--text-color <hex>] [--bg-color <hex>]
nr labels delete  <id-or-name>
```

#### `labels list`

Lists all labels — both Gmail system labels (`INBOX`, `SENT`, `SPAM`, etc.) and any user-created labels.

```bash
nr labels list
nr labels list --json
```

#### `labels info`

Gets detailed information for a single label. Accepts either the label ID or its display name. If a name matches multiple labels, exits with code 2.

```bash
nr labels info INBOX
nr labels info "Work/Projects"
nr labels info Label_18
```

```
ID:               INBOX
Name:             INBOX
Type:             system
Messages Total:   27668
Messages Unread:  21642
Threads Total:    23445
Threads Unread:   19272
```

#### `labels create`

Creates a new user label. Optionally set text and background colors — both must be provided together or neither.

| Flag | Description |
|---|---|
| `--text-color <hex>` | Text color (e.g. `#ffffff`). Requires `--bg-color`. |
| `--bg-color <hex>` | Background color (e.g. `#4986e7`). Requires `--text-color`. |

```bash
nr labels create "Work/Projects"
nr labels create "Urgent" --text-color "#ffffff" --bg-color "#cc3a21"
```

#### `labels delete`

Deletes a user label. Messages with this label are not deleted — only the label is removed. Accepts a label ID or display name.

```bash
nr labels delete "Work/Projects"
nr labels delete Label_18
```

---

## Attachments Commands

```
nr attachments list      <message-id>
nr attachments download  <message-id> <attachment-id>  [--output <path>] [--force]
```

#### `attachments list`

Lists all attachments on a message without downloading them.

```bash
# Find messages with attachments first
nr messages list --query "has:attachment" --max 10

# Then list attachments on a specific message
nr attachments list 19c8fe3345e9052c
```

```
+-------+--------------------------------------------+-----------------+----------+
| Index | Filename                                   | MIME Type       | Size     |
+-------+--------------------------------------------+-----------------+----------+
| 1     | report-2026-Q1.pdf                         | application/pdf | 625.7 KB |
+-------+--------------------------------------------+-----------------+----------+
```

#### `attachments download`

Downloads an attachment by its ID (from `attachments list`).

| Argument / Flag | Description |
|---|---|
| `<message-id>` | Gmail message ID |
| `<attachment-id>` | Attachment ID from `attachments list --json` |
| `--output <path>` | Destination file or directory. Omit to write to current directory |
| `--force` | Overwrite if file already exists |

```bash
# Get attachment IDs in JSON
nr attachments list 19c8fe3345e9052c --json

# Download to current directory
nr attachments download 19c8fe3345e9052c ANGjdJ_PiF0...

# Download to a specific directory
nr attachments download 19c8fe3345e9052c ANGjdJ_PiF0... --output ~/Downloads

# Download to a specific file path
nr attachments download 19c8fe3345e9052c ANGjdJ_PiF0... --output ~/docs/report.pdf

# Overwrite existing file
nr attachments download 19c8fe3345e9052c ANGjdJ_PiF0... --output ~/docs/report.pdf --force
```

---

## Global Flags

These flags are accepted by every command:

| Flag | Short | Description |
|---|---|---|
| `--json` | | Output machine-readable JSON to stdout. Disables all rich rendering. |
| `--ui` | | Enable Spectre.Console rich tables and colors (TTY only, ignored if redirected) |
| `--verbose` | `-v` | Emit debug-level diagnostic output to stderr |
| `--credentials <path>` | | Path to `client_secrets.json` (overrides config and default location) |
| `--config <path>` | | Path to `config.json` (overrides default location) |
| `--account <name>` | | Account name to use. Overrides `NR_ACCOUNT` env and config `defaultAccount`. Default: `"default"` |
| `--log` | | Enable JSONL debug logging to a timestamped file (`nr-YYYYMMDD.jsonl`) in the current directory |
| `--log-flat` | | Enable structured text logging to a timestamped file (`nr-YYYYMMDD.log`) in the current directory |
| `--log-file <path>` | | Write log to this path (appends if exists). Format follows `--log` or `--log-flat` |
| `--log-level <level>` | | Minimum log level: `verbose`, `debug`, `information` (default), `warning`, `error` |

---

## Configuration

The config file is optional. All settings have defaults.

| Platform | Path |
|---|---|
| Windows | `%APPDATA%\northernrange\config.json` |
| macOS / Linux | `~/.config/northernrange/config.json` |

```json
{
  "defaultAccount": "personal",
  "accounts": {
    "personal": {},
    "work": { "credentialsPath": "/path/to/work_client_secrets.json" }
  },
  "defaultLabel": "INBOX",
  "defaultMaxResults": 25,
  "defaultOutputFormat": "text",
  "dateFormat": "iso8601",
  "credentialsPath": null,
  "httpTimeoutSeconds": 30
}
```

| Setting | Default | Description |
|---|---|---|
| `defaultAccount` | `null` → `"default"` | Account used when `--account` is not specified |
| `accounts` | `null` | Named account configurations. Each key is an alias; value may contain `credentialsPath` override |
| `defaultLabel` | `"INBOX"` | Default label for list commands when `--label` is not specified |
| `defaultMaxResults` | `25` | Default `--max` for list commands |
| `defaultOutputFormat` | `"text"` | Set to `"json"` to always output JSON (equivalent to always passing `--json`) |
| `dateFormat` | `"iso8601"` | `"iso8601"` or `"local"` for plain text date display |
| `credentialsPath` | `null` | Absolute path to `client_secrets.json` (global default; per-account overrides in `accounts`) |
| `httpTimeoutSeconds` | `30` | Timeout per HTTP request to the Gmail API |

### Environment variables

| Variable | Equivalent to |
|---|---|
| `NR_ACCOUNT` | `--account` flag |
| `NR_CREDENTIALS` | `--credentials` flag |
| `NR_CONFIG` | `--config` flag |
| `NR_DEFAULT_LABEL` | `defaultLabel` config key |
| `NR_MAX_RESULTS` | `defaultMaxResults` config key |
| `NR_JSON` | `--json` flag (set to `1`) |

---

## Using with AI Agents and Scripts

`nr` is built to be a reliable tool for AI agents that need to read Gmail. The `--json` flag is the integration surface.

**Typical agent workflow:**

```bash
# 1. Check auth state
nr auth status --json
# → {"authenticated":true,"email":"...","tokenExpiry":"...","tokenValid":true}

# 2. List recent messages
nr messages list --max 10 --json
# → {"messages":[...],"nextPageToken":"...","resultSizeEstimate":201}

# 3. Read a specific message
nr messages read 19cb08f9253d9482 --json
# → {"id":"...","headers":{...},"body":{"mimeType":"text/plain","text":"..."},"attachments":[...]}

# 4. Download an attachment
nr attachments download <msg-id> <att-id> --output /tmp/attachment.pdf
echo $?   # 0 = success, 6 = file exists (use --force), 4 = API error, 5 = not found
```

**Pipe-friendly queries:**

```bash
# All unread messages as JSON, paginate until no nextPageToken
nr messages list --query "is:unread" --max 100 --json

# Find messages from a specific sender with attachments
nr messages list --query "from:invoices@vendor.com has:attachment" --json

# Get raw message for piping to another tool
nr messages read <id> --format raw | some-other-tool
```

**Exit codes:**

| Code | Meaning |
|---|---|
| `0` | Success |
| `1` | Unexpected error |
| `2` | Invalid arguments |
| `3` | Not authenticated — run `nr auth login` |
| `4` | Gmail API error |
| `5` | Resource not found (message, thread, label) |
| `6` | File write error (e.g. file exists, use `--force`) |

Errors always go to **stderr** as plain text. **stdout** contains only the command's output (JSON or plain text). This separation means scripts can safely parse stdout without filtering error messages.

---

## Headless / Remote Server Login

`nr auth login` works on headless servers (no GUI/browser). When a browser cannot be opened, the authorization URL is printed to the terminal so you can complete the flow manually:

```
Open this URL in your browser to authenticate:

  https://accounts.google.com/o/oauth2/auth?...&redirect_uri=http://127.0.0.1:12345/authorize/&...

Could not open a browser automatically.
If you are on a remote server, either:
  1. Use SSH port forwarding:  ssh -L 12345:127.0.0.1:12345 yourserver
     then open the URL in your local browser.
  2. Or, run 'nr auth login' on a machine with a browser and copy the
     token file to this machine (see below).
```

**SSH port-forwarding flow:**

1. Note the port number in the printed URL (e.g. `12345`)
2. In a **new** terminal, reconnect with port forwarding: `ssh -L 12345:127.0.0.1:12345 yourserver`
3. Open the URL in your local browser — the OAuth redirect will tunnel back to the server

## Machine-to-Machine Use (No Human Browser Flow)

For fully automated scenarios where `nr auth login` can't be run interactively:

**Personal Gmail:** Complete the browser flow once on any machine, then copy the token directory to the target machine. Each account has its own subdirectory:

| Platform | Token directory |
|---|---|
| Windows | `%APPDATA%\northernrange\tokens\<account>\` |
| macOS / Linux | `~/.config/northernrange/tokens/<account>/` |

For the default account, `<account>` is `default`.

The refresh token is long-lived. It survives indefinitely for Google Cloud projects in production status, and for 6 months of inactivity on projects in testing status.

**Google Workspace:** Use a Service Account with domain-wide delegation — no browser flow ever required. This requires a Workspace organization with admin access.

---

## Data Paths

| Platform | Config | Tokens | Logs |
|---|---|---|---|
| Windows | `%APPDATA%\northernrange\` | `%APPDATA%\northernrange\tokens\<account>\` | `%APPDATA%\northernrange\logs\` |
| macOS / Linux | `~/.config/northernrange/` | `~/.config/northernrange/tokens/<account>/` | `~/.config/northernrange/logs/` |

Each account's tokens are stored in a separate subdirectory (e.g. `tokens/default/`, `tokens/work/`). Existing single-account installs are auto-migrated to `tokens/default/` on first run.

Logs roll daily and are retained for 7 days. No email content is ever written to disk except when explicitly using `attachments download`.

---

## Roadmap

Sending, drafts, and label management are now supported. Planned additions:

- Label rename
- Marking messages read / unread
- Moving and archiving messages
- Forwarding messages

---

## Tech Stack

| | |
|---|---|
| Runtime | .NET 10.0, single-file self-contained binary |
| CLI framework | Cocona 2.2.0 |
| Gmail API | Google.Apis.Gmail.v1 |
| Auth | Google.Apis.Auth (OAuth2 installed app flow) |
| MIME parsing | MimeKit 4.x |
| Rich output | Spectre.Console |
| Logging | Serilog → Microsoft.Extensions.Logging |

---

## About the Name

The name "Northern Range" is a play on [Himalaya](https://github.com/pimalaya/himalaya), a popular CLI email client. It is inspired by the Northern Range — the mountain range found in the northern part of the island of Trinidad in the twin island republic of Trinidad and Tobago, where this tool was developed. 😊
