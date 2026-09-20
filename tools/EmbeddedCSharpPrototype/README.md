# Embedded C# boundary prototype

Run from the repository root with the .NET 10 SDK:

```sh
dotnet run --project tools/EmbeddedCSharpPrototype --configuration Release
```

This isolated experiment pins `Microsoft.CodeAnalysis.CSharp` 5.0.0 and explicitly
selects C# 14. It does not alter the generator or any generated consumer dependency.
The executable checks 14 cases and fails on a boundary, syntax, source-preservation,
or source-position mismatch.

Roslyn parses a single block, expression, type, or member with `consumeFullText`
disabled. The consumed syntax span ends before the following CSharpCC grammar
text. Compilation units are bounded by a top-level `PARSER_END(name)` token
sequence; comments, strings, interpolation, raw strings, and inactive conditional
text do not terminate the unit. Original source slices and parser class brace
positions are retained. Syntax diagnostics map back to the original grammar's
one-based line and column without requiring referenced application types.

The fixtures cover nested generic arguments, nullable types, modern using and
namespace forms, lambdas, patterns, switch expressions, collection expressions,
records, required/init members, primary constructors, C# 14 field-backed
properties, extension declarations, and null-conditional assignment. Negative
checks cover a missing initializer and an incomplete block.

The experiment supports choosing Roslyn for embedded C# parsing. It is **not** a
claim that the production grammar reader accepts these constructs yet. The next
work must connect the boundary to the grammar token stream and model, preserve
insertion points and comments, handle parser class validation and malformed end
delimiters, and establish the .NET-only bootstrap before changing the master
grammar. Existing grammar fixtures must continue to pass through that integration.

References:

- [Roslyn ParseTokens](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.csharp.syntaxfactory.parsetokens)
- [Roslyn syntax model](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/work-with-syntax)
- [C# 14](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-14)
