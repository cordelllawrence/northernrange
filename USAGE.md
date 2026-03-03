# northernrange usage

A Gmail CLI for scripts, AI agents, and power users.

```
nr <command> [subcommand] [options]
```

---

## Global options

Available on every command.

| Option | Description |
|---|---|
| `--json` | Output machine-readable JSON to stdout. Also enabled by `NR_JSON=1`. |
| `--ui` | Enable Spectre.Console rich rendering (auto-disabled when stdout is redirected). |
| `-v` / `--verbose` | Emit debug diagnostics to stderr. Never affects stdout. |
| `--credentials <path>` | Path to `client_secrets.json`. Overrides config and the default location. |
| `--config <path>` | Path to `config.json`. Overrides the default location. |
| `--log` | Enable JSONL debug logging to a timestamped file (`nr-YYYYMMDD.jsonl`) in the current directory. |
| `--log-flat` | Enable structured text logging to a timestamped file (`nr-YYYYMMDD.log`) in the current directory. |
| `--log-file <path>` | Write log to this path (appends if exists). Format follows `--log` or `--log-flat`. |
| `--log-level <level>` | Minimum log level: `verbose`, `debug`, `information` (default), `warning`, `error`. |

Default credential path: `%APPDATA%\northernrange\client_secrets.json` (Windows) or `~/.config/northernrange/client_secrets.json` (macOS/Linux).

---

## auth

### `nr auth login`

Authenticate with Gmail via the OAuth2 browser flow. Opens your browser to Google's consent screen; the refresh token is stored locally. Run once — all subsequent commands authenticate silently.

Requires a `client_secrets.json` (OAuth2 Desktop client ID, downloaded from Google Cloud Console → APIs & Services → Credentials).

```
nr auth login
nr auth login --credentials ~/secrets/client_secrets.json
nr auth login --force
```

| Option | Description |
|---|---|
| `--force` | Delete the stored token and re-run the full browser consent flow. Use after revoking a token, switching accounts, or changing OAuth scopes. |

### `nr auth logout`

Revoke the OAuth2 token with Google and delete it locally. After logout, all Gmail commands exit with code 3.

```
nr auth logout
```

### `nr auth status`

Show authentication state: account email, token expiry, and validity. Reads the local token store only — no network request. Exit code 0 if authenticated, 3 if not.

```
nr auth status
nr auth status --json
```

---

## messages

### `nr messages list`

List messages from the mailbox. Returns ID, From, Subject, Date, and snippet. When more results exist, prints a `Next page:` hint with the exact command to continue.

```
nr messages list
nr messages list -l UNREAD -n 50
nr messages list -q "from:alice@example.com is:unread"
nr messages list -q "has:attachment" --json
nr messages list --page-token 07712902107382443779
```

| Option | Description |
|---|---|
| `-l` / `--label` | Filter by label ID or display name. Default: `INBOX`. System labels: `INBOX`, `SENT`, `SPAM`, `TRASH`, `UNREAD`, `STARRED`, `DRAFT`, `IMPORTANT`. Get user label IDs from `nr labels list`. |
| `-q` / `--query` | Gmail search query — same syntax as the Gmail search box. Passed unmodified to the API. |
| `-n` / `--max` | Max messages to return (1–500). Default: 25. Also set via `defaultMaxResults` in config or `NR_MAX_RESULTS`. |
| `--page-token` | Pagination token from a previous list response. |
| `--format` | API response format. `metadata` (default): headers + snippet. `minimal`: IDs only, fastest. |

### `nr messages read <id>`

Read a single message. Decodes the body (plain text preferred over HTML) and lists attachments. Get IDs from `nr messages list`.

```
nr messages read 19cb08f9253d9482
nr messages read <id> --json
nr messages read <id> --format metadata
nr messages read <id> --format raw > message.eml
```

| Option | Description |
|---|---|
| `--format` | `full` (default): decoded body and attachments. `metadata`: headers only, no body. `raw`: original RFC 2822 bytes written to stdout. |
| `--include-headers` | Comma-separated headers to include with `--format metadata`. Default: `From,To,Cc,Subject,Date,Message-ID`. Header names are case-insensitive. |

---

## threads

### `nr threads list`

List email threads. A thread groups an original message with all its replies. Supports the same `--label`, `--query`, `--max`, and `--page-token` flags as `nr messages list`.

```
nr threads list
nr threads list -q "from:boss@company.com" -n 10
nr threads list --json
```

| Option | Description |
|---|---|
| `-l` / `--label` | Filter by label. Default: `INBOX`. |
| `-q` / `--query` | Gmail search query. |
| `-n` / `--max` | Max threads to return (1–500). Default: 25. |
| `--page-token` | Pagination token from a previous list response. |

### `nr threads read <id>`

Read all messages in a thread in chronological order. Shows From, Date, and decoded body for each message. Get IDs from `nr threads list`.

```
nr threads read 19cb04f07919b8a7
nr threads read <id> --json
nr threads read <id> --format metadata
```

