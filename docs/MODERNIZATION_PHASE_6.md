# Phase 6: embedded C# and reproducible bootstrap

Status: implemented and verified locally on Linux with .NET SDK 10.0.111.
All 253 tests pass in Debug and in a clean Release checkout whose path contains
spaces. The rebuilt clean reader verifies all seven bootstrap files; the isolated
prototype passes 14 checks. See [phase 7 validation](MODERNIZATION_PHASE_7.md) for
consumer benchmarks and remote CI status.

## Embedded C# boundary

The generator pins `Microsoft.CodeAnalysis.CSharp` 5.0.0 and parses embedded code
as C# 14. CSharpCC retains production, lexical-rule, and lookahead semantics.
The isolated prototype established syntax boundaries; the production reader now
uses the handwritten `CSharpCCParser.Embedded.cs` implementation.

Reader/stream constructors retain original source before the legacy character
stream decodes Unicode escapes. At embedded boundaries, Roslyn consumes the C#
fragment, preserves its text, and resumes the grammar lexer after the fragment.
Syntax diagnostics use original grammar coordinates, including expanded tab
columns. No application type resolution or compilation runs during generation.
Caller-owned streams/readers remain open. Reinitializing the reader replaces its
source and preprocessor state; caller-supplied token managers retain the historical
token-only parser path.

Compilation units retain parser-body insertion positions, helper declarations,
namespace scopes, aliases, and imports. File-scoped namespaces are emitted as block
namespaces, preserving the older boilerplate syntax. The generated parser inherits
its constants class before any user-supplied interfaces. Required System imports
are supplied for boilerplate references. Global usings are emitted with the parser,
not duplicated into each support file. Header `#define`/`#undef` state applies to
embedded fragments and token-manager declarations.

Embedded action returns/throws retain the existing jump guards. Returns inside
lambdas and local functions are not rewritten. Modern output omits redundant
switch breaks after actions known to return or throw. Original C# fragments can
occupy a single internal token image; token-list boundaries are not a C# syntax API.
The public/protected library signatures remain covered by the API snapshot.

## Tested syntax matrix

The following consumer fixtures run in both legacy and C# 14 output modes. Modern
consumers treat nullable, unused generated fields/locals, unreachable code, and
rethrow warnings as errors. Consumer projects reference neither CSharpCC nor Roslyn.

| Context | Positive coverage | Negative/boundary coverage |
| --- | --- | --- |
| Production return types and parameters | Nullable types, nested generics, default parameters | Invalid defaults; long parameter lists/literals |
| Production calls and semantic lookahead | Lambdas, collection arguments, pattern expressions | Malformed argument collections and switch expressions; long whitespace before an expression continuation |
| Declaration/action blocks and CODE bodies | Target-typed construction, nullable locals, local functions, lambdas, list patterns, switch/collection expressions | Invalid lambda, construction, pattern, and collection syntax; outer returns versus nested function returns |
| Strings | Ordinary escapes, interpolation, raw literals | Unterminated raw/interpolated literals; braces and PARSER_END text inside comments/strings; literals longer than the parser window |
| Parser/helper declarations | Aliases, global/static usings, file/block/nested namespaces, interface inheritance, expression-bodied members, records, required/init properties, helper primary constructors | Malformed declarations; missing/duplicate parser classes; unsupported parser shapes; a helper named PARSER_END |
| Applicable C# 14 | Field-backed properties, extension declarations, null-conditional assignment | Malformed forms of each construct |
| Token-manager members/actions | Records, properties, expression bodies, CommonTokenAction, collection/pattern actions | Preprocessor-disabled invalid text; shared header symbols |
| Preprocessor and delimiters | Inactive text, header-defined symbols, original comments and strings | Missing/mismatched PARSER_END; invalid text in active C# fragments; source-position diagnostics |

The parser declaration must be one non-generic class with an explicit brace body,
without a primary constructor. Top-level statements and multiple parser parts in
the embedded unit are rejected. User-supplied bases must be interfaces: the
constants base class occupies the class-inheritance slot. Modern helper types may
use records and primary constructors. Grammar production declarations, access
modifiers, and assignment targets still follow CSharpCC's grammar syntax; this is
not a promise to accept every C# member form as a production.

