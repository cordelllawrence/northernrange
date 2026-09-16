# northernrange — Review Sweep Plan

Full-codebase review at 0.1.2-beta, organised as phases from the outside in:
the CLI surface an agent sees first, then the contracts behind it, then the code
and architecture that implement them, then the project scaffolding around it.

Each phase has a checklist. Items marked **(seed)** are concrete observations
already made during the survey; the rest are areas to examine. Findings from each
phase go into a dated `docs/journal/YYYY-MM-DD-phase-N-findings.md`, and fixes land as one
commit per phase so the diff stays reviewable.

Baseline at start of sweep (2026-09-16):

- `main` is 5 commits ahead of `origin/main`, nothing to pull. Working tree clean.
- Build: 0 warnings. Tests: 87 passed.
- No secrets or binaries tracked. `client_secret*.json` sit untracked in the repo root.

---

## Phase 1 — CLI language: verbs, nouns, flags, values

Decisions and outcomes: `2026-09-16-phase-1-cli-grammar.md`. The one open item
(`--to` / `--subject` required at the parser) moved to Phase 2.

Goal: a consistent, predictable command grammar. This is the surface agents
learn from `--llm`, so every inconsistency here is a prompt-engineering tax.

### 1a. Command tree (nouns and verbs)

- [x] **(seed)** Group names are nouns except `send`, which is a verb. `send new` and
      `send reply` sit oddly beside `messages`, `threads`, `drafts`. Decide: keep
      `send` as a verb group, or move to `messages send` / `messages reply`, or a
      `compose` noun.
- [x] **(seed)** Draft creation lives in two places: `send new --draft` and the
      `drafts` group. Decide whether `drafts create` / `drafts reply` should exist,
      or whether `--draft` on `send` is the only way.
- [x] **(seed)** Single-resource fetch verb varies: `messages read`, `threads read`,
      `labels info`. Pick one (`read`, `show`, or `get`) across all groups.
- [x] **(seed)** `messages label <id>` uses a noun as a verb. Compare with
      `messages modify`, `messages tag`, or `labels apply`.
- [x] Missing verbs worth a deliberate yes/no: `messages archive`, `messages trash`,
      `messages mark-read` / `--unread`, `labels rename` / `labels update`,
      `drafts read`, `threads label`, `auth accounts` (list accounts).
- [x] Is `attachments` a top-level noun, or does it belong under `messages` (an
      attachment has no identity without a message ID)?
- [x] Deletion verbs: `labels delete`, `drafts delete`. Should destructive verbs take
      `--yes` / `--confirm` for human use, or is "explicit verb is the consent" the
      documented policy for an agent-first tool? Write the policy down either way.

### 1b. Short flags and long-flag names

- [x] **(seed)** `-a` means `--add` in `messages label` and `--attach` in `send new`
      / `send reply`. One short flag, two meanings.
- [x] **(seed)** `--force` means "overwrite the file" in `attachments download` and
      "discard the token and re-consent" in `auth login`.
- [x] **(seed)** `--format` means "API response shape" on read commands, while output
      shape is controlled by `--json` / `--ui` / `defaultOutputFormat`. The word
      "format" is overloaded across the flag and the config key.
- [x] **(seed)** `-n` / `--max` allows 1–500 on messages and threads but 1–100 on drafts.
      Document why or unify. Consider `--limit` as the conventional name.
- [x] **(seed)** `--log-flat` is an odd name. Consider `--log-format jsonl|text`.
- [x] Short-flag inventory across all commands: `-a -c -l -n -o -q -r -s -t -v`.
      Confirm no clashes with global options and that every common repeat flag has one.
- [x] Long-flag style: all kebab-case? (`--page-token`, `--body-file`,
      `--include-headers`, `--reply-all`, `--text-color`, `--bg-color`). Check `bg`
      vs `background`.
- [x] Positional vs option: `attachments download <msg> <att>` vs everything else
      taking a single positional. Fine, but confirm the rule and state it.

### 1c. Flag values and enums

- [x] **(seed)** `--format` value sets differ per command: messages list
      `metadata|minimal`, messages read `full|metadata|raw`, threads read
      `full|metadata|minimal`. No `raw` for threads, no `minimal` for messages read.
      Decide the canonical set and which commands support which.
- [x] **(seed)** `--include-headers` is documented as comma-separated in `docs/USAGE.md`
      but implemented as a repeatable option. Pick one, or support both.
