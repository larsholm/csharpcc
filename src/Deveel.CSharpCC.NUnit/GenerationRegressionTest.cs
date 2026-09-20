#nullable enable

using System.Threading.Tasks;
using NUnit.Framework;

namespace Deveel.CSharpCC.NUnit;

[TestFixture]
public class GenerationRegressionTest {
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
        using var fixture = new ParserFixture();
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
        using var fixture = new ParserFixture();
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
        using var fixture = new ParserFixture();
        await fixture.GenerateAndBuild(Header + production, Driver(isStatic), $"STATIC={isStatic}");
        await Expect(fixture, "a", "ok");
        await Expect(fixture, "ab", "ok");
        await Expect(fixture, "b", "parse-error");
        await Expect(fixture, "aa", "parse-error");
    }

    private static string Driver(bool isStatic) => $$"""
        using System;
        using System.IO;
        using Fixture;
        class Program {
            static void Main(string[] args) {
                try {
                    var parser = new FixtureParser(new StringReader(args[0]));
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
