#nullable enable

using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Deveel.CSharpCC.NUnit;

// Each consumer is built without references to CSharpCC. Running the generator
// and consumer out of process also bounds failures caused by lexer EOF loops.
internal sealed class ParserFixture : IDisposable {
    public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "csharpcc fixture " + Guid.NewGuid().ToString("N"));
    public bool ModernOutput { get; init; }

    public ParserFixture() => Directory.CreateDirectory(DirectoryPath);

    public async Task<ProcessResult> Generate(string grammar, params string[] options) {
        if (ModernOutput)
            options = [.. options, "CSHARP_VERSION=14"];
        File.WriteAllText(Path.Combine(DirectoryPath, "Parser.cc"), grammar);
        var repository = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (repository != null && !File.Exists(Path.Combine(repository.FullName, "src", "CSharpCC.slnx")))
            repository = repository.Parent;
        Assert.That(repository, Is.Not.Null, "Integration tests must run from a repository checkout.");

        var configuration = new DirectoryInfo(TestContext.CurrentContext.TestDirectory).Parent!.Name;
        var generator = Path.Combine(repository!.FullName, "src", "csharpcc", "bin", configuration, "net10.0", "csharpcc.dll");
        var arguments = new string[options.Length + 3];
        arguments[0] = generator;
        arguments[1] = "-OUTPUT_DIRECTORY=" + DirectoryPath;
        for (int i = 0; i < options.Length; i++)
            arguments[i + 2] = "-" + options[i];
        arguments[^1] = Path.Combine(DirectoryPath, "Parser.cc");
        return await RunProcess(arguments);
    }

    public async Task GenerateAndBuild(string grammar, string driver, params string[] options) {
        var generation = await Generate(grammar, options);
        Assert.That(generation.ExitCode, Is.Zero, generation.Output);

        File.WriteAllText(Path.Combine(DirectoryPath, "Consumer.csproj"), $$"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>disable</ImplicitUsings>
                <Nullable>{{(ModernOutput ? "enable" : "disable")}}</Nullable>
                <LangVersion>14</LangVersion>
                <WarningsAsErrors>{{(ModernOutput ? "nullable;CS0168;CS0169;CS0162;CA2200" : "")}}</WarningsAsErrors>
              </PropertyGroup>
            </Project>
            """);
        File.WriteAllText(Path.Combine(DirectoryPath, "Program.cs"), driver);
        // Parser output must survive Windows console code pages before the test
        // process can compare Unicode token images. This is consumer test setup,
        // not generated parser behavior or an application-wide encoding policy.
        File.WriteAllText(Path.Combine(DirectoryPath, "ConsoleSetup.cs"), """
            internal static class FixtureConsoleSetup {
                [System.Runtime.CompilerServices.ModuleInitializer]
                internal static void Initialize() {
                    System.Console.OutputEncoding = new System.Text.UTF8Encoding(false);
                }
            }
            """);
        var build = await RunProcess(["build", "Consumer.csproj", "--nologo", "--verbosity", "quiet"], 60);
        Assert.That(build.ExitCode, Is.Zero, build.Output);
    }

    public Task<ProcessResult> Run(string input) => RunProcess([Path.Combine(DirectoryPath, "bin", "Debug", "net10.0", "Consumer.dll"), input]);

    private async Task<ProcessResult> RunProcess(string[] arguments, int timeoutSeconds = 15) {
        var start = new ProcessStartInfo("dotnet") {
            WorkingDirectory = DirectoryPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        foreach (var argument in arguments)
            start.ArgumentList.Add(argument);
        if (arguments[0].EndsWith("Consumer.dll", StringComparison.Ordinal)) {
            start.StandardOutputEncoding = Encoding.UTF8;
            start.StandardErrorEncoding = Encoding.UTF8;
        }
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start dotnet.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds));
        try {
            await process.WaitForExitAsync(timeout.Token);
        } catch (OperationCanceledException) {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync();
            Assert.Fail($"Process timed out after {timeoutSeconds}s: dotnet {string.Join(' ', arguments)}\n{await stdout}\n{await stderr}");
        }
        return new ProcessResult(process.ExitCode, await stdout + await stderr);
    }

    public void Dispose() => Directory.Delete(DirectoryPath, recursive: true);

    internal sealed record ProcessResult(int ExitCode, string Output);
}
