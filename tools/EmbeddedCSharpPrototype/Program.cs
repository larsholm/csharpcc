using Microsoft.CodeAnalysis.CSharp.Syntax;

int cases = 0;
void Check(bool condition, string message) {
    if (!condition) throw new InvalidOperationException(message);
}
void Fragment(string name, string code, Func<string, int, EmbeddedCSharp.Fragment> parse, string tail = " <EOF> }") {
    string prefix = "// grammar prefix\nvoid Input() : {}\n";
    var result = parse(prefix + code + tail, prefix.Length);
    Check(result.Errors.Length == 0, name + ": " + string.Join("; ", result.Errors.Select(e => e.Message)));
    Check(result.Text == code, name + ": source text changed: " + result.Text);
    Check(result.End == prefix.Length + code.Length, name + ": consumed a grammar delimiter");
    cases++;
}

Fragment("ordinary and verbatim strings", """
    { var a = "} PARSER_END(P)"; var b = @"{""quoted""}"; /* } */ }
    """, EmbeddedCSharp.Block);
Fragment("raw and interpolated strings", """"
    { var a = """ } PARSER_END(P) """; var b = $$"""text { {{new { Name = "}" }.Name}} }"""; }
    """", EmbeddedCSharp.Block);
Fragment("nested interpolation", """
    { var value = $"{Get($"{new { Text = "}" }.Text}")}"; }
    """, EmbeddedCSharp.Block);
Fragment("preprocessor and inactive text", """
    {
    #if NEVER_DEFINED
    } PARSER_END(P) arbitrary invalid C#
    #else
      int[] values = [1, 2, 3];
    #endif
    }
    """, EmbeddedCSharp.Block);
Fragment("patterns, lambdas and collection expressions", """
    { Func<int, int> twice = x => x * 2; var result = value switch { [var head, ..] => head, _ => 0 }; List<int> xs = [1, ..other]; }
    """, EmbeddedCSharp.Block);
Fragment("C# 14 null conditional assignment", "{ person?.Name = GetName(); }", EmbeddedCSharp.Block);
Fragment("nested generic expression", "Build<Dictionary<string, List<int>>>() is { Count: > 0 }", EmbeddedCSharp.Expression, " ) <EOF>");
Fragment("nullable generic type", "Dictionary<string, List<int?>>?", EmbeddedCSharp.Type, " Input() : {} { <EOF> }");
Fragment("primary constructor and required/init", "public record Item(string Name) { public required int Id { get; init; } }", EmbeddedCSharp.Member);
Fragment("C# 14 field backed property", "public string Name { get; set => field = value.Trim(); } = \"\";", EmbeddedCSharp.Member);
Fragment("C# 14 extension declaration", "public static class E { extension(string value) { public bool Empty => value.Length == 0; } }", EmbeddedCSharp.Member);

const string compilation = """"
    // Leading comments and blank lines must survive.

    using static System.Math;
    using Alias = System.Collections.Generic.List<string?>;
    namespace Example;
    // PARSER_END(P)
    public partial class P {
        const string Marker = "PARSER_END(P)";
        const string Raw = """ } PARSER_END(P) { """;
        string Format(int x) => $"PARSER_END(P): {x}";
        public record Helper(int Value);
    }
    public record Extra(string Name);
    #if NEVER_DEFINED
    PARSER_END(P)
    #endif
    // Trailing comment before the grammar delimiter.

    """";
string grammar = "PARSER_BEGIN(P)\n" + compilation + "PARSER_END(P)\nvoid Input() : {} { <EOF> }";
var unit = EmbeddedCSharp.CompilationUnit(grammar, "PARSER_BEGIN(P)\n".Length, "P");
Check(unit.Errors.Length == 0, "Compilation unit failed: " + string.Join("; ", unit.Errors.Select(e => e.Message)));
Check(unit.Text == compilation, "Compilation unit did not preserve original text.");
var parser = unit.Syntax.DescendantNodes().OfType<ClassDeclarationSyntax>().Single(c => c.Identifier.ValueText == "P");
Check(grammar[unit.Start + parser.OpenBraceToken.SpanStart] == '{', "Parser body insertion point moved.");
Check(grammar[unit.Start + parser.CloseBraceToken.SpanStart] == '}', "Parser body end insertion point moved.");
cases++;

string broken = "// grammar\nvoid Input() :\n{ int value = ; } { <EOF> }";
var invalid = EmbeddedCSharp.Block(broken, broken.IndexOf('{'));
Check(invalid.Errors.Length != 0, "Missing initializer was accepted.");
Check(invalid.Errors[0].Offset == broken.IndexOf(';') && invalid.Errors[0].Line == 3 && invalid.Errors[0].Column == 15,
    "Original diagnostic position was not preserved: " + invalid.Errors[0]);
cases++;

string incomplete = "// grammar\n{ var value = 1;";
var missingBrace = EmbeddedCSharp.Block(incomplete, incomplete.IndexOf('{'));
Check(missingBrace.Errors.Any(e => e.Message.Contains("} expected", StringComparison.Ordinal)), "Missing brace was accepted.");
Check(missingBrace.Errors.All(e => e.Line == 2 && e.Offset <= incomplete.Length), "Incomplete input diagnostic escaped its source.");
cases++;
Console.WriteLine($"Passed {cases} embedded C# boundary checks using Roslyn and C# 14.");
