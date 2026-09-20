# Phase 4 progress: nullable analysis and resource ownership

Status: the utility/CLI and ownership batch is implemented. Phase 4 remains in
progress until the grammar model and generation engine have been migrated and
nullable analysis can be enabled across the core project.

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

## Verification

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
Next, annotate optional grammar links, model staged initialization explicitly,
and migrate semantic analysis, lookahead, and generation without changing token
identity or emission order. Bootstrap and emitted parser sources need an explicit
nullable policy before enabling analysis across the core project. They have not
been edited or silently excluded by this batch.
