# Phase 1 — CLI grammar decisions

Date: 2026-09-16. Applies to 0.2.0 (first breaking release after 0.1.2-beta).
Checklist source: `2026-09-16-review-plan.md`, Phase 1.

Guiding rule: an agent that has seen one command group should be able to
guess the others. Every noun uses the same verbs, every verb takes the same
flags, every flag means one thing.

## Command tree

| Before | After | Why |
| --- | --- | --- |
| `send new` | `messages send` | `send` was the only verb-named group. Composing a message is an action on messages. |
| `send reply <id>` | `messages reply <id>` | Same. |
| `labels info <id>` | `labels show <id>` | `info` appeared nowhere else. |
| `messages label <id>` | unchanged | Reads naturally ("label this message"); no better verb without inventing one. |
| `attachments …` | unchanged | Attachments need a message ID anyway; nesting under `messages` gains nothing. |
| `drafts …` | unchanged | `--draft` on `messages send` / `messages reply` is the only way to create a draft. `drafts` lists, sends, and deletes them. |

Verb rules:

- `list` returns a page of summaries. `read` returns the content of a mail item
  (messages, threads). `show` returns the properties of a non-mail resource
  (labels). `create` / `delete` / `send` / `reply` / `download` are literal.
- Destructive verbs (`labels delete`, `drafts delete`) take no confirmation
  flag. The explicit verb is the consent. This is documented policy for an
  agent-first tool; a human wrapper can add its own prompt.

Deferred, not rejected: `messages archive`, `messages trash`, `messages mark`
(read/unread), `labels rename`, `drafts read`, `auth accounts`. These are
feature work, not review work.

## Flags

| Flag | Decision |
| --- | --- |
| `-a` | Means `--attach` only. `messages label --add` / `--remove` lose their short forms; they were the only `-a` / `-r` in the tool and agents use long flags. |
| `--force` | Kept on both `auth login` and `attachments download`. Shared meaning: proceed even though something already exists (a token, a file). |
| `--format` | Kept as the API-detail flag. Output shape stays on `--json` / `--ui`. Value sets unified below. |
| `-n` / `--max` | Kept. Range is 1–500 on every list command, including `drafts list` (Gmail allows 500 there too). |
| `--log-flat` | Replaced by `--log-format jsonl\|text` (default `jsonl`). `--log` turns file logging on; `--log-file` and `--log-level` unchanged. |
| `--log-level` | Help text now lists `fatal`, which the parser already accepted. |
| `--include-headers` | Accepts both a comma-separated value and repeated flags. |
| `--bg-color` | Kept. |
| `--flag=value` | The pre-parse scan for logging flags now understands the equals form. |

## Values

- `--format` on `messages list` and `threads list`: `metadata` (default), `minimal`.
- `--format` on `messages read`: `full` (default), `metadata`, `minimal`, `raw`.
- `--format` on `threads read`: `full` (default), `metadata`, `minimal`. Gmail has no raw thread fetch.
- All enum-like values are case-insensitive. An unknown value exits 2 with the allowed list in the message.
- `NR_JSON` accepts `1`, `true`, `yes` (case-insensitive).
- Environment twins exist only for settings that choose *what* to operate on (`NR_ACCOUNT`, `NR_CONFIG`, `NR_CREDENTIALS`, `NR_DEFAULT_LABEL`, `NR_MAX_RESULTS`) and for output shape (`NR_JSON`). Diagnostics flags (`--verbose`, `--log*`, `--ui`) have none.

Deferred to Phase 2: making `--to` and `--subject` required at the parser level.
Doing that today would route a missing `--to` through Cocona's parse error,
which exits 129 until Phase 2 fixes parse-error exit codes.

## Text

- Group descriptions list every subcommand.
- Command descriptions: one imperative sentence, then where to get IDs if the command takes one.
- Success lines: `Sent message <id> (thread <tid>).`, `Saved draft <id>.`, `Created label '<name>' (<id>).`, `Deleted label '<name>'.`, `Deleted draft <id>.`, `Downloaded <file> (<size>) to <path>.`
- Error lines: what is wrong, then how to fix it. `No recipient given. Use --to <address>, repeatable.`
