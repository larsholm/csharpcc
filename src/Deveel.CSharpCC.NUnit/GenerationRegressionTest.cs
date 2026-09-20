#nullable enable

using System.Threading.Tasks;
using NUnit.Framework;

namespace Deveel.CSharpCC.NUnit;

[TestFixture(false)]
[TestFixture(true)]
public class GenerationRegressionTest(bool modernOutput) {
    private const string Header = """
        PARSER_BEGIN(FixtureParser)
        namespace Fixture;
        using System;
        public class FixtureParser {}
        PARSER_END(FixtureParser)

        """;

    [TestCase(false)]
    [TestCase(true)]
    public async Task OptionalBranchesCanFallThrough(bool isStatic) {
        const string production = """
            TOKEN: { < A: "a" > | < B: "b" > | < C: "c" > }
            void Input() : {} { [ <A> | LOOKAHEAD(2) <B> <C> ] <EOF> }
            """;
        using var fixture = new ParserFixture { ModernOutput = modernOutput };
        await fixture.GenerateAndBuild(Header + production, Driver(isStatic), $"STATIC={isStatic}");
        await Expect(fixture, "", "ok");
        await Expect(fixture, "a", "ok");
        await Expect(fixture, "bc", "ok");
        await Expect(fixture, "b", "parse-error");
        await Expect(fixture, "aa", "parse-error");
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task EofOnlyLexersAcceptEmptyInputAndRejectCharacters(bool isStatic) {
        using var fixture = new ParserFixture { ModernOutput = modernOutput };
        await fixture.GenerateAndBuild(Header + "void Input() : {} { <EOF> }", Driver(isStatic), $"STATIC={isStatic}");
        await Expect(fixture, "", "ok");
        await Expect(fixture, "a", "lexical-error");
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task InlineTokenKindsAreIndependentOfSequencePositions(bool isStatic) {
        const string production = """
            void Input() : {} { ( LOOKAHEAD(2) "a" "b" | "a" ) <EOF> }
            """;
        using var fixture = new ParserFixture { ModernOutput = modernOutput };
        await fixture.GenerateAndBuild(Header + production, Driver(isStatic), $"STATIC={isStatic}");
        await Expect(fixture, "a", "ok");
        await Expect(fixture, "ab", "ok");
        await Expect(fixture, "b", "parse-error");
        await Expect(fixture, "aa", "parse-error");
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task LookaheadCanExpandCallsToOtherProductions(bool isStatic) {
        const string production = """
            void Input() : {} { ( LOOKAHEAD(3) Entry() "c" | Entry() ) <EOF> }
            void Entry() : {} { "a" "b" }
            """;
        using var fixture = new ParserFixture { ModernOutput = modernOutput };
        await fixture.GenerateAndBuild(Header + production, Driver(isStatic), $"STATIC={isStatic}");
        await Expect(fixture, "ab", "ok");
        await Expect(fixture, "abc", "ok");
        await Expect(fixture, "ac", "parse-error");
        await Expect(fixture, "abcc", "parse-error");
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task DebugOutputSupportsNfaTablesAndLookahead(bool isStatic) {
        const string production = """
            TOKEN: { < WORD: (["a"-"z"])+ > }
            void Input() : {} { ( LOOKAHEAD(2) Entry() ":" Entry() | Entry() ) <EOF> }
            void Entry() : {} { <WORD> }
            """;
        using var fixture = new ParserFixture { ModernOutput = modernOutput };
        await fixture.GenerateAndBuild(Header + production, Driver(isStatic), $"STATIC={isStatic}",
            "DEBUG_LOOKAHEAD=true", "DEBUG_TOKEN_MANAGER=true");
        await Expect(fixture, "one", "ok");
        await Expect(fixture, "one:two", "ok");
        await Expect(fixture, "one:", "parse-error");
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task LexicalActionsCanUseAccumulatedImages(bool isStatic) {
        const string production = """
            SKIP: { " " { if (image.Length != 1) throw new Exception("skip image"); } }
            MORE: { "[" { if (image.Length != 1) throw new Exception("more image"); } : CONTENT }
            <CONTENT> MORE: { < (["a"-"z"])+ > { if (image.Length < 2) throw new Exception("content image"); } }
            <CONTENT> TOKEN: { < WORD: "]" > { matchedToken.Image = image.ToString(); } : DEFAULT }
            Token Input() : { Token t; } { t=<WORD> <EOF> { return t; } }
            """;
        string driver = $$"""
            using System;
            using System.IO;
            using Fixture;
            class Program {
                static void Main(string[] args) {
                    var parser = new FixtureParser(new StringReader(args[0]));
                    Console.Write({{(isStatic ? "FixtureParser" : "parser")}}.Input().Image);
                }
            }
            """;
        using var fixture = new ParserFixture { ModernOutput = modernOutput };
        await fixture.GenerateAndBuild(Header + production, driver, $"STATIC={isStatic}");
        await Expect(fixture, " [hello]", "[hello]");
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task OptionalLookaheadKeepsItsParentAndFirstToken(bool isStatic) {
        const string production = """
            void Input() : {} { [ LOOKAHEAD(1) "a" ] [ LOOKAHEAD(2) "b" "c" ] <EOF> }
            """;
        using var fixture = new ParserFixture { ModernOutput = modernOutput };
        await fixture.GenerateAndBuild(Header + production, Driver(isStatic), $"STATIC={isStatic}");
        foreach (string input in new[] { "", "a", "bc", "abc" })
            await Expect(fixture, input, "ok");
        foreach (string input in new[] { "b", "c", "bca" })
            await Expect(fixture, input, "parse-error");
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task NonChoiceLookaheadDoesNotReplaceTheFirstConsumedToken(bool isStatic) {
        const string production = """
            void Input() : {} { LOOKAHEAD(2) "a" "b" <EOF> }
            """;
        using var fixture = new ParserFixture { ModernOutput = modernOutput };
        await fixture.GenerateAndBuild(Header + production, Driver(isStatic), $"STATIC={isStatic}");
        await Expect(fixture, "ab", "ok");
        await Expect(fixture, "b", "parse-error");
        await Expect(fixture, "a", "parse-error");
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task OverlappingNfaAlternativesIgnoreRemovedStates(bool isStatic) {
        const string production = """
            TOKEN: { < WORD: (["a"-"z"])+ | "a" (["a"-"z"])* > }
            void Input() : {} { <WORD> <EOF> }
            """;
        using var fixture = new ParserFixture { ModernOutput = modernOutput };
        await fixture.GenerateAndBuild(Header + production, Driver(isStatic), $"STATIC={isStatic}");
        await Expect(fixture, "abc", "ok");
        await Expect(fixture, "z", "ok");
        await Expect(fixture, "", "parse-error");
        await Expect(fixture, "123", "lexical-error");
    }

    [Test]
    public async Task EmbeddedStringsKeepTheirCSharpEscapes() {
        const string production = """
            string Input() : {} { <EOF> { return "say \"hello\" \\ \t"; } }
            """;
        const string driver = """
            using System;
            using System.IO;
            using Fixture;
            class Program {
                static void Main() { Console.Write(new FixtureParser(new StringReader("")).Input()); }
            }
            """;
        using var fixture = new ParserFixture { ModernOutput = modernOutput };
        await fixture.GenerateAndBuild(Header + production, driver, "STATIC=false");
        await Expect(fixture, "", "say \"hello\" \\ \t");
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task UnicodeRangesIncludeEveryBitAndRespectTheirEndpoints(bool isStatic) {
        const string production = """
            TOKEN: {
                < RANGE: ["\u0101"-"\u0321"] >
              | < ASCII: ~["?", "\u007f", "\u0080"-"\uffff"] >
              | < OTHER: ~[] >
            }
            int Input() : { Token t; } { (t=<RANGE> | t=<ASCII> | t=<OTHER>) <EOF> { return t.Kind; } }
            """;
        string driver = $$"""
            using System;
            using System.IO;
            using Fixture;
            class Program {
                static void Main() {
                    var parser = new FixtureParser(new StringReader(""));
                    for (int code = 0; code <= 0xffff; code++) {
                        if (code >= 0x500 && (code & 63) != 63) continue;
                        {{(isStatic ? "FixtureParser" : "parser")}}.ReInit(new StringReader(new string((char)code, 1)));
                        int kind = {{(isStatic ? "FixtureParser" : "parser")}}.Input();
                        int expected = code >= 0x101 && code <= 0x321 ? FixtureParserConstants.RANGE :
                            code < 0x80 && code != 0x3f && code != 0x7f ? FixtureParserConstants.ASCII : FixtureParserConstants.OTHER;
                        if (kind != expected) throw new Exception($"U+{code:X4}: expected {expected}, got {kind}");
                    }
                    Console.Write("ok");
                }
            }
            """;
        using var fixture = new ParserFixture { ModernOutput = modernOutput };
        await fixture.GenerateAndBuild(Header + production, driver, $"STATIC={isStatic}", "UNICODE_INPUT=true");
        await Expect(fixture, "", "ok");
    }

    private static string Driver(bool isStatic) => $$"""
        using System;
        using System.IO;
        using Fixture;
        class Program {
            static void Main(string[] args) {
                try {
                    var parser = new FixtureParser(new StringReader(args[0]));
                    {{(isStatic ? "FixtureParser" : "parser")}}.disable_tracing();
                    {{(isStatic ? "FixtureParserTokenManager" : "(parser.tokenSource ?? throw new InvalidOperationException())")}}.SetDebugStream(TextWriter.Null);
                    {{(isStatic ? "FixtureParser" : "parser")}}.Input();
                    Console.Write("ok");
                } catch (ParseException) { Console.Write("parse-error"); }
                  catch (TokenManagerError) { Console.Write("lexical-error"); }
            }
        }
        """;

    private static async Task Expect(ParserFixture fixture, string input, string expected) {
        var result = await fixture.Run(input);
        Assert.That(result.ExitCode, Is.Zero, result.Output);
        Assert.That(result.Output, Is.EqualTo(expected));
    }
}
