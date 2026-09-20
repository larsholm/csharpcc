using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

// Isolated phase 6 experiment; this is not yet connected to the grammar reader.
internal static class EmbeddedCSharp {
    private static readonly CSharpParseOptions Options = new(LanguageVersion.CSharp14);

    internal sealed record Error(string Id, string Message, int Offset, int Line, int Column);
    internal sealed record Fragment(int Start, int End, string Text, SyntaxNode Syntax, Error[] Errors);

    internal static Fragment Block(string source, int offset) => Describe(source, offset,
        SyntaxFactory.ParseStatement(source, offset, options: Options, consumeFullText: false));

    internal static Fragment Expression(string source, int offset) => Describe(source, offset,
        SyntaxFactory.ParseExpression(source, offset, options: Options, consumeFullText: false));

    internal static Fragment Type(string source, int offset) => Describe(source, offset,
        SyntaxFactory.ParseTypeName(source, offset, options: Options, consumeFullText: false));

    internal static Fragment Member(string source, int offset) => Describe(source, offset,
        SyntaxFactory.ParseMemberDeclaration(source[offset..], options: Options, consumeFullText: false)
            ?? throw new InvalidOperationException("Expected a C# member declaration."));

    internal static Fragment CompilationUnit(string source, int offset, string parserName) {
        var tokens = SyntaxFactory.ParseTokens(source[offset..], options: Options).ToArray();
        int braces = 0;
        for (int i = 0; i < tokens.Length; i++) {
            var token = tokens[i];
            if (braces == 0 && token.IsKind(SyntaxKind.IdentifierToken) && token.Text == "PARSER_END" &&
                i + 3 < tokens.Length && tokens[i + 1].IsKind(SyntaxKind.OpenParenToken) &&
                tokens[i + 2].ValueText == parserName && tokens[i + 3].IsKind(SyntaxKind.CloseParenToken)) {
                int end = offset + token.SpanStart;
                var root = CSharpSyntaxTree.ParseText(source[offset..end], Options).GetRoot();
                return Describe(source, offset, root, end);
            }
            if (token.IsKind(SyntaxKind.OpenBraceToken)) braces++;
            if (token.IsKind(SyntaxKind.CloseBraceToken)) braces--;
        }
        throw new InvalidOperationException("Expected PARSER_END(" + parserName + ").");
    }

    private static Fragment Describe(string source, int offset, SyntaxNode node, int? end = null) {
        var text = SourceText.From(source);
        var errors = node.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).Select(d => {
            int position = Math.Min(offset + d.Location.SourceSpan.Start, source.Length);
            var location = text.Lines.GetLinePosition(position);
            return new Error(d.Id, d.GetMessage(), position, location.Line + 1, location.Character + 1);
        }).ToArray();
        int start = end.HasValue ? offset : offset + node.SpanStart;
        int finish = end ?? offset + node.Span.End;
        return new Fragment(start, finish, source[start..finish], node, errors);
    }
}
