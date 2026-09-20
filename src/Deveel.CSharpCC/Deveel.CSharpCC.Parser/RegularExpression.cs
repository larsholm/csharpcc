#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace Deveel.CSharpCC.Parser {
    public abstract class RegularExpression : Expansion {
        private IList<Token> lhsTokens;

        protected RegularExpression() {
            lhsTokens = new List<Token>();
	        Label = "";
        }

        public string Label { get; internal set; }

        // Token kind and position in a containing expansion are different identities.
        // Grammar construction writes Expansion.Ordinal; token passes use this value.
        internal new int Ordinal { get; set; }

        public Token? RhsToken { get; internal set; }

        public IList<Token> LhsTokens {
            get { return lhsTokens; }
			internal set { lhsTokens = value; }
        }

        public bool IsPrivate { get; internal set; }

        public TokenProduction? TokenProductionContext { get; internal set; }

        internal TokenProduction ProductionContext => TokenProductionContext ??
            throw new InvalidOperationException("RegularExpression.TokenProductionContext has not been initialized.");

        public virtual bool CanMatchAnyChar {
            get { return false; }
        }

        internal int WalkStatus { get; set; }

        // EOF is handled separately from character matching and returns no NFA.
        public abstract Nfa? GenerateNfa(bool ignoreCase);

        internal Nfa GenerateRequiredNfa(bool ignoreCase) => GenerateNfa(ignoreCase) ??
            throw new InvalidOperationException("EOF has no character-matching NFA.");

        public override StringBuilder Dump(int indent, IList alreadyDumped) {
            var sb = base.Dump(indent, alreadyDumped);
            alreadyDumped.Add(this);
            sb.Append(' ').Append(Label);
            return sb;
        }
    }
}
