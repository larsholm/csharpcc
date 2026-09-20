# Phase 5: modern generated output

`CSHARP_VERSION=14` opts into modern output. `legacy` remains the default, and
`CLR_VERSION` remains a separate option. Both command-line and grammar settings
accept the new value under the existing validation and precedence rules.

Modern files enable nullable analysis. Optional token images, links, exception
metadata, stream encodings, and staged parser fields have nullable annotations.
Private checked accessors express initialization requirements. Token advancement
uses null-coalescing assignment while retaining the existing linked token objects.
Error collections are typed even with `CLR_VERSION=1.0`; mask arrays and empty
collections use collection expressions. Support streams retain their ownership
behavior and release buffers through `Done()`.

Modern generation omits unused catch variables, unused lexical-action fields,
unreferenced EOF labels, and redundant breaks in terminating default branches.
The simple stream rethrows with `throw;`, preserving the original exception stack.
Legacy syntax remains unchanged for the established fixture baseline.

## Regression fixes found during verification

- Parser and constants writers now truncate existing files. Switching language
  modes previously could leave trailing content when replacement output was shorter.
- MORE lexical actions referenced the undefined Java-era name `jjimageLen`.
  They now use `ccImageLen` in both modes. A fixture exercises SKIP, MORE, and
  TOKEN actions together and verifies the accumulated token image.

These corrections are separate from the modern syntax choice. Neither changes
newly generated legacy files in the existing baseline scenarios.

## Verification

Verified locally on Linux with .NET SDK 10.0.111:

- Release and Debug: all 155 tests pass.
- Both language modes compile and execute the shared integration and generation
  regression fixtures: static/instance parsers, caching, lookahead, calls between
  productions, EOF-only lexers, malformed input, lexical states, special tokens,
  Unicode streams, custom streams/managers, debug output, and lexical actions.
- Modern consumers compile with nullable, CS0168, CS0169, CS0162, and CA2200
  diagnostics treated as errors. Consumers have no CSharpCC project or runtime
  reference. Additional combinations cover disabled error reporting, disabled
  position tracking, parser-aware token managers, and `CLR_VERSION=1.0`.
- Reflection compares generated public/protected members in both language modes
  for static and instance parsers. The handwritten library's 864-entry exported
  API snapshot also remains identical to phase 4.
- Regeneration is byte-stable, edited support files survive, and switching modes
  in the same directory produces the same legacy files as a fresh generation.
- The phase 4 baseline's 11 CLI scenarios retain their exit codes, diagnostics,
  and all 62 generated files. Only source line numbers in the CLI's diagnostic
  stack trace require normalization after the help text changed.

The tests enforce the generated warning categories above without suppressing
unrelated library or legacy-bootstrap warnings. Cross-platform execution still
requires the GitHub Actions matrix. Phase 6 (embedded C# and .NET bootstrap) and
phase 7 (final validation) remain open.
