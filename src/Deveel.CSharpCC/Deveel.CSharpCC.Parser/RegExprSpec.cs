#nullable enable

using System;

namespace Deveel.CSharpCC.Parser {
    public class RegExprSpec {
        public RegularExpression? RegularExpression { get; internal set; }

        internal RegularExpression RequiredExpression => RegularExpression ??
            throw new InvalidOperationException("RegExprSpec.RegularExpression has not been initialized.");

        public Action? Action { get; internal set; }

        public string? NextState { get; internal set; }

        public Token? NextStateToken { get; internal set; }
    }
}