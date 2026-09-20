using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Deveel.CSharpCC.Parser;

public partial class CSharpCCParser {
    private static readonly CSharpParseOptions EmbeddedOptions = new(LanguageVersion.CSharp14);
    private CSharpParseOptions fragmentOptions = EmbeddedOptions;
    private GrammarSource? previousSource;
    private GrammarSource? EmbeddedSource {
        get {
            if (cc_inputStream is not GrammarSource source || !ReferenceEquals(token_source.GrammarInput, source)) return null;
            if (!ReferenceEquals(previousSource, source)) {
                previousSource = source;
                fragmentOptions = EmbeddedOptions;
            }
            return source;
        }
    }

    private bool TryReadCSharpCompilationUnit() {
        if (EmbeddedSource is not { } source) return false;
        int start = source.After(token);
        string remaining = source.Text.ToString(TextSpan.FromBounds(start, source.Text.Length));
        int braces = 0;
        int length = -1;
        foreach (var item in SyntaxFactory.ParseTokens(remaining, options: EmbeddedOptions)) {
            if (braces == 0 && item.IsKind(SyntaxKind.IdentifierToken) && item.Text == "PARSER_END") {
                var marker = SyntaxFactory.ParseTokens(remaining, offset: item.SpanStart, options: EmbeddedOptions).Take(4).ToArray();
                if (marker.Length == 4 && marker[1].IsKind(SyntaxKind.OpenParenToken) &&
                    marker[2].IsKind(SyntaxKind.IdentifierToken) && marker[3].IsKind(SyntaxKind.CloseParenToken)) {
                    length = item.SpanStart;
                    break;
                }
            }
            if (item.IsKind(SyntaxKind.OpenBraceToken)) braces++;
            if (item.IsKind(SyntaxKind.CloseBraceToken)) braces--;
        }
        if (length < 0) {
            CSharpCCErrors.ParseError(token, "Expected PARSER_END after the embedded C# compilation unit.");
            throw new MetaParseException();
        }
        string text = remaining[..length];
        var root = CSharpSyntaxTree.ParseText(text, EmbeddedOptions).GetCompilationUnitRoot();
        CheckCSharpErrors(source, start, root);
        var symbols = new HashSet<string>(StringComparer.Ordinal);
        foreach (var directive in root.DescendantTrivia(descendIntoTrivia: true).Select(t => t.GetStructure()).OfType<DirectiveTriviaSyntax>()) {
            if (!directive.IsActive) continue;
            if (directive is DefineDirectiveTriviaSyntax define) symbols.Add(define.Name.ValueText);
            if (directive is UndefDirectiveTriviaSyntax undefine) symbols.Remove(undefine.Name.ValueText);
        }
        fragmentOptions = EmbeddedOptions.WithPreprocessorSymbols(symbols);
        var candidates = root.DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Where(c => c.Identifier.ValueText == parserTypeName && !c.Ancestors().OfType<TypeDeclarationSyntax>().Any()).ToArray();
        if (candidates.Length != 1) {
            CSharpCCErrors.ParseError(token, candidates.Length == 0
                ? "Parser class has not been defined between PARSER_BEGIN and PARSER_END."
                : "Multiple declaration of parser class.");
            throw new MetaParseException();
        }
        var parser = candidates[0];
        if (parser.OpenBraceToken.IsMissing || parser.CloseBraceToken.IsMissing || parser.OpenBraceToken.RawKind == 0) {
            CSharpCCErrors.ParseError(token, "The parser class must declare a body with braces.");
            throw new MetaParseException();
        }
        if (parser.TypeParameterList != null || parser.ParameterList != null ||
            parser.Modifiers.Any(SyntaxKind.StaticKeyword) || root.Members.OfType<GlobalStatementSyntax>().Any()) {
            CSharpCCErrors.ParseError(source.MakeToken(0, start + parser.SpanStart, start + parser.SpanStart, ""),
                "The parser must be a non-generic class without a primary constructor; top-level statements are not supported.");
            throw new MetaParseException();
        }
        var fileNamespace = parser.Ancestors().OfType<FileScopedNamespaceDeclarationSyntax>().SingleOrDefault();
        if (fileNamespace != null)
            text = text[..fileNamespace.SemicolonToken.SpanStart] + "{" + text[fileNamespace.SemicolonToken.Span.End..];
        int insertion = parser.BaseList?.ColonToken.Span.End ?? parser.Identifier.Span.End;
        string inheritance = parser.BaseList is null ? " : " + parserTypeName + "Constants" : " " + parserTypeName + "Constants, ";
        string before = text[..insertion] + inheritance + text[insertion..parser.CloseBraceToken.SpanStart];
        string after = text[parser.CloseBraceToken.SpanStart..];
        if (fileNamespace != null) after += "\n}\n";
        var header = new StringBuilder();
        foreach (string symbol in symbols.Order(StringComparer.Ordinal)) header.Append("#define ").AppendLine(symbol);
        foreach (var external in root.Externs) header.AppendLine(external.ToString());
        foreach (var import in root.Usings.Where(u => u.GlobalKeyword.IsKind(SyntaxKind.None)))
            header.AppendLine(import.ToString());
        var namespaces = parser.Ancestors().OfType<BaseNamespaceDeclarationSyntax>().Reverse().ToArray();
        foreach (var ns in namespaces) {
            header.Append("namespace ").Append(ns.Name).AppendLine(" {");
            foreach (var external in ns.Externs) header.AppendLine(external.ToString());
            foreach (var import in ns.Usings) header.AppendLine(import.ToString());
        }
        // The lexer emits Console/Exception references even when the embedded
        // unit uses fully qualified names and has no System import.
        if (!root.Usings.Concat(namespaces.SelectMany(n => n.Usings)).Any(u =>
            u.Alias is null && u.StaticKeyword.IsKind(SyntaxKind.None) && u.Name?.ToString() == "System")) {
            header.AppendLine("using System;");
            int importPosition = namespaces.LastOrDefault() switch {
                { Usings.Count: > 0 } ns => ns.Usings[^1].Span.End,
                NamespaceDeclarationSyntax ns => ns.OpenBraceToken.Span.End,
                FileScopedNamespaceDeclarationSyntax ns => ns.SemicolonToken.Span.End,
                _ => root.Usings.Count != 0 ? root.Usings[^1].Span.End :
                    root.AttributeLists.Count != 0 ? root.AttributeLists[0].SpanStart : root.Members[0].SpanStart
            };
            before = before.Insert(importPosition, "\nusing System;\n");
        }
        CSharpCCGlobals.CompilationLayout = new(before, after, header.ToString(),
            string.Concat(Enumerable.Repeat("}\n", namespaces.Length)),
            string.Concat(namespaces.Select(n => "namespace " + n.Name + "{\n")));
        Token closing = RememberCompilationTokens(source, start, root, parser);
        ResumeGrammar(source, closing, start + length);
        return true;
    }

