#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace Deveel.CSharpCC.Parser {
    public class NonTerminal : Expansion {
        public string? Name { get; internal set; }

        internal string RequiredName => Name ??
            throw new InvalidOperationException("NonTerminal.Name has not been initialized.");

        public IList<Token> ArgumentTokens { get; internal set; } = new List<Token>();

        public IList<Token> LhsTokens { get; internal set; } = new List<Token>();

        // The semantic pass resolves this link after all productions have been parsed.
        public NormalProduction? Production { get; internal set; }

        internal NormalProduction ResolvedProduction => Production ??
            throw new InvalidOperationException("NonTerminal.Production has not been initialized.");

        public override StringBuilder Dump(int indent, IList alreadyDumped) {
            return base.Dump(indent, alreadyDumped).Append(' ').Append(Name);
        }
    }
}