- [x] **(seed)** `--to` and `--subject` are documented "Required" but declared optional
      with manual validation, so `--help` and the `--llm` schema show them as
      optional. Make Cocona enforce it so the generated docs are truthful.
      (Done in Phase 2 once binder errors mapped to exit 2.)
- [x] Case sensitivity of enum-like values (`--format RAW`, `--log-level DEBUG`).
- [x] `--log-level` accepts `fatal` in code but the help text does not list it.
- [x] `NR_JSON` only accepts `1`. Accept `true`/`yes` too, or document strictly.
- [x] Environment-variable coverage: `NR_ACCOUNT NR_CREDENTIALS NR_CONFIG
      NR_DEFAULT_LABEL NR_MAX_RESULTS NR_JSON`. No env for `--verbose`, `--ui`,
      `--log*`. Decide the rule for which flags get an env twin.

### 1d. Help and description text

- [x] **(seed)** Group descriptions in `NorthernRangeApp` are stale: "messages (list,
      read)" omits `label`; "labels (list, info)" omits `create`, `delete`.
- [x] Every `[Command]` and `[Option]` description: same voice, same tense, same
      punctuation, mentions where to get IDs, mentions defaults.
- [x] Success messages: "Sent.  Message-ID: …" (double space), "Deleted label 'id'."
      echoes the raw input rather than the resolved name. Standardise the sentence
      shape for create / delete / send / download.
- [x] Error-message style: "Provide at least one --add or --remove label." vs "At
      least one --to / -t recipient is required." Pick one pattern.

---

## Phase 1½ — Pagination (targeted, do before Phase 2)

Goal: paging through a mailbox works the same in text and JSON mode, and it is
covered by tests that run without a network.

Reproduced on 2026-09-16 against a live mailbox (read-only). **Done 2026-09-16**
(commit "Phase 1½: fix pagination"): `NextPageHint` rebuilds the command from
the flags actually given; empty pages still print the hint; drafts keep Gmail's
order; invisible characters are stripped from table cells. Services are now
testable through `tests/Fakes/FakeGmail.cs` (in-memory HTTP behind the real
Google client).

- [x] **(seed, confirmed)** The "Next page:" hint drops every other flag. After
      `nr messages list -q "in:anywhere" -n 2`, the hint is
      `nr messages list --page-token <tok>`. Gmail does **not** reject the token; it
      continues from the cursor but under the *new* filter (default label, no query,
      default page size). Page 2 came back with 25 INBOX rows instead of 2 query
      rows. Every subsequent hint drifts further. Same defect in `threads list` and
      `drafts list`. Fix: the hint must echo `--label`, `--query`, `--max`,
      `--format`, and `--account` exactly as given, or the JSON result must carry a
      ready-made `nextCommand` field and the hint print that.
- [x] **(seed)** An empty page with a `nextPageToken` ends paging silently in text
      mode. All three list commands print "No … found." and return before the hint
      line. Gmail does return empty pages with a token when a label filter and a
      query are combined. JSON mode is unaffected. Fix: print the hint whenever a
      token is present, and say "No results on this page" rather than "No messages
      found".
- [x] **(seed)** `drafts list` sorts each page by date descending *within the page*.
      Across pages the order is Gmail's, so page boundaries can interleave. Either
      drop the client-side sort or document it as per-page.
- [x] **(seed)** Snippets containing zero-width and format characters (U+034F,
      U+200C, U+FEFF, seen in marketing mail) break table column alignment. Strip
      Unicode categories Cf and Mn before truncation in `PlainTextRenderer`.
- [x] Token round-trip: tokens are 20-digit strings. Cocona keeps them as strings,
      and `--page-token=<tok>` (equals form) works (verified live).
- [x] `--max` above Gmail's page cap: rejected at 501 with exit 2 before any
      request; Gmail's own cap is also 500. `resultSizeEstimate` is passed through
      unchanged.

Test approach (no interfaces needed): construct `GmailService` with a
`BaseClientService.Initializer` whose `HttpClientFactory` returns a fake
`HttpMessageHandler`. Canned JSON responses drive `ListAsync`; assertions check
the outgoing query string (`pageToken`, `labelIds`, `q`, `maxResults`) and the
returned `NextPageToken`. This is the standard way to unit test Google.Apis
clients and unlocks tests for every service in Phase 4.

---

