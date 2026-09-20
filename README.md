_*NOTE*: The upstream project is unmaintained. This checkout has been migrated to .NET 10._

CSharpCC
========

The scope of this project is to port the functionalities provided by JavaCC for the generation of parsers and lexical analyzers (lexers) for .NET projects. The produced code will consist of C# files easily embeddable in projects, without the requirement of any external reference.

Build and test (.NET 10)
=======================

Install the .NET 10 SDK, then run from the repository root:

```sh
dotnet build src/CSharpCC.sln --configuration Release
dotnet test src/CSharpCC.sln --configuration Release --no-build
```

All four projects target `net10.0`. `global.json` selects a stable .NET 10 SDK.
NuGet packages restore automatically; .NET Framework, Mono, Java, and the bundled
legacy executables are not needed for normal builds. Existing assembly versions
and namespaces are preserved.

Generate a parser
=================

```sh
dotnet run --project src/csharpcc --configuration Release -- -OUTPUT_DIRECTORY=artifacts/parser src/SimpleParserApp/SimpleParser.cc
```

Copy the generated C# files into your application. Generated parsers have no
CSharpCC runtime dependency. Run the command without grammar arguments to see
available generator options (the legacy CLI returns exit code 1 for help).
`CLR_VERSION` is a legacy generator option, not the application's target framework.

Run the sample
==============

```sh
dotnet run --project src/SimpleParserApp --configuration Release
```

Enter `read and print 'hello'`. The sample prints `'hello'` and exits. On Unix,
you can also pipe input:

```sh
printf "read and print 'hello'\n" | dotnet run --project src/SimpleParserApp --configuration Release --no-build
```

The sample generates its parser during the build into `obj/<configuration>/net10.0/GeneratedParser`,
using the .NET 10 CLI. Tests cover grammar generation, regeneration and preservation
of edited support files, and running the generated parser with valid and invalid input.
GitHub Actions builds and tests Debug and Release on Linux, Windows, and macOS.
The legacy source and generated code still produce compiler/analyzer warnings.

Bootstrap parser sources
========================

The library's grammar reader depends on generated C# sources beside `CSharpCC.cc`.
These sources are now included in the repository so a fresh checkout can build
with only the .NET 10 SDK. They were generated from the existing grammar using
`tools/csharpcc-ikvm-1.1.1/csharpcc.exe`; the existing `Token.cs` is retained.

Only when changing the grammar reader itself, regenerate into a temporary directory
with that historical tool (requires .NET Framework on Windows or Mono):

```sh
mono tools/csharpcc-ikvm-1.1.1/csharpcc.exe -OUTPUT_DIRECTORY=/tmp/csharpcc-bootstrap src/Deveel.CSharpCC/Deveel.CSharpCC.Parser/CSharpCC.cc
```

Copy back `CSharpCCParser.cs`, `CSharpCCParserConstants.cs`,
`CSharpCCParserTokenManager.cs`, `CSharpCharStream.cs`, `ParseException.cs`, and
`TokenMgrError.cs`, then build and test. Do not replace the hand-adapted `Token.cs`.
The old bootstrap tool is retained for this maintenance workflow only.

Some Points
===========

Performance tests under Java have proven JavaCC produces parsers that are sensibly faster, compared to the other generators, adding the advantage of having smaller footprints in projects, since it is not necessary to reference entire librares, but only few files (YACC style).

History
=======

To cover such lack of support in .NET environments, a first attempt was done (by me), creating a Java project named _CSharpCC_ that was adjusted (not ported yet) to generate C# files. Although the project succesfully accomplished its goal, it has always been a pain to maintain it and to involve further contributors. Furthermore, because of some lacks in the original JavaCC, the application has never been too much scalable.
