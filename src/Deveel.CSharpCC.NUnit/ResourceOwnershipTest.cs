#nullable enable

extern alias Cli;

using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Deveel.CSharpCC.Parser;
using Deveel.CSharpCC.Util;
using NUnit.Framework;
using CliProgram = Cli::Deveel.CSharpCC.Parser.Program;

namespace Deveel.CSharpCC.NUnit;

[TestFixture]
[NonParallelizable]
public class ResourceOwnershipTest {
    private string directory = string.Empty;
    private const string ChecksumMarker = "/* CSharpCC - OriginalChecksum=";
    private const string Grammar = """
        PARSER_BEGIN(OwnedParser)
        namespace Owned;
        public class OwnedParser {}
        PARSER_END(OwnedParser)
        TOKEN: { < WORD: "word" > }
        void Input() : {} { <WORD> <EOF> }
        """;

    [SetUp]
    public void SetUp() {
        directory = Path.Combine(Path.GetTempPath(), "csharpcc ownership " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        Options.init();
        CSharpCCErrors.ReInit();
        CSharpCCGlobals.ReInit();
    }

    [TearDown]
    public void TearDown() {
        Options.init();
        CSharpCCErrors.ReInit();
        CSharpCCGlobals.ReInit();
        Directory.Delete(directory, recursive: true);
    }

    [TestCase("Close")]
    [TestCase("Dispose")]
    [TestCase("DisposeAsync")]
    [TestCase("OwnerClose")]
    public async Task WriterCompletionWritesExactlyOneValidChecksum(string completion) {
        string path = Path.Combine(directory, "Token.cs");
        using var owner = new OutputFile(path);
        var writer = owner.GetTextWriter();
        writer.WriteLine("// æøå: content included in the checksum");
        switch (completion) {
            case "Close": writer.Close(); break;
            case "Dispose": writer.Dispose(); break;
            case "DisposeAsync": await writer.DisposeAsync(); break;
            case "OwnerClose": owner.Close(); break;
        }
        writer.Close();
        writer.Dispose();
        owner.Close();
        owner.Dispose();

        string text = File.ReadAllText(path);
        int start = text.IndexOf(ChecksumMarker, StringComparison.Ordinal);
        Assert.That(start, Is.GreaterThanOrEqualTo(0));
        Assert.That(text.LastIndexOf(ChecksumMarker, StringComparison.Ordinal), Is.EqualTo(start));
        string checksum = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(text[..start]))).ToLowerInvariant();
        Assert.That(text[start..], Is.EqualTo(ChecksumMarker + checksum + " (do not edit this line) */" + Environment.NewLine));
        Assert.That(() => owner.GetTextWriter(), Throws.TypeOf<ObjectDisposedException>());
        AssertReleased(path);
        using var nextGeneration = new OutputFile(path);
        Assert.That(nextGeneration.needToWrite, Is.True);
    }

    [Test]
    public void FailedTemplateGenerationReleasesFileWithoutCertifyingPartialOutput() {
        string path = Path.Combine(directory, "Token.cs");
        Assert.Throws<IOException>(() => {
            using var owner = new OutputFile(path);
            var writer = owner.GetTextWriter();
            writer.WriteLine("partial output");
            var generator = new CSharpFileGenetor("Missing.Template", Options.getOptions());
            generator.Generate(writer);
            owner.Close();
        });
        Assert.That(File.ReadAllText(path), Does.Not.Contain(ChecksumMarker));
        AssertReleased(path);
    }

    [Test]
    public void ClosingABrokenWriterReportsFailureAndStillAllowsRepeatedCleanup() {
        string path = Path.Combine(directory, "Token.cs");
        using var owner = new OutputFile(path);
        var writer = (StreamWriter)owner.GetTextWriter();
        writer.WriteLine("pending output");
        writer.BaseStream.Dispose();
        Assert.Throws<ObjectDisposedException>(() => writer.Close());
        Assert.DoesNotThrow(() => writer.Dispose());
        Assert.DoesNotThrow(() => owner.Dispose());
        AssertReleased(path);
    }

    [Test]
    public void DisposingAnUnopenedOwnerDoesNotCreateAFile() {
        string path = Path.Combine(directory, "Token.cs");
        var owner = new OutputFile(path);
        owner.Dispose();
        owner.Dispose();
        Assert.That(File.Exists(path), Is.False);
        Assert.That(() => owner.GetTextWriter(), Throws.TypeOf<ObjectDisposedException>());
    }

    [TestCase(false)]
    [TestCase(true)]
    public void TemplateGenerationLeavesCallerOwnedReadersAndWritersOpen(bool fail) {
        var generator = new CSharpFileGenetor("unused", new Dictionary<string, object> {
            ["ENABLED"] = true, ["NAME"] = "æøå"
        });
        using var input = new TrackingReader(fail ? "#if true\nunfinished\n" : "#if ENABLED\n${NAME}\n#else\nunused\n#fi\n");
        using var output = new TrackingWriter();
        if (fail)
            Assert.Throws<IOException>(() => generator.Generate(input, output));
        else {
            generator.Generate(input, output);
            Assert.That(output.ToString(), Is.EqualTo("æøå" + Environment.NewLine));
        }
        Assert.That(input.Disposed, Is.False);
        Assert.That(output.Disposed, Is.False);
        output.Write("still open");
        Assert.DoesNotThrow(() => input.Read());
    }

    [TestCase("${VALUE:-fallback}", "fallback")]
    [TestCase("${VALUE?yes:no}", "no")]
    public void TemplateValuesWithNoTextHaveDefinedFallbacks(string template, string expected) {
        var generator = new CSharpFileGenetor("unused", new Dictionary<string, object> { ["VALUE"] = new NoText() });
        using var input = new StringReader(template);
        using var output = new StringWriter();
        generator.Generate(input, output);
        Assert.That(output.ToString(), Is.EqualTo(expected + Environment.NewLine));
    }

    [TestCase("#if true\nmissing end\n")]
    [TestCase("${UNFINISHED")]
    [TestCase("${FLAG?missing-colon}")]
    public void TemplateGeneratorCanBeReusedAfterFailure(string invalidTemplate) {
        var generator = new CSharpFileGenetor("unused", Options.getOptions());
        using var invalid = new StringReader(invalidTemplate);
        using var valid = new StringReader("valid");
        using var output = new StringWriter();
        Assert.Throws<IOException>(() => generator.Generate(invalid, output));
        output.GetStringBuilder().Clear();
        generator.Generate(valid, output);
        Assert.That(output.ToString(), Is.EqualTo("valid" + Environment.NewLine));
    }

    [TestCase("success", 0)]
    [TestCase("syntax-error", 1)]
    [TestCase("semantic-error", 1)]
    public void CliReleasesInputAndOutputFilesBeforeReturning(string scenario, int expectedExitCode) {
        string source = scenario switch {
            "syntax-error" => "not a grammar " + new string('x', 20000),
            "semantic-error" => Grammar.Replace("<WORD> <EOF>", "Missing() <EOF>"),
            _ => Grammar
        };
        string input = Path.Combine(directory, "Parser.cc");
        File.WriteAllText(input, source);
        int result = CliProgram.MainProgram(["-STATIC=false", "-OUTPUT_DIRECTORY=" + directory, input]);
        Assert.That(result, Is.EqualTo(expectedExitCode));
        foreach (var path in Directory.GetFiles(directory))
            AssertReleased(path);
    }

    [Test]
    public void InvalidEncodingDoesNotLeaveTheInputFileOpen() {
        string input = Path.Combine(directory, "Parser.cc");
        File.WriteAllText(input, Grammar);
        Assert.Throws<ArgumentException>(() => CliProgram.MainProgram(["-GRAMMAR_ENCODING=not-an-encoding", input]));
        AssertReleased(input);
    }

    [Test]
    public void GenerationFailureReleasesTheCliInputAndEarlierOutputs() {
        string input = Path.Combine(directory, "Parser.cc");
        File.WriteAllText(input, Grammar);
        Directory.CreateDirectory(Path.Combine(directory, "OwnedParserTokenManager.cs"));
        Assert.That(() => CliProgram.MainProgram(["-STATIC=false", "-OUTPUT_DIRECTORY=" + directory, input]), Throws.Exception);
        foreach (var path in Directory.GetFiles(directory))
            AssertReleased(path);
    }

    [TestCase("parser", "OwnedParser.cs")]
    [TestCase("lexer", "OwnedParserTokenManager.cs")]
    [TestCase("constants", "OwnedParserConstants.cs")]
    public void GeneratorFailuresAfterOpeningAnOutputStillReleaseIt(string generator, string fileName) {
        Options.SetCmdLineOption("OUTPUT_DIRECTORY=" + directory);
        CSharpCCGlobals.cu_name = "OwnedParser";
        // Fault injection: each generator copies this list after opening its
        // output. Force that copy to throw so cleanup is tested mid-generation.
        CSharpCCGlobals.ToolNames = null!;
        System.Action generate = generator switch {
            "parser" => ParseGen.start,
            "lexer" => LexGen.start,
            _ => OtherFilesGen.start
        };
        Assert.Throws<ArgumentNullException>(generate);
        string path = Path.Combine(directory, fileName);
        Assert.That(File.Exists(path), Is.True);
        AssertReleased(path);
    }

    private static void AssertReleased(string path) {
        using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None)) { }
        string renamed = path + ".renamed";
        File.Move(path, renamed);
        File.Move(renamed, path);
    }

    private sealed class TrackingReader(string text) : StringReader(text) {
        public bool Disposed { get; private set; }
        protected override void Dispose(bool disposing) {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class TrackingWriter : StringWriter {
        public bool Disposed { get; private set; }
        protected override void Dispose(bool disposing) {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class NoText {
        public override string? ToString() => null;
    }
}
