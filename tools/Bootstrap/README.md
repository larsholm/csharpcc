# .NET bootstrap regeneration

Run the commands under [Bootstrap parser sources](../../README.md#bootstrap-parser-sources)
from the repository root. The tool accepts explicit generator, grammar, and output
paths; paths containing spaces are supported. `--check` returns 1 if any output
file is absent or differs, ignoring platform line endings. It never changes files.

The first build uses the checked-in reader. That generator reads `CSharpCC.cc`
and emits a new reader. This tool adapts it to the core's established public API.
Build and test the new reader, then use `--check` to prove it regenerates the same
files. No historical executable participates in this workflow.

Source ownership:

- `CSharpCC.cc` owns parser productions, lexical rules, actions, and options.
- The normal generator and Unicode stream template own emitted implementations.
- `BootstrapNames` in this tool adapts identifiers, access modifiers, and parameter
  names required by the historical bootstrap API. It also makes the reader partial
  and routes reader/stream constructors through the source-preserving input used
  by the handwritten Roslyn boundary. It uses Roslyn syntax tokens;
  strings and comments are not subject to textual replacement.
- `Compatibility/*.cs.txt` owns the historical `Token`, `ParseException`, and
  `TokenMgrError` contracts. The token template includes internal property bridges
  for the current generator while retaining its public fields and token factory.
- `CSharpCCParser.Embedded.cs`, `GrammarSource.cs`, and `CompilationLayout.cs` own
  the handwritten source boundary and embedded C# integration. Regeneration never
  replaces these files.
- `.editorconfig` explicitly treats the seven output files as generated code.
  Their historical contracts retain an oblivious nullable context. Handwritten
  library sources continue to enforce nullable warnings as errors.

Generation occurs in a temporary directory. No output files are replaced if the
underlying generator fails or emits syntactically invalid C#. Checksums generated
before adaptation are removed. Reproducibility is checked over the adapted files.
The tool pins Roslyn 5.0.0; generated parsers do not gain a compiler-service runtime
dependency from this build tool.
