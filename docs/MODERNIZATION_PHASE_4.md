# Phase 4 progress: nullable analysis and resource ownership

Status: the utility/CLI and ownership batch and the grammar-model, semantic, and
lookahead batch are implemented. Phase 4 remains in progress until the generation
engine and shared state have been migrated and nullable analysis can be enabled
across the core project with an explicit bootstrap-source policy.

## Nullable contracts

Nullable analysis is enabled for all three utility files (`OutputFile`,
`CSharpFileGenerator`, and `ListUtil`), `Options`, `CSharpCCErrors`, `CSharpFiles`,
and the CLI. These files build without nullable warnings. Nullable warnings are
errors in the core and CLI projects; the core currently opts in file by file.
No warning suppressions or null-forgiving operators were added to production code.

Optional locations, option inputs, template lines, and output-writer state are
annotated explicitly. Template substitution handles values whose `ToString()`
returns null. Reader lookahead is captured in local variables before use, and
the template reader now returns the line it reads instead of discarding it when
its lookahead buffer is empty. Reusing a generator resets that buffer.

Options initialize from their defaults at first use, so string getters have a
defined non-null result even before an explicit `init()`. Resetting still restores
the same defaults. Validation carries null-flow annotations instead of forcing
casts past the compiler. Required utility inputs are checked at their boundaries.

## Resource ownership

- The CLI resolves the encoding before opening the input file and owns its reader
  through parsing and generation. Returns and exceptions dispose it.
- Parser, token-manager, and constants generation release their output writers
  when generation throws after opening a file. `ParseEngine` continues to borrow
  its caller's writer.
- Template generation disposes its embedded-resource reader. Its internal
  reader/writer overload leaves both caller-owned objects open, even on failure.
- Support-file generation owns `OutputFile` with a `using` declaration and calls
  `Close()` only after successful generation. Disposing the owner without closing
  it releases resources without certifying partial output with a checksum.
- Closing or disposing the returned checksum writer, including `DisposeAsync`,
  writes exactly one checksum trailer. Repeated cleanup is harmless. Closing
  failures propagate instead of being swallowed, and cleanup runs in `finally`.
- The digest stream disposes its output, hash buffer, and hash algorithm even if
  disposal of the output fails.

`OutputFile` is internal. Its owner-disposal/explicit-completion distinction is
intentional; `CSharpFiles` follows that protocol. Edited support files retain
their existing preservation behavior.

## Utility/CLI batch verification

Verified locally on Linux with .NET SDK 10.0.111 against phase 3 commit `7c2a85a`:

- 93 tests pass in Release and Debug: the previous 71 plus 22 ownership cases.
- New tests cover checksum contents, repeated and asynchronous disposal,
  uncompleted output, failed templates, caller-owned streams, generator reuse,
  CLI success/syntax/semantic failures, invalid encoding, and exceptions after
  each generator opens an output file. Files are reopened exclusively and renamed
  before process exit, so process termination cannot conceal leaked handles.
- Eleven CLI scenarios retain their exit codes and diagnostic contents. Debug
  stack-trace source line numbers are normalized because the CLI source changed.
  Their 62 generated files remain byte-for-byte identical.
- Public/protected CLR members still match across 864 reflection snapshot
  entries. Nullable metadata adds source-level contracts without changing those
  CLR signatures. Output/API comparisons were one-time migration checks.
- A full Release rebuild reports 18 existing warnings and no errors. The four
  removed warnings were unused exception variables in the migrated cleanup paths.
- `git diff --check` passes. Windows/macOS handle-reuse checks will run in the
  existing CI matrix; they have not been verified locally.

The test assembly has friend access to core/CLI internals so it can exercise the
writer protocol and run the CLI in-process without adding public APIs.

```sh
dotnet build src/CSharpCC.sln -c Release --no-incremental
dotnet test src/CSharpCC.sln -c Release --no-build
dotnet test src/CSharpCC.sln -c Debug
```