| Option | Description |
|---|---|
| `--format` | `full` (default): body text for each message. `metadata`: headers only. `minimal`: IDs only. |

---

## labels

### `nr labels list`

List all labels — both Gmail system labels (`INBOX`, `SENT`, `SPAM`, `TRASH`, `UNREAD`, `STARRED`, `DRAFT`, `IMPORTANT`, `CATEGORY_*`) and user-created labels. Use the ID or name with `--label` in list commands.

```
nr labels list
nr labels list --json
```

### `nr labels info <id>`

Show details for a single label: message counts, thread counts, unread counts, and color (user labels). Accepts either a label ID or display name. Exits with code 2 if a display name matches more than one label.

```
nr labels info INBOX
nr labels info "Financial Updates"
nr labels info Label_18 --json
```

---

## attachments

### `nr attachments list <message-id>`

List attachments in a message without downloading them. Shows filename, MIME type, and size. Use `--json` to get attachment IDs for `nr attachments download`. Find messages with `nr messages list -q "has:attachment"`.

```
nr attachments list 19c8fe3345e9052c
nr attachments list <message-id> --json
```

### `nr attachments download <message-id> <attachment-id>`

Download an attachment to disk. Without `--output`, uses the original filename in the current directory. Without `--force`, exits with code 6 if the file already exists. Get the attachment ID from `nr attachments list <id> --json`.

```
nr attachments download <msg-id> <att-id>
nr attachments download <msg-id> <att-id> -o ~/Downloads
nr attachments download <msg-id> <att-id> -o ~/docs/report.pdf --force
```

| Option | Description |
|---|---|
| `-o` / `--output` | Destination file or directory. If a directory, the original filename is used inside it. |
| `--force` | Overwrite the output file if it already exists. |

---

## send

### `nr send new`

Compose and send a new email. Body text comes from `--body`, `--body-file`, or stdin (in that order). Use `--draft` to save instead of sending.

```
nr send new -t alice@example.com -s "Hello" --body "Hi there"
nr send new -t alice@example.com -t bob@example.com -s "Report" --body-file report.txt --attach report.pdf
echo "Body text" | nr send new -t alice@example.com -s "Piped body"
nr send new -t self@example.com -s "Draft" --body "WIP" --draft
```

| Option | Description |
|---|---|
| `-t` / `--to` | Recipient address. Repeat for multiple: `-t a@b.com -t c@d.com`. Required. |
| `-c` / `--cc` | CC address. Repeat for multiple. |
| `--bcc` | BCC address. Repeat for multiple. |
| `-s` / `--subject` | Subject line. Required. |
| `--body` | Body text inline. Falls back to `--body-file` then stdin if omitted. |
| `--body-file` | Path to a plain-text file whose contents become the body. |
| `-a` / `--attach` | Path to a local file to attach. Repeat for multiple. |
| `--draft` | Save as a draft instead of sending. |

### `nr send reply <message-id>`

Reply to an existing message. Subject and threading headers (`In-Reply-To`, `References`) are set automatically. Body from `--body`, `--body-file`, or stdin. Use `--draft` to save instead of sending. Get message IDs from `nr messages list`.

```
nr send reply 19cb08f9253d9482 --body "Thanks, sounds good."
nr send reply <id> --reply-all --body "See attached" --attach report.pdf
nr send reply <id> --body "WIP reply" --draft
```

| Option | Description |
|---|---|
| `--body` | Reply body text. Falls back to `--body-file` then stdin if omitted. |
| `--body-file` | Path to a plain-text file whose contents become the reply body. |
| `-a` / `--attach` | Path to a local file to attach. Repeat for multiple. |
| `--reply-all` | CC all original recipients (To + Cc) in addition to the sender. |
| `--draft` | Save as a draft instead of sending. |

---

## drafts

### `nr drafts list`

List saved drafts sorted newest-first. Shows Draft ID, date, recipient, and subject.

```
nr drafts list
nr drafts list -n 10
nr drafts list --json
```

| Option | Description |
|---|---|
| `-n` / `--max` | Max drafts to return (1–100). Default: 25. |

### `nr drafts send <draft-id>`

Send an existing draft immediately. The draft is removed from Drafts after sending. Get draft IDs from `nr drafts list --json` (`draftId` field).

```
nr drafts send r8234567890123456
nr drafts send <draft-id> --json
```

### `nr drafts delete <draft-id>`

Permanently delete a draft. Cannot be undone. Get draft IDs from `nr drafts list --json`.

```
nr drafts delete r8234567890123456
```

---

## Exit codes

| Code | Meaning |
|---|---|
| 0 | Success |
| 1 | General error |
| 2 | Invalid arguments |
| 3 | Authentication required (`nr auth login`) |
| 4 | API error |
| 5 | Not found |
| 6 | File conflict (use `--force` to overwrite) |
| 7 | File error (missing path, unreadable file) |
