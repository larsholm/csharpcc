extern alias Sample;

using System;
using System.IO;
using NUnit.Framework;
using SimpleParser = Sample::Deveel.CSharpCC.Parser.SimpleParser;
using ParseException = Sample::Deveel.CSharpCC.Parser.ParseException;

namespace Deveel.CSharpCC.NUnit {
    [TestFixture]
    [NonParallelizable]
    public class GeneratedParserTest {
        [TestCase("read and print 'hello'", "'hello'")]
        [TestCase("READ AND PRINT 'hello'", "'hello'")]
        [TestCase("read\tand  print 'æøå'", "'æøå'")]
        public void ParsesInputWithGeneratedParser(string input, string expected) {
            var originalOutput = Console.Out;
            using var output = new StringWriter();
            try {
                Console.SetOut(output);
                new SimpleParser(new StringReader(input)).Input();
                Assert.That(output.ToString(), Is.EqualTo(expected + Environment.NewLine));
            } finally {
                Console.SetOut(originalOutput);
            }
        }

        [TestCase("read print 'hello'")]
        [TestCase("read and print 'hello' read")]
        [TestCase("")]
        public void RejectsInvalidInput(string input) {
            Assert.Throws<ParseException>(() => new SimpleParser(new StringReader(input)).Input());
        }
    }
}
