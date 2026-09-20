#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

using Deveel.CSharpCC.Util;
using System.Globalization;

namespace Deveel.CSharpCC.Parser {
	public static class CSharpFiles {
		private static String ReplaceBackslash(String str) {
			StringBuilder b;
			int i = 0, len = str.Length;

			while (i < len && str[i++] != '\\')
				;

			if (i == len) // No backslash found.
				return str;

			char c;
			b = new StringBuilder();
			for (i = 0; i < len; i++)
				if ((c = str[i]) == '\\')
					b.Append("\\\\");
				else
					b.Append(c);

			return b.ToString();
		}

		internal static double GetVersion(String fileName) {
            IList<string> tn = new List<string>(CSharpCCGlobals.ToolNames);
            tn.Add(CSharpCCGlobals.ToolName);

			string commentHeader = "/* " + CSharpCCGlobals.GetIdString(tn, fileName) + " Version ";
			string file = Path.Combine(Options.getOutputDirectory().FullName, ReplaceBackslash(fileName));

			if (!File.Exists(file)) {
				// Has not yet been created, so it must be up to date.
				return typeof (CSharpCCParser).Assembly.GetName().Version?.Major ?? 0;
			}

			try {
				using var reader = new StringReader(file);
				string? str;
				double version = 0.0;

				// Although the version comment should be the first line, sometimes the
				// user might have put comments before it.
				while ((str = reader.ReadLine()) != null) {
					if (str.StartsWith(commentHeader)) {
						str = str.Substring(commentHeader.Length);
						int pos = str.IndexOf(' ');
						if (pos >= 0)
							str = str.Substring(0, pos);
						if (str.Length > 0) {
							try {
								version = Double.Parse(str, CultureInfo.InvariantCulture);
							} catch (FormatException) {
								// Ignore - leave version as 0.0
							}
						}

						break;
					}
				}

				return version;
			} catch (IOException) {
				return 0.0;
			}
		}

		private static void GenerateFile(string fileName, string templateName, string[] optionNames) {
			GenerateFile(fileName, templateName, Options.getOptions(), optionNames);
		}

		private static void GenerateFile(string fileName, string templateName, IDictionary<string, object> options, string[] optionNames) {
			options["MODERN_CSHARP"] = Options.ModernCSharp;
			options["NULLABLE"] = Options.ModernCSharp ? "?" : "";
			options["INPUT_STREAM_REF"] = Options.ModernCSharp ? "RequiredInputStream" : "inputStream";
			if (Options.ModernCSharp)
				optionNames = [.. optionNames, "CSHARP_VERSION"];
			try {
				string file = Path.Combine(Options.getOutputDirectory().FullName, fileName);
				using OutputFile outputFile = new OutputFile(file, typeof(CSharpFiles).Assembly.GetName().Version?.ToString(), optionNames);

				if (!outputFile.needToWrite) {
					return;
				}

				TextWriter ostr = outputFile.GetTextWriter();
				if (Options.ModernCSharp)
					ostr.WriteLine("#nullable enable");

				bool nsFound = false;
                if (CSharpCCGlobals.CompilationLayout is { } layout) {
                    string header = layout.SupportHeader;
                    // These two legacy templates already import System at the
                    // innermost namespace scope. Keep aliases and outer imports.
                    if (fileName is "SimpleCharStream.cs" or "TokenManagerError.cs") {
                        int scope = header.LastIndexOf('{');
                        int import = header.LastIndexOf("using System;", StringComparison.Ordinal);
                        if (import > scope) header = header.Remove(import, "using System;".Length);
                    }
                    ostr.Write(header);
                } else if (CSharpCCGlobals.cu_to_insertion_point_1.Count != 0 &&
					CSharpCCGlobals.cu_to_insertion_point_1[0].kind == CSharpCCParserConstants.NAMESPACE) {
					for (int i = 1; i < CSharpCCGlobals.cu_to_insertion_point_1.Count; i++) {
						if (CSharpCCGlobals.cu_to_insertion_point_1[i].kind == CSharpCCParserConstants.SEMICOLON) {
							CSharpCCGlobals.cline = CSharpCCGlobals.cu_to_insertion_point_1[0].beginLine;
							CSharpCCGlobals.ccol = CSharpCCGlobals.cu_to_insertion_point_1[0].beginColumn;
							for (int j = 0; j <= i - 1; j++) {
								CSharpCCGlobals.PrintToken(CSharpCCGlobals.cu_to_insertion_point_1[j], ostr);
							}

							nsFound = true;
							ostr.WriteLine("{");
							ostr.WriteLine();
							ostr.WriteLine();
							break;
						}
					}
				}

				CSharpFileGenetor generator = new CSharpFileGenetor(templateName, options);
				generator.Generate(ostr);

				if (CSharpCCGlobals.CompilationLayout is { } ending)
                    ostr.Write(ending.SupportFooter);
                else if (nsFound)
					ostr.WriteLine("}");

				outputFile.Close();
			} catch (IOException e) {
				Console.Error.WriteLine("Failed to create  " + fileName + ": " + e.Message);
				CSharpCCErrors.SemanticError("Could not open file " + fileName + " for writing.");
				throw new InvalidOperationException();
			}			
		}

		public static void GenerateICharStream() {
			GenerateFile("ICharStream.cs", "Deveel.CSharpCC.Templates.ICharStream.template", new String[] { "STATIC", "SUPPORT_CLASS_VISIBILITY_PUBLIC" });
		}

		public static void GenerateParseException() {
			GenerateFile("ParseException.cs", "Deveel.CSharpCC.Templates.ParseException.template", new string[] { "KEEP_LINE_COLUMN", "SUPPORT_CLASS_VISIBILITY_PUBLIC" });
		}

		public static void GenerateTokenManagerError() {
			GenerateFile("TokenManagerError.cs", "Deveel.CSharpCC.Templates.TokenManagerError.template", new string[] { "SUPPORT_CLASS_VISIBILITY_PUBLIC" });
		}

		public static void GenerateToken() {
			GenerateFile("Token.cs", "Deveel.CSharpCC.Templates.Token-2.0.template", new String[] {"TOKEN_EXTENDS", "KEEP_LINE_COLUMN", "SUPPORT_CLASS_VISIBILITY_PUBLIC"});
		}

		public static void GenerateITokenManager() {
			GenerateFile("ITokenManager.cs", "Deveel.CSharpCC.Templates.ITokenManager.template", new String[] { "SUPPORT_CLASS_VISIBILITY_PUBLIC" });
		}

		public static void GenerateSimpleCharStream() {
			string prefix = (Options.getStatic() ? "static " : "");
			IDictionary<string, object> options = Options.getOptions();
			options["PREFIX"] = prefix;

			GenerateFile("SimpleCharStream.cs", "Deveel.CSharpCC.Templates.SimpleCharStream.template", options, new String[] { "STATIC", "SUPPORT_CLASS_VISIBILITY_PUBLIC" });
		}

		public static void GenerateUnicodeCharStream() {
			string prefix = (Options.getStatic() ? "static " : "");
			IDictionary<string, object> options = Options.getOptions();
			options["PREFIX"] = prefix;

			GenerateFile("UnicodeCharStream.cs", "Deveel.CSharpCC.Templates.UnicodeCharStream.template", options, new String[] { "STATIC", "SUPPORT_CLASS_VISIBILITY_PUBLIC" });
		}
	}
}
