using System;
using System.IO;
using System.Text;
using System.Security.Cryptography;

using NUnit.Framework;

namespace Deveel.CSharpCC.Parser {
	[TestFixture]
    [NonParallelizable]
	public class GenerateParserTest {
        private string outputDirectory;

        [SetUp]
        public void SetUp() {
            ReInitAll();
            outputDirectory = Path.Combine(Path.GetTempPath(), "csharpcc-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(outputDirectory);
            Options.SetCmdLineOption("OUTPUT_DIRECTORY=" + outputDirectory);
        }

		private void ReInitAll() {
			Expansion.reInit();
			CSharpCCErrors.ReInit();
			CSharpCCGlobals.ReInit();
			Options.init();
			CSharpCCParserInternals.reInit();
			RStringLiteral.reInit();
			// CSharpFiles.reInit();
			LexGen.reInit();
			NfaState.reInit();
			MatchInfo.reInit();
			LookaheadWalk.reInit();
			Semanticize.reInit();
			ParseGen.reInit();
			OtherFilesGen.reInit();
			ParseEngine.reInit();
		}

        [TearDown]
        public void TearDown() {
            Directory.Delete(outputDirectory, true);
        }

		[Test]
		public void GenerateNoErrors() {
			var input = MakeUpGrammar();
			SetupOptions();

			using (var reader = new StringReader(input)) {
				var parser = new CSharpCCParser(reader);

				CSharpCCGlobals.FileName = CSharpCCGlobals.OriginalFileName = "SimpleParser.cc";
				parser.csharpcc_input();
				CSharpCCGlobals.CreateOutputDir(Options.getOutputDirectory().FullName);

				Semanticize.start();
				ParseGen.start();
				LexGen.start();
				OtherFilesGen.start();
			}

			Assert.That(CSharpCCErrors.ErrorCount, Is.Zero);
            foreach (var file in new[] { "SimpleParser.cs", "SimpleParserConstants.cs", "SimpleParserTokenManager.cs",
                         "TokenManagerError.cs", "Token.cs", "ParseException.cs", "SimpleCharStream.cs" }) {
                Assert.That(new FileInfo(Path.Combine(outputDirectory, file)).Length, Is.GreaterThan(0), file);
            }
		}

        [Test]
        public void RegeneratesUnmodifiedSupportFiles() {
            GenerateNoErrors();
            var tokenPath = Path.Combine(outputDirectory, "Token.cs");
            var original = File.ReadAllText(tokenPath);
            var marker = "/* CSharpCC - OriginalChecksum=";
            var checksumStart = original.LastIndexOf(marker, StringComparison.Ordinal);
            var expected = Convert.ToHexString(MD5.HashData(Encoding.UTF8.GetBytes(original.Substring(0, checksumStart)))).ToLowerInvariant();
            Assert.That(original.Substring(checksumStart), Does.StartWith(marker + expected));

            ReInitAll();
            Options.SetCmdLineOption("OUTPUT_DIRECTORY=" + outputDirectory);
            GenerateNoErrors();

            Assert.That(CSharpCCErrors.WarningCount, Is.Zero);
            Assert.That(File.ReadAllText(tokenPath), Is.EqualTo(original));
            using var stream = File.Open(tokenPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }

        [Test]
        public void PreservesEditedSupportFiles() {
            GenerateNoErrors();
            var tokenPath = Path.Combine(outputDirectory, "Token.cs");
            File.AppendAllText(tokenPath, "// Custom user code" + Environment.NewLine);
            var edited = File.ReadAllText(tokenPath);

            ReInitAll();
            Options.SetCmdLineOption("OUTPUT_DIRECTORY=" + outputDirectory);
            GenerateNoErrors();

            Assert.That(File.ReadAllText(tokenPath), Is.EqualTo(edited));
        }

		private void SetupOptions() {
			Options.SetCmdLineOption("STATIC=false");
		}

		private string MakeUpGrammar() {
			var sb = new StringBuilder();
			sb.AppendLine("PARSER_BEGIN(SimpleParser)");
			sb.AppendLine("namespace Deveel.CSharpCC.Parser;");
			sb.AppendLine();
			sb.AppendLine("using System;");
			sb.AppendLine();
			sb.AppendLine("public class SimpleParser {");
			sb.AppendLine("}");
			sb.AppendLine();
			sb.AppendLine("PARSER_END(SimpleParser)");
			sb.AppendLine();
			sb.AppendLine("TOKEN: {");
			sb.AppendLine("< READ: \"read\" > |");
			sb.AppendLine("< AND: \"and\" > |");
			sb.AppendLine("< PRINT: \"print\" >");
			sb.AppendLine("}");
			sb.AppendLine();
			sb.AppendLine("SKIP: {");
			sb.AppendLine("\" \" |");
			sb.AppendLine("\"\\t\"");
			sb.AppendLine("}");
			sb.AppendLine();
			sb.AppendLine("MORE: {");
			sb.AppendLine("\"/*\" : IN_MULTI_LINE_COMMENT");
			sb.AppendLine("}");
			sb.AppendLine();
			sb.AppendLine("<IN_MULTI_LINE_COMMENT>");
			sb.AppendLine("SPECIAL_TOKEN: {");
			sb.AppendLine("<MULTI_LINE_COMMENT: \"*/\" > : DEFAULT");
			sb.AppendLine("}");
			sb.AppendLine();
			sb.AppendLine("TOKEN: {");
			sb.AppendLine("< STRING_LITERAL: \"'\" ( \"''\" | \"\\\\\" [\"a\"-\"z\", \"\\\\\", \"%\", \"_\", \"'\"] | ~[\"'\",\"\\\\\"] )* \"'\" >");
			sb.AppendLine("}");
			sb.AppendLine();
			sb.AppendLine("void Input() :");
			sb.AppendLine("{ Token t; string line; }");
			sb.AppendLine("{");
			sb.AppendLine("\"READ\" \"AND\" \"PRINT\" t = <STRING_LITERAL> { line = t.Image; } <EOF>");
			sb.AppendLine("{ Console.Out.WriteLine(line); }");
			sb.AppendLine("}");
			return sb.ToString();
		}
	}
}
