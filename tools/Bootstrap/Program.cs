using System.Diagnostics;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

bool check = false;
var arguments = new Dictionary<string, string>(StringComparer.Ordinal);
for (int i = 0; i < args.Length; i++) {
    if (args[i] == "--check") { check = true; continue; }
    if (i + 1 == args.Length || args[i] is not ("--generator" or "--grammar" or "--output"))
        throw new ArgumentException("Usage: --generator <csharpcc.dll> --grammar <CSharpCC.cc> --output <directory> [--check]");
    arguments.Add(args[i], Path.GetFullPath(args[i + 1]));
    i++;
}
foreach (string name in new[] { "--generator", "--grammar", "--output" })
    if (!arguments.ContainsKey(name)) throw new ArgumentException("Missing " + name);
string staging = Path.Combine(Path.GetTempPath(), "csharpcc bootstrap " + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(staging);
try {
    var start = new ProcessStartInfo("dotnet") { UseShellExecute = false };
    foreach (string argument in new[] { arguments["--generator"], "-STATIC=false", "-CSHARP_VERSION=legacy",
        "-UNICODE_INPUT=true", "-UNICODE_ESCAPE=true", "-OUTPUT_DIRECTORY=" + staging, arguments["--grammar"] })
        start.ArgumentList.Add(argument);
    using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start generator.");
    await process.WaitForExitAsync();
    if (process.ExitCode != 0) return process.ExitCode;

    var files = new Dictionary<string, string>();
    foreach (string name in new[] { "CSharpCCParser.cs", "CSharpCCParserConstants.cs", "CSharpCCParserTokenManager.cs", "UnicodeCharStream.cs" }) {
        string original = File.ReadAllText(Path.Combine(staging, name));
        var root = CSharpSyntaxTree.ParseText(original, new CSharpParseOptions(LanguageVersion.CSharp14)).GetRoot();
        var errors = root.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToArray();
        if (errors.Length != 0)
            throw new InvalidOperationException(name + ": " + string.Join("; ", errors.Select(d => d.ToString())));
        var rewritten = new BootstrapNames(name == "CSharpCCParserTokenManager.cs").Visit(root) ?? throw new InvalidOperationException("Missing syntax root.");
        string text = rewritten.ToFullString();
        // The generator's support-file checksum precedes adaptation and is no longer valid.
        text = string.Join('\n', text.Replace("\r\n", "\n").Split('\n')
            .Where(line => !line.StartsWith("/* CSharpCC - OriginalChecksum=", StringComparison.Ordinal)));
        text = text.TrimEnd('\n') + "\n";
        files.Add(name == "UnicodeCharStream.cs" ? "CSharpCharStream.cs" : name, text);
    }
    foreach (string name in new[] { "Token.cs", "ParseException.cs", "TokenMgrError.cs" })
        files.Add(name, File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Compatibility", name + ".txt")));

    if (check) {
        bool matches = true;
        foreach (var file in files) {
            string path = Path.Combine(arguments["--output"], file.Key);
            if (!File.Exists(path) || File.ReadAllText(path).Replace("\r\n", "\n") != file.Value.Replace("\r\n", "\n")) {
                Console.Error.WriteLine("Bootstrap file is out of date: " + file.Key);
                matches = false;
            }
        }
        if (matches) Console.WriteLine($"Verified {files.Count} bootstrap files.");
        return matches ? 0 : 1;
    }
    Directory.CreateDirectory(arguments["--output"]);
    foreach (var file in files)
        File.WriteAllText(Path.Combine(arguments["--output"], file.Key), file.Value.Replace("\r\n", "\n"), new UTF8Encoding(false));
    Console.WriteLine($"Regenerated {files.Count} bootstrap files in {arguments["--output"]}.");
    return 0;
} finally {
    Directory.Delete(staging, recursive: true);
}

// Adapt identifiers only. Grammar actions, strings, comments and formatting retain their text.
// Public bootstrap names predate the current consumer generator and remain supported.
sealed class BootstrapNames(bool tokenManager) : CSharpSyntaxRewriter {
    public override SyntaxNode? VisitClassDeclaration(ClassDeclarationSyntax node) {
        var result = (ClassDeclarationSyntax?)base.VisitClassDeclaration(node);
        if (result?.Identifier.ValueText == "CSharpCCParser")
            result = result.AddModifiers(SyntaxFactory.Token(SyntaxKind.PartialKeyword).WithTrailingTrivia(SyntaxFactory.Space));
        if (result?.Identifier.ValueText == "CSharpCCParserTokenManager")
            result = result.AddMembers(SyntaxFactory.ParseMemberDeclaration(
                "\n  internal CSharpCharStream GrammarInput { get { return input_stream; } }\n")!);
        return result;
    }

    public override SyntaxNode? VisitObjectCreationExpression(ObjectCreationExpressionSyntax node) {
        var result = (ObjectCreationExpressionSyntax?)base.VisitObjectCreationExpression(node);
        if (result?.Type.ToString() == "CSharpCharStream" &&
            node.Ancestors().OfType<ClassDeclarationSyntax>().FirstOrDefault()?.Identifier.ValueText == "CSharpCCParser")
            result = result.WithType(SyntaxFactory.IdentifierName("GrammarSource").WithTriviaFrom(result.Type));
        return result;
    }

    public override SyntaxNode? VisitExpressionStatement(ExpressionStatementSyntax node) {
        var result = (ExpressionStatementSyntax?)base.VisitExpressionStatement(node);
        if (result?.Expression is InvocationExpressionSyntax { Expression: MemberAccessExpressionSyntax member } call &&
            member.Expression.ToString() == "cc_inputStream" && member.Name.Identifier.ValueText == "ReInit")
            result = result.WithExpression(SyntaxFactory.AssignmentExpression(SyntaxKind.SimpleAssignmentExpression,
                SyntaxFactory.IdentifierName("cc_inputStream"),
                SyntaxFactory.ObjectCreationExpression(SyntaxFactory.IdentifierName("GrammarSource"))
                    .WithNewKeyword(SyntaxFactory.Token(SyntaxKind.NewKeyword).WithTrailingTrivia(SyntaxFactory.Space))
                    .WithArgumentList(call.ArgumentList))).WithTriviaFrom(result);
        return result;
    }

    private static readonly Dictionary<string, string> Names = new(StringComparer.Ordinal) {
        ["UnicodeCharStream"] = "CSharpCharStream",
        ["TokenManagerError"] = "TokenMgrError",
        ["LEXICAL_ERROR"] = "LexicalError",
        ["STATIC_LEXER_ERROR"] = "StaticLexerError",
        ["INVALID_LEXICAL_STATE"] = "InvalidLexicalState",
        ["LOOP_DETECTED"] = "LoopDetected",
        ["tokenSource"] = "token_source",
        ["cc_nt"] = "mcc_nt",
        ["cc_lookingAhead"] = "lookingAhead",
        ["ccStrLiteralImages"] = "mccstrLiteralImages",
        ["ccNewLexState"] = "mccnewLexState",
        ["ccFillToken"] = "mccFillToken",
        ["TokenImage"] = "tokenImage"
    };

    private static SyntaxTriviaList TrimLineEnds(SyntaxTriviaList trivia) => SyntaxFactory.TriviaList(
        trivia.Where((item, index) => !item.IsKind(SyntaxKind.WhitespaceTrivia) ||
            index + 1 == trivia.Count || !trivia[index + 1].IsKind(SyntaxKind.EndOfLineTrivia)));

    public override SyntaxToken VisitToken(SyntaxToken token) {
        var result = base.VisitToken(token);
        if (token.IsKind(SyntaxKind.IdentifierToken)) {
            string? name = tokenManager && token.ValueText == "inputStream" ? "input_stream" :
                Names.GetValueOrDefault(token.ValueText);
            if (token.ValueText == "reader" && token.Parent?.AncestorsAndSelf().OfType<BaseMethodDeclarationSyntax>()
                .FirstOrDefault()?.ParameterList.Parameters.Any(p => p.Identifier.ValueText == "reader" &&
                    p.Type?.ToString() == "System.IO.TextReader") == true)
                name = "stream";
            if (name != null) result = SyntaxFactory.Identifier(result.LeadingTrivia, name, result.TrailingTrivia);
        }
        return result.WithLeadingTrivia(TrimLineEnds(result.LeadingTrivia)).WithTrailingTrivia(TrimLineEnds(result.TrailingTrivia));
    }

    private static SyntaxTokenList Access(SyntaxTokenList modifiers, SyntaxKind access) =>
        SyntaxFactory.TokenList(modifiers.Select(t => t.IsKind(SyntaxKind.PublicKeyword) ||
            t.IsKind(SyntaxKind.PrivateKeyword) || t.IsKind(SyntaxKind.InternalKeyword)
                ? SyntaxFactory.Token(t.LeadingTrivia, access, t.TrailingTrivia) : t));

    public override SyntaxNode? VisitFieldDeclaration(FieldDeclarationSyntax node) {
        var result = (FieldDeclarationSyntax?)base.VisitFieldDeclaration(node);
        if (result == null) return null;
        string name = result.Declaration.Variables.First().Identifier.ValueText;
        if (name == "lookingAhead") result = result.WithModifiers(Access(result.Modifiers, SyntaxKind.PublicKeyword));
        if (tokenManager && name == "input_stream") result = result.WithModifiers(Access(result.Modifiers, SyntaxKind.ProtectedKeyword));
        if (name == "tokenImage") result = result.WithModifiers(SyntaxFactory.TokenList(result.Modifiers.Where(t => !t.IsKind(SyntaxKind.StaticKeyword))));
        if (name == "staticFlag") {
            var constant = result.Modifiers.First(t => t.IsKind(SyntaxKind.ConstKeyword));
            result = result.WithModifiers(result.Modifiers.Replace(constant,
                SyntaxFactory.Token(constant.LeadingTrivia, SyntaxKind.StaticKeyword, SyntaxFactory.TriviaList(SyntaxFactory.Space)))
                .Add(SyntaxFactory.Token(SyntaxKind.ReadOnlyKeyword).WithTrailingTrivia(constant.TrailingTrivia)));
        }
        return result;
    }

    public override SyntaxNode? VisitMethodDeclaration(MethodDeclarationSyntax node) {
        var result = (MethodDeclarationSyntax?)base.VisitMethodDeclaration(node);
        if (result?.Identifier.ValueText == "mccFillToken")
            result = result.WithModifiers(Access(result.Modifiers, SyntaxKind.ProtectedKeyword));
        if (result?.ParameterList.Parameters.Any(p => p.Type?.ToString() == "System.Text.Encoding") == true)
            result = result.WithModifiers(Access(result.Modifiers, SyntaxKind.InternalKeyword));
        return result;
    }

    public override SyntaxNode? VisitConstructorDeclaration(ConstructorDeclarationSyntax node) {
        var result = (ConstructorDeclarationSyntax?)base.VisitConstructorDeclaration(node);
        if (result?.ParameterList.Parameters.Any(p => p.Type?.ToString() == "System.Text.Encoding") == true)
            result = result.WithModifiers(Access(result.Modifiers, SyntaxKind.InternalKeyword));
        return result;
    }
}
