#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace Deveel.CSharpCC.Parser {
    public class NormalProduction {
        private readonly IList<Token> returnTypeTokens;
        private readonly IList<Token> parameterTokens;

        public NormalProduction() {
            returnTypeTokens = new List<Token>();
            parameterTokens = new List<Token>();
        }

        // CODE productions have no grammar expansion; BNF productions receive one during parsing.
        public Expansion? Expansion { get; internal set; }

        internal Expansion RequiredExpansion => Expansion ??
            throw new InvalidOperationException("This production has no grammar expansion.");

        internal bool IsEmptyPossible { get; set; }

        internal List<NormalProduction> LeftExpansions { get; } = [];

        internal int WalkStatus { get; set; }

        internal Token? FirstToken { get; set; }

        internal Token? LastToken { get; set; }

        public string? AccessModifier { get; internal set; }

        public int Column { get; internal set; }

        public int Line { get; internal set; }

        public string? Lhs { get; internal set; }

        internal string RequiredName => Lhs ??
            throw new InvalidOperationException("NormalProduction.Lhs has not been initialized.");

        internal IList<NonTerminal> Parents { get; } = [];

        public IList<Token> ReturnTypeTokens {
            get { return returnTypeTokens; }
        }

        public IList<Token> ParameterTokens {
            get { return parameterTokens; }
        }

        protected StringBuilder DumpPrefix(int indent)
        {
            var sb = new StringBuilder(128);
            for (int i = 0; i < indent; i++)
                sb.Append("  ");
            return sb;
        }

        public virtual StringBuilder Dump(int indent, IList alreadyDumped) {
            StringBuilder sb = DumpPrefix(indent)
                .Append(GetHashCode())
                .Append(' ')
                .Append(GetType().Name)
                .Append(' ')
                .Append(Lhs);

            if (!alreadyDumped.Contains(this)) {
                alreadyDumped.Add(this);
                if (Expansion != null) {
                    sb.AppendLine()
                        .Append(Expansion.Dump(indent + 1, alreadyDumped));
                }
            }

            return sb;
        }
    }
}
