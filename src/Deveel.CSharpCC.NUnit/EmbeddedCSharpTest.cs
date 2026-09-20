#nullable enable

using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Deveel.CSharpCC.Parser;
using NUnit.Framework;

namespace Deveel.CSharpCC.NUnit;

[TestFixture(false)]
[TestFixture(true)]
public class EmbeddedCSharpTest(bool modernOutput) {
    private const string Header = """
        PARSER_BEGIN(FixtureParser)
        namespace Fixture;
        using System;
        public class FixtureParser {}
        PARSER_END(FixtureParser)

        """;

    [TestCase(false)]
    [TestCase(true)]
    public async Task ActionBlocksPreserveModernExpressionsAndStringDelimiters(bool isStatic) {
        const string productions = """"
            string Input() : {
                int[] values = [1, 2, 3];
                System.Text.StringBuilder builder = new();
                Func<int, int> twice = x => { return x * 2; };
                int Local(int x) { return x + 1; }
            } {
                <EOF> {
                    string? text = null;
                    text ??= "ok";
                    var match = values switch { [1, .., 3] => Local(twice(2)), _ => 0 };
                    var raw = """
                        } PARSER_END(FixtureParser) { "quoted"
                        """;
                    var interpolated = $"{text}:{match}:{raw.Contains("PARSER_END")}";
                    // } PARSER_END(FixtureParser) is part of this comment.
                    builder.Append(interpolated);
                    return builder.ToString();
                }
            }
            """";
        using var fixture = new ParserFixture { ModernOutput = modernOutput };
        await fixture.GenerateAndBuild(Header + productions, Driver(isStatic), $"STATIC={isStatic}");
        var result = await fixture.Run("");
        Assert.That(result.ExitCode, Is.Zero, result.Output);
        Assert.That(result.Output.Trim(), Is.EqualTo("ok:5:True"));
    }

    [Test]
    public async Task NestedNamespacesKeepAliasesAndSupplyBoilerplateImports() {
        const string grammar = """
            PARSER_BEGIN(FixtureParser)
            namespace Outer {
                using Text = System.String;
                namespace Fixture {
                    public class FixtureParser { public Text Message => "nested"; }
                }
            }
            PARSER_END(FixtureParser)
            string Input() : {} { <EOF> { return Message; } }
            """;
        using var fixture = new ParserFixture { ModernOutput = modernOutput };
        await fixture.GenerateAndBuild(grammar, Driver(false).Replace("using Fixture;", "using Outer.Fixture;"), "STATIC=false");
        var result = await fixture.Run("");
        Assert.That(result.ExitCode, Is.Zero, result.Output);
        Assert.That(result.Output.Trim(), Is.EqualTo("nested"));
    }

    [Test]
    public async Task NestedGenericNullableTypesSurviveProductionSignatures() {
        const string productions = """
            System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<int?>>? Input(
                System.Collections.Generic.Dictionary<string, System.Collections.Generic.List<int?>>? seed = null) : {} {
                <EOF> { return seed ?? new() { ["x"] = [1, null] }; }
            }
            """;
        const string driver = """
            using System;
            using System.IO;
            using Fixture;
            class Program {
                static void Main() {
                    var parser = new FixtureParser(new StringReader(""));
                    Console.WriteLine(parser.Input()?["x"].Count);
                }
            }
            """;
        using var fixture = new ParserFixture { ModernOutput = modernOutput };
        await fixture.GenerateAndBuild(Header + productions, driver, "STATIC=false");
        var result = await fixture.Run("");
        Assert.That(result.ExitCode, Is.Zero, result.Output);
        Assert.That(result.Output.Trim(), Is.EqualTo("2"));
        Assert.That(Directory.GetFiles(Path.Combine(fixture.DirectoryPath, "bin", "Debug", "net10.0"), "Microsoft.CodeAnalysis*.dll"), Is.Empty);
    }

