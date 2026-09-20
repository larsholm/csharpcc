#nullable enable

using System;
using System.Threading.Tasks;
using Deveel.CSharpCC.Parser;
using NUnit.Framework;

namespace Deveel.CSharpCC.NUnit;

[TestFixture]
[NonParallelizable]
public class GrammarModelTest {
    [TestCase("é", "\\u00e9")]
    [TestCase("\u0001", "\\u0001")]
    [TestCase("😀", "\\ud83d\\ude00")]
    public void EscapingPreservesNonAsciiUtf16CodeUnits(string value, string expected) {
        Assert.That(CSharpCCGlobals.AddEscapes(value), Is.EqualTo(expected));
        Assert.That(CSharpCCGlobals.AddUnicodeEscapes(value), Is.EqualTo(expected));
    }

    [Test]
    public void UnicodeEscapingPreservesExistingBackslashEscapes() {
        Assert.That(CSharpCCGlobals.AddUnicodeEscapes("a\\b"), Is.EqualTo("a\\b"));
    }

    [TestCase("2147483648")]
    [TestCase("999999999999999999999999999999999999999")]
    [TestCase("0x80000000")]
    public async Task OutOfRangeGrammarIntegersProduceSourceDiagnostics(string literal) {
        string grammar = "options { LOOKAHEAD = " + literal + "; }\n" + """
            PARSER_BEGIN(FixtureParser)
            namespace Fixture;
            using System;
            public class FixtureParser {}
            PARSER_END(FixtureParser)
            void Input() : {} { <EOF> }
            """;
        using var fixture = new ParserFixture();
        var result = await fixture.Generate(grammar, "STATIC=false");
        Assert.That(result.ExitCode, Is.EqualTo(1), result.Output);
        Assert.That(result.Output, Does.Contain("Line 1, Column 23: Integer literal must be between 0 and 2147483647."));
        Assert.That(result.Output, Does.Not.Contain("OverflowException").And.Not.Contain("InvalidOperationException"));
    }

    [Test]
    public async Task PrivateLexicalFragmentsCanBeReferencedByPublicTokens() {
        const string grammar = """
            PARSER_BEGIN(FixtureParser)
            namespace Fixture;
            using System;
            public class FixtureParser {}
            PARSER_END(FixtureParser)
            TOKEN: { < #LETTER: ["a"-"z"] > | < WORD: (<LETTER>)+ > }
            void Input() : {} { <WORD> <EOF> }
            """;
        using var fixture = new ParserFixture();
        var result = await fixture.Generate(grammar, "STATIC=false");
        Assert.That(result.ExitCode, Is.Zero, result.Output);
    }

