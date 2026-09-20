# Phase 6: embedded C# and bootstrap investigation

Status: prototype and initial bootstrap defect fixes; integration and reproducible
self-hosting are not complete.

## Embedded C# decision

Use Roslyn for embedded C# parsing, retaining CSharpCC's production and lexical-rule
model. The isolated [boundary prototype](../tools/EmbeddedCSharpPrototype/README.md)
passes 14 checks on .NET 10. It demonstrates syntax-only validation of C# 14
without application type resolution, preservation of source text and parser-body
insertion positions, and diagnostic translation to original grammar coordinates.
It exercises strings, interpolation, raw strings, comments, inactive preprocessor
text, nested generics, modern declarations/expressions, and incomplete input.

This decision still requires integration tests before production use. Roslyn must
remain a generator dependency only. The checked-in grammar reader currently uses
its legacy C# productions; the prototype has not changed its accepted syntax.

The integration must retain original source spans and trivia through the grammar
model, expose balanced C# fragments without having the legacy lexer reject their
contents first, and delegate embedded compilation units, blocks, signatures, and
expressions to Roslyn. Parser class selection, namespace forms, inheritance,
return/throw action handling, malformed grammar delimiters, and public API
compatibility need explicit coverage. A syntax support matrix will describe the
supported embedding contexts after those tests pass.

## Bootstrap findings

The checked-in .NET reader cannot parse the master grammar's own `const` members.
An isolated seed generated using the historical tool with `const` added to the
modifier production revealed further issues:

- The bootstrap lexer requires Unicode input support to read the grammar's own
  identifier ranges. The master grammar also uses the obsolete option spelling
  `CSHARP_UNICODE_ESCAPE`; the current generator expects `UNICODE_ESCAPE`.
- Explicit lexical productions never set `TokenProduction.IsExplicit`, so private
  lexical fragments are wrongly rejected as inline definitions. This still needs
  to be fixed in the master grammar and regenerated reader.
- `ZeroOrOne` did not set its child's parent. Semantic analysis consequently moved
  valid optional lookahead as though it were outside a choice.
- The non-choice lookahead rewrite replaced the first consumed expansion instead
  of inserting its synthetic choice and retained the syntactic lookahead amount
  despite reporting that it was ignored.
- Unicode escaping passed a string's full length as a substring length after a
  nonzero offset. Non-ASCII text and backslashes could throw during generation.
- ASCII NFA emission indexed a removed state's `-1` identifier before checking it.
  The failure reduces to `(["a"-"z"])+ | "a" (["a"-"z"])*`.

The last four defects are fixed in handwritten sources and have dedicated
regression coverage. Optional/non-choice lookahead and overlapping NFA cases run
with static and instance parsers in both output modes. Escaping tests include a
control character, accented text, a surrogate pair, and a backslash.

With those fixes and temporary seed adjustments, the .NET generator emits the
master grammar in the isolated experiment. That is only the first stage: the
emitted reader must still compile against the core's historical token/support API,
regenerate itself again, and pass the fixture suite. No experiment output has
replaced the checked-in bootstrap files. The final workflow must require only the
.NET toolchain; the temporary historical seed is not a proposed user prerequisite.

## Current verification

On Linux with .NET SDK 10.0.111, Debug and Release each pass all 171 solution
tests. The isolated Roslyn prototype passes all 14 boundary checks. The library's
864-entry public/protected API snapshot remains unchanged, as do the 62 generated
files in the 11-scenario legacy baseline (normalizing CLI stack-frame line numbers).
These checks cover the committed phase 5 implementation and the initial phase 6
fixes; they do not establish completion of the bootstrap or syntax integration.

Numeric-literal diagnostics, production integration, a second-generation build,
the complete syntax matrix, and final cross-platform validation remain open.
