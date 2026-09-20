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

Option names are case-insensitive across cultures. Command-line settings take
precedence over grammar options; duplicate settings keep the first value from
that source. Invalid command-line values produce warnings and are ignored.
String values preserve their spelling, spaces, and path separators; surrounding
double quotes in the argument value are removed.

Generated language modes
========================

Output uses the legacy C# syntax by default. Opt in to modern output with
`-CSHARP_VERSION=14`, or add `CSHARP_VERSION = 14;` to the grammar's `options` block:

```sh
dotnet run --project src/csharpcc --configuration Release -- -CSHARP_VERSION=14 -OUTPUT_DIRECTORY=artifacts/modern-parser src/SimpleParserApp/SimpleParser.cc
```

Modern output uses nullable annotations, typed collections, collection expressions,
and null-coalescing assignment for token links. It requires a C# 14 compiler;
the tested consumer configuration is .NET 10 with these project settings:

```xml
<TargetFramework>net10.0</TargetFramework>
<LangVersion>14</LangVersion>
<Nullable>enable</Nullable>
<WarningsAsErrors>nullable</WarningsAsErrors>
```

The [C# 14 compiler ships with the .NET 10 SDK](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-14).
`CSHARP_VERSION=legacy` explicitly selects the default mode. This option is
independent of `CLR_VERSION`; modern mode always uses typed error collections.
Neither mode adds a CSharpCC runtime dependency to the generated application.

Modern files contain `#nullable enable`. Token images and links can be null;
parser state fields can be null before initialization, and manually constructed
`ParseException` instances can lack token details. Check these values in your
own actions and consumer code. Existing public CLR signatures are preserved,
but nullable analysis can surface new warnings in callers and custom support
classes. Regeneration preserves edited support files, so reconcile customized
files when changing modes. The output option controls generated boilerplate;
embedded C# is preserved and may itself require a newer consumer compiler.

Embedded C# in grammars
======================

The .NET grammar reader uses Roslyn with C# 14 syntax for parser declarations,
production signatures, action/declaration blocks, call arguments, semantic
lookahead, `CODE` bodies, and `TOKEN_MGR_DECLS`. Both output modes accept these
fragments. Generated applications have no Roslyn runtime dependency.

Supported fixtures include nullable types, aliases and global/static usings, block
and file-scoped namespaces, lambdas, patterns, collection expressions, raw and
interpolated strings, helper records and primary constructors, required/init
properties, and C# 14 field-backed properties, extension declarations, and
null-conditional assignment. Syntax errors report original grammar coordinates;
application type checking remains the consumer compiler’s responsibility.

The parser itself must be a single non-generic class with a brace-delimited body
and no primary constructor. Existing bases must be interfaces because the
generator supplies the constants base class. See the
[tested syntax matrix and boundaries](docs/MODERNIZATION_PHASE_6.md).

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
The integration suite also compiles independent consumers for static parsers,
token caching, lookahead, comment tokens, Unicode escapes, and custom streams.
See the [modernization baseline](docs/MODERNIZATION_BASELINE.md) for the tested
configurations and the [remaining plan](MODERNIZATION_PLAN.md).
GitHub Actions builds and tests Debug and Release on Linux, Windows, and macOS.
The legacy source and generated code still produce compiler/analyzer warnings.

Bootstrap parser sources
========================

The seven checked-in bootstrap files let a fresh checkout build with only the
.NET 10 SDK. Regeneration now uses the current .NET generator and a compatibility
adapter; Mono, Java, and the historical executable are not required.

After changing `CSharpCC.cc`, generator code, or bootstrap compatibility sources,
run these commands from the repository root:

```sh
dotnet build src/CSharpCC.sln --configuration Release
dotnet run --project tools/Bootstrap --configuration Release -- --generator src/csharpcc/bin/Release/net10.0/csharpcc.dll --grammar src/Deveel.CSharpCC/Deveel.CSharpCC.Parser/CSharpCC.cc --output src/Deveel.CSharpCC/Deveel.CSharpCC.Parser
dotnet test src/CSharpCC.sln --configuration Release
dotnet run --project tools/Bootstrap --configuration Release -- --generator src/csharpcc/bin/Release/net10.0/csharpcc.dll --grammar src/Deveel.CSharpCC/Deveel.CSharpCC.Parser/CSharpCC.cc --output src/Deveel.CSharpCC/Deveel.CSharpCC.Parser --check
```

`--check` verifies the second generation without modifying files. The tool retains
historical public token/support members through explicit compatibility sources;
do not edit generated bootstrap files directly. See
[the bootstrap tool](tools/Bootstrap/README.md) for source ownership and details.
CI regenerates, rebuilds, and tests the reader on each configured platform.

Some Points
===========

Performance tests under Java have proven JavaCC produces parsers that are sensibly faster, compared to the other generators, adding the advantage of having smaller footprints in projects, since it is not necessary to reference entire librares, but only few files (YACC style).

History
=======

To cover such lack of support in .NET environments, a first attempt was done (by me), creating a Java project named _CSharpCC_ that was adjusted (not ported yet) to generate C# files. Although the project succesfully accomplished its goal, it has always been a pain to maintain it and to involve further contributors. Furthermore, because of some lacks in the original JavaCC, the application has never been too much scalable.
