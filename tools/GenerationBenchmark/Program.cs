using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;
using System.Text.Json;
if (args.Length == 0) {
    Console.Error.WriteLine("Usage: dotnet run --project tools/GenerationBenchmark -- <csharpcc.dll> [<baseline-csharpcc.dll>]");
    return 1;
}
if (args[0] == "--consumer") {
    if (args.Length < 3) throw new ArgumentException("Expected a language mode and generator assembly path.");
    return await ConsumerBenchmarks.Run(args[1], args[2..]);
}
var output = Console.Out;
var errors = Console.Error;
var reports = new List<object>();
foreach (string path in args) {
    var context = new GeneratorContext(Path.GetFullPath(path));
    var assembly = context.LoadFromAssemblyPath(Path.GetFullPath(path));
    var main = assembly.GetType("Deveel.CSharpCC.Parser.Program")!.GetMethod("MainProgram")!;
    foreach (int count in new[] { 5, 200 }) {
        string directory = Path.Combine(Path.GetTempPath(), "csharpcc benchmark " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string grammar = "PARSER_BEGIN(BenchParser)\nnamespace Bench;\nusing System;\npublic class BenchParser {}\nPARSER_END(BenchParser)\nTOKEN: { < WORD: ([\"a\"-\"z\"])+ > }\nSKIP: { \" \" }\n" +
            "void Input() : {} { " + string.Join(" ", Enumerable.Range(0, count).Select(i => $"Part{i}()")) + " <EOF> }\n" +
            string.Join("\n", Enumerable.Range(0, count).Select(i => $"void Part{i}() : {{ int length = 0; Token t; }} {{ t=<WORD> {{ length += t.Image.Length; }} }}"));
        string input = Path.Combine(directory, "Parser.cc");
        File.WriteAllText(input, grammar);
        string[] options = ["-STATIC=false", "-OUTPUT_DIRECTORY=" + directory, input];
        Console.SetOut(TextWriter.Null); Console.SetError(TextWriter.Null);
        try {
            int Run() => (int)main.Invoke(null, [options])!;
            for (int i = 0; i < 3; i++) if (Run() != 0) throw new Exception("Warmup failed");
            var times = new List<double>(); var bytes = new List<long>();
            for (int i = 0; i < 15; i++) {
                long allocated = GC.GetTotalAllocatedBytes(true);
                var watch = Stopwatch.StartNew();
                if (Run() != 0) throw new Exception("Generation failed");
                watch.Stop();
                times.Add(watch.Elapsed.TotalMilliseconds); bytes.Add(GC.GetTotalAllocatedBytes(true) - allocated);
            }
            reports.Add(new { Generator = path, Productions = count + 1, MedianMilliseconds = times.Order().ElementAt(7), MedianAllocatedBytes = bytes.Order().ElementAt(7) });
        } finally { Console.SetOut(output); Console.SetError(errors); Directory.Delete(directory, true); }
        GC.Collect();
    }
    context.Unload();
}
Console.WriteLine(JsonSerializer.Serialize(new {
    Runtime = System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription,
    OperatingSystem = System.Runtime.InteropServices.RuntimeInformation.OSDescription,
    TieredCompilation = Environment.GetEnvironmentVariable("DOTNET_TieredCompilation"),
    WarmupRuns = 3,
    MeasuredRuns = 15,
    Results = reports
}, new JsonSerializerOptions { WriteIndented = true }));
return 0;
sealed class GeneratorContext(string path) : AssemblyLoadContext(isCollectible: true) {
    private readonly AssemblyDependencyResolver resolver = new(path);
    protected override Assembly? Load(AssemblyName name) => resolver.ResolveAssemblyToPath(name) is { } resolved ? LoadFromAssemblyPath(resolved) : null;
}
