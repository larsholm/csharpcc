#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Deveel.CSharpCC.NUnit;

[TestFixture]
public class ModernOutputTest {
    [TestCase(false, false, false)]
    [TestCase(true, false, true)]
    [TestCase(true, true, false)]
    [TestCase(false, true, true)]
    public async Task NullableConsumersCanUseGeneratedParsers(bool isStatic, bool cacheTokens, bool unicode) {
        const string grammar = """
            PARSER_BEGIN(FixtureParser)
            namespace Fixture;
            using System;
            public class FixtureParser {}
            PARSER_END(FixtureParser)
            SKIP: { " " }
            TOKEN: { < WORD: (["a"-"z"])+ > }
            void Input() : {} { ( LOOKAHEAD(2) <WORD> ":" <WORD> | <WORD> ) <EOF> }
            """;
        string driver = $$"""
            using System;
            using System.IO;
            using Fixture;
            class Program {
                static void Main(string[] args) {
                    var parser = new FixtureParser(new StringReader(args[0]));
                    try {
                        {{(isStatic ? "FixtureParser" : "parser")}}.Input();
                        Console.Write("ok");
                    } catch (ParseException) { Console.Write("parse-error"); }
                }
            }
            """;
        using var fixture = new ParserFixture { ModernOutput = true };
        await fixture.GenerateAndBuild(grammar, driver, $"STATIC={isStatic}",
            $"CACHE_TOKENS={cacheTokens}", $"UNICODE_ESCAPE={unicode}");
        foreach (string file in Directory.GetFiles(fixture.DirectoryPath, "*.cs")) {
            if (Path.GetFileName(file) != "Program.cs")
                Assert.That(File.ReadAllText(file), Does.Contain("#nullable enable"), file);
        }
        foreach (var (input, expected) in new[] { ("one", "ok"), ("one:two", "ok"), ("one:", "parse-error") }) {
            var result = await fixture.Run(input);
            Assert.That(result.ExitCode, Is.Zero, result.Output);
            Assert.That(result.Output, Is.EqualTo(expected));
        }
    }
    [TestCase(false, false, false)]
    [TestCase(true, false, true)]
    [TestCase(false, true, true)]
    public async Task ModernOutputSupportsIndependentOptions(bool isStatic, bool positions, bool parserAware) {
        const string grammar = """
            PARSER_BEGIN(FixtureParser)
            namespace Fixture;
            using System;
            public class FixtureParser {}
            PARSER_END(FixtureParser)
            TOKEN: { < WORD: (["a"-"z"])+ > }
            void Input() : {} { <WORD> <EOF> }
            """;
        string driver = $$"""
            using System;
            using System.IO;
            using Fixture;
            class Program {
                static void Main(string[] args) {
                    var parser = new FixtureParser(new StringReader(args[0]));
                    try { {{(isStatic ? "FixtureParser" : "parser")}}.Input(); Console.Write("ok"); }
                    catch (ParseException) { Console.Write("parse-error"); }
                }
            }
            """;
        using var fixture = new ParserFixture { ModernOutput = true };
        await fixture.GenerateAndBuild(grammar, driver, $"STATIC={isStatic}", $"KEEP_LINE_COLUMN={positions}",
            $"TOKEN_MANAGER_USES_PARSER={parserAware}", "ERROR_REPORTING=false", "CLR_VERSION=1.0");
        Assert.That((await fixture.Run("one")).Output, Is.EqualTo("ok"));
        Assert.That((await fixture.Run("")).Output, Is.EqualTo("parse-error"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task LanguageModesPreservePublicApiAndRegenerateStably(bool isStatic) {
        const string grammar = """
            PARSER_BEGIN(FixtureParser)
            namespace Fixture;
            using System;
            public class FixtureParser {}
            PARSER_END(FixtureParser)
            void Input() : {} { ( LOOKAHEAD(2) "a" "b" | "a" ) <EOF> }
            """;
        const string driver = """
            using System;
            using System.Linq;
            using System.Reflection;
            using Fixture;
            class Program {
                static void Main() {
                    var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance |
                        BindingFlags.Static | BindingFlags.DeclaredOnly;
                    foreach (var type in typeof(FixtureParser).Assembly.GetTypes().Where(t => t.IsPublic).OrderBy(t => t.FullName)) {
                        Console.WriteLine(type.FullName + ":" + type.BaseType);
                        foreach (var member in type.GetMembers(flags).Where(m => m switch {
                            MethodBase b => b.IsPublic || b.IsFamily || b.IsFamilyOrAssembly,
                            FieldInfo f => f.IsPublic || f.IsFamily || f.IsFamilyOrAssembly,
                            PropertyInfo p => p.GetAccessors(true).Any(a => a.IsPublic || a.IsFamily || a.IsFamilyOrAssembly),
                            _ => false
                        }).Select(m => m.MemberType + ":" + m).OrderBy(m => m)) Console.WriteLine(member);
                    }
                }
            }
            """;
        using var legacy = new ParserFixture();
        using var modern = new ParserFixture { ModernOutput = true };
        string[] options = [$"STATIC={isStatic}", "CLR_VERSION=1.0"];
        await legacy.GenerateAndBuild(grammar, driver, options);
        await modern.GenerateAndBuild(grammar, driver, options);
        var legacyApi = await legacy.Run("");
        var modernApi = await modern.Run("");
        Assert.That(legacyApi.ExitCode, Is.Zero, legacyApi.Output);
        Assert.That(modernApi.ExitCode, Is.Zero, modernApi.Output);
        Assert.That(modernApi.Output, Is.EqualTo(legacyApi.Output));
        Assert.That(File.ReadAllText(Path.Combine(modern.DirectoryPath, "FixtureParser.cs")),
            Does.Contain("List<int[]> cc_expentries = []").And.Not.Contain("ArrayList"));

        var snapshot = Directory.GetFiles(modern.DirectoryPath, "*.cs")
            .ToDictionary(path => Path.GetFileName(path), File.ReadAllText);
        var regeneration = await modern.Generate(grammar, options);
        Assert.That(regeneration.ExitCode, Is.Zero, regeneration.Output);
        foreach (var file in snapshot)
            Assert.That(File.ReadAllText(Path.Combine(modern.DirectoryPath, file.Key)), Is.EqualTo(file.Value), file.Key);

        // Reuse one output directory in both directions; shorter files must be truncated.
        Assert.That((await legacy.Generate(grammar, [.. options, "CSHARP_VERSION=14"])).ExitCode, Is.Zero);
        Assert.That((await legacy.Generate(grammar, options)).ExitCode, Is.Zero);
        using var fresh = new ParserFixture();
        Assert.That((await fresh.Generate(grammar, options)).ExitCode, Is.Zero);
        foreach (string file in Directory.GetFiles(fresh.DirectoryPath, "*.cs"))
            Assert.That(File.ReadAllText(Path.Combine(legacy.DirectoryPath, Path.GetFileName(file))), Is.EqualTo(File.ReadAllText(file)));

        string tokenPath = Path.Combine(modern.DirectoryPath, "Token.cs");
        string customized = File.ReadAllText(tokenPath).Replace("public Token()", "/* User customization */ public Token()");
        File.WriteAllText(tokenPath, customized);
        Assert.That((await modern.Generate(grammar, options)).ExitCode, Is.Zero);
        Assert.That(File.ReadAllText(tokenPath), Is.EqualTo(customized));
    }

}
