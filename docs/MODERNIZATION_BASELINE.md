# Modernization phase 1 baseline

The regression suite now contains 20 tests, including ten integration cases that
invoke the CLI, compile generated code in an independent .NET 10 project, and run
that consumer. Consumer projects have no CSharpCC project or package reference.
They are created under temporary paths containing spaces and removed afterward.
Generator/consumer processes have 15-second timeouts; consumer builds have a
60-second timeout. Timed-out process trees are terminated.

## Coverage

| Configuration | Behavior checked |
| --- | --- |
| Static/instance × cached/uncached tokens | Two-token lookahead, alternatives, SKIP, MORE, SPECIAL_TOKEN, lexical-state transitions, Unicode text, token locations, invalid syntax, lexical errors, unfinished comments, repeated EOF, reinitialization, and 6,000-character tokens |
| Static/instance Unicode-escape stream | Escaped and literal Unicode, malformed/truncated escapes, repeated EOF, reinitialization, and an escape across the 4,096-character input-buffer boundary |
| Static/instance Unicode stream API | Backslash parity, repeated `u` escapes, trailing backslash, backup, image/suffix extraction, an 8-character initial token buffer growing past 9,000 characters, reader ownership, and position reset |
| Custom token manager | Implementing generated `ITokenManager`, parsing, EOF, and reinitialization without referencing CSharpCC |
| Custom character stream | Implementing generated `ICharStream`, lookahead, EOF, and reinitialization without referencing CSharpCC |
| Existing sample and generation tests | Valid/invalid input, actual support-file checksums, stable regeneration, preservation of edited support files, and a token-constants source snapshot |

The snapshot normalizes CRLF to LF only. Generated syntax-error exceptions must
contain expected token sequences. This is representative coverage, not every
combination of generator options; in particular the custom implementations use
instance parsers.

## Existing defects repaired while establishing the baseline

- The custom-token-manager resource had a misspelled filename and declared
  `TokenManager` instead of the `ITokenManager` expected by generated parsers.
- `UNICODE_ESCAPE` read a different, nonexistent option key. Its stream template
  was missing, and generated lexers referred to the wrong stream class name.
- Restored the Unicode template from the bootstrap character-stream implementation,
  adapting static members, encoding overloads, and .NET's zero-length EOF read
  semantics. Generated consumers do not depend on the bootstrap executable.
- Lookahead expansions started with null internal names despite the generator
  expecting empty strings.
- Lookahead and token-cache paths emitted Java-style `next`, `kind`, `length`,
  and `add` member references. Expected-token sequence deduplication was incorrect.
- Static lexer/parser code accessed static members through object references.
  Emission now selects a type or instance receiver according to the options.
- Lexical token masks used Java's octal `077`, which C# interprets as decimal 77.
  Correcting the mask to 63 restores SPECIAL_TOKEN handling.
- Unicode matching emitted `readonly` on methods, which is invalid for these
  generated class members.

All repairs are in handwritten generator code or source templates. Existing
bootstrap parser files have not been edited.

## Warning baseline

A full Release rebuild with .NET SDK 10.0.111 reports 23 warnings and zero errors:

| Diagnostic | Count | Meaning |
| --- | ---: | --- |
| CS0168 | 14 | Unused local variables |
| CS0108 | 2 | Hidden inherited members |
| CS0162 | 2 | Unreachable generated code |
| CS0169 | 1 | Unused generated field |
| CS0414 | 1 | Assigned but unused field |
| CA2200 | 3 | Exception rethrows that reset stack information |

This count is for the solution rebuild, not every generated fixture. These
warnings remain visible for subsequent modernization; no blanket suppression
was introduced.

## Verification commands

```sh
dotnet build src/CSharpCC.sln -c Release --no-incremental
dotnet test src/CSharpCC.sln -c Release --no-build
dotnet test src/CSharpCC.sln -c Debug
```

Integration tests require a repository checkout and the .NET 10 SDK. They locate
the matching Debug/Release CLI built by the solution. The existing CI matrix
runs these same tests on Linux, Windows, and macOS; only Linux results have been
verified locally so far.
