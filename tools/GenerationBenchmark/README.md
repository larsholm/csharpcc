# Generation benchmark

Compare built .NET 10 generator assemblies without changing either checkout:

```sh
DOTNET_TieredCompilation=0 dotnet run --project tools/GenerationBenchmark --configuration Release -- /path/to/baseline/csharpcc.dll src/csharpcc/bin/Release/net10.0/csharpcc.dll
```

On PowerShell, set `$env:DOTNET_TieredCompilation = '0'` before running the command.
Both assemblies must retain their build output dependencies and `.deps.json`.
The runner loads each generator in its own assembly context, uses three warmup
runs and 15 measurements, and reports median elapsed milliseconds and total
managed allocations as JSON. Disable tiered compilation for consistent warm
comparisons; this is not a cold-start measurement.

The two synthetic grammars contain 6 and 201 productions. Each measured invocation
reads a grammar, validates it, and regenerates files in a temporary directory.
Existing generated support files participate in normal regeneration/checksum
handling. Console output is excluded. Temporary output is deleted after each
case. Results include file I/O and depend on the machine; compare runs on the
same machine rather than treating these numbers as universal thresholds.

For independent generated-consumer measurements:

```sh
DOTNET_TieredCompilation=0 dotnet run --project tools/GenerationBenchmark --configuration Release -- --consumer legacy /path/to/baseline/csharpcc.dll src/csharpcc/bin/Release/net10.0/csharpcc.dll
DOTNET_TieredCompilation=0 dotnet run --project tools/GenerationBenchmark --configuration Release -- --consumer 14 src/csharpcc/bin/Release/net10.0/csharpcc.dll
```

This generates and builds a Release consumer without project/package references,
then checks for generator/Roslyn assemblies in its output. It parses 20-word and
200-word inputs, reusing the parser with ReInit and a new StringReader each time.
Each case warms up 500 times and measures seven batches of 3,000 parses. All return
counts are checked. Output gives median nanoseconds and allocated bytes per parse.
The figures include reader construction and parser reinitialization. These narrow
fixtures are useful comparisons, not a claim about every grammar or workload.
