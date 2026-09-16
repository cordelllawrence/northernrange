# northernrange — Code Review

**Scope:** full codebase at 0.1.2-beta (.NET 10). **Reviewer:** automated pass.
**Verdict:** Healthy, well-layered codebase that builds clean with zero warnings.
Findings below are improvements, not blockers. Severity is relative to a
pre-1.0 agent-facing CLI where the `--json` contract and exit codes matter most.

Each finding lists `file:line` and a concrete recommendation.

> **Status: RESOLVED.** All findings below were applied (commit "Apply
> code-review fixes; restore exit-code contract"), and an xUnit suite was added.
> Line numbers refer to the pre-fix code. See the "Resolution" note on each item.

---

## C0 — ErrorHandlingFilter never fired (critical; found while fixing). RESOLVED.

Discovered via smoke testing, not the static pass: `[ErrorHandlingFilter]` was
applied at the **class** level, but Cocona does **not** propagate class-level
filters to the methods of nested `[HasSubCommands]` classes. So the filter never
ran — every `NrException` (validation, auth, API, not-found) escaped as an
**unhandled exception with a stack trace and exit code 1**, defeating both the
centralized error handling and the entire documented exit-code contract (the core
"agent-first" promise). Global DI registration of `ICommandFilter` did not work in
this Cocona version either. *Fix:* moved `[ErrorHandlingFilter]` to every command
method (verified: validation now exits 2 with a clean one-line message). This was
the single highest-impact fix in the pass.

---

## Architecture — assessment

**Strengths (keep doing this):**

- **Clean layering with a one-way dependency flow.** `Commands → Gmail services →
  Models`, with `Auth/Config/Output/Mime/Errors/Filters` as cross-cutting
  utilities. Nothing in `Gmail/` reaches up into `Commands/` or `Output/`. The
  Gmail layer is reusable behind a different front end as-is.
- **Records as the JSON contract.** `Models/*` records serialize straight to the
  documented `--json` shape — one source of truth, no hand-written serializers.
- **Reflection-based self-documentation** (`--llm*`) is a genuine differentiator
  and structurally can't drift from the real command tree.
- **Disciplined stdout/stderr split** and console-sink suppression in `--json`
  mode keep machine output clean.
- **Centralized error handling** (`ErrorHandlingFilter` + `NrException` + typed
  exit codes) and **centralized account resolution** (`AccountResolver`) keep the
  command classes thin and uniform.

**Structural notes (minor):**

- `GmailClientFactory`'s cache + `SemaphoreSlim` is mild over-engineering for a
  process that runs one command and usually builds one client — but it's
  harmless and forward-looking. No action needed.
- Spectre `--ui` mode is only partially honored: tables and key-value blocks
  respect it, but several human paths call `WritePlain`/`WriteDivider` directly
  (message body, thread read, attachment header). It's "best-effort," which is
  fine — just don't advertise it as comprehensive.

---

## Findings

### Correctness

**C1 — `threads list` does not resolve label *names* to IDs. (Medium)**
`Commands/ThreadsCommands.cs:50` passes `effectiveLabel` straight through to
`ThreadService.ListAsync`, which assigns it to `req.LabelIds`
(`Gmail/ThreadService.cs:33`). `messages list` instead resolves names via
`_labelService.GetAsync(...)` (`Commands/MessagesCommands.cs:57`). Result:
`nr threads list -l "Work/Projects"` sends the display name as a label *ID* and
Gmail returns a 400 / empty set, while the identical `messages list` works.
*Fix:* resolve the label in the threads command exactly as messages list does
(or push resolution down into the service for both).

**C2 — `messages read --format raw` is not binary-safe. (Medium)**
`Gmail/MessageService.cs:148-149` decodes the base64url payload to bytes then
`Encoding.UTF8.GetString(...)`, and `Commands/MessagesCommands.cs:174-176` writes
that string via `Console.Write`. The docs promise "raw RFC 2822 bytes to
stdout … pipe to `.eml`", but round-tripping bytes through a UTF-8 string plus
console re-encoding can corrupt any non-UTF8 raw MIME. *Fix:* carry the raw
bytes through and write them to `Console.OpenStandardOutput()` unchanged.

**C3 — Attachment ops don't map 401 → AuthRequired. (Low–Medium)**
`Gmail/AttachmentService.cs:38-42` and `:83-87` inline their `catch` blocks and
omit the `Unauthorized → AuthRequired(3)` case that every other service has. An
expired token during `attachments list/download` exits **4 (ApiError)** instead
of **3 (AuthRequired)**, so an agent won't know to re-auth. *Fix:* route through
the shared mapper (see M1).

**C4 — `labels delete` can't delete an all-caps-named user label. (Low)**
`IsLikelyLabelId` (`Gmail/LabelService.cs:139-141`) treats any all-caps string as
a label ID. `GetAsync` self-heals via a name-resolution fallback
(`:49-58`), but `DeleteAsync` (`:119-137`) has **no** fallback: deleting a user
label literally named `URGENT` tries `Delete("me","URGENT")` → 404. *Fix:* give
delete the same name-resolution fallback, or resolve names up front.

### `--json` contract consistency

**M2 — `drafts delete` ignores `--json`. (Medium)**
`Commands/DraftCommands.cs:99-115` always writes plain text and has no JSON
branch, unlike `labels delete` which emits `{ "deleted": true, "id": … }`
(`Commands/LabelsCommands.cs:155-158`). An agent running
`nr drafts delete <id> --json` gets non-JSON on stdout. *Fix:* mirror
`labels delete` — emit `{ "deleted": true, "draftId": … }` in JSON mode.

**L1 — `drafts list` exposes no pagination. (Low)**
`DraftListResult` carries `NextPageToken` and the service returns it
(`Gmail/SendService.cs:191-194`), but the command (`Commands/DraftCommands.cs:34-71`)
has no `--page-token` option and prints no "Next page:" hint — inconsistent with
messages/threads. Either wire it up or note the limitation.

**L2 — `auth status` calls `Environment.Exit` mid-pipeline. (Low–Medium)**
`Commands/AuthCommands.cs:101` and `:144` exit the process directly, bypassing
Cocona's return-code path **and** the `finally { await Log.CloseAndFlushAsync(); }`
in `Program.cs:136-139`, so buffered log entries can be lost. Every other command
returns normally and lets the filter set the code. *Fix:* return an exit code (or
throw a sentinel) instead of `Environment.Exit`.

### Maintainability / DRY

**M1 — `MapApiException` is duplicated 4× plus one inline copy. (Medium)**
Near-identical copies live in `MessageService.cs:202`, `ThreadService.cs:127`,
`LabelService.cs:160`, `SendService.cs:321`, and inlined in
`AttachmentService.cs`. They have already diverged — `ThreadService` omits the
`Forbidden` case, `SendService` has a send-specific `Forbidden` message, and
`AttachmentService` omits `Unauthorized` (the root of **C3**). *Fix:* extract one
`GmailErrorMapper.Map(ex, notFoundMessage = null)` helper; removes ~60 LOC and
eliminates the drift class of bug.

### Dead / inert code

**D1 — `OutputWriter.WriteError` is never called.** (`Output/OutputWriter.cs:81-84`)
Errors flow through `NrException` + the filter. Remove.

**D2 — `AppPaths.GetUserInfoPath()` is never called.** (`Config/AppPaths.cs:34-35`)
`AuthService` builds the same path inline. Either consume the helper from
`AuthService` (preferable — single source of the path) or delete it.

**D3 — `config.httpTimeoutSeconds` is read but never applied. (Medium)**
`AppConfig.HttpTimeoutSeconds` (`Config/AppConfig.cs:12`) defaults to 30 and is
documented in README / USAGE / the `--llm` docs as effective, but nothing sets it
on any `GmailService` / `HttpClient`. *Fix:* apply it in `GmailClientFactory`
(e.g. `service.HttpClient.Timeout = TimeSpan.FromSeconds(cfg.HttpTimeoutSeconds)`)
or remove it from config and docs. Inert-but-documented config is misleading.

### Resilience

**R1 — No 429/503 retry or backoff. (Medium)**
`2026-03-02-requirements.md §9.4` specifies exponential backoff via Google's
`ConfigurableBackOff`, but no initializer sets a backoff policy and there's no
retry anywhere — under rate limiting the tool fails immediately with
`ApiError(4)`. *Fix:* set `DefaultExponentialBackOffPolicy` on the
`BaseClientService.Initializer` in `GmailClientFactory.cs:41`, or update the docs
to state retries aren't implemented.

**R2 — `messages list --max 500` fans out 500 concurrent `messages.get`. (Low)**
`Gmail/MessageService.cs:63-64` maps every listed ID to a parallel `get`. The
N+1 is inherent to Gmail's list API (it returns only IDs), and parallelism is the
right instinct, but unbounded concurrency at large `--max` can itself trigger 429s
(compounding R1). *Fix:* cap concurrency (e.g. `Parallel.ForEachAsync` with
`MaxDegreeOfParallelism`, or a chunked semaphore).

### Robustness (low)

**R3 — `Console.InputEncoding = Encoding.UTF8` can throw. (Low)**
`Program.cs:17` — setting `InputEncoding` can throw `IOException` when stdin is
redirected/closed in some hosts. `OutputEncoding` is the one that matters for the
contract; wrap the input assignment defensively.

### Documentation drift (now fixed in this pass)

- `2026-03-02-requirements.md` stated scope `gmail.readonly`; actual is `gmail.modify`. ✔ annotated
- `2026-03-02-requirements.md` listed shipped features (send, drafts, labels, multi-account)
  as out-of-scope. ✔ annotated
- `../USAGE.md` exit-code table listed a phantom code `7`; only `6` exists. ✔ fixed
- `README.md` opened by calling the tool "read-only". ✔ fixed
- Minor: `northernrange.csproj` pins `Google.Apis.Gmail.v1` `1.68.0.3399`, while
  `2026-03-02-requirements.md §2` mentions `1.73.x`. Cosmetic; align if desired.

---

## Resolution

All items applied and verified by a clean build (0 warnings) plus an 87-test
xUnit suite:

- **Correctness:** C0 (filter), C1 (thread label resolution), C2 (binary-safe
  raw), C3 (attachment 401 mapping), C4 (all-caps label delete).
- **Consistency/DRY:** M1 (`GmailErrorMapper`), M2 (`drafts delete --json`),
  L1 (`drafts list` paging), L2 (`auth status` exit via pipeline).
- **Dead/inert:** D1, D2 removed; D3 (`httpTimeoutSeconds`) now applied.
- **Resilience:** R1 (503 + exception backoff), R2 (bounded list concurrency),
  R3 (guarded console encoding).

Note R1 covers 503 + transient exceptions; 429 is not specially retried in this
Google.Apis version (no 429 backoff-policy flag). Tracked as a future item.
