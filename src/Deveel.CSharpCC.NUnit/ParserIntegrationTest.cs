using System;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Deveel.CSharpCC.NUnit;

[TestFixture]
public class ParserIntegrationTest {
    private const string Grammar = """
        PARSER_BEGIN(FixtureParser)
        namespace Fixture;
        using System;
        public class FixtureParser {}
        PARSER_END(FixtureParser)

        SKIP: { " " | "\t" | "\r" | "\n" }
        MORE: { "/*" : IN_COMMENT }
        <IN_COMMENT> MORE: { < ~[] > }
        <IN_COMMENT> SPECIAL_TOKEN: { < COMMENT: "*/" > : DEFAULT }
        TOKEN: { < WORD: (["a"-"z", "é", "æ", "ø", "å"])+ > | < COLON: ":" > }

        Token Input() : { Token t; }
        {
          ( LOOKAHEAD(2) t=<WORD> ":" <WORD> | t=<WORD> ) <EOF>
          { return t; }
        }
        """;

    private static string Driver(bool isStatic, string source = "new System.IO.StringReader(args[0])", string helpers = "") => $$"""
        using System;
        using Fixture;
        class Program {
            static int Main(string[] args) {
                try {
                    var parser = new FixtureParser({{source}});
                    var token = {{(isStatic ? "FixtureParser" : "parser")}}.Input();
                    Console.Write($"{token.Image}|{token.BeginLine}:{token.BeginColumn}-{token.EndLine}:{token.EndColumn}|{token.SpecialToken?.Image}");
                    if ({{(isStatic ? "FixtureParser" : "parser")}}.GetNextToken().Kind != 0 ||
                        {{(isStatic ? "FixtureParser" : "parser")}}.GetNextToken().Kind != 0)
                        return 4;
                    {{(isStatic ? "FixtureParser" : "parser")}}.ReInit({{source}});
                    if ({{(isStatic ? "FixtureParser" : "parser")}}.Input().Image != token.Image)
                        return 5;
                    return 0;
                } catch (ParseException error) {
                    if (error.CurrentToken != null && (error.ExpectedTokenSequences == null || error.ExpectedTokenSequences.Length == 0))
                        return 6;
                    Console.Write("parse-error"); return 2;
                } catch (TokenManagerError) { Console.Write("lexical-error"); return 3; }
            }
        }
        {{helpers}}
        """;

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public async Task GeneratedParserHandlesLookaheadCommentsPositionsAndEof(bool isStatic, bool cacheTokens) {
        using var fixture = new ParserFixture();
        await fixture.GenerateAndBuild(Grammar, Driver(isStatic), $"STATIC={isStatic}", $"CACHE_TOKENS={cacheTokens}");
        await Expect(fixture, "word", 0, "word|1:1-1:4|");
        await Expect(fixture, "word:other", 0, "word|1:1-1:4|");
        await Expect(fixture, "/* comment */\n\tæøå", 0, "æøå|2:9-2:11|/* comment */");
        await Expect(fixture, "", 2, "parse-error");
        await Expect(fixture, "word:", 2, "parse-error");
        await Expect(fixture, "word word", 2, "parse-error");
        await Expect(fixture, "@", 3, "lexical-error");
        await Expect(fixture, "/* unfinished", 3, "lexical-error");
        var longWord = new string('a', 6000);
        await Expect(fixture, longWord, 0, $"{longWord}|1:1-1:6000|");
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task UnicodeEscapesAreDecoded(bool isStatic) {
        using var fixture = new ParserFixture();
        await fixture.GenerateAndBuild(Grammar, Driver(isStatic), $"STATIC={isStatic}", "UNICODE_ESCAPE=true");
        await Expect(fixture, @"\u0061bc", 0, "abc|1:1-1:8|");
        await Expect(fixture, "æøå", 0, "æøå|1:1-1:3|");
        await Expect(fixture, "", 2, "parse-error");
        await Expect(fixture, @"\u00", 2, "parse-error");
        await Expect(fixture, @"\u00zz", 2, "parse-error");
        await Expect(fixture, new string('a', 4095) + @"\u0062", 0, $"{new string('a', 4095)}b|1:1-1:4096|");
    }

    [Test]
    public async Task UserTokenManagerCanBeImplementedWithoutGeneratorReference() {
        const string grammar = """
            PARSER_BEGIN(FixtureParser)
            namespace Fixture;
            using System;
            public class FixtureParser {}
            PARSER_END(FixtureParser)
            TOKEN: { < WORD: "word" > }
            Token Input() : { Token t; } { t=<WORD> <EOF> { return t; } }
            """;
        const string helpers = """
            class CustomTokens : ITokenManager {
                private bool emitted;
                public Token GetNextToken() {
                    if (emitted) return new Token(0, "");
                    emitted = true;
                    return new Token(FixtureParserConstants.WORD, "custom") {
                        BeginLine = 1, BeginColumn = 1, EndLine = 1, EndColumn = 6
                    };
                }
            }
            """;
        using var fixture = new ParserFixture();
        await fixture.GenerateAndBuild(grammar, Driver(false, "new CustomTokens()", helpers), "STATIC=false", "USER_TOKEN_MANAGER=true");
        await Expect(fixture, "", 0, "custom|1:1-1:6|");
    }

    [Test]
    public async Task UserCharacterStreamCanBeImplementedWithoutGeneratorReference() {
        const string helpers = """
            class CustomStream(string text) : ICharStream {
                private int position;
                private int start;
                public char ReadChar() => position < text.Length ? text[position++] : throw new System.IO.EndOfStreamException();
                public char BeginToken() { start = position; return ReadChar(); }
                public void Backup(int amount) => position -= amount;
                public string GetImage() => text[start..position];
                public char[] GetSuffix(int length) => text[(position-length)..position].ToCharArray();
                public int BeginLine => 1;
                public int EndLine => 1;
                public int BeginColumn => start + 1;
                public int EndColumn => position;
                public int Line => EndLine;
                public int Column => EndColumn;
                public void Done() {}
            }
            """;
        using var fixture = new ParserFixture();
        await fixture.GenerateAndBuild(Grammar, Driver(false, "new CustomStream(args[0])", helpers), "STATIC=false", "USER_CHAR_STREAM=true");
        await Expect(fixture, "word:other", 0, "word|1:1-1:4|");
        await Expect(fixture, "", 2, "parse-error");
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task UnicodeStreamPreservesEscapesAndBackup(bool isStatic) {
        string receiver = isStatic ? "UnicodeCharStream" : "stream";
        var driver = $$"""
            using System;
            using System.IO;
            using Fixture;
            class Program {
                static int Main(string[] args) {
                    var stream = new UnicodeCharStream(new StringReader(args[0]), 1, 1, 8);
                    var value = new System.Text.StringBuilder();
                    try {
                        value.Append({{receiver}}.BeginToken());
                        while (true) value.Append({{receiver}}.ReadChar());
                    } catch (EndOfStreamException) {}
                    if (value.Length > 0) {
                        {{receiver}}.Backup(1);
                        if ({{receiver}}.ReadChar() != value[value.Length - 1]) return 1;
                        if ({{receiver}}.GetImage() != value.ToString()) return 2;
                        if (new string({{receiver}}.GetSuffix(value.Length)) != value.ToString()) return 3;
                    }
                    // EOF must remain EOF after backup, and must not dispose the supplied reader.
                    for (int i = 0; i < 2; i++) {
                        try { {{receiver}}.ReadChar(); return 4; } catch (EndOfStreamException) {}
                    }
                    using var reader = new StringReader("reinit");
                    stream.ReInit(reader, 2, 3, 8);
                    if ({{receiver}}.BeginToken() != 'r' || {{receiver}}.BeginLine != 2 || {{receiver}}.BeginColumn != 3)
                        return 5;
                    {{receiver}}.Done();
                    if (reader.Read() != -1) return 6;
                    Console.Write(value);
                    return 0;
                }
            }
            """;
        using var fixture = new ParserFixture();
        await fixture.GenerateAndBuild(Grammar, driver, $"STATIC={isStatic}", "UNICODE_ESCAPE=true");
        await Expect(fixture, @"\u0061", 0, "a");
        await Expect(fixture, @"\uuuu0061", 0, "a");
        await Expect(fixture, @"\\u0061", 0, @"\\u0061");
        await Expect(fixture, @"\\\u0061", 0, @"\\a");
        await Expect(fixture, @"\", 0, @"\");
        await Expect(fixture, new string('a', 9000), 0, new string('a', 9000));
        await Expect(fixture, "", 0, "");
    }

    private static async Task Expect(ParserFixture fixture, string input, int exitCode, string output) {
        var result = await fixture.Run(input);
        Assert.That(result.ExitCode, Is.EqualTo(exitCode), result.Output);
        Assert.That(result.Output, Is.EqualTo(output), $"Input: {input}");
    }
}
