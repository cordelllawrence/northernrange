# Phase 3 — Command prologue extraction

Date: 2026-10-03.
Scope: close the Phase 2 `--ui` straggler, then do Phase 3's main task — remove
the prologue repetition from the command layer.

## Phase 2 straggler: `--ui` auto-disable on redirected stdout

`Console.IsOutputRedirected` is a BCL property backed by `isatty` on POSIX and
`GetFileType` on Windows, so the auto-disable branch is the same code on both
platforms. The verification gap was that nothing exercised that branch in
tests, which meant any regression would only show up on a real redirect.

`OutputWriter.DetermineMode` now has an `internal` overload that takes the
redirect signal and the stderr sink as parameters; the public one delegates
with `Console.IsOutputRedirected` and `Console.Error`. `OutputWriterModeTests`
feeds both values of the redirect flag and asserts the warning text lands on
stderr, which pins the behaviour on any platform the test suite runs on.

Seven tests added; 180 total pass.

## Phase 3 main: `CommandSession` / `CommandPrelude`

Before: every command method opened with the same four lines.

```csharp
var ctx  = _resolver.Resolve(globals);
var mode = _output.DetermineMode(globals, ctx.Config);
using var scope = _logger.BeginScope(new Dictionary<string, object> { ["Command"] = "<name>" });
var gmail = await _gmailFactory.GetServiceAsync(ctx.CredentialsPath, ctx.TokenStorePath, ctx.Config.HttpTimeoutSeconds);
```

Seven classes, nineteen methods, same prologue each time.

After: one line in, one line to open Gmail.

```csharp
using var session = _prelude.Begin(globals, _logger, "<name>", ("Key", value));
var gmail = await session.GmailAsync();
```

`CommandSession` carries the resolved account, chosen output mode, and a lazy
Gmail client; `Dispose` closes the log scope. `CommandPrelude` owns the two
collaborators (`AccountResolver`, `GmailClientFactory`) plus `OutputWriter` so
commands no longer construct the client or resolve the account directly.

For the one command that cannot pick an account up front, `auth status`
without `--account`, there is `BeginConfigOnly` which returns
`(AppConfig, OutputMode, IDisposable? scope)`. That branch still enumerates
accounts and resolves per iteration via `_prelude.Resolver`.

Secondary cleanups that fell out of the same pass:

- `AuthCommands.StatusAsync` no longer has two copies of the status rendering.
  Both the single-account and multi-account branches call `WriteStatusEntry`.
- Dropped the now-unused `_resolver` and `_gmailFactory` fields and their
  constructor parameters from all seven command classes.

Build is clean; 180 tests still pass with no changes to test code required —
constructor DI is resolved by Cocona, so swapping two injected services for
one made no difference to the test surface.

## Deferred

Still on the Phase 3 list, intentionally left for later:

- `Program.cs` hand-rolls flag pre-scans for `--verbose` / `--json` / `--log*`
  / `--llm*`. Cocona cannot configure Serilog before it parses, so the fix is
  either a shared flag-name constants file or a two-pass build. Either option
  is a bigger surgery than this phase called for.
- `ParamValidation` is still a single-method class.
- `GlobalOptions` positional-record audit for `--llm` reflection.
- Nullable `!` and `?? ""` audit.
- `SendService` owning drafts (rename or split).

## Items marked obsolete

- `Environment.Exit(1)` inside Program.cs's try/finally — the Phase 2 rewrite
  already replaced that with `Environment.ExitCode = …`, so the `finally`
  flush runs. The review-plan entry was stale.
- Serilog redundant expression — Phase 2 replaced the condition. Entry stale.

## New item raised

Documentation reframing (added to Phase 6a): rewrite `README.md` and
`docs/ARCHITECTURE.md` to position `nr` as an open-source CLI that gives local
AI agents and their harnesses direct Gmail access, rather than a general-purpose
Gmail CLI. The `--llm` doc generator, JSON contracts and exit-code contract are
the obvious evidence to lead with.
