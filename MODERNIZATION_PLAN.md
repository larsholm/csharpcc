# CSharpCC modernization plan

Status: phases 1–3 implemented and verified locally on Linux. Phase 4 is in
progress: utilities, options, the CLI, file ownership, the grammar model, semantic
analysis, and lookahead are migrated. Nullable analysis of the generation engine
and shared state remains, along with an explicit bootstrap-source policy.
Phases 5–7 remain.
Cross-platform execution will run through the existing GitHub Actions matrix.
See [the phase 1 baseline](docs/MODERNIZATION_BASELINE.md) for coverage and fixes
and [phase 2 verification](docs/MODERNIZATION_PHASE_2.md) for syntax changes.
[Phase 3 verification](docs/MODERNIZATION_PHASE_3.md) covers collections and options.
[Phase 4 progress](docs/MODERNIZATION_PHASE_4.md) records nullable/resource work
and its remaining scope.

Modernize the .NET 10 codebase, improve generated parsers, and support modern C#
inside grammar files. Deliver each phase as a separate, reviewable change.

## Compatibility rules

- Keep existing public names, signatures, parser behavior, and CLI exit codes.
- Preserve reference identity and mutation semantics of grammar nodes; do not
  convert the existing node hierarchy to records.
- Keep generated parsers independent of CSharpCC and any compiler-service runtime.
- Preserve current generated syntax by default. Introduce an explicit opt-in
  modern output mode before emitting syntax requiring newer C# compilers.
- Modify generator code, templates, and `CSharpCC.cc` as appropriate; regenerate
  derived files rather than treating them as independently maintained sources.
- Keep existing grammar files working. Document any newly discovered pre-existing
  defects separately from modernization regressions.

## 1. Establish behavioral coverage

Extend the current nine tests with a fixture harness that generates, compiles,
and executes parsers. Cover static and instance parsers, lookahead, lexical states,
SKIP/MORE/SPECIAL_TOKEN, Unicode, EOF, malformed input, token caching, and custom
token managers/character streams. Use representative combinations, not an
exhaustive Cartesian product. Run potentially hanging cases in subprocesses
with timeouts.

Record diagnostics, token positions, output checksums, and regeneration behavior.
Record the current warning baseline. Capture representative output snapshots,
normalizing only platform line endings and incidental paths.

Investigate two concrete support-template gaps before relying on these modes:
`ITokenMnager.template` differs from the requested resource name and declares
`TokenManager` where emitted code expects `ITokenManager`; the referenced
`UnicodeCharStream.template` is absent. Add reproductions and fix confirmed defects.

Acceptance: supported fixtures compile and behave as expected; failures in
previously untested modes are understood and resolved before their modernization.

## 2. Modernize handwritten syntax

Replace type-check/cast pairs with declaration and property patterns in
`ParseEngine`, `LookaheadWalk`, `Semanticize`, and template utilities. Use switch
expressions for short value computations while keeping long procedural branches
readable. Preserve branch order and fallback behavior.

Use raw string literals for test grammars, interpolation for simple diagnostics,
get-only properties for constructor-only assignments, and collection expressions
or target-typed `new` where the resulting type is clear. Use file-scoped namespaces
in handwritten files where convenient. Consider primary constructors only for
small internal helpers; use C# 14 `field` only where custom accessors benefit.

Acceptance: fixture behavior and public API remain unchanged. Reuse behavioral
tests for syntax-only edits rather than adding tests that mirror syntax.

## 3. Strengthen collections and option parsing

Replace option-setting `ArrayList` instances with `HashSet<string>` and use
`StringComparer.OrdinalIgnoreCase` consistently for option keys. Remove
culture-sensitive key normalization while preserving user-supplied values.
Convert internal `Hashtable`/non-generic collections to typed equivalents,
preserving ordering and equality semantics. Retain public compatibility wrappers
where a generic conversion would otherwise change a public signature.

Use `TryParse` for option values and ranges for clear substring operations.
Check quoted values, numeric overflow, duplicate settings, command-line precedence,
and Turkish-culture casing. Preserve existing warning/error policy.

Acceptance: targeted option tests pass and no collection change alters parser
behavior. Treat any span-based optimization separately and require measurements.

## 4. Add nullable analysis and explicit resource ownership

Enable nullable analysis file by file, starting with utilities, options, and the
CLI, then moving through the grammar model and generation engine. Mark genuine
optional values, validate required inputs at boundaries, and express initialization
invariants instead of broadly adding null-forgiving operators.

