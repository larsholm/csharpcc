# Phase 2: handwritten syntax

The lookahead engine and semantic walkers now use declaration patterns instead
of repeated type checks and casts. Property patterns express explicit lookahead,
semantic actions, and code productions. Combined `or`/`not` patterns simplify
equivalent checks without changing branch order or fallback behavior.

Short value computations use switch expressions in `ParseEngine.CodeCheck` and
template condition evaluation. Longer procedural branches retain their existing
structure. Collection expressions create the same mutable lists, and target-typed
construction removes redundant type names where the type is explicit.

Constructor-only properties in `ZeroOrMore`, `ZeroOrOne`, and the template utility
are get-only. The private `Phase3Data` helper uses a primary constructor with
get-only properties. Grammar nodes retain their existing reference identity and
public mutation APIs. There was no useful need for C# 14 `field` accessors here.

The diagnostic and template utilities use file-scoped namespaces. The original
generation test grammar uses a raw string literal, preserving its text, native
line endings, and trailing newline. Simple utility code also uses interpolation,
inline `out` variables, and null-coalescing assignment.

## Verification

Verified locally on Linux with .NET SDK 10.0.111 against phase 1 commit `082d023`:

- All 20 tests pass in both Release and Debug, including independent generated
  consumers with no CSharpCC runtime dependency.
- Eleven before/after CLI scenarios produce identical diagnostics and exit codes;
  their 62 generated files are byte-for-byte identical. Scenarios cover the
  static/instance and cached/uncached combinations, static/instance Unicode,
  custom character streams, custom token managers, the sample grammar, an
  undefined production, and invalid grammar syntax.
- Reflection snapshots of the core assembly's exported types and public/protected
  members match across 864 entries, including accessor visibility, method and
  field attributes, parameter metadata, and constants.
- The solution build still reports the existing 23 warnings and no errors.
- `git diff --check` passes.

The output and API comparisons were one-time migration checks against saved
baseline binaries. The committed behavioral suite remains the regression check:

```sh
dotnet test src/CSharpCC.slnx -c Release
dotnet test src/CSharpCC.slnx -c Debug
```

No templates, bootstrap parser sources, or generated-language defaults changed.
Windows and macOS verification remains with the existing CI matrix. Phase 3 is
next: typed internal collections and culture-independent option parsing.
