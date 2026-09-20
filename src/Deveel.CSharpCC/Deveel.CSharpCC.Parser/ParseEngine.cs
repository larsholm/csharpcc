using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Deveel.CSharpCC.Parser {
    public static class ParseEngine {
        private static TextWriter? outputWriter;
        private static TextWriter ostr {
            get => outputWriter ?? throw new InvalidOperationException("The generation output writer has not been initialized.");
            set => outputWriter = value;
        }
        private static int gensymindex = 0;
        private static int indentamt;
        private static bool cc2LA;

        private static IDictionary<Expansion, Phase3Data> phase3table = new Dictionary<Expansion, Phase3Data>();
        private static IList<Phase3Data> phase3list = [];


        private static bool CodeCheck(Expansion? exp) {
            if (exp is RegularExpression)
                return false;
            if (exp is NonTerminal nonTerminal) {
                NormalProduction prod = nonTerminal.ResolvedProduction;
                if (prod is CodeProduction)
                    return true;
                return CodeCheck(prod.Expansion);
            }
            if (exp is Choice ch) {
                foreach (var choice in ch.Choices) {
                    if (CodeCheck(choice)) {
                        return true;
                    }
                }
                return false;
            }
            if (exp is Sequence seq) {
                foreach (var unit in seq.Units) {
                    if (unit is Lookahead { IsExplicit: true }) {
                        // An explicit lookahead (rather than one generated implicitly). Assume
                        // the user knows what he / she is doing, e.g.
                        //    "A" ( "B" | LOOKAHEAD("X") jcode() | "C" )* "D"
                        return false;
                    }
                    if (CodeCheck((unit)))
                        return true;
                    if (!Semanticize.EmptyExpansionExists(unit))
                        return false;
                }
                return false;
            }
            return exp switch {
                OneOrMore oneOrMore => CodeCheck(oneOrMore.Expansion),
                ZeroOrMore zeroOrMore => CodeCheck(zeroOrMore.Expansion),
                ZeroOrOne zeroOrOne => CodeCheck(zeroOrOne.RequiredExpansion),
                TryBlock tryBlock => CodeCheck(tryBlock.RequiredExpansion),
                _ => false
            };
        }

        private static bool[] firstSet = [];
        private static IList<Lookahead> phase2list = [];

        /**
         * Sets up the array "firstSet" above based on the Expansion argument
         * passed to it.  Since this is a recursive function, it assumes that
         * "firstSet" has been reset before the first call.
         */

        private static void GenFirstSet(Expansion? exp) {
            if (exp is RegularExpression regularExpression) {
                firstSet[regularExpression.Ordinal] = true;
            } else if (exp is NonTerminal nonTerminal) {
                if (nonTerminal.ResolvedProduction is not CodeProduction) {
                    GenFirstSet(nonTerminal.ResolvedProduction.Expansion);
                }
            } else if (exp is Choice ch) {
                foreach (var expansion in ch.Choices) {
                    GenFirstSet(expansion);
                }
            } else if (exp is Sequence seq) {
                if (seq.Units[0] is Lookahead { ActionTokens.Count: not 0 }) {
                    cc2LA = true;
                }
                for (int i = 0; i < seq.Units.Count; i++) {
                    Expansion unit = seq.Units[i];
                    // Javacode productions can not have FIRST sets. Instead we generate the FIRST set
                    // for the preceding LOOKAHEAD (the semantic checks should have made sure that
                    // the LOOKAHEAD is suitable).
                    if (unit is NonTerminal { Production: CodeProduction }) {
                        if (i > 0 && seq.Units[i - 1] is Lookahead la) {
                            GenFirstSet(la.RequiredExpansion);
                        }
                    } else {
                        GenFirstSet(seq.Units[i]);
                    }
                    if (!Semanticize.EmptyExpansionExists(seq.Units[i])) {
                        break;
                    }
                }
            } else if (exp is OneOrMore om) {
                GenFirstSet(om.Expansion);
            } else if (exp is ZeroOrMore zm) {
                GenFirstSet(zm.Expansion);
            } else if (exp is ZeroOrOne zo) {
                GenFirstSet(zo.Expansion);
            } else if (exp is TryBlock tb) {
                GenFirstSet(tb.Expansion);
            }
        }

        private const int NOOPENSTM = 0;
        private const int OPENIF = 1;
        private const int OPENSWITCH = 2;

        private static bool EndsWithJump(Expansion expansion) {
            if (expansion is Action action)
                return action.ActionTokens.Count != 0 && action.ActionTokens[^1].EndsWithJump;
            if (expansion is Sequence sequence)
                return sequence.Units.Count != 0 && EndsWithJump(sequence.Units[^1]);
            if (expansion is Choice choice) {
                foreach (var alternative in choice.Choices)
                    if (!EndsWithJump(alternative)) return false;
                return choice.Choices.Count != 0;
            }
            return false;
        }

        private static String buildLookaheadChecker(Lookahead[] conds, String[] actions, bool defaultTerminates = false, bool[]? actionTerminates = null) {

            // The state variables.
            int state = NOOPENSTM;
            var openStatements = new Stack<int>();
            bool[] casedValues = new bool[CSharpCCGlobals.tokenCount];
            String retval = "";
            Lookahead la;
            Token? t = null;
            int tokenMaskSize = (CSharpCCGlobals.tokenCount - 1)/32 + 1;
            int[] tokenMask = new int[tokenMaskSize];

            // Iterate over all the conditions.
            int index = 0;
            while (index < conds.Length) {

                la = conds[index];
                cc2LA = false;

                if ((la.Amount == 0) ||
                    Semanticize.EmptyExpansionExists(la.RequiredExpansion) ||
                    CodeCheck(la.RequiredExpansion)) {

                    // This handles the following cases:
                    // . If syntactic lookahead is not wanted (and hence explicitly specified
                    //   as 0).
                    // . If it is possible for the lookahead expansion to recognize the empty
                    //   string - in which case the lookahead trivially passes.
                    // . If the lookahead expansion has a JAVACODE production that it directly
                    //   expands to - in which case the lookahead trivially passes.
                    if (la.ActionTokens.Count == 0) {
                        // In addition, if there is no semantic lookahead, then the
                        // lookahead trivially succeeds.  So break the main loop and
                        // treat this case as the default last action.
                        break;
                    } else {
                        // This case is when there is only semantic lookahead
                        // (without any preceding syntactic lookahead).  In this
                        // case, an "if" statement is generated.
                        switch (state) {
                            case NOOPENSTM:
                                retval += "\n" + "if (";
                                openStatements.Push(OPENIF);
                                break;
                            case OPENIF:
                                retval += "\u0002\n" + "} else if (";
                                break;
                            case OPENSWITCH:
                                retval += "\u0002\n" + "default:" + "\u0001";
                                if (Options.getErrorReporting()) {
                                    retval += "\ncc_la1[" + CSharpCCGlobals.maskindex + "] = cc_gen;";
                                    CSharpCCGlobals.maskindex++;
                                }
                                CSharpCCGlobals.maskVals.Add(tokenMask);
                                retval += "\n" + "if (";
                                openStatements.Push(OPENIF);
                                break;
                        }

                        CSharpCCGlobals.PrintTokenSetup(la.ActionTokens[0]);
                        foreach (var token in la.ActionTokens) {
                            t = token;
                            retval += CSharpCCGlobals.PrintToken(t);
                        }

                        retval += CSharpCCGlobals.PrintTrailingComments(t);
                        retval += ") {\u0001" + actions[index];
                        state = OPENIF;
                    }

                } else if (la.Amount == 1 && la.ActionTokens.Count == 0) {
                    // Special optimal processing when the lookahead is exactly 1, and there
                    // is no semantic lookahead.

                    if (firstSet.Length != CSharpCCGlobals.tokenCount) {
                        firstSet = new bool[CSharpCCGlobals.tokenCount];
                    }
                    for (int i = 0; i < CSharpCCGlobals.tokenCount; i++) {
                        firstSet[i] = false;
                    }
                    // cc2LA is set to false at the beginning of the containing "if" statement.
                    // It is checked immediately after the end of the same statement to determine
                    // if lookaheads are to be performed using calls to the cc2 methods.
                    GenFirstSet(la.RequiredExpansion);
                    // GenFirstSet may find that semantic attributes are appropriate for the next
                    // token.  In which case, it sets cc2LA to true.
                    if (!cc2LA) {

                        // This case is if there is no applicable semantic lookahead and the lookahead
                        // is one (excluding the earlier cases such as JAVACODE, etc.).
                        switch (state) {
                            case OPENIF:
                                retval += "\u0002\n" + "} else {\u0001";
                                // Control flows through to next case.
                                goto case NOOPENSTM;
                            case NOOPENSTM:
                                retval += "\n" + "switch (";
                                if (Options.getCacheTokens()) {
                                    retval += (Options.ModernCSharp ? "cc_nextToken.Kind) {\u0001" : "cc_nt.Kind) {\u0001");
                                } else {
                                    retval += "(cc_ntKind==-1)?cc_ntk():cc_ntKind) {\u0001";
                                }
                                for (int i = 0; i < CSharpCCGlobals.tokenCount; i++) {
                                    casedValues[i] = false;
                                }
                                openStatements.Push(OPENSWITCH);
                                tokenMask = new int[tokenMaskSize];
                                for (int i = 0; i < tokenMaskSize; i++) {
                                    tokenMask[i] = 0;
                                }
                                // Don't need to do anything if state is OPENSWITCH.
                                break;
                        }
                        for (int i = 0; i < CSharpCCGlobals.tokenCount; i++) {
                            if (firstSet[i]) {
                                if (!casedValues[i]) {
                                    casedValues[i] = true;
                                    retval += "\u0002\ncase ";
                                    int j1 = i/32;
                                    int j2 = i%32;
                                    tokenMask[j1] |= 1 << j2;
                                    string? s;
                                    if (!CSharpCCGlobals.names_of_tokens.TryGetValue(i, out s)) {
                                        retval += i;
                                    } else {
                                        retval += s;
                                    }
                                    retval += ":\u0001";
                                }
                            }
                        }
                        retval += actions[index];
                        if (!Options.ModernCSharp || actionTerminates?[index] != true)
                            retval += "\nbreak;";
                        state = OPENSWITCH;
                    }

                } else {
                    // This is the case when lookahead is determined through calls to
                    // jj2 methods.  The other case is when lookahead is 1, but semantic
                    // attributes need to be evaluated.  Hence this crazy control structure.

                    cc2LA = true;

                }

                if (cc2LA) {
                    // In this case lookahead is determined by the jj2 methods.

                    switch (state) {
                        case NOOPENSTM:
                            retval += "\n" + "if (";
                            openStatements.Push(OPENIF);
                            break;
                        case OPENIF:
                            retval += "\u0002\n" + "} else if (";
                            break;
                        case OPENSWITCH:
                            retval += "\u0002\n" + "default:" + "\u0001";
                            if (Options.getErrorReporting()) {
                                retval += "\ncc_la1[" + CSharpCCGlobals.maskindex + "] = cc_gen;";
                                CSharpCCGlobals.maskindex++;
                            }
                            CSharpCCGlobals.maskVals.Add(tokenMask);
                            retval += "\n" + "if (";
                            openStatements.Push(OPENIF);
                            break;
                    }
                    CSharpCCGlobals.cc2index++;
                    // At this point, la.la_expansion.InternalName must be "".
                    la.RequiredExpansion.InternalName = "_" + CSharpCCGlobals.cc2index;
                    phase2list.Add(la);
                    retval += "cc_2" + la.RequiredExpansion.InternalName + "(" + la.Amount + ")";
                    if (la.ActionTokens.Count != 0) {
                        // In addition, there is also a semantic lookahead.  So concatenate
                        // the semantic check with the syntactic one.
                        retval += " && (";
                        CSharpCCGlobals.PrintTokenSetup(la.ActionTokens[0]);
                        foreach (var token in la.ActionTokens) {
                            t = token;
                            retval += CSharpCCGlobals.PrintToken(t);
                        }

                        retval += CSharpCCGlobals.PrintTrailingComments(t);
                        retval += ")";
                    }

                    retval += ") {\u0001" + actions[index];
                    state = OPENIF;
                }

                index++;
            }

            // Generate code for the default case.  Note this may not
            // be the last entry of "actions" if any condition can be
            // statically determined to be always "true".

            switch (state) {
                case NOOPENSTM:
                    retval += actions[index];
                    break;
                case OPENIF:
                    retval += "\u0002\n" + "} else {\u0001" + actions[index];
                    break;
                case OPENSWITCH:
                    retval += "\u0002\n" + "default:" + "\u0001";
                    if (Options.getErrorReporting()) {
                        retval += "\ncc_la1[" + CSharpCCGlobals.maskindex + "] = cc_gen;";
                        CSharpCCGlobals.maskVals.Add(tokenMask);
                        CSharpCCGlobals.maskindex++;
                    }
                    retval += actions[index];
                    break;
            }

            // C# requires a terminating statement in every switch section, including
            // defaults that contain nested lookahead if/else blocks.
            bool omitDefaultBreak = Options.ModernCSharp && state == OPENSWITCH &&
                (index == conds.Length ? defaultTerminates : actionTerminates?[index] == true);
            foreach (int statement in openStatements) {
                if (statement == OPENSWITCH && !omitDefaultBreak)
                    retval += "\nbreak;";
                omitDefaultBreak = false;
                retval += "\u0002\n}";
            }

            return retval;
        }

        internal static void dumpFormattedString(String str) {
            char ch = ' ';
            char prevChar;
            bool indentOn = true;
            for (int i = 0; i < str.Length; i++) {
                prevChar = ch;
                ch = str[i];
                if (ch == '\n' && prevChar == '\r') {
                    // do nothing - we've already printed a new line for the '\r'
                    // during the previous iteration.
                } else if (ch == '\n' || ch == '\r') {
                    if (indentOn) {
                        phase1NewLine();
                    } else {
                        ostr.WriteLine();
                    }
                } else if (ch == '\u0001') {
                    indentamt += 2;
                } else if (ch == '\u0002') {
                    indentamt -= 2;
                } else if (ch == '\u0003') {
                    indentOn = false;
                } else if (ch == '\u0004') {
                    indentOn = true;
                } else {
                    ostr.Write(ch);
                }
            }
        }

        private static void buildPhase1Routine(BnfProduction p) {
            Token t = p.ReturnTypeTokens[0];
            bool voidReturn = t.kind == CSharpCCParserConstants.VOID;

            CSharpCCGlobals.PrintTokenSetup(t);
            CSharpCCGlobals.ccol = 1;
            CSharpCCGlobals.PrintLeadingComments(t, ostr);
            ostr.Write("  " + CSharpCCGlobals.staticOpt() + " " + (p.AccessModifier ?? "public") + " ");
            CSharpCCGlobals.cline = t.beginLine;
            CSharpCCGlobals.ccol = t.beginColumn;
            CSharpCCGlobals.PrintTokenOnly(t, ostr);
            for (int i = 1; i < p.ReturnTypeTokens.Count; i++) {
                t = p.ReturnTypeTokens[i];
                CSharpCCGlobals.PrintToken(t, ostr);
            }
            CSharpCCGlobals.PrintTrailingComments(t, ostr);
            ostr.Write(" " + p.Lhs + "(");
            if (p.ParameterTokens.Count != 0) {
                CSharpCCGlobals.PrintTokenSetup(p.ParameterTokens[0]);
                foreach (var token in p.ParameterTokens) {
                    t = token;
                    CSharpCCGlobals.PrintToken(t, ostr);
                }
                CSharpCCGlobals.PrintTrailingComments(t, ostr);
            }
            ostr.Write(") {");
            indentamt = 4;
            if (Options.getDebugParser()) {
                ostr.WriteLine("");
                ostr.WriteLine("    trace_call(\"" + p.Lhs + "\");");
                ostr.Write("    try {");
                indentamt = 6;
            }
            if (p.DeclarationTokens.Count != 0) {
                CSharpCCGlobals.PrintTokenSetup(p.DeclarationTokens[0]);
                CSharpCCGlobals.cline--;
                foreach (var token in p.DeclarationTokens) {
                    t = token;
                    CSharpCCGlobals.PrintToken(t, ostr);
                }
                CSharpCCGlobals.PrintTrailingComments(t, ostr);
            }
            String code = phase1ExpansionGen(p.RequiredExpansion);
            dumpFormattedString(code);
            ostr.WriteLine("");
            if (p.IsJumpPatched && !voidReturn) {
                ostr.WriteLine("    throw new InvalidOperationException(\"Missing return statement in function\");");
            }
            if (Options.getDebugParser()) {
                ostr.WriteLine("    } finally {");
                ostr.WriteLine("      trace_return(\"" + p.Lhs + "\");");
                ostr.WriteLine("    }");
            }
            ostr.WriteLine("  }");
            ostr.WriteLine("");
        }

        private static void phase1NewLine() {
            ostr.WriteLine("");
            for (int i = 0; i < indentamt; i++) {
                ostr.Write(" ");
            }
        }

        private static String phase1ExpansionGen(Expansion e) {
            String retval = "";
            Token? t = null;
            Lookahead[] conds;
            String[] actions;
            if (e is RegularExpression regularExpression) {
                retval += "\n";
                if (regularExpression.LhsTokens.Count != 0) {
                    CSharpCCGlobals.PrintTokenSetup(regularExpression.LhsTokens[0]);
                    foreach (var token in regularExpression.LhsTokens) {
                        t = token;
                        retval += CSharpCCGlobals.PrintToken(t);
                    }
                    retval += CSharpCCGlobals.PrintTrailingComments(t);
                    retval += " = ";
                }
                String tail = regularExpression.RhsToken == null ? ");" : ")." + regularExpression.RhsToken.image + ";";
                if (regularExpression.Label.Equals("")) {
                    string? label;
                    if (CSharpCCGlobals.names_of_tokens.TryGetValue(regularExpression.Ordinal, out label)) {
                        retval += "cc_consume_token(" + label + tail;
                    } else {
                        retval += "cc_consume_token(" + regularExpression.Ordinal + tail;
                    }
                } else {
                    retval += "cc_consume_token(" + regularExpression.Label + tail;
                }
            } else if (e is NonTerminal nonTerminal) {
                retval += "\n";
                if (nonTerminal.LhsTokens.Count != 0) {
                    CSharpCCGlobals.PrintTokenSetup(nonTerminal.LhsTokens[0]);
                    foreach (var token in nonTerminal.LhsTokens) {
                        t = token;
                        retval += CSharpCCGlobals.PrintToken(t);
                    }
                    retval += CSharpCCGlobals.PrintTrailingComments(t);
                    retval += " = ";
                }
                retval += nonTerminal.Name + "(";
                if (nonTerminal.ArgumentTokens.Count != 0) {
                    CSharpCCGlobals.PrintTokenSetup(nonTerminal.ArgumentTokens[0]);
                    foreach (var token in nonTerminal.ArgumentTokens) {
                        t = token;
                        retval += CSharpCCGlobals.PrintToken(t);
                    }
                    retval += CSharpCCGlobals.PrintTrailingComments(t);
                }
                retval += ");";
            } else if (e is Action action) {
                retval += "\u0003\n";
                if (action.ActionTokens.Count != 0) {
                    CSharpCCGlobals.PrintTokenSetup(action.ActionTokens[0]);
                    CSharpCCGlobals.ccol = 1;
                    foreach (var token in action.ActionTokens) {
                        t = token;
                        retval += CSharpCCGlobals.PrintToken(t);
                    }
                    retval += CSharpCCGlobals.PrintTrailingComments(t);
                }
                retval += "\u0004";
            } else if (e is Choice choice) {
                conds = new Lookahead[choice.Choices.Count];
                actions = new String[choice.Choices.Count + 1];
                var actionTerminates = new bool[choice.Choices.Count];
                actions[choice.Choices.Count] = "\n" + "cc_consume_token(-1);\n" + "throw new ParseException();";
                // In previous line, the "throw" never throws an exception since the
                // evaluation of cc_consume_token(-1) causes ParseException to be
                // thrown first.
                Sequence nestedSeq;
                for (int i = 0; i < choice.Choices.Count; i++) {
                    nestedSeq = (Sequence) (choice.Choices[i]);
                    actions[i] = phase1ExpansionGen(nestedSeq);
                    actionTerminates[i] = EndsWithJump(nestedSeq);
                    conds[i] = (Lookahead) (nestedSeq.Units[0]);
                }
                retval = buildLookaheadChecker(conds, actions, defaultTerminates: true, actionTerminates);
            } else if (e is Sequence sequence) {
                // We skip the first element in the following iteration since it is the
                // Lookahead object.
                foreach (var unit in sequence.Units) {
                    retval += phase1ExpansionGen(unit);
                }
            } else if (e is OneOrMore oneOrMore) {
                Expansion nested_e = oneOrMore.Expansion;
                Lookahead la;
                if (nested_e is Sequence nestedSequence) {
                    la = (Lookahead) (nestedSequence.Units[0]);
                } else {
                    la = new Lookahead();
                    la.Amount = Options.getLookahead();
                    la.Expansion = nested_e;
                }
                retval += "\n";
                int labelIndex = ++gensymindex;
                retval += "while (true) {\u0001";
                retval += phase1ExpansionGen(nested_e);
                conds = new Lookahead[1];
                conds[0] = la;
                actions = new String[2];
                actions[0] = "\n;";
                actions[1] = "\ngoto label_" + labelIndex + ";";
                retval += buildLookaheadChecker(conds, actions, defaultTerminates: true);
                retval += "\u0002\n" + "}";
                retval += "label_" + labelIndex + ":;\n";
            } else if (e is ZeroOrMore zeroOrMore) {
                Expansion nested_e = zeroOrMore.Expansion;
                Lookahead la;
                if (nested_e is Sequence nestedSequence) {
                    la = (Lookahead) (nestedSequence.Units[0]);
                } else {
                    la = new Lookahead();
                    la.Amount = Options.getLookahead();
                    la.Expansion = nested_e;
                }
                retval += "\n";
                int labelIndex = ++gensymindex;
                retval += "while (true) {\u0001";
                conds = new Lookahead[1];
                conds[0] = la;
                actions = new String[2];
                actions[0] = "\n;";
                actions[1] = "\ngoto label_" + labelIndex + ";";
                retval += buildLookaheadChecker(conds, actions, defaultTerminates: true);
                retval += phase1ExpansionGen(nested_e);
                retval += "\u0002\n" + "}";
                retval += "label_" + labelIndex + ":;\n";
            } else if (e is ZeroOrOne zeroOrOne) {
                Expansion nested_e = zeroOrOne.RequiredExpansion;
                Lookahead la;
                if (nested_e is Sequence nestedSequence) {
                    la = (Lookahead) (nestedSequence.Units[0]);
                } else {
                    la = new Lookahead();
                    la.Amount = Options.getLookahead();
                    la.Expansion = nested_e;
                }
                conds = new Lookahead[1];
                conds[0] = la;
                actions = new String[2];
                actions[0] = phase1ExpansionGen(nested_e);
                actions[1] = "\n;";
                retval += buildLookaheadChecker(conds, actions);
            } else if (e is TryBlock tryBlock) {
                Expansion nested_e = tryBlock.RequiredExpansion;
                IList<Token> list;
                retval += "\n";
                retval += "try {\u0001";
                retval += phase1ExpansionGen(nested_e);
                retval += "\u0002\n" + "}";
                for (int i = 0; i < tryBlock.CatchBlocks.Count; i++) {
                    retval += " catch (";
                    list = tryBlock.Types[i];
                    if (list.Count != 0) {
                        CSharpCCGlobals.PrintTokenSetup(list[0]);
                        foreach (var token in list) {
                            t = token;
                            retval += CSharpCCGlobals.PrintToken(t);
                        }
                        retval += CSharpCCGlobals.PrintTrailingComments(t);
                    }
                    retval += " ";
                    t = tryBlock.Ids[i];
                    CSharpCCGlobals.PrintTokenSetup(t);
                    retval += CSharpCCGlobals.PrintToken(t);
                    retval += CSharpCCGlobals.PrintTrailingComments(t);
                    retval += ") {\u0003\n";
                    list = tryBlock.CatchBlocks[i];
                    if (list.Count != 0) {
                        CSharpCCGlobals.PrintTokenSetup(list[0]);
                        CSharpCCGlobals.ccol = 1;
                        foreach (var token in list) {
                            t = token;
                            retval += CSharpCCGlobals.PrintToken(t);
                        }
                        retval += CSharpCCGlobals.PrintTrailingComments(t);
                    }
                    retval += "\u0004\n" + "}";
                }
                if (tryBlock.FinallyBlocks != null) {
                    retval += " finally {\u0003\n";
                    if (tryBlock.FinallyBlocks.Count != 0) {
                        CSharpCCGlobals.PrintTokenSetup(tryBlock.FinallyBlocks[0]);
                        CSharpCCGlobals.ccol = 1;
                        foreach (var token in tryBlock.FinallyBlocks) {
                            t = token;
                            retval += CSharpCCGlobals.PrintToken(t);
                        }
                        retval += CSharpCCGlobals.PrintTrailingComments(t);
                    }
                    retval += "\u0004\n" + "}";
                }
            }
            return retval;
        }

        private static void buildPhase2Routine(Lookahead la) {
            Expansion e = la.RequiredExpansion;
            ostr.WriteLine("  private " + CSharpCCGlobals.staticOpt() + " bool cc_2" + e.InternalName + "(int xla) {");
            ostr.WriteLine("    cc_la = xla; cc_lastpos = cc_scanpos = token;");
            ostr.WriteLine("    try { return !cc_3" + e.InternalName + "(); }");
            ostr.WriteLine("    catch(LookaheadSuccess) { return true; }");
            if (Options.getErrorReporting())
                ostr.WriteLine("    finally { cc_save(" + (Int32.Parse(e.InternalName.Substring(1), CultureInfo.InvariantCulture) - 1) + ", xla); }");
            ostr.WriteLine("  }");
            ostr.WriteLine("");
            Phase3Data p3d = new(e, la.Amount);
            phase3list.Add(p3d);
            phase3table[e] = p3d;
        }

        private static bool xsp_declared;

        private static Expansion? cc3_expansion;

        private static String genReturn(bool value) {
            String retval = (value ? "true" : "false");
            if (Options.getDebugLookahead() && cc3_expansion?.Parent is NormalProduction traceProduction) {
                String tracecode = "trace_return(\"" + traceProduction.Lhs + "(LOOKAHEAD " +
                                   (value ? "FAILED" : "SUCCEEDED") + ")\");";
                if (Options.getErrorReporting()) {
                    tracecode = "if (!cc_rescan) " + tracecode;
                }
                return "{ " + tracecode + " return " + retval + "; }";
            } else {
                return "return " + retval + ";";
            }
        }

        private static void generate3R(Expansion e, Phase3Data inf) {
            Expansion seq = e;
            if (e.InternalName.Equals("")) {
                while (true) {
                    if (seq is Sequence { Units.Count: 2 } sequenceExpansion) {
                        seq = sequenceExpansion.Units[1];
                    } else if (seq is NonTerminal nonTerminal) {
                        NormalProduction ntprod = nonTerminal.ResolvedProduction;
                        if (ntprod is CodeProduction) {
                            break; // nothing to do here
                        } else {
                            seq = ntprod.RequiredExpansion;
                        }
                    } else
                        break;
                }

                if (seq is RegularExpression tokenExpression) {
                    e.InternalName = "cc_scan_token(" + tokenExpression.Ordinal + ")";
                    return;
                }

                gensymindex++;
                e.InternalName = "R_" + gensymindex;
            }
            if (!phase3table.TryGetValue(e, out var p3d) ||
                p3d.Count < inf.Count) {
                p3d = new Phase3Data(e, inf.Count);
                phase3list.Add(p3d);
                phase3table[e] = p3d;
            }
        }

        private static void setupPhase3Builds(Phase3Data inf) {
            Expansion e = inf.Expansion;
            if (e is RegularExpression) {
                ; // nothing to here
            } else if (e is NonTerminal nonTerminal) {
                // All expansions of non-terminals have the "name" fields set.  So
                // there's no need to check it below for "nonTerminal" and "ntexp".  In
                // fact, we rely here on the fact that the "name" fields of both these
                // variables are the same.
                NormalProduction ntprod = nonTerminal.ResolvedProduction;
                if (ntprod is CodeProduction) {
                    ; // nothing to do here
                } else {
                    generate3R(ntprod.RequiredExpansion, inf);
                }
            } else if (e is Choice choice) {
                for (int i = 0; i < choice.Choices.Count; i++) {
                    generate3R(choice.Choices[i], inf);
                }
            } else if (e is Sequence sequence) {
                // We skip the first element in the following iteration since it is the
                // Lookahead object.
                int cnt = inf.Count;
                for (int i = 1; i < sequence.Units.Count; i++) {
                    Expansion eseq = sequence.Units[i];
                    setupPhase3Builds(new Phase3Data(eseq, cnt));
                    cnt -= minimumSize(eseq);
                    if (cnt <= 0)
                        break;
                }
            } else if (e is TryBlock tryBlock) {
                setupPhase3Builds(new Phase3Data(tryBlock.RequiredExpansion, inf.Count));
            } else if (e is OneOrMore oneOrMore) {
                generate3R(oneOrMore.Expansion, inf);
            } else if (e is ZeroOrMore zeroOrMore) {
                generate3R(zeroOrMore.Expansion, inf);
            } else if (e is ZeroOrOne zeroOrOne) {
                generate3R(zeroOrOne.RequiredExpansion, inf);
            }
        }

        private static String gencc_3Call(Expansion e) {
            return e.InternalName.StartsWith("cc_scan_token") ? e.InternalName : "cc_3" + e.InternalName + "()";
        }

        private static void buildPhase3Routine(Phase3Data inf, bool recursive_call) {
            Expansion e = inf.Expansion;
            Token? t = null;
            if (e.InternalName.StartsWith("cc_scan_token"))
                return;

            if (!recursive_call) {
                ostr.WriteLine("  private " + CSharpCCGlobals.staticOpt() + "bool cc_3" + e.InternalName + "() {");
                xsp_declared = false;
                if (Options.getDebugLookahead() && e.Parent is NormalProduction parentNormalProduction) {
                    ostr.Write("    ");
                    if (Options.getErrorReporting()) {
                        ostr.Write("if (!cc_rescan) ");
                    }
                    ostr.WriteLine("trace_call(\"" + parentNormalProduction.Lhs + "(LOOKING AHEAD...)\");");
                    cc3_expansion = e;
                } else {
                    cc3_expansion = null;
                }
            }
            if (e is RegularExpression regularExpression) {
                if (regularExpression.Label.Equals("")) {
                    string? label;
                    if (CSharpCCGlobals.names_of_tokens.TryGetValue(regularExpression.Ordinal, out label)) {
                        ostr.WriteLine("    if (cc_scan_token(" + label + ")) " + genReturn(true));
                    } else {
                        ostr.WriteLine("    if (cc_scan_token(" + regularExpression.Ordinal + ")) " + genReturn(true));
                    }
                } else {
                    ostr.WriteLine("    if (cc_scan_token(" + regularExpression.Label + ")) " + genReturn(true));
                }
            } else if (e is NonTerminal nonTerminal) {
                // All expansions of non-terminals have the "name" fields set.  So
                // there's no need to check it below for "nonTerminal" and "ntexp".  In
                // fact, we rely here on the fact that the "name" fields of both these
                // variables are the same.
                NormalProduction ntprod = nonTerminal.ResolvedProduction;
                if (ntprod is CodeProduction) {
                    ostr.WriteLine("    if (true) { cc_la = 0; cc_scanpos = cc_lastpos; " + genReturn(false) + "}");
                } else {
                    Expansion ntexp = ntprod.RequiredExpansion;
                    ostr.WriteLine("    if (" + gencc_3Call(ntexp) + ") " + genReturn(true));
                }
            } else if (e is Choice choice) {
                Sequence nested_seq;
                if (choice.Choices.Count != 1) {
                    if (!xsp_declared) {
                        xsp_declared = true;
                        ostr.WriteLine((Options.ModernCSharp ? "    Token? xsp;" : "    Token xsp;"));
                    }
                    ostr.WriteLine("    xsp = cc_scanpos;");
                }
                for (int i = 0; i < choice.Choices.Count; i++) {
                    nested_seq = (Sequence) (choice.Choices[i]);
                    Lookahead la = (Lookahead) (nested_seq.Units[0]);
                    if (la.ActionTokens.Count != 0) {
                        // We have semantic lookahead that must be evaluated.
                        CSharpCCGlobals.lookaheadNeeded = true;
                        ostr.WriteLine("    cc_lookingAhead = true;");
                        ostr.Write("    cc_semLA = ");
                        CSharpCCGlobals.PrintTokenSetup(la.ActionTokens[0]);
                        foreach (var token in la.ActionTokens) {
                            t = token;
                            CSharpCCGlobals.PrintToken(t, ostr);
                        }
                        CSharpCCGlobals.PrintTrailingComments(t, ostr);
                        ostr.WriteLine(";");
                        ostr.WriteLine("    cc_lookingAhead = false;");
                    }
                    ostr.Write("    if (");
                    if (la.ActionTokens.Count != 0) {
                        ostr.Write("!cc_semLA || ");
                    }
                    if (i != choice.Choices.Count - 1) {
                        ostr.WriteLine(gencc_3Call(nested_seq) + ") {");
                        ostr.WriteLine("    cc_scanpos = xsp;");
                    } else {
                        ostr.WriteLine(gencc_3Call(nested_seq) + ") " + genReturn(true));
                    }
                }
                for (int i = 1; i < choice.Choices.Count; i++) {
                    ostr.WriteLine("    }");
                }
            } else if (e is Sequence sequence) {
                // We skip the first element in the following iteration since it is the
                // Lookahead object.
                int cnt = inf.Count;
                for (int i = 1; i < sequence.Units.Count; i++) {
                    Expansion eseq = (Expansion) (sequence.Units[i]);
                    buildPhase3Routine(new Phase3Data(eseq, cnt), true);

                    cnt -= minimumSize(eseq);
                    if (cnt <= 0)
                        break;
                }
            } else if (e is TryBlock tryBlock) {
                buildPhase3Routine(new Phase3Data(tryBlock.RequiredExpansion, inf.Count), true);
            } else if (e is OneOrMore oneOrMore) {
                if (!xsp_declared) {
                    xsp_declared = true;
                    ostr.WriteLine((Options.ModernCSharp ? "    Token? xsp;" : "    Token xsp;"));
                }
                Expansion nested_e = oneOrMore.Expansion;
                ostr.WriteLine("    if (" + gencc_3Call(nested_e) + ") " + genReturn(true));
                ostr.WriteLine("    while (true) {");
                ostr.WriteLine("      xsp = cc_scanpos;");
                ostr.WriteLine("      if (" + gencc_3Call(nested_e) + ") { cc_scanpos = xsp; break; }");
                ostr.WriteLine("    }");
            } else if (e is ZeroOrMore zeroOrMore) {
                if (!xsp_declared) {
                    xsp_declared = true;
                    ostr.WriteLine((Options.ModernCSharp ? "    Token? xsp;" : "    Token xsp;"));
                }
                Expansion nested_e = zeroOrMore.Expansion;
                ostr.WriteLine("    while (true) {");
                ostr.WriteLine("      xsp = cc_scanpos;");
                ostr.WriteLine("      if (" + gencc_3Call(nested_e) + ") { cc_scanpos = xsp; break; }");
                ostr.WriteLine("    }");
            } else if (e is ZeroOrOne zeroOrOne) {
                if (!xsp_declared) {
                    xsp_declared = true;
                    ostr.WriteLine((Options.ModernCSharp ? "    Token? xsp;" : "    Token xsp;"));
                }
                Expansion nested_e = zeroOrOne.RequiredExpansion;
                ostr.WriteLine("    xsp = cc_scanpos;");
                ostr.WriteLine("    if (" + gencc_3Call(nested_e) + ") cc_scanpos = xsp;");
            }
            if (!recursive_call) {
                ostr.WriteLine("    " + genReturn(false));
                ostr.WriteLine("  }");
                ostr.WriteLine("");
            }
        }

        private static int minimumSize(Expansion e) {
            return minimumSize(e, Int32.MaxValue);
        }

        private static int minimumSize(Expansion e, int oldMin) {
            int retval = 0; // should never be used.  Will be bad if it is.
            if (e.IsMinimumSize) {
                // recursive search for minimum size unnecessary.
                return Int32.MaxValue;
            }

            e.IsMinimumSize = true;
            if (e is RegularExpression) {
                retval = 1;
            } else if (e is NonTerminal nonTerminal) {
                NormalProduction ntprod = nonTerminal.ResolvedProduction;
                if (ntprod is CodeProduction) {
                    retval = Int32.MaxValue;
                    // Make caller think this is unending (for we do not go beyond JAVACODE during
                    // phase3 execution).
                } else {
                    Expansion ntexp = ntprod.RequiredExpansion;
                    retval = minimumSize(ntexp);
                }
            } else if (e is Choice choice) {
                int min = oldMin;
                Expansion nested_e;
                for (int i = 0; min > 1 && i < choice.Choices.Count; i++) {
                    nested_e = choice.Choices[i];
                    int min1 = minimumSize(nested_e, min);
                    if (min > min1)
                        min = min1;
                }
                retval = min;
            } else if (e is Sequence sequence) {
                int min = 0;
                // We skip the first element in the following iteration since it is the
                // Lookahead object.
                for (int i = 1; i < sequence.Units.Count; i++) {
                    Expansion eseq = sequence.Units[i];
                    int mineseq = minimumSize(eseq);
                    if (min == Int32.MaxValue || 
                        mineseq == Int32.MaxValue) {
                        min = Int32.MaxValue; // Adding infinity to something results in infinity.
                    } else {
                        min += mineseq;
                        if (min > oldMin)
                            break;
                    }
                }
                retval = min;
            } else if (e is TryBlock tryBlock) {
                retval = minimumSize(tryBlock.RequiredExpansion);
            } else if (e is OneOrMore oneOrMore) {
                retval = minimumSize(oneOrMore.Expansion);
            } else if (e is ZeroOrMore or ZeroOrOne or Lookahead or Action) {
                retval = 0;
            }
            e.IsMinimumSize = false;
            return retval;
        }

	    internal static void build(TextWriter ps) {
            Token? t = null;

            ostr = ps;

            foreach (var p in CSharpCCGlobals.bnfproductions) {
                if (p is CodeProduction jp) {
                    t = jp.ReturnTypeTokens[0];
                    CSharpCCGlobals.PrintTokenSetup(t);
                    CSharpCCGlobals.ccol = 1;
                    CSharpCCGlobals.PrintLeadingComments(t, ostr);
                    ostr.Write("  " + CSharpCCGlobals.staticOpt() + (p.AccessModifier != null ? p.AccessModifier + " " : ""));
                    CSharpCCGlobals.cline = t.beginLine;
                    CSharpCCGlobals.ccol = t.beginColumn;
                    CSharpCCGlobals.PrintTokenOnly(t, ostr);
                    for (int i = 1; i < jp.ReturnTypeTokens.Count; i++) {
                        t = jp.ReturnTypeTokens[i];
                        CSharpCCGlobals.PrintToken(t, ostr);
                    }
                    CSharpCCGlobals.PrintTrailingComments(t, ostr);
                    ostr.Write(" " + jp.Lhs + "(");
                    if (jp.ParameterTokens.Count != 0) {
                        CSharpCCGlobals.PrintTokenSetup(jp.ParameterTokens[0]);
                        foreach (var token in jp.ParameterTokens) {
                            t = token;
                            CSharpCCGlobals.PrintToken(t, ostr);
                        }
                        CSharpCCGlobals.PrintTrailingComments(t, ostr);
                    }
                    ostr.Write(") {");
                    if (Options.getDebugParser()) {
                        ostr.WriteLine("");
                        ostr.WriteLine("    trace_call(\"" + jp.Lhs + "\");");
                        ostr.Write("    try {");
                    }
                    if (jp.CodeTokens.Count != 0) {
                        CSharpCCGlobals.PrintTokenSetup(jp.CodeTokens[0]);
                        CSharpCCGlobals.cline--;
                        CSharpCCGlobals.PrintTokenList(jp.CodeTokens, ostr);
                    }
                    ostr.WriteLine("");
                    if (Options.getDebugParser()) {
                        ostr.WriteLine("    } finally {");
                        ostr.WriteLine("      trace_return(\"" + jp.Lhs + "\");");
                        ostr.WriteLine("    }");
                    }
                    ostr.WriteLine("  }");
                    ostr.WriteLine("");
                } else {
                    buildPhase1Routine((BnfProduction) p);
                }
            }

            foreach (var lookahead in phase2list) {
                buildPhase2Routine(lookahead);
            }

            // Expanding a production can append further lookahead routines to this work list.
            for (int i = 0; i < phase3list.Count; i++) {
                setupPhase3Builds(phase3list[i]);
            }

            foreach (var phase3Data in phase3table) {
                buildPhase3Routine(phase3Data.Value, false);
            }
        }

        public static void reInit() {
            outputWriter = null;
            gensymindex = 0;
            indentamt = 0;
            cc2LA = false;
            phase2list = [];
            phase3list = [];
            phase3table = new Dictionary<Expansion, Phase3Data>();
            firstSet = [];
            xsp_declared = false;
            cc3_expansion = null;
        }

        private class Phase3Data(Expansion expansion, int count) {
            public Expansion Expansion { get; } = expansion;
            public int Count { get; } = count;
        }
    }
}