Convert resource ownership to `using` declarations where appropriate. Audit the
custom checksum writer before changing `Close`/`Dispose` behavior, since closing
also writes the checksum trailer. Keep caller-owned readers/writers open where
the API requires it. Ensure exceptions release owned handles.

Acceptance: migrated handwritten files have no nullable warnings; success and
failure cases allow immediate file reuse/deletion on Windows. Enable nullable
analysis project-wide after generated-source handling is explicit.

## 5. Modernize generated output

Add a generated-language option independent of the historical `CLR_VERSION`
setting: initially legacy output (default) and C# 14 output. Validate option values
and document the required consumer compiler. Preserve emitted public API shapes.

Update `ParseGen`, `LexGen`, `OtherFilesGen`, and `Templates` to emit typed
collections, clearer initialization, appropriate modern syntax, and accurate
nullable annotations in modern mode. Remove avoidable generated warnings at their
source. Do not change token identity, buffering, or stream ownership incidentally.

Acceptance: both output modes generate, compile, and execute the same fixtures.
Compile modern output with nullable warnings treated as errors. Verify output has
no CSharpCC runtime dependency and regeneration produces stable results.

## 6. Support modern C# in grammar code

This phase is a parser feature project. Retargeting the generator does not extend
the C# syntax recognized by `CSharpCC.cc`.

First prototype a boundary between CSharpCC grammar syntax and embedded C#.
Evaluate Roslyn for parsing embedded declarations, expressions, and action blocks;
retain CSharpCC handling for productions, lexical rules, and lookahead. Prefer
Roslyn if the prototype preserves source text, insertion points, and diagnostics
without changing existing grammars. Otherwise document the limitation and extend
the existing C# productions incrementally. Resolve this design choice before
undertaking the full feature migration.

The prototype must handle braces and grammar delimiter text inside strings,
comments, interpolation, and raw strings; nested generic arguments; contextual
keywords; preprocessor directives; and incomplete input. Translate diagnostics
back to original grammar line/column positions. Syntax validation must not require
resolving the user's application types.

Implement and test supported syntax in groups: nullable types and modern using/
namespace forms; lambdas and expression-bodied members; patterns and switch
expressions; target-typed construction and collection expressions; interpolated
and raw strings; records, init/required members, and primary constructors in
embedded helper declarations; then applicable C# 14 constructs. Define the valid
embedding contexts and publish a tested support matrix rather than claiming
unqualified support for every C# compilation-unit form.

Any Roslyn dependency belongs to the generator only. Establish reproducible
bootstrap regeneration under .NET 10 before modernizing code embedded in the
generator's own grammar; the historical bootstrap executable cannot be assumed
to understand those changes. A newly built generator must regenerate a second
build that passes the same fixture suite.

Also replace exception-based numeric-literal conversion in `CSharpCC.cc` with
diagnostics for invalid/out-of-range literals. Phase 3 handles command-line
integer overflow, but oversized integer literals in grammar files currently fail
inside the bootstrap reader before reaching option validation.

Acceptance: every supported feature has positive, negative, and delimiter-edge
fixtures; existing grammars still work; clean builds and bootstrap regeneration
require only the documented .NET toolchain.

## 7. Final validation and documentation

Run Debug/Release builds and tests on Linux, Windows, and macOS, including a clean
source copy and paths containing spaces. Check handwritten public API and emitted
parser API compatibility, generation diagnostics, deterministic regeneration, and
dependency-free consumer builds. Compare generation time, parsing throughput, and
allocations on representative fixtures before accepting performance-sensitive edits.

Document language modes, grammar syntax support, nullable behavior, and the new
bootstrap workflow. Keep unrelated warnings visible; promote resolved warning
categories to errors rather than suppressing the entire legacy baseline.

Completion means all planned features have evidence in the fixture suite, CI is
green on the supported platforms, and the README describes commands verified
from a clean checkout.

## Order and scope

Implement phases 1–5 first, followed by phase 6 and final validation. Phase 6 is
the largest and highest-uncertainty part; estimate it after the embedded-C# and
bootstrap prototypes. Global-state removal, concurrent generation, wholesale AST
redesign, and speculative lexer optimization are separate future projects.

## References

- [C# 14 features](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-14)
- [Pattern matching](https://learn.microsoft.com/en-us/dotnet/csharp/fundamentals/functional/pattern-matching)
- [Nullable reference types](https://learn.microsoft.com/en-us/dotnet/csharp/nullable-references)
- [Roslyn syntax model and source preservation](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/work-with-syntax)