## Phase 2 — Contracts: exit codes, JSON shape, stdout/stderr

**Done 2026-09-16** (commit "Phase 2: exit-code and JSON contract"). Decisions and
outcomes: `2026-09-16-phase-2-contracts.md`. Also found and fixed while here: global
options were only accepted *after* the subcommand, and `--help` exited 129. The
`--to` / `--subject` item deferred from Phase 1 is done (required at the parser).
Still open: `--ui` auto-disable on redirected stdout is verified on Windows only.

Goal: the promises `README.md` makes to agents are true in every path, including
the paths Cocona owns before our code runs.

### 2a. Exit codes

- [x] **(seed)** Unknown option exits **129**, unknown command exits **1**, and a
      non-numeric `--max` exits **1** with the message
      `Option 'max' requires Nullable\`1 value`. The documented contract is **2** for
      invalid arguments. These are Cocona's parse errors, thrown before
      `ErrorHandlingFilter` runs. Fix at the host level (custom parse-error handling
      or a top-level wrapper).
- [x] **(seed)** `--log-file=path` (equals syntax) exits 129 because the hand-rolled
      pre-scan in `Program.cs` only understands space-separated form.
- [x] `auth status` returns exit 3 when no account is authenticated. That uses an
      error code for a successful query. Decide if that is intended (grep-friendly
      for scripts) and document it as a deliberate exception.
- [x] `Ctrl+C` / `OperationCanceledException`: what code does the process exit with?
- [x] Every `NrException` site: is the chosen code right? Build a table of
      (command, failure, expected code) and turn it into tests.

### 2b. JSON output contract

- [x] **(seed)** Errors in `--json` mode are plain text on stderr. Agents may want a
      JSON error envelope (`{ "error": { "code": 3, "message": … } }`). Decide and
      document.
- [x] Every command has a JSON branch; confirm every branch emits exactly one JSON
      document and nothing else on stdout (no "Next page:" hints, no blank lines).
- [x] Field naming: camelCase everywhere, no nulls vs missing ambiguity (`Never`
      ignore condition is set; confirm that is what agents want).
- [x] Date/time fields: ISO-8601 with offset everywhere. Check `Date` in list results
      vs `TokenExpiry`.
- [x] Anonymous objects (`new { deleted = true, id }`) used for delete results while
      everything else uses records. Records give the `--llm` schema generator
      something to reflect on; anonymous types do not.
- [x] **(seed)** The `--llm` response-type map in `LlmDocGenerator` is hand-maintained
      and already omits `labels delete` and `drafts delete`. Either derive it from an
      attribute on each command or add a test that every command has an entry.
- [x] `--llm --json` schema versus real output: add a test that serialises a sample
      of each result record and validates it against the generated schema.

### 2c. stdout / stderr / logging discipline

- [x] **(seed)** A file log is written to `%APPDATA%\northernrange\logs` on **every**
      run, including `--help`, at Information level. `README.md` says "No local state"
      and lists only token, profile, and explicit log files. Either stop the
      always-on log, make it opt-in, or document it.
- [x] **(seed)** Information-level log lines include query strings, subjects, label
      names, recipients, and email addresses. That is PII written to disk by default.
      Review what belongs at Information vs Debug.
- [x] `--verbose` output: confirm nothing secret (token, auth code, client secret) is
      ever logged at any level.
- [ ] Redirected stdout with `--ui`: confirm auto-disable actually works on Windows
      and Linux.
- [x] `--ui` is "best-effort" (`WritePlain` and `WriteDivider` ignore mode). Either
      finish it or document it as partial in `--help`.

---

## Phase 3 — Code quality: commands and cross-cutting

Goal: remove repetition, tighten the seams, make the command layer boring.

- [ ] **(seed)** Every command method repeats the same prologue: resolve account,
      determine output mode, open a log scope, build the Gmail client. Seven classes,
      ~20 methods. Extract a `CommandContext` / base helper so a command is only its
      own logic.
- [ ] **(seed)** `Program.cs` pre-scans raw `args` for `--verbose`, `--json`, `--log*`
      and `--llm*` by hand, duplicating what `GlobalOptions` declares. Two sources of
      truth for flag names. Find a way to configure Serilog after Cocona parses, or
      centralise the flag names.
- [ ] `Program.cs` catch block calls `Environment.Exit(1)` inside a `try` whose
      `finally` flushes logs. Verify flush still happens; return the code instead.
