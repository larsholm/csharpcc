# Phase 3: collections and options

Option storage, duplicate tracking, and returned option snapshots now use
`StringComparer.OrdinalIgnoreCase`. Keys no longer pass through culture-sensitive
uppercase conversion. Snapshots remain independent copies. Stream-template
generation retains their comparer instead of copying into a case-sensitive map.

The command-line and grammar assignment lists are `HashSet<string>` instances.
The lexer's private state-block table is also a typed set, using ordinal,
case-sensitive equality to retain its original membership semantics. This table
was never enumerated, and its values duplicated its keys. Lexical-state traversal
now uses typed `foreach` loops over the same collections in the same order.

Public `Dump(..., IList)` signatures and `Semanticize.hasIgnoreCase(Hashtable, ...)`
remain available. Keeping the latter compatibility implementation also preserves
caller-supplied comparers and enumeration order. Generated-source collections and
the bootstrap parser are unchanged.

## Option behavior and repaired defects

- Lowercase option names, including `static` and `ignore_case`, work in Turkish
  culture as well as English. Duplicate checks use the same comparison.
- Range expressions strip the surrounding pair of quotes from string values
  correctly. Previously, the substring length could throw. Empty strings, paths
  with spaces, and embedded `:`/`=` characters retain their contents.
- Invariant `int.TryParse` replaces exception-based command-line conversion.
  Overflow, wrong types, and nonpositive integers warn and leave the setting
  unchanged. An invalid setting does not prevent a later valid assignment.
- Legacy `CLR_VERSION` values must parse as finite invariant numbers; malformed
  or nonfinite strings now warn at assignment instead of failing or affecting
  generation later. Existing numeric thresholds are retained.
- Null/empty command-line arguments and null grammar option values follow the
  existing invalid-option warning policy instead of throwing.
- Debug-lookahead normalization now updates `DEBUG_PARSER` instead of attempting
  to add a duplicate dictionary key. The documented override warning remains,
  and repeated normalization is safe.

Command-line precedence, duplicate-setting policy, warning text/locations, and
warning destinations are preserved. CLI option warnings still go to stdout and
do not increment the grammar warning counter; grammar warnings use stderr and
increment it. Unquoted boolean/integer values retain their previous type checks;
quoted values are strings.

The bootstrap reader still converts grammar integer literals using `Int32.Parse`
before calling option validation. Oversized literals in grammar files remain a
pre-existing limitation, recorded for the phase 6 grammar/bootstrap work.

## Verification

Verified on Linux with .NET SDK 10.0.111 against phase 2 commit `c5e9007`:

- 71 tests pass in Release and Debug: the previous 20 plus 51 option cases.
- Before implementation, 19 of the initial 40 new cases failed, reproducing the
  casing, quoting, overflow, null-input, and normalization defects.
- Tests cover real grammar option blocks, both assignment orders, duplicate
  detection, warning locations/counts, independent snapshots, state reset,
  numeric limits, and legacy CLR-version thresholds under Danish culture.
- Eleven before/after CLI scenarios produce identical diagnostics and exit codes;
  all 62 generated files match byte-for-byte for previously working settings.
- Core exported types and public/protected members match across 864 reflection
  snapshot entries. These output/API comparisons were one-time migration checks.
- A full Release rebuild reports 22 warnings and no errors, down from 23 because
  the unused exception variable in command-line parsing was removed.
- `git diff --check` passes. Windows/macOS verification remains with CI.

```sh
dotnet build src/CSharpCC.slnx -c Release --no-incremental
dotnet test src/CSharpCC.slnx -c Release --no-build
dotnet test src/CSharpCC.slnx -c Debug
```

Phase 4 is next: nullable analysis and explicit resource ownership.