Preprocessor symbols are those declared in the embedded header, not symbols from
a consumer's future build configuration. Syntax-only validation cannot establish
whether application types, extension members, or references exist. Such errors
remain the consumer compiler's responsibility. The generated-language option
controls boilerplate; user-provided modern C# still requires an appropriate compiler.

## .NET-only bootstrap

`tools/Bootstrap` generates the seven checked-in files from `CSharpCC.cc`, then
adapts identifiers and historical support contracts without changing string or
comment contents. Compatibility sources own `Token`, `ParseException`, and
`TokenMgrError`; the adapter also connects the partial reader to original source.
The workflow requires only the .NET 10 SDK. No historical executable or Mono runs
in regeneration, builds, tests, or CI.

Regeneration stages output in a temporary directory and supports a read-only
`--check`. A newly built reader regenerates identical files; the regenerated reader
builds and runs the fixture suite. CI now performs both generation checks around
a rebuild/test on Linux, Windows, and macOS, using a checkout path containing
spaces. These configured checks are not evidence of remote runs having completed.
See the [verified workflow commands](../README.md#bootstrap-parser-sources) and
[source ownership](../tools/Bootstrap/README.md).

## Defects exposed and fixed

- The master grammar now recognizes its own const members, uses the current
  Unicode option name, and enables Unicode input.
- Explicit lexical productions set IsExplicit, allowing private lexical fragments
  while still rejecting private references from parser productions.
- Oversized decimal/hexadecimal grammar integers produce diagnostics instead of
  conversion exceptions. Invalid values do not trigger misleading option warnings.
- Optional expansions set their child's parent; non-choice lookahead insertion
  preserves the consumed expansion and clears ignored syntactic lookahead.
- Unicode escaping handles non-ASCII text and preserves existing C# backslash
  escapes. NFA emission checks removed state IDs before indexing arrays.
- Full 64-bit masks include bit 63; partial Unicode ranges use the current character's
  bit index; population counting includes the final bit. Boundary fixtures cover
  both static and instance parsers in both output modes.

## Compatibility and performance evidence

The 864-entry public/protected API snapshot is now a durable test. The historical
constants output snapshot also remains unchanged. The 11-scenario baseline retains
its exit codes and all nine constants files. Of 62 generated files, 53 change:
source-preserving header/body layout, propagated imports, resulting checksums, and
corrected Unicode NFA emission account for these differences. Embedded syntax
errors now have Roslyn source diagnostics instead of legacy parser stack traces.
Byte-identical legacy output is therefore not claimed for this feature migration.

A preliminary Linux generation comparison uses three warmups and 15 measured runs,
with tiered compilation disabled and file regeneration included. The baseline is
the preserved .NET 10 generator from before phase 5; medians from one same-machine
run are shown below. These are generation measurements, not consumer throughput.

| Grammar | Baseline | Current | Baseline managed allocation | Current managed allocation |
| --- | ---: | ---: | ---: | ---: |
| 6 productions | 0.54 ms | 0.66 ms | 789 KB | 880 KB |
| 201 productions | 3.82 ms | 8.98 ms | 4.08 MB | 6.19 MB |

Passing the entire remaining input to Roslyn at each boundary initially took about
52 ms for the larger grammar. Bounded, expanding parse windows reduced that to
about 9 ms. Windows expand on diagnostics or when a fragment's full span, including
trailing trivia, reaches the window edge. Tests exercise long literals and an
expression continuation after long whitespace. The remaining overhead pays for
modern syntax validation and source tracking; it has not been presented as a speedup.
The [generation benchmark](../tools/GenerationBenchmark/README.md) makes this
comparison reproducible. Generated-parser throughput/allocation comparisons are recorded in phase 7.

The legacy bootstrap keeps its explicit generated-code nullable policy and visible
compiler warnings. Handwritten code continues to treat nullable warnings as errors.
No blanket warning suppression was added.