- [ ] Serilog config expression `isLogFlat || (logFile is not null && isLogFlat)` is
      redundant; simplify and add a table of (flags → sinks) as a comment or test.
- [ ] `ParamValidation` has one method. Either grow it (email format, hex colour,
      label name rules) or inline it.
- [ ] `GlobalOptions` is a positional record with ten parameters. Fine for Cocona,
      but check the `--llm` generator's reliance on `GetConstructors()[0]`.
- [ ] `AuthCommands.StatusAsync` duplicates the key-value rendering in two places.
- [ ] `AuthCommands.GetAllAccountNames` discovers accounts from token directories.
      Confirm that is intended behaviour and covered by a test.
- [ ] Nullable annotations: any `!` suppressions or `?? ""` that hide real nulls.
- [ ] Naming: `_gmailFactory` vs `GmailClientFactory`, `SendService` also owns
      drafts; consider `DraftService` split or rename.

---

## Phase 4 — Code quality: services, MIME, auth, config

Goal: the Gmail layer is correct, safe, and testable without a network.

### 4a. Gmail services

- [ ] Services are concrete classes taking a `GmailService` per call. No interface,
      so nothing at the command layer can be unit-tested. Decide: introduce
      `IGmailGateway`-style interfaces, or accept command-layer tests as integration
      only.
- [ ] `GmailClientFactory` cache + semaphore: keep or simplify (previous review said
      harmless; revisit once Phase 3 settles who owns the client).
- [ ] 429 retry still not handled (known). Check whether the current Google.Apis
      version has a usable hook before deciding.
- [ ] `LabelService.IsLikelyLabelId` heuristic: list the cases and test them
      (`INBOX`, `Label_18`, `URGENT`, `CATEGORY_PROMOTIONS`, mixed-case user labels).
- [ ] `MessageService` bounded concurrency: what is the cap, is it configurable,
      is it documented?
- [ ] Every `catch` routes through `GmailErrorMapper`; grep for stragglers.

### 4b. MIME, send, reply

- [ ] `MimeParser` body selection (plain over HTML) with nested multipart,
      `multipart/related`, inline images, and no-text-part messages.
- [ ] `SendService` reply headers: `In-Reply-To`, `References` chaining when the
      original already has `References`; `Reply-To` header respected; reply-all
      excludes self.
- [ ] Attachment size limits (Gmail 25 MB) and MIME type detection for `--attach`.
- [ ] Body encoding: 8-bit vs quoted-printable, CRLF normalisation, BOM stripping
      when body comes from `--body-file`.
- [ ] Address validation: what happens with `--to "not an email"`?

### 4c. Auth

- [ ] `ConsoleCodeReceiver`: headless flow prints a `127.0.0.1` redirect URL. That
      cannot complete on a remote server without port forwarding. Verify the docs
      match reality, or add a manual-paste-the-code path.
- [ ] Token file permissions: `SetUnixFileMode` is applied to the directory. Confirm
      the token file itself and `user_info.json` get 600, and what happens on Windows.
- [ ] `logout` when the network is down: does the local token still get deleted,
      and is the message truthful?
- [ ] Scope: `gmail.modify`. Is that the minimum for what ships? Note it in the docs.
- [ ] Refresh-token expiry / revoked-consent path: does it map to exit 3 with a
      "run nr auth login" hint?

### 4d. Config

- [ ] `ConfigPersister.Save` rewrites the user's `config.json` on login. Does it
      preserve unknown keys and formatting? Round-trip test.
- [ ] Precedence table (flag → env → config → default) for every setting, verified
      by `ConfigTests`.
- [ ] `AppPaths` on macOS: `~/.config` vs `~/Library/Application Support`. Pick and
      document.
- [ ] `defaultOutputFormat` accepts what values? Validate at load.

---

## Phase 5 — Architecture

Goal: confirm the layering still holds and decide the two or three structural
questions that affect everything else.

- [ ] Re-verify the one-way dependency flow (`Commands → Gmail → Models`) with a
      quick grep; note any new leak.
- [ ] Output-mode handling: `OutputMode` is threaded through some writers and not
      others. Decide whether `OutputWriter` should own the mode (set once) instead of
      every call passing it.
- [ ] Is `LlmDocGenerator` (529 lines, the largest file) the right shape? Consider
      splitting reflection (command-tree model) from rendering (Markdown / JSON).