    [Test]
    public async Task ModernSignaturesArgumentsAndSemanticLookaheadCompile() {
        const string productions = """
            string? Input() : { string? result; } {
                LOOKAHEAD({ new[] { 1, 2 } is [1, ..] })
                result = Entry([1, 2], value => value + 1) <EOF> { return result; }
            }
            string? Entry(System.Collections.Generic.List<int> values, Func<int, int> map) : {} {
                { return $"{map(values[0])}:{values.Count}"; }
            }
            """;
        using var fixture = new ParserFixture { ModernOutput = modernOutput };
        await fixture.GenerateAndBuild(Header + productions, Driver(false), "STATIC=false");
        var result = await fixture.Run("");
        Assert.That(result.ExitCode, Is.Zero, result.Output);
        Assert.That(result.Output.Trim(), Is.EqualTo("2:2"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CompilationUnitsPreserveModernHelpersAndNamespaceScopes(bool fileScoped) {
        string unit = """"
            PARSER_BEGIN(FixtureParser)
            global using Numbers = System.Collections.Generic.List<int>;
            using System;
            using static System.Math;
            NAMESPACE
            using Text = System.String;
            public record Payload(int Value) { public required Text Name { get; init; } }
            public record PARSER_END(FixtureParser Value);
            public class Holder(int initial) {
                public int Value { get; set; } = initial;
                public string Label { get; set => field = value.Trim(); } = "";
            }
            public static class Helpers {
                extension(Payload value) { public int Twice() => value.Value * 2; }
            }
            public class FixtureParser : IDisposable {
                public void Dispose() {}
                public string Marker() => """} PARSER_END(FixtureParser) {""";
            }
            END_NAMESPACE
            PARSER_END(FixtureParser)
            string Input() : {} { <EOF> {
                Numbers numbers = [3];
                var payload = new Payload(numbers[0]) { Name = "ok" };
                Holder? holder = new(Abs(-2));
                holder?.Value = payload.Twice();
                holder ??= new(0);
                holder.Label = " trimmed ";
                return $"{holder.Value}:{holder.Label}:{Marker().Contains("PARSER_END")}";
            } }
            """";
        unit = unit.Replace("END_NAMESPACE", fileScoped ? "" : "}")
            .Replace("NAMESPACE", fileScoped ? "namespace Fixture;" : "namespace Fixture {");
        using var fixture = new ParserFixture { ModernOutput = modernOutput };
        await fixture.GenerateAndBuild(unit, Driver(false), "STATIC=false");
        var result = await fixture.Run("");
        Assert.That(result.ExitCode, Is.Zero, result.Output);
        Assert.That(result.Output.Trim(), Is.EqualTo("6:trimmed:True"));
    }

    [Test]
    public async Task LongFragmentsAndWhitespaceCannotPrematurelyEndTheCSharpBoundary() {
        string payload = new('x', 1400);
        string spaces = new(' ', 1300);
        string productions = $$""""
            TOKEN: { < A: "a" > }
            string Input(string marker = "{{payload}}") : {} {
                ( LOOKAHEAD({ marker.Length > 0 {{spaces}} && marker.Length < 0 }) <A> { return "wrong branch"; }
                | <A> { return marker.Length + ":" + """{{payload}} } PARSER_END(FixtureParser)""".Length; } )
            }
            """";
        using var fixture = new ParserFixture { ModernOutput = modernOutput };
        await fixture.GenerateAndBuild(Header + productions, Driver(false), "STATIC=false");
        var result = await fixture.Run("a");
        Assert.That(result.ExitCode, Is.Zero, result.Output);
        Assert.That(result.Output.Trim(), Is.EqualTo("1400:" + (payload + " } PARSER_END(FixtureParser)").Length));
    }

    [Test]
    public async Task TokenManagerMembersAndCodeProductionsAcceptModernSyntax() {
        const string productions = """"
            TOKEN_MGR_DECLS: {
                public record Entry(int Count);
                public Entry Current { get; private set; } = new(0);
                public void CommonTokenAction(Token t) => Current = new(Current.Count + 1);
                public string Description => $"{Current.Count}:" + """} PARSER_END(FixtureParser)""";
            }
            TOKEN: { < A: "a" > { int[] values = [1]; _ = values switch { [1] => true, _ => false }; } }
            CODE string Compute() { Func<string> value = () => "ok"; return value(); }
            string Input() : { string value; } {
                <A> value = Compute() <EOF> {
                    return value + ":" + (tokenSource == null ? "missing" : tokenSource.Description);
                }
            }
            """";
        using var fixture = new ParserFixture { ModernOutput = modernOutput };
        await fixture.GenerateAndBuild(Header + productions, Driver(false), "STATIC=false", "COMMON_TOKEN_ACTION=true");
        var result = await fixture.Run("a");
        Assert.That(result.ExitCode, Is.Zero, result.Output);
        Assert.That(result.Output.Trim(), Is.EqualTo("ok:2:} PARSER_END(FixtureParser)"));
    }

    [Test]
    public async Task CompilationUnitSymbolsApplyToActionsAndTokenManagerMembers() {
        string header = Header.Replace("namespace Fixture;", "#define FEATURE\nnamespace Fixture;");
        const string production = """
            TOKEN_MGR_DECLS: {
            #if FEATURE
                public string Value => "ok";
            #else
                unparseable { "text
            #endif
            }
            string Input() : {} { <EOF> {
            #if FEATURE
                return tokenSource == null ? "missing" : tokenSource.Value;
            #else
                invalid } "text
            #endif
            } }
            """;
        using var fixture = new ParserFixture { ModernOutput = modernOutput };
        await fixture.GenerateAndBuild(header + production, Driver(false), "STATIC=false");
        var result = await fixture.Run("");
        Assert.That(result.ExitCode, Is.Zero, result.Output);
        Assert.That(result.Output.Trim(), Is.EqualTo("ok"));
    }

    [Test]
    public void ReaderReinitializationRetainsSourceBoundariesAndTokenManagerCompatibility() {
        using var first = new StringReader("{ var text = $\"first\"; }");
        var parser = new CSharpCCParser(first);
        var tokens = new List<Token>();
        parser.Block(tokens);
        Assert.That(tokens[0].image, Does.Contain("$\"first\""));
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("{ int[] values = [1, 2]; }"));
        parser.ReInit(stream);
        tokens.Clear();
        parser.Block(tokens);
        Assert.That(tokens[0].image, Does.Contain("[1, 2]"));
        Assert.That(stream.CanRead, Is.True);
        using var legacy = new StringReader("{ int answer = 42; }");
        parser.ReInit(new CSharpCCParserTokenManager(new CSharpCharStream(legacy)));
        tokens.Clear();
        parser.Block(tokens);
        Assert.That(tokens.Exists(t => t.image == "42"), Is.True);
    }

