using System;
using System.IO;

using Deveel.CSharpCC.Parser;

namespace SimpleParserApp {
    class Program {
        static void Main(string[] args) {
            string line = Console.In.ReadLine();
            if (line == null)
                return;

            var parser = new SimpleParser(new StringReader(line));
            parser.Input();
        }
    }
}
