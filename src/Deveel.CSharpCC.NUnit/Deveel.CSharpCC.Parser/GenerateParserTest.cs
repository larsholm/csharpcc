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
        public void TokenConstantsMatchCompatibilitySnapshot() {
            GenerateNoErrors();
            var expected = File.ReadAllText(Path.Combine(TestContext.CurrentContext.TestDirectory, "Snapshots", "SimpleParserConstants.cs.txt"));
            var actual = File.ReadAllText(Path.Combine(outputDirectory, "SimpleParserConstants.cs"));
            Assert.That(actual.Replace("\r\n", "\n"), Is.EqualTo(expected.Replace("\r\n", "\n")));
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

        private string MakeUpGrammar() => """
            PARSER_BEGIN(SimpleParser)
            namespace Deveel.CSharpCC.Parser;

            using System;

            public class SimpleParser {
            }

            PARSER_END(SimpleParser)

            TOKEN: {
            < READ: "read" > |
            < AND: "and" > |
            < PRINT: "print" >
            }

            SKIP: {
            " " |
            "\t"
            }

            MORE: {
            "/*" : IN_MULTI_LINE_COMMENT
            }

            <IN_MULTI_LINE_COMMENT>
            SPECIAL_TOKEN: {
            <MULTI_LINE_COMMENT: "*/" > : DEFAULT
            }

            TOKEN: {
            < STRING_LITERAL: "'" ( "''" | "\\" ["a"-"z", "\\", "%", "_", "'"] | ~["'","\\"] )* "'" >
            }

            void Input() :
            { Token t; string line; }
            {
            "READ" "AND" "PRINT" t = <STRING_LITERAL> { line = t.Image; } <EOF>
            { Console.Out.WriteLine(line); }
            }
            """.ReplaceLineEndings(Environment.NewLine) + Environment.NewLine;
	}
}
