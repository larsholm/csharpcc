using System.Diagnostics;
using System.Text.Json;

internal static class ConsumerBenchmarks {
    internal static async Task<int> Run(string mode, string[] generators) {
        if (mode is not ("legacy" or "14") || generators.Length == 0)
            throw new ArgumentException("Usage: --consumer <legacy|14> <csharpcc.dll> [<baseline-csharpcc.dll>]");
        var reports = new List<object>();
        foreach (string generator in generators) {
            string directory = Path.Combine(Path.GetTempPath(), "csharpcc consumer benchmark " + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            try {
                string grammar = Path.Combine(directory, "Parser.cc");
                File.WriteAllText(grammar, """
                    PARSER_BEGIN(BenchParser)
                    namespace Bench;
                    using System;
                    public class BenchParser {}
                    PARSER_END(BenchParser)
                    SKIP: { " " }
                    TOKEN: { < WORD: (["a"-"z"])+ > }
                    int Input() : { int count = 0; } { ( <WORD> { count++; } )+ <EOF> { return count; } }
                    """);
                var arguments = new List<string> { Path.GetFullPath(generator), "-STATIC=false", "-OUTPUT_DIRECTORY=" + directory };
                // Old comparison builds predate CSHARP_VERSION. Their default is legacy.
                if (mode == "14") arguments.Add("-CSHARP_VERSION=14");
                arguments.Add(grammar);
                await Dotnet(directory, arguments, 30);
                File.WriteAllText(Path.Combine(directory, "Consumer.csproj"), $$"""
                    <Project Sdk="Microsoft.NET.Sdk">
                      <PropertyGroup>
                        <OutputType>Exe</OutputType>
                        <TargetFramework>net10.0</TargetFramework>
                        <LangVersion>14</LangVersion>
                        <ImplicitUsings>disable</ImplicitUsings>
                        <Nullable>{{(mode == "14" ? "enable" : "disable")}}</Nullable>
                      </PropertyGroup>
                    </Project>
                    """);
                File.WriteAllText(Path.Combine(directory, "Program.cs"), Driver);
                await Dotnet(directory, ["build", "Consumer.csproj", "-c", "Release", "--nologo", "-v", "quiet"], 60);
                string binaries = Path.Combine(directory, "bin", "Release", "net10.0");
                if (Directory.GetFiles(binaries, "Microsoft.CodeAnalysis*.dll").Length != 0 ||
                    Directory.GetFiles(binaries, "Deveel.CSharpCC*.dll").Length != 0)
                    throw new InvalidOperationException("Consumer contains a generator runtime dependency.");
                string result = await Dotnet(directory, [Path.Combine(binaries, "Consumer.dll")], 60);
                using var json = JsonDocument.Parse(result);
                reports.Add(new { Generator = generator, Mode = mode, Results = json.RootElement.Clone() });
            } finally {
                Directory.Delete(directory, recursive: true);
            }
        }
        Console.WriteLine(JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    private static async Task<string> Dotnet(string directory, IEnumerable<string> arguments, int seconds) {
        var start = new ProcessStartInfo("dotnet") {
            WorkingDirectory = directory, UseShellExecute = false,
            RedirectStandardOutput = true, RedirectStandardError = true
        };
        foreach (string argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start dotnet.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException) {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            throw new TimeoutException("Benchmark process timed out.");
        }
        string output = await stdout;
        string errors = await stderr;
        if (process.ExitCode != 0) throw new InvalidOperationException(output + errors);
        return output;
    }

    private const string Driver = """
        using System;
        using System.Collections.Generic;
        using System.Diagnostics;
        using System.IO;
        using System.Linq;
        using System.Text.Json;
        using Bench;
        class Program {
            static void Main() {
                var results = new List<object>();
                foreach (int words in new[] { 20, 200 }) {
                    string input = string.Join(" ", Enumerable.Repeat("word", words));
                    var parser = new BenchParser(new StringReader(input));
                    for (int i = 0; i < 500; i++) {
                        parser.ReInit(new StringReader(input));
                        if (parser.Input() != words) throw new Exception("Incorrect warmup result");
                    }
                    const int iterations = 3000;
                    var times = new List<double>();
                    var allocations = new List<double>();
                    for (int sample = 0; sample < 7; sample++) {
                        long before = GC.GetTotalAllocatedBytes(true);
                        long started = Stopwatch.GetTimestamp();
                        int total = 0;
                        for (int i = 0; i < iterations; i++) {
                            parser.ReInit(new StringReader(input));
                            total += parser.Input();
                        }
                        double nanoseconds = Stopwatch.GetElapsedTime(started).TotalNanoseconds;
                        long bytes = GC.GetTotalAllocatedBytes(true) - before;
                        if (total != words * iterations) throw new Exception("Incorrect measured result");
                        times.Add(nanoseconds / iterations);
                        allocations.Add((double)bytes / iterations);
                    }
                    results.Add(new { Words = words, Iterations = iterations, Samples = 7,
                        MedianNanosecondsPerParse = times.Order().ElementAt(3),
                        MedianBytesPerParse = allocations.Order().ElementAt(3) });
                }
                Console.WriteLine(JsonSerializer.Serialize(results));
            }
        }
        """;
}
