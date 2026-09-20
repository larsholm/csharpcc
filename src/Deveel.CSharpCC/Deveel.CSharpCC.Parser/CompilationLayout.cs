namespace Deveel.CSharpCC.Parser;

internal sealed record CompilationLayout(string BeforeMembers, string AfterMembers,
    string SupportHeader, string SupportFooter, string NamespaceHeader);
