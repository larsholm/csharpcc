#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

namespace Deveel.CSharpCC.Parser {
	public class TryBlock : Expansion {
		public Expansion? Expansion { get; internal set; }

        internal Expansion RequiredExpansion => Expansion ??
            throw new InvalidOperationException("TryBlock.Expansion has not been initialized.");

		public IList<Token> Ids { get; internal set; } = [];

		public IList<IList<Token>> CatchBlocks { get; internal set; } = [];

		public IList<Token>? FinallyBlocks { get; internal set; }

		public IList<IList<Token>> Types { get; internal set; } = [];

		public override StringBuilder Dump(int indent, IList alreadyDumped) {
			StringBuilder sb = base.Dump(indent, alreadyDumped);
			if (alreadyDumped.Contains(this))
				return sb;
			alreadyDumped.Add(this);
			sb.AppendLine()
				.Append(RequiredExpansion.Dump(indent + 1, alreadyDumped));
			return sb;
		}
	}
}