## Remaining phase 4 work

The initial whole-solution nullable audit found roughly 300 warnings in
handwritten code, many involving state initialized in several parser passes.
The grammar-model batch below handles optional links, staged initialization,
semantic analysis, and lookahead. Next, migrate the generation engine and shared
state without changing token identity or emission order, starting with the
confirmed generation defects listed below. Bootstrap and emitted parser sources
need an explicit nullable policy before enabling analysis across the core
project. They have not been edited or silently excluded by these batches.

## Grammar-model, semantic, and lookahead batch

Nullable analysis is now enabled in 34 additional handwritten files, including
the production and expansion models, regular-expression combinators, tree
walkers, `Semanticize`, `LookaheadWalk`, and `LookaheadCalc`. Nullable warnings
remain errors for these files, without suppressions or null-forgiving operators.

Unresolved production/token references and parent links are explicitly nullable.
Internal accessors check the invariants required after parsing and resolution,
so broken internal state produces a specific exception. Invalid grammars still
receive semantic diagnostics before generation. Lexical-state wildcards retain
their null representation until semantic analysis expands them. EOF explicitly
has no character-matching NFA. CODE productions have no expansion tree, and tree
walkers skip absent roots without passing null to their callbacks. Sparse
lookahead tables and the optional common-prefix result are annotated and checked
where their values are required.

This audit also found and fixed previously uninitialized collections:

- Production parent lists now start empty. Previously resolving a call to another
  production threw before parser generation.
- Left-recursion edges now use an initialized list instead of an uninitialized
  array plus a separate length counter. Traversal order and reference comparisons
  are preserved.
- Nonterminal argument and assignment token lists now start empty, allowing the
  grammar reader to collect call arguments instead of silently discarding them.
- Try-block catch collections start empty; the finally block remains optional.

Verification against the preceding ownership batch, commit `34a7157`:

- 106 tests pass in Release and Debug, including 13 new cases. New coverage runs
  standalone generated parsers with static and instance production calls,
  arguments, assigned return values, repeated calls, and CODE productions with
  wildcard token rules. It also checks direct/indirect left recursion, missing
  references, lexical cycles, and ambiguity prefixes across production boundaries.
- Ambiguity tests deliberately run semantic analysis with `BUILD_PARSER=false`
  and `BUILD_TOKEN_MANAGER=false`; they verify diagnostics, not compilation of
  ambiguous generated parsers. See the separate generation defects below.
- Before the fixes, the initial collection-contract test and both production-call
  integration cases failed. All now pass.
- Eleven existing CLI scenarios retain identical diagnostics and exit codes, and
  their 62 generated files are byte-for-byte identical. All 864 public/protected
  CLR API snapshot entries match. Nullable metadata adds source-level contracts.
- A full Release rebuild still reports 18 existing warnings and no errors.
  `git diff --check` passes. Validation is local to Linux; cross-platform execution
  remains assigned to the existing CI matrix.

### Confirmed generation defects for the next batch

The broader fixtures exposed three separate defects, reproduced with the
unmodified `34a7157` CLI. These remain open and are not nullable regressions.
Each fragment below follows this common header and uses `STATIC=false`:

```text
PARSER_BEGIN(FixtureParser)
namespace Fixture;
using System;
public class FixtureParser {}
PARSER_END(FixtureParser)
```

- `TOKEN: { < A: "a" > } void Input() : {} { [<A>] <EOF> }` generates an
  optional-branch switch whose default case has no terminating statement. The
  standalone consumer fails with CS8070.
- `void Input() : {} { <EOF> }` crashes in `RStringLiteral.DumpStrLiteralImages`
  when the lexer has no character-token rules.
- `void Input() : {} { ("a" | "a" "b") <EOF> }` emits the expected ambiguity
  warning, then crashes in `ParseEngine.GenFirstSet` while indexing the token set.

Turn these into generation/compilation regression tests and repair them as part
of the generation-engine work before extending the emitted-language modes.