    [TestCase("Func<int, int> invalid = x => ;")]
    [TestCase("var invalid = value switch { 1 => }; ")]
    [TestCase("string invalid = $\"{1 + }\";")]
    [TestCase("value?.Name = ;")]
    [TestCase("System.Text.StringBuilder value = new(;")]
    public async Task MalformedModernActionsAreRejected(string body) {
        using var fixture = new ParserFixture { ModernOutput = modernOutput };
        var result = await fixture.Generate(Header + "void Input() : {} { <EOF> { " + body + " } }", "STATIC=false");
        Assert.That(result.ExitCode, Is.EqualTo(1), result.Output);
        Assert.That(result.Output, Does.Contain("Embedded C# CS"));
    }

    [TestCase("public record Broken(string Name { }")]
    [TestCase("public class Broken { public required string Name { get; init } }")]
    [TestCase("public class Broken { public string Name { get; set => field = ; } }")]
    [TestCase("public class Broken(int value = ) {}")]
    [TestCase("public static class Broken { extension(string value) { public bool Empty => ; } }")]
    public async Task MalformedHelperDeclarationsAreRejected(string declaration) {
        using var fixture = new ParserFixture { ModernOutput = modernOutput };
        var result = await fixture.Generate(Header.Replace("public class FixtureParser {}", declaration + "\npublic class FixtureParser {}") +
            "void Input() : {} { <EOF> }", "STATIC=false");
        Assert.That(result.ExitCode, Is.EqualTo(1), result.Output);
        Assert.That(result.Output, Does.Contain("Embedded C# CS"));
    }