    [Test]
    public void ProductionRelationshipsAndCallTokensStartEmpty() {
        var production = new BnfProduction();
        var call = new NonTerminal();
        Assert.That(production.Parents, Is.Not.Null.And.Empty);
        Assert.That(production.LeftExpansions, Is.Not.Null.And.Empty);
        Assert.That(call.ArgumentTokens, Is.Not.Null.And.Empty);
        Assert.That(call.LhsTokens, Is.Not.Null.And.Empty);
        Assert.That(call.Production, Is.Null, "Production references are resolved during semantic analysis.");
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task GeneratedParsersResolveProductionCallsAndPreserveArguments(bool isStatic) {
        const string grammar = """
            PARSER_BEGIN(FixtureParser)
            namespace Fixture;
            using System;
            public class FixtureParser {}
            PARSER_END(FixtureParser)
            SKIP: { " " }
            TOKEN: { < WORD: (["a"-"z"])+ > }
            int Input() : { int total = 0; int weight; }
            { ( weight=Entry(2) { total += weight; } )+ <EOF> { return total; } }
            int Entry(int weight) : {}
            { <WORD> { return weight; } }
            """;
        var driver = $$"""
            using System;
            using System.IO;
            using Fixture;
            class Program {
                static int Main(string[] args) {
                    var parser = new FixtureParser(new StringReader(args[0]));
                    try {
                        Console.Write({{(isStatic ? "FixtureParser" : "parser")}}.Input());
                        return 0;
                    } catch (ParseException) { Console.Write("parse-error"); return 2; }
                }
            }
            """;
        using var fixture = new ParserFixture();
        await fixture.GenerateAndBuild(grammar, driver, $"STATIC={isStatic}");
        var valid = await fixture.Run("one two three");
        Assert.That(valid.ExitCode, Is.Zero, valid.Output);
        Assert.That(valid.Output, Is.EqualTo("6"));
        var invalid = await fixture.Run("");
        Assert.That(invalid.ExitCode, Is.EqualTo(2), invalid.Output);
        Assert.That(invalid.Output, Is.EqualTo("parse-error"));
    }

    [TestCase("void Input() : {} { Input() }", "Left recursion detected")]
    [TestCase("void Input() : {} { Other() } void Other() : {} { Input() }", "Left recursion detected")]
    [TestCase("void Input() : {} { Missing() }", "Non-terminal Missing has not been defined")]
    [TestCase("TOKEN: { < WORD: <MISSING> > } void Input() : {} { <WORD> <EOF> }", "Undefined lexical token name")]
    [TestCase("TOKEN: { < #LETTER: \"a\" > } void Input() : {} { <LETTER> <EOF> }", "refers to a private")]
    [TestCase("TOKEN: { < FIRST: <SECOND> > | < SECOND: <FIRST> > } void Input() : {} { <FIRST> <EOF> }", "Loop in regular expression detected")]
    public async Task InvalidReferencesAndRecursionProduceGrammarDiagnostics(string productions, string expected) {
        string grammar = """
            PARSER_BEGIN(FixtureParser)
            namespace Fixture;
            using System;
            public class FixtureParser {}
            PARSER_END(FixtureParser)
            """ + Environment.NewLine + productions;
        using var fixture = new ParserFixture();
        var result = await fixture.Generate(grammar, "STATIC=false");
        Assert.That(result.ExitCode, Is.EqualTo(1), result.Output);
        Assert.That(result.Output, Does.Contain(expected));
        Assert.That(result.Output, Does.Not.Contain("NullReferenceException"));
        Assert.That(result.Output, Does.Not.Contain("InvalidOperationException"));
    }

    [TestCase("void Input() : {} { (\"a\" | \"a\" \"b\") <EOF> }", "Choice conflict involving")]
    [TestCase("void Input() : {} { Entry() \"a\" <EOF> } void Entry() : {} { [\"a\"] }", "Choice conflict in")]
    [TestCase("void Input() : {} { (\"a\")* \"a\" <EOF> }", "Choice conflict in")]
    public async Task AmbiguityDiagnosticsRetainTheirCommonPrefix(string productions, string expected) {
        string grammar = """
            PARSER_BEGIN(FixtureParser)
            namespace Fixture;
            using System;
            public class FixtureParser {}
            PARSER_END(FixtureParser)
            """ + Environment.NewLine + productions;
        using var fixture = new ParserFixture();
        // Exercise semantic analysis independently of code emission for ambiguous grammars.
        var result = await fixture.Generate(grammar, "STATIC=false", "BUILD_PARSER=false", "BUILD_TOKEN_MANAGER=false");
        Assert.That(result.ExitCode, Is.Zero, result.Output);
        Assert.That(result.Output, Does.Contain(expected));
        Assert.That(result.Output, Does.Contain("is: \"a\""));
    }

    [Test]
    public void AnUnresolvedTokenReferenceCannotBuildAnNfa() {
        var reference = new RJustName(new Token(), "UNRESOLVED");
        Assert.That(reference.RegularExpression, Is.Null);
        Assert.That(() => reference.GenerateNfa(false), Throws.TypeOf<InvalidOperationException>()
            .With.Message.Contains("has not been initialized"));
        Assert.That(new REndOfFile().GenerateNfa(false), Is.Null, "EOF is not a character-matching NFA.");
    }

    [Test]
    public async Task CodeProductionsCanHaveNoExpansionTree() {
        const string grammar = """
            PARSER_BEGIN(FixtureParser)
            namespace Fixture;
            using System;
            public class FixtureParser {}
            PARSER_END(FixtureParser)
            <*> TOKEN: { < A: "a" > }
            CODE int Value() { return 7; }
            int Input() : { int result; } { <A> result=Value() <EOF> { return result; } }
            """;
        const string driver = """
            using System;
            using System.IO;
            using Fixture;
            class Program {
                static void Main() { Console.Write(new FixtureParser(new StringReader("a")).Input()); }
            }
            """;
        using var fixture = new ParserFixture();
        await fixture.GenerateAndBuild(grammar, driver, "STATIC=false");
        var result = await fixture.Run("");
        Assert.That(result.ExitCode, Is.Zero, result.Output);
        Assert.That(result.Output, Is.EqualTo("7"));
    }
}