    private Token RememberCompilationTokens(GrammarSource source, int start, CompilationUnitSyntax root, ClassDeclarationSyntax parser) {
        var kinds = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < tokenImage.Length; i++) {
            string image = tokenImage[i];
            if (image.Length >= 2 && image[0] == '"' && image[^1] == '"') kinds[image[1..^1]] = i;
        }
        Token previous = token;
        foreach (var item in root.DescendantTokens().Where(t => !t.IsKind(SyntaxKind.EndOfFileToken))) {
            int kind = kinds.GetValueOrDefault(item.Text, IDENTIFIER);
            if (item.IsKind(SyntaxKind.StringLiteralToken)) kind = STRING_LITERAL;
            if (item.IsKind(SyntaxKind.CharacterLiteralToken)) kind = CHARACTER_LITERAL;
            if (item.IsKind(SyntaxKind.NumericLiteralToken))
                kind = item.Value is float or double or decimal ? FLOATING_POINT_LITERAL : INTEGER_LITERAL;
            Token current = source.MakeToken(kind, start + item.SpanStart, start + item.Span.End, item.Text);
            previous.next = current;
            previous = current;
            IList<Token> destination = item.SpanStart < parser.OpenBraceToken.SpanStart ? CSharpCCGlobals.cu_to_insertion_point_1 :
                item.SpanStart < parser.CloseBraceToken.SpanStart ? CSharpCCGlobals.cu_to_insertion_point_2 :
                CSharpCCGlobals.cu_from_insertion_point_2;
            destination.Add(current);
        }
        return previous;
    }

    private bool TryReadCSharpParameters(IList<Token>? tokens) => ReadCSharpFragment("parameters", tokens is null ? null : tokens.Add);
    private bool TryReadCSharpArguments(IList<Token>? tokens) => ReadCSharpFragment("arguments", tokens is null ? null : tokens.Add);
    private bool TryReadCSharpType(IList<Token>? tokens) => ReadCSharpFragment("type", tokens is null ? null : tokens.Add);
    private bool TryReadCSharpExpression(System.Collections.IList? tokens) => ReadCSharpFragment("expression", t => tokens?.Add(t));

    private bool TryReadCSharpMembers(IList<Token>? tokens) {
        if (processingTypeUnit || EmbeddedSource is not { } source) return false;
        int start = source.After(token);
        const string prefix = "class __TokenManager ";
        var node = SyntaxFactory.ParseMemberDeclaration(prefix + source.Text.ToString(TextSpan.FromBounds(start, source.Text.Length)),
            options: fragmentOptions, consumeFullText: false);
        if (node is not ClassDeclarationSyntax declaration) return false;
        CheckCSharpErrors(source, start - prefix.Length, declaration);
        int bodyStart = start - prefix.Length + declaration.OpenBraceToken.Span.End;
        int bodyEnd = start - prefix.Length + declaration.CloseBraceToken.SpanStart;
        int end = start - prefix.Length + declaration.Span.End;
        Token opening = source.MakeToken(LBRACE, bodyStart - 1, bodyStart, "{");
        token.next = opening;
        Token closing = source.MakeToken(RBRACE, bodyEnd, end, "}");
        string body = source.Text.ToString(TextSpan.FromBounds(bodyStart, bodyEnd));
        if (!string.IsNullOrWhiteSpace(body)) {
            Token content = source.MakeToken(IDENTIFIER, bodyStart, bodyEnd, body);
            opening.next = content;
            content.next = closing;
            tokens?.Add(content);
        } else opening.next = closing;
        CSharpCCGlobals.TokenManagerHasCommonAction = declaration.Members.OfType<MethodDeclarationSyntax>()
            .Any(m => m.Identifier.ValueText == "CommonTokenAction");
        ResumeGrammar(source, closing, end);
        return true;
    }

    private bool ReadCSharpFragment(string kind, System.Action<Token>? addToken) {
        if (EmbeddedSource is not { } source) return false;
        int start = source.After(token);
        SyntaxNode node = ParseCSharpFragment(source, start, kind);
        CheckCSharpErrors(source, start, node);
        bool parentheses = kind is "parameters" or "arguments";
        int first = start + node.SpanStart;
        int end = start + node.Span.End;
        int bodyStart = parentheses ? start + node.GetFirstToken().Span.End : first;
        int bodyEnd = parentheses ? start + node.GetLastToken().SpanStart : end;
        Token previous = token;
        if (parentheses) {
            previous.next = source.MakeToken(LPAREN, first, bodyStart, "(");
            previous = previous.next;
        }
        string body = source.Text.ToString(TextSpan.FromBounds(bodyStart, bodyEnd));
        if (!string.IsNullOrWhiteSpace(body)) {
            Token content = source.MakeToken(body == "void" ? VOID : IDENTIFIER, bodyStart, bodyEnd, body);
            previous.next = content;
            previous = content;
            addToken?.Invoke(content);
        }
        if (parentheses) {
            previous.next = source.MakeToken(RPAREN, bodyEnd, end, ")");
            previous = previous.next;
        }
        ResumeGrammar(source, previous, end);
        return true;
    }

    private static void CheckCSharpErrors(GrammarSource source, int start, SyntaxNode syntax) {
        var errors = syntax.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        foreach (var error in errors) {
            int offset = Math.Clamp(start + error.Location.SourceSpan.Start, 0, source.Text.Length);
            CSharpCCErrors.ParseError(source.MakeToken(0, offset, offset, ""),
                $"Embedded C# {error.Id}: {error.GetMessage()}");
        }
        if (errors.Length != 0) throw new MetaParseException();
    }

    private SyntaxNode ParseCSharpFragment(GrammarSource source, int start, string kind) {
        int remaining = source.Text.Length - start;
        int length = Math.Min(512, remaining);
        for (;;) {
            // Roslyn's string factory scans its input when creating source text.
            // Bound that work for the many small fragments in large grammars.
            string text = source.Text.ToString(new TextSpan(start, length));
            SyntaxNode node = kind switch {
                "parameters" => SyntaxFactory.ParseParameterList(text, options: fragmentOptions, consumeFullText: false),
                "arguments" => SyntaxFactory.ParseArgumentList(text, options: fragmentOptions, consumeFullText: false),
                "type" => SyntaxFactory.ParseTypeName(text, options: fragmentOptions, consumeFullText: false),
                "block" => SyntaxFactory.ParseStatement(text, options: fragmentOptions, consumeFullText: false),
                _ => SyntaxFactory.ParseExpression(text, options: fragmentOptions, consumeFullText: false)
            };
            // A standalone void is a valid production return type, although
            // ParseTypeName rejects it in the variable-type context.
            if (kind == "type" && node is PredefinedTypeSyntax predefined && predefined.Keyword.IsKind(SyntaxKind.VoidKeyword))
                node = SyntaxFactory.PredefinedType(SyntaxFactory.Token(predefined.Keyword.LeadingTrivia,
                    SyntaxKind.VoidKeyword, predefined.Keyword.TrailingTrivia));
            // FullSpan includes trailing trivia. A valid prefix ending in
            // whitespace at the window edge is not enough to establish a boundary.
            if (length == remaining || (!node.ContainsDiagnostics && node.FullSpan.End < length)) return node;
            length = (int)Math.Min(remaining, (long)length * 2);
        }
    }

    private void ResumeGrammar(GrammarSource source, Token last, int end) {
        token = last;
        token.next = null;
        cc_ntKind = -1;
        cc_gen++;
        source.Seek(end);
        token_source.ReInit(source);
    }

    private bool TryReadCSharpBlock(IList<Token>? tokens) {
        // A caller-supplied token manager can retain the historical token-only API.
        if (EmbeddedSource is not { } source) return false;
        Token opening = GetToken(1);
        if (opening.kind != LBRACE) return false;
        int start = source.Offset(opening);
        var syntax = ParseCSharpFragment(source, start, "block");
        CheckCSharpErrors(source, start, syntax);
        if (syntax is not BlockSyntax block) return false;

        int bodyStart = start + block.OpenBraceToken.Span.End;
        int bodyEnd = start + block.CloseBraceToken.SpanStart;
        string body = source.Text.ToString(TextSpan.FromBounds(bodyStart, bodyEnd));
        if (inAction) {
            var insertions = new List<(int Offset, string Text)>();
            foreach (var statement in block.DescendantNodes(node =>
                node is not AnonymousFunctionExpressionSyntax and not LocalFunctionStatementSyntax)) {
                if (statement is not (ReturnStatementSyntax or ThrowStatementSyntax)) continue;
                insertions.Add((start + statement.SpanStart - bodyStart, "{if (true) "));
                insertions.Add((start + statement.Span.End - bodyStart, "}"));
                jumpPatched = true;
            }
            var patched = new StringBuilder(body);
            foreach (var insertion in insertions.OrderByDescending(i => i.Offset))
                patched.Insert(insertion.Offset, insertion.Text);
            body = patched.ToString();
        }

        int end = start + block.Span.End;
        Token closing = source.MakeToken(RBRACE, bodyEnd, end, "}");
        if (!string.IsNullOrWhiteSpace(body)) {
            Token content = source.MakeToken(IDENTIFIER, bodyStart, bodyEnd, body);
            content.EndsWithJump = inAction && AlwaysJumps(block);
            opening.next = content;
            content.next = closing;
            tokens?.Add(content);
        } else {
            opening.next = closing;
        }
        // Drop any speculative grammar lookahead into the C# fragment. Resume
        // exactly after its closing brace, including the original trailing trivia.
        ResumeGrammar(source, closing, end);
        return true;
    }

    private static bool AlwaysJumps(StatementSyntax statement) => statement switch {
        ReturnStatementSyntax or ThrowStatementSyntax => true,
        BlockSyntax { Statements.Count: > 0 } block => AlwaysJumps(block.Statements[^1]),
        IfStatementSyntax { Else: { } otherwise } condition => AlwaysJumps(condition.Statement) && AlwaysJumps(otherwise.Statement),
        _ => false
    };
}