    [TestCase("public class FixtureParser<T> {}", "non-generic class")]
    [TestCase("public class FixtureParser(int value) {}", "primary constructor")]
    [TestCase("public class Other {}", "Parser class has not been defined")]
    [TestCase("public class FixtureParser;", "body with braces")]
    [TestCase("public partial class FixtureParser {} public partial class FixtureParser {}", "Multiple declaration")]
    public async Task UnsupportedParserShapesHaveExplicitDiagnostics(string declaration, string message) {
        using var fixture = new ParserFixture { ModernOutput = modernOutput };
        var result = await fixture.Generate(Header.Replace("public class FixtureParser {}", declaration) +
            "void Input() : {} { <EOF> }", "STATIC=false");
        Assert.That(result.ExitCode, Is.EqualTo(1), result.Output);
        Assert.That(result.Output, Does.Contain(message));
    }

    [TestCase("", "Expected PARSER_END")]
    [TestCase("PARSER_END(Other)", "must be the same as that used at PARSER_BEGIN")]
    public async Task InvalidCompilationUnitDelimitersAreRejected(string marker, string message) {
        using var fixture = new ParserFixture { ModernOutput = modernOutput };
        var result = await fixture.Generate(Header.Replace("PARSER_END(FixtureParser)", marker) +
            "void Input() : {} { <EOF> }", "STATIC=false");
        Assert.That(result.ExitCode, Is.EqualTo(1), result.Output);
        Assert.That(result.Output, Does.Contain(message));
    }

    [TestCase("void Input(string? value = ) : {} { <EOF> }")]
    [TestCase("void Input() : {} { LOOKAHEAD({ value switch { 1 => } }) <EOF> }")]
    [TestCase("void Input() : {} { Entry([1,,2]) <EOF> } void Entry(int[] values) : {} { <EOF> }")]
    public async Task MalformedSignaturesAndExpressionsAreRejected(string production) {
        using var fixture = new ParserFixture { ModernOutput = modernOutput };
        var result = await fixture.Generate(Header + production, "STATIC=false");
        Assert.That(result.ExitCode, Is.EqualTo(1), result.Output);
        Assert.That(result.Output, Does.Contain("Embedded C# CS"));
    }

    [Test]
    public async Task InactivePreprocessorTextDoesNotEndAnAction() {
        const string production = """
            string Input() : {} { <EOF> {
            #if NEVER_DEFINED
                } "unterminated PARSER_END(FixtureParser)
            #endif
                return "ok";
            } }
            """;
        using var fixture = new ParserFixture { ModernOutput = modernOutput };
        await fixture.GenerateAndBuild(Header + production, Driver(false), "STATIC=false");
        var result = await fixture.Run("");
        Assert.That(result.ExitCode, Is.Zero, result.Output);
        Assert.That(result.Output.Trim(), Is.EqualTo("ok"));
    }

    [TestCase("int[] values = [1, ;", "CS1003", 7, 28)]
    [TestCase("var invalid = ;", "CS1525", 7, 23)]
    [TestCase("var raw = \"\"\"unterminated;", "CS8997", 7, 35)]
    public async Task InvalidEmbeddedSyntaxReportsGrammarCoordinates(string body, string diagnostic, int line, int column) {
        using var fixture = new ParserFixture { ModernOutput = modernOutput };
        var result = await fixture.Generate(Header + "void Input() : {} { <EOF> {\n\t" + body + "\n} }", "STATIC=false");
        Assert.That(result.ExitCode, Is.EqualTo(1), result.Output);
        Assert.That(result.Output, Does.Contain($"Line {line}, Column {column}: Embedded C# {diagnostic}"));
        Assert.That(result.Output, Does.Not.Contain(" at Deveel."));
    }

    private static string Driver(bool isStatic) => $$"""
        using System;
        using System.IO;
        using Fixture;
        class Program {
            static void Main(string[] args) {
                var parser = new FixtureParser(new StringReader(args[0]));
                Console.WriteLine({{(isStatic ? "FixtureParser" : "parser")}}.Input());
            }
        }
        """;
}