- [ ] Trim safety: `PublishTrimmed` with reflection-based doc generation and
      `System.Text.Json` reflection serialisation. `IL2026`/`IL2104` are suppressed.
      Tests run untrimmed. Add a smoke test that runs the **published** binary for
      `--llm --json` and one `--json` command.
- [ ] Test architecture: 87 tests cover pure helpers only. Decide on a layer of
      end-to-end tests that execute the built binary and assert exit codes, stdout
      JSON validity, and stderr emptiness. This is the layer that would have caught
      Phase 2's exit-code findings.
- [ ] Provider abstraction: see Appendix A. If a second mail backend is wanted,
      the `Gmail/` layer needs an interface boundary and the `Models/` records need
      provider-neutral names. Decide before Phase 3 so the command prologue refactor
      targets the right seam.

### Test currency audit (run at the start of Phase 5, reuse in every phase)

Current coverage, by source file, from the 87 tests:

| Area | Covered | Not covered |
| --- | --- | --- |
| `Mime/MimeParser` | body selection, headers, attachments, base64url, dates | `multipart/related`, inline images, no-text-part |
| `Output/PlainTextRenderer` | table, truncate, date, display width | zero-width format chars (see Phase 1½) |
| `Output/Llm*Generator` | markdown, filter, JSON schema | schema vs real output equivalence |
| `Config/*` | loader, env precedence, resolver, persister | `Save` round-trip preserving unknown keys, macOS paths |
| `Gmail/GmailErrorMapper` | mapping | — |
| `Gmail/SendService` | `BuildReplyFields`, `ListDraftsAsync` paging | `SendNewAsync`, MIME building, attachments |
| `Gmail/MessageService` | `ListAsync` paging and formats | `GetAsync` formats, `GetRawAsync`, `ModifyLabelsAsync`, concurrency cap |
| `Gmail/ThreadService` | `ListAsync` paging | `GetAsync` |
| `Gmail/LabelService` | none | `IsLikelyLabelId`, name resolution, ambiguous names, delete fallback |
| `Gmail/AttachmentService` | none | output path rules, `--force`, directory vs file, exit 6 paths |
| `Gmail/GmailClientFactory` | none | timeout applied, backoff policy set |
| `Auth/*` | none | status from token store, logout with network down, headless URL |
| `Commands/*` | none | every command's JSON branch, exit codes, "Next page" hint |
| `Filters/ErrorHandlingFilter` | none | `NrException` → code, unexpected → 1, cancellation |
| `Output/OutputWriter`, `JsonlLogFormatter`, `NrHelpRenderer` | none | mode detection, redirected stdout, JSON options |
| `Program.cs` | none | flag pre-scan, log sink selection, parse-error exit codes |

Rule for the sweep: every fix in Phases 1½ through 4 lands with a test in the
row it belongs to, and every "(seed, confirmed)" item gets a regression test
before the fix so the test is seen to fail first.

- [ ] Extensibility: adding a new command today touches the command class, the
      `NorthernRangeApp` attribute, the `LlmDocGenerator` map, `docs/USAGE.md`, and
      `README.md`. Reduce that to one or two places.

---

## Phase 6 — Docs, dependencies, repo hygiene, CI

Goal: everything around the code is current and single-sourced.

### 6a. Documentation

- [ ] `docs/USAGE.md` is hand-written and already drifts from the code (`--include-headers`).
      Generate it from `nr --llm-full`, or add a test that diffs them.
- [ ] `README.md`, `docs/USAGE.md`, `docs/ARCHITECTURE.md`, and the journal reviews overlap. Define
      what each one is for and remove duplication (exit-code table appears in three).
- [x] **(seed)** `requirements.md` and `llm-documentation-plan.md` were gitignored yet
      referenced from tracked docs. Moved to `docs/journal/` and tracked, 2026-09-16.
- [x] Docs layout: durable docs in `docs/`, dated plans and reviews in `docs/journal/`.
      Done 2026-09-16. Only `README.md` remains in the root.
- [ ] Add `CHANGELOG.md`; the version is 0.1.2-beta with no history.
- [ ] `docs/ARCHITECTURE.md` status note: still accurate after this sweep?

### 6b. Dependencies

- [ ] **(seed)** All eight packages have newer versions. Notably Google.Apis 1.68 →
      1.76, Spectre.Console 0.49 → 0.57, Serilog.Extensions.Logging 8 → 10. No known
      vulnerabilities. Upgrade in one commit, rerun tests and a manual smoke.
