#nullable enable

using System;

namespace Deveel.CSharpCC.Parser {
	public class RJustName : RegularExpression {
		public RJustName(Token token, string image) {
			Line = token.beginLine;
			Column = token.beginColumn;
			Label = image;
		}

		public RegularExpression? RegularExpression { get; internal set; }

        internal RegularExpression RequiredExpression => RegularExpression ??
            throw new InvalidOperationException("RJustName.RegularExpression has not been initialized.");

		public override Nfa GenerateNfa(bool ignoreCase) {
			return RequiredExpression.GenerateRequiredNfa(ignoreCase);
		}
	}
}