- [ ] Cocona 2.2.0 has had no release since 2023. Note the risk; no action unless it
      blocks the Phase 2 exit-code fix.
- [ ] `.NET 10` target: confirm the CI image and local SDK match.

### 6c. Repo hygiene and CI

- [ ] **(seed)** `client_Secrets.json` and `client_secret_northernrange.json` live in
      the repo root. They are ignored, but one wrong `git add -f` away from a leak.
      Move them to the config directory the tool already looks in.
- [ ] `.claude/settings.json` allow-list references `d:/dev2/…` paths. Stale.
- [ ] **(seed)** No CI on push or pull request. `release.yml` only builds on tags, and
      never runs the tests. Add `ci.yml`: build, test, `dotnet format --verify-no-changes`.
- [ ] `build.sh` vs `release.yml`: two publish recipes. Make one call the other or
      document which is canonical.
- [ ] `.gitattributes` and line endings on Windows-authored files.
- [ ] Push the 5 local commits once the baseline is agreed.

---

## Order and sizing

| Phase | Focus | Expected output |
| --- | --- | --- |
| 1 | CLI language | Decision list + renames (breaking, so do it now while beta) |
| 2 | Contracts | Exit-code and JSON fixes + a contract test suite |
| 3 | Commands / cross-cutting | Prologue extraction, `Program.cs` cleanup |
| 4 | Services / MIME / auth / config | Correctness fixes + unit tests |
| 5 | Architecture | Decisions on interfaces, output mode, e2e tests, trim smoke |
| 6 | Docs / deps / CI | Generated usage, upgrades, `ci.yml`, hygiene |

Phases 1 and 2 first because they change the public surface; everything after
should be built on the final grammar. Phase 5 decisions can be made early but
applied last so they do not churn Phases 3 and 4.

---

## Appendix A — Can `nr` work with Outlook?

Short answer: not today, and not through POP3. Yes with a moderate amount of
work through Microsoft Graph or IMAP.

### Where the code stands

The Gmail dependency is not isolated. Every service method takes a
`GmailService`, `Models/*` mirror Gmail concepts (labels, threads, attachment
IDs, page tokens), auth is Google OAuth only, and the account config has no
notion of a provider. Adding a second backend means introducing a provider
interface and a provider field per account first.

### Options

| Route | Fit | Notes |
| --- | --- | --- |
| **Microsoft Graph (Mail API)** | Best for Outlook | Direct analogue of the Gmail API. OAuth2 via MSAL and an Azure app registration (same shape as `client_secrets.json`). Folders and categories instead of labels, conversations instead of threads, `@odata.nextLink` instead of page tokens. Search, send, reply, drafts, attachments all present. Works for outlook.com and Microsoft 365. |
| **IMAP + SMTP via MailKit** | Best for "any provider" | MailKit is the sibling of MimeKit, already a dependency. Covers Outlook, Microsoft 365, Gmail, Fastmail, self-hosted. Microsoft now requires OAuth2 for IMAP, so auth is still an app registration. No server-side threads (rebuild from `References`), no page tokens (page by UID ranges), folders not labels, drafts via append to the Drafts folder, send over SMTP. |
| **POP3** | Poor | Download-only. No folders, no search, no labels, no threads, no send, no drafts. Could back `messages list` and `messages read` and nothing else. Not worth a provider slot. |

### What it would take

1. Define `IMailProvider` (or one interface per resource: messages, threads,
   labels, drafts, send, attachments) with the current `Models/*` records as
   the return types. The Gmail services become the first implementation.
2. Make the model vocabulary provider-neutral where it leaks Gmail: `label`
   becomes `label` for Gmail and `folder`/`category` for Outlook, or the CLI
   keeps "label" as its own word and each provider maps it.
3. Add `provider: gmail | graph | imap` to each account in `config.json` and
   route `auth login` to the matching flow.
4. Implement the second provider. A Graph backend is roughly the size of the
   current `Gmail/` plus `Auth/` layers, about 1,100 lines, plus tests.

Recommendation: if Outlook specifically is the goal, Graph. If the goal is
"works with whatever mailbox I have", IMAP via MailKit, accepting weaker
threading and paging. Either way, decide before Phase 3 so the command-layer
refactor builds on the provider seam instead of on `GmailService`.
