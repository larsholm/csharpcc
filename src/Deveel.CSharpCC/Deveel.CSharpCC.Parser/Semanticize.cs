#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;

namespace Deveel.CSharpCC.Parser {
    public class Semanticize {
        private static IList<IList<RegExprSpec>> removeList = [];
        private static IList<RegExprSpec> itemList = [];

        public static RegularExpression? other;

        private static string? loopString;

        private static void prepareToRemove(IList<RegExprSpec> vec, RegExprSpec item) {
            removeList.Add(vec);
            itemList.Add(item);
        }

        private static void removePreparedItems() {
            for (int i = 0; i < removeList.Count; i++) {
                IList<RegExprSpec> list = removeList[i];
                list.Remove(itemList[i]);
            }
            removeList.Clear();
            itemList.Clear();
        }

		#region start_new

        public static void start() {
            if (CSharpCCErrors.ErrorCount != 0)
                throw new MetaParseException();

            if (Options.getLookahead() > 1 && !Options.getForceLaCheck() && Options.getSanityCheck()) {
                CSharpCCErrors.Warning("Lookahead adequacy checking not being performed since option LOOKAHEAD " +
                                       "is more than 1.  Set option FORCE_LA_CHECK to true to force checking.");
            }

            /*
     * The following walks the entire parse tree to convert all LOOKAHEAD's
     * that are not at choice points (but at beginning of sequences) and converts
     * them to trivial choices.  This way, their semantic lookahead specification
     * can be evaluated during other lookahead evaluations.
     */
            foreach (var production in CSharpCCGlobals.bnfproductions)
                ExpansionTreeWalker.PostOrderWalk(production.Expansion, new LookaheadFixer());

            /*
     * The following loop populates "production_table"
     */
            foreach (var p in CSharpCCGlobals.bnfproductions) {
                if (CSharpCCGlobals.production_table.ContainsKey(p.RequiredName))
                    CSharpCCErrors.SemanticError(p, p.Lhs + " occurs on the left hand side of more than one production.");
                else
                    CSharpCCGlobals.production_table[p.RequiredName] = p;
            }

            /*
     * The following walks the entire parse tree to make sure that all
     * non-terminals on RHS's are defined on the LHS.
     */
            foreach (var production in CSharpCCGlobals.bnfproductions)
                ExpansionTreeWalker.PreOrderWalk(production.Expansion, new ProductionDefinedChecker());

            /*
     * The following loop ensures that all target lexical states are
     * defined.  Also piggybacking on this loop is the detection of
     * <EOF> and <name> in token productions.  After reporting an
     * error, these entries are removed.  Also checked are definitions
     * on inline private regular expressions.
     * This loop works slightly differently when USER_TOKEN_MANAGER
     * is set to true.  In this case, <name> occurrences are OK, while
     * regular expression specs generate a warning.
     */
            foreach (var tp in CSharpCCGlobals.rexprlist) {
                IList<RegExprSpec> respecs = tp.RegexSpecs;
                foreach (var res in respecs) {
                    if (res.NextState != null) {
                        if (!CSharpCCGlobals.lexstate_S2I.ContainsKey(res.NextState)) {
                            CSharpCCErrors.SemanticError(res.NextStateToken,
                                "Lexical state \"" + res.NextState +
                                "\" has not been defined.");
                        }
                    }
                    if (res.RequiredExpression is REndOfFile) {
                        //CSharpCCErrors.SemanticError(res.RequiredExpression, "Badly placed <EOF>.");
                        if (tp.LexStates != null) {
                            CSharpCCErrors.SemanticError(res.RequiredExpression,
                                "EOF action/state change must be specified for all states, " +
                                "i.e., <*>TOKEN:.");
                        }
                        if (tp.Kind != TokenProduction.TOKEN) {
                            CSharpCCErrors.SemanticError(res.RequiredExpression,
                                "EOF action/state change can be specified only in a " +
                                "TOKEN specification.");
                        }
                        if (CSharpCCGlobals.nextStateForEof != null ||
                            CSharpCCGlobals.actForEof != null)
                            CSharpCCErrors.SemanticError(res.RequiredExpression, "Duplicate action/state change specification for <EOF>.");
                        CSharpCCGlobals.actForEof = res.Action;
                        CSharpCCGlobals.nextStateForEof = res.NextState;
                        prepareToRemove(respecs, res);
                    } else if (tp.IsExplicit && Options.getUserTokenManager()) {
                        CSharpCCErrors.Warning(res.RequiredExpression,
                            "Ignoring regular expression specification since " +
                            "option USER_TOKEN_MANAGER has been set to true.");
                    } else if (tp.IsExplicit && !Options.getUserTokenManager() && res.RequiredExpression is RJustName) {
                        CSharpCCErrors.Warning(res.RequiredExpression,
                            "Ignoring free-standing regular expression reference.  " +
                            "If you really want this, you must give it a different label as <NEWLABEL:<"
                            + res.RequiredExpression.Label + ">>.");
                        prepareToRemove(respecs, res);
                    } else if (!tp.IsExplicit && res.RequiredExpression.IsPrivate) {
                        CSharpCCErrors.SemanticError(res.RequiredExpression,
                            "Private (#) regular expression cannot be defined within " +
                            "grammar productions.");
                    }
                }
            }

            removePreparedItems();

            /*
     * The following loop inserts all names of regular expressions into
     * "named_tokens_table" and "ordered_named_tokens".
     * Duplications are flagged as errors.
     */
            foreach (var tp in CSharpCCGlobals.rexprlist) {
                IList<RegExprSpec> respecs = tp.RegexSpecs;
                foreach (var res in respecs) {
                    if (res.RequiredExpression is not RJustName &&
                        !String.IsNullOrEmpty(res.RequiredExpression.Label)) {
                        string s = res.RequiredExpression.Label;
                        if (CSharpCCGlobals.named_tokens_table.ContainsKey(s))
                            CSharpCCErrors.SemanticError(res.RequiredExpression, "Multiply defined lexical token name \"" + s + "\".");
                        else {
                            CSharpCCGlobals.named_tokens_table[s] = res.RequiredExpression;
                            CSharpCCGlobals.ordered_named_tokens.Add(res.RequiredExpression);
                        }
                        if (CSharpCCGlobals.lexstate_S2I.ContainsKey(s)) {
                            CSharpCCErrors.SemanticError(res.RequiredExpression,
                                "Lexical token name \"" + s + "\" is the same as " +
                                "that of a lexical state.");
                        }
                    }
                }
            }

            /*
     * The following code merges multiple uses of the same string in the same
     * lexical state and produces error messages when there are multiple
     * explicit occurrences (outside the BNF) of the string in the same
     * lexical state, or when within BNF occurrences of a string are duplicates
     * of those that occur as non-TOKEN's (SKIP, MORE, SPECIAL_TOKEN) or private
     * regular expressions.  While doing this, this code also numbers all
     * regular expressions (by setting their ordinal values), and populates the
     * table "names_of_tokens".
     */

            CSharpCCGlobals.tokenCount = 1;
            foreach (var tp in CSharpCCGlobals.rexprlist) {
                IList<RegExprSpec> respecs = tp.RegexSpecs;
                if (tp.LexStates == null) {
                    tp.LexStates = new String[CSharpCCGlobals.lexstate_I2S.Count];
                    int i = 0;
                    foreach (var value in CSharpCCGlobals.lexstate_I2S.Values)
                        tp.LexStates[i++] = value;
                }
                var table = new IDictionary<string, IDictionary<string, RegularExpression>>[tp.LexStates.Length];
                for (int i = 0; i < tp.LexStates.Length; i++) {
                    table[i] = CSharpCCGlobals.simple_tokens_table.TryGetValue(tp.LexStates[i], out var stateTable)
                        ? stateTable
                        : throw new InvalidOperationException("No token table exists for lexical state " + tp.LexStates[i] + ".");
                }

	            foreach (var res in respecs) {
                    if (res.RequiredExpression is RStringLiteral sl) {
                        // This loop performs the checks and actions with respect to each lexical state.
                        for (int i = 0; i < table.Length; i++) {
                            // Get table of all case variants of "sl.Image" into table2.
                            IDictionary<string, RegularExpression>? table2;
                            if (!table[i].TryGetValue(sl.Image.ToUpper(), out table2)) {
                                // There are no case variants of "sl.Image" earlier than the current one.
                                // So go ahead and insert this item.
                                if (sl.Ordinal == 0)
                                    sl.Ordinal = CSharpCCGlobals.tokenCount++;
                                table2 = new Dictionary<string, RegularExpression>();
                                table2[sl.Image] = sl;
                                table[i][sl.Image.ToUpper()] = table2;
                            } else if (hasIgnoreCase(table2, sl.Image)) {
                                // hasIgnoreCase sets "other" if it is found.
                                // Since IGNORE_CASE version exists, current one is useless and bad.
                                if (!sl.ProductionContext.IsExplicit) {
                                    // inline BNF string is used earlier with an IGNORE_CASE.
                                    CSharpCCErrors.SemanticError(sl,
                                        "String \"" + sl.Image + "\" can never be matched " +
                                        "due to presence of more general (IGNORE_CASE) regular expression " +
                                        "at line " + other.Line + ", column " + other.Column + ".");
                                } else {
                                    // give the standard error message.
                                    CSharpCCErrors.SemanticError(sl,
                                        "Duplicate definition of string token \"" + sl.Image + "\" " +
                                        "can never be matched.");
                                }
                            } else if (sl.ProductionContext.IgnoreCase) {
                                // This has to be explicit.  A warning needs to be given with respect
                                // to all previous strings.
                                String pos = "";
                                int count = 0;
                                foreach (var rexp in table2.Values) {
                                    if (count != 0)
                                        pos += ",";
                                    pos += " line " + rexp.Line;
                                    count++;
                                }
                                if (count == 1)
                                    CSharpCCErrors.Warning(sl, "String with IGNORE_CASE is partially superceded by string at" + pos + ".");
                                else
                                    CSharpCCErrors.Warning(sl, "String with IGNORE_CASE is partially superceded by strings at" + pos + ".");
                                // This entry is legitimate.  So insert it.
                                if (sl.Ordinal == 0)
                                    sl.Ordinal = CSharpCCGlobals.tokenCount++;

                                table2[sl.Image] = sl;
                                // The above "put" may override an existing entry (that is not IGNORE_CASE) and that's
                                // the desired behavior.
                            } else {
                                // The rest of the cases do not involve IGNORE_CASE.
                                RegularExpression? re;
                                if (!table2.TryGetValue(sl.Image, out re)) {
                                    if (sl.Ordinal == 0)
                                        sl.Ordinal = CSharpCCGlobals.tokenCount++;
                                    table2[sl.Image] = sl;
                                } else if (tp.IsExplicit) {
                                    // This is an error even if the first occurrence was implicit.
                                    if (tp.LexStates[i].Equals("DEFAULT"))
                                        CSharpCCErrors.SemanticError(sl, "Duplicate definition of string token \"" + sl.Image + "\".");
                                    else {
                                        CSharpCCErrors.SemanticError(sl,
                                            "Duplicate definition of string token \"" + sl.Image +
                                            "\" in lexical state \"" + tp.LexStates[i] + "\".");
                                    }
                                } else if (re.ProductionContext.Kind != TokenProduction.TOKEN) {
                                    CSharpCCErrors.SemanticError(sl,
                                        "String token \"" + sl.Image + "\" has been defined as a \"" +
                                        TokenProduction.kindImage[re.ProductionContext.Kind] + "\" token.");
                                } else if (re.IsPrivate) {
                                    CSharpCCErrors.SemanticError(sl,
                                        "String token \"" + sl.Image +
                                        "\" has been defined as a private regular expression.");
                                } else {
                                    // This is now a legitimate reference to an existing RStringLiteral.
                                    // So we assign it a number and take it out of "rexprlist".
                                    // Therefore, if all is OK (no errors), then there will be only unequal
                                    // string literals in each lexical state.  Note that the only way
                                    // this can be legal is if this is a string declared inline within the
                                    // BNF.  Hence, it belongs to only one lexical state - namely "DEFAULT".
                                    sl.Ordinal = re.Ordinal;
                                    prepareToRemove(respecs, res);
                                }
                            }
                        }
                    } else if (res.RequiredExpression is not RJustName)
                        res.RequiredExpression.Ordinal = CSharpCCGlobals.tokenCount++;
                    if (res.RequiredExpression is not RJustName &&
                        !String.IsNullOrEmpty(res.RequiredExpression.Label))
                        CSharpCCGlobals.names_of_tokens[res.RequiredExpression.Ordinal] = res.RequiredExpression.Label;
                    if (res.RequiredExpression is not RJustName)
                        CSharpCCGlobals.rexps_of_tokens[res.RequiredExpression.Ordinal] = res.RequiredExpression;
                }
            }

            removePreparedItems();

            /*
     * The following code performs a tree walk on all regular expressions
     * attaching links to "RJustName"s.  Error messages are given if
     * undeclared names are used, or if "RJustNames" refer to private
     * regular expressions or to regular expressions of any kind other
     * than TOKEN.  In addition, this loop also removes top level
     * "RJustName"s from "rexprlist".
     * This code is not executed if Options.getUserTokenManager() is set to
     * true.  Instead the following block of code is executed.
     */

            if (!Options.getUserTokenManager()) {
                FixRJustNames frjn = new FixRJustNames();
                foreach (var tp in CSharpCCGlobals.rexprlist) {
                    IList<RegExprSpec> respecs = tp.RegexSpecs;
                    foreach (var res in respecs) {
                        frjn.root = res.RequiredExpression;
                        ExpansionTreeWalker.PreOrderWalk(res.RequiredExpression, frjn);
                        if (res.RequiredExpression is RJustName)
                            prepareToRemove(respecs, res);
                    }
                }
            }

            removePreparedItems();

            /*
     * The following code is executed only if Options.getUserTokenManager() is
     * set to true.  This code visits all top-level "RJustName"s (ignores
     * "RJustName"s nested within regular expressions).  Since regular expressions
     * are optional in this case, "RJustName"s without corresponding regular
     * expressions are given ordinal values here.  If "RJustName"s refer to
     * a named regular expression, their ordinal values are set to reflect this.
     * All but one "RJustName" node is removed from the lists by the end of
     * execution of this code.
     */

            if (Options.getUserTokenManager()) {
                foreach (var tp in CSharpCCGlobals.rexprlist) {
                    IList<RegExprSpec> respecs = tp.RegexSpecs;
                    foreach (var res in respecs) {
                        if (res.RequiredExpression is RJustName jn) {
                            RegularExpression? rexp;
                            if (!CSharpCCGlobals.named_tokens_table.TryGetValue(jn.Label, out rexp)) {
                                jn.Ordinal = CSharpCCGlobals.tokenCount++;
                                CSharpCCGlobals.named_tokens_table[jn.Label] = jn;
                                CSharpCCGlobals.ordered_named_tokens.Add(jn);
                                CSharpCCGlobals.names_of_tokens[jn.Ordinal] = jn.Label;
                            } else {
                                jn.Ordinal = rexp.Ordinal;
                                prepareToRemove(respecs, res);
                            }
                        }
                    }
                }
            }

            removePreparedItems();

            /*
     * The following code is executed only if Options.getUserTokenManager() is
     * set to true.  This loop labels any unlabeled regular expression and
     * prints a warning that it is doing so.  These labels are added to
     * "ordered_named_tokens" so that they may be generated into the ...Constants
     * file.
     */
            if (Options.getUserTokenManager()) {
                foreach (var tp in CSharpCCGlobals.rexprlist) {
                    IList<RegExprSpec> respecs = tp.RegexSpecs;
                    foreach (var res in respecs) {
                        int ii = res.RequiredExpression.Ordinal;
                        if (!CSharpCCGlobals.names_of_tokens.ContainsKey(ii)) {
                            CSharpCCErrors.Warning(res.RequiredExpression,
                                "Unlabeled regular expression cannot be referred to by " +
                                "user generated token manager.");
                        }
                    }
                }
            }

            if (CSharpCCErrors.ErrorCount != 0)
                throw new MetaParseException();

            // The following code sets the value of the "emptyPossible" field of NormalProduction
            // nodes.  This field is initialized to false, and then the entire list of
            // productions is processed.  This is repeated as long as at least one item
            // got updated from false to true in the pass.
            bool emptyUpdate = true;
            while (emptyUpdate) {
                emptyUpdate = false;
                foreach (var prod in CSharpCCGlobals.bnfproductions) {
                    if (EmptyExpansionExists(prod.Expansion)) {
                        if (!prod.IsEmptyPossible)
                            emptyUpdate = prod.IsEmptyPossible = true;
                    }
                }
            }

            if (Options.getSanityCheck() && CSharpCCErrors.ErrorCount == 0) {
                // The following code checks that all ZeroOrMore, ZeroOrOne, and OneOrMore nodes
                // do not contain expansions that can expand to the empty token list.
                foreach (var prod in CSharpCCGlobals.bnfproductions)
                    ExpansionTreeWalker.PreOrderWalk(prod.Expansion, new EmptyChecker());

                // The following code goes through the productions and adds pointers to other
                // productions that it can expand to without consuming any tokens.  Once this is
                // done, a left-recursion check can be performed.
                foreach (var prod in CSharpCCGlobals.bnfproductions)
                    addLeftMost(prod, prod.Expansion);

                // Now the following loop calls a recursive walk routine that searches for
                // actual left recursions.  The way the algorithm is coded, once a node has
                // been determined to participate in a left recursive loop, it is not tried
                // in any other loop.
                foreach (var prod in CSharpCCGlobals.bnfproductions) {
                    if (prod.WalkStatus == 0)
                        prodWalk(prod);
                }

                // Now we do a similar, but much simpler walk for the regular expression part of
                // the grammar.  Here we are looking for any kind of loop, not just left recursions,
                // so we only need to do the equivalent of the above walk.
                // This is not done if option USER_TOKEN_MANAGER is set to true.
                if (!Options.getUserTokenManager()) {
                    foreach (var tp in CSharpCCGlobals.rexprlist) {
                        IList<RegExprSpec> respecs = tp.RegexSpecs;
                        foreach (var res in respecs) {
                            RegularExpression rexp = res.RequiredExpression;
                            if (rexp.WalkStatus == 0) {
                                rexp.WalkStatus = -1;
                                if (rexpWalk(rexp)) {
                                    loopString = "..." + rexp.Label + "... --> " + loopString;
                                    CSharpCCErrors.SemanticError(rexp, "Loop in regular expression detected: \"" + loopString + "\"");
                                }
                                rexp.WalkStatus = 1;
                            }
                        }
                    }
                }

                /*
       * The following code performs the lookahead ambiguity checking.
       */
                if (CSharpCCErrors.ErrorCount == 0) {
                    foreach (var prod in CSharpCCGlobals.bnfproductions)
                        ExpansionTreeWalker.PreOrderWalk(prod.Expansion, new LookaheadChecker());
                }
            } // matches "if (Options.getSanityCheck()) {"

            if (CSharpCCErrors.ErrorCount != 0)
                throw new MetaParseException();
        }

		#endregion

		[System.Diagnostics.CodeAnalysis.MemberNotNullWhen(true, nameof(other))]
        public static bool hasIgnoreCase(IDictionary<string, RegularExpression> table, String str) {
            RegularExpression? rexp;
            if (table.TryGetValue(str, out rexp) &&
                !rexp.ProductionContext.IgnoreCase)
                return false;
            foreach (var regularExpression in table.Values) {
                rexp = regularExpression;
                if (rexp.ProductionContext.IgnoreCase) {
                    other = rexp;
                    return true;
                }
            }
            return false;
        }

		[System.Diagnostics.CodeAnalysis.MemberNotNullWhen(true, nameof(other))]
        public static bool hasIgnoreCase(Hashtable table, String str) {
			RegularExpression? rexp = null;
			if ((rexp = (RegularExpression?) table[str]) != null &&
				!rexp.ProductionContext.IgnoreCase)
				return false;
			foreach (RegularExpression regularExpression in table.Values) {
				rexp = regularExpression;
				if (rexp.ProductionContext.IgnoreCase) {
					other = rexp;
					return true;
				}
			}
			return false;
		}

		

        private static void addLeftMost(NormalProduction prod, Expansion? exp) {
            if (exp is NonTerminal nonTerminal) {
                for (int i = 0; i < prod.LeftExpansions.Count; i++) {
                    if (prod.LeftExpansions[i] == nonTerminal.ResolvedProduction)
                        return;
                }
                prod.LeftExpansions.Add(nonTerminal.ResolvedProduction);
            } else if (exp is OneOrMore oneOrMore)
                addLeftMost(prod, oneOrMore.Expansion);
            else if (exp is ZeroOrMore zeroOrMore)
                addLeftMost(prod, zeroOrMore.Expansion);
            else if (exp is ZeroOrOne zeroOrOne)
                addLeftMost(prod, zeroOrOne.Expansion);
            else if (exp is Choice choiceExpansion) {
                foreach (var choice in choiceExpansion.Choices)
                    addLeftMost(prod, choice);
            } else if (exp is Sequence sequenceExpansion) {
                foreach (var unit in sequenceExpansion.Units) {
                    addLeftMost(prod, unit);
                    if (!EmptyExpansionExists(unit))
                        break;
                }
            } else if (exp is TryBlock tryBlock)
                addLeftMost(prod, tryBlock.Expansion);
        }

        private static bool prodWalk(NormalProduction prod) {
            prod.WalkStatus = -1;
            for (int i = 0; i < prod.LeftExpansions.Count; i++) {
                if (prod.LeftExpansions[i].WalkStatus == -1) {
                    prod.LeftExpansions[i].WalkStatus = -2;
                    loopString = prod.Lhs + "... --> " + prod.LeftExpansions[i].Lhs + "...";
                    if (prod.WalkStatus == -2) {
                        prod.WalkStatus = 1;
                        CSharpCCErrors.SemanticError(prod, "Left recursion detected: \"" + loopString + "\"");
                        return false;
                    } else {
                        prod.WalkStatus = 1;
                        return true;
                    }
                } else if (prod.LeftExpansions[i].WalkStatus == 0) {
                    if (prodWalk(prod.LeftExpansions[i])) {
                        loopString = prod.Lhs + "... --> " + loopString;
                        if (prod.WalkStatus == -2) {
                            prod.WalkStatus = 1;
                            CSharpCCErrors.SemanticError(prod, "Left recursion detected: \"" + loopString + "\"");
                            return false;
                        } else {
                            prod.WalkStatus = 1;
                            return true;
                        }
                    }
                }
            }
            prod.WalkStatus = 1;
            return false;
        }

        private static bool rexpWalk(RegularExpression rexp) {
            if (rexp is RJustName jn) {
                if (jn.RequiredExpression.WalkStatus == -1) {
                    jn.RequiredExpression.WalkStatus = -2;
                    loopString = "..." + jn.RequiredExpression.Label + "...";
                    // Note: Only the regexpr's of RJustName nodes and the top leve
                    // regexpr's can have labels.  Hence it is only in these cases that
                    // the labels are checked for to be added to the loopString.
                    return true;
                } else if (jn.RequiredExpression.WalkStatus == 0) {
                    jn.RequiredExpression.WalkStatus = -1;
                    if (rexpWalk(jn.RequiredExpression)) {
                        loopString = "..." + jn.RequiredExpression.Label + "... --> " + loopString;
                        if (jn.RequiredExpression.WalkStatus == -2) {
                            jn.RequiredExpression.WalkStatus = 1;
                            CSharpCCErrors.SemanticError(jn.RequiredExpression, "Loop in regular expression detected: \"" + loopString + "\"");
                            return false;
                        } else {
                            jn.RequiredExpression.WalkStatus = 1;
                            return true;
                        }
                    } else {
                        jn.RequiredExpression.WalkStatus = 1;
                        return false;
                    }
                }
            } else if (rexp is RChoice rChoice) {
                foreach (var choice in rChoice.Choices) {
                    if (rexpWalk(choice))
                        return true;
                }
                return false;
            } else if (rexp is RSequence rSequence) {
                foreach (var unit in rSequence.Units) {
                    if (rexpWalk(unit))
                        return true;
                }
                return false;
            } else if (rexp is ROneOrMore rOneOrMore)
                return rexpWalk(rOneOrMore.RegularExpression);
            else if (rexp is RZeroOrMore rZeroOrMore)
                return rexpWalk(rZeroOrMore.RequiredExpression);
            else if (rexp is RZeroOrOne rZeroOrOne)
                return rexpWalk(rZeroOrOne.RequiredExpression);
            else if (rexp is RRepetitionRange rRepetitionRange)
                return rexpWalk(rRepetitionRange.RequiredExpression);
            return false;
        }

        public static void reInit() {
			removeList = [];
			itemList = [];
			other = null;
			loopString = null;
        }

        public static bool EmptyExpansionExists(Expansion? expansion) {
            if (expansion is NonTerminal nonTerminal)
                return nonTerminal.ResolvedProduction.IsEmptyPossible;
            else if (expansion is Action)
                return true;
            else if (expansion is RegularExpression)
                return false;
            else if (expansion is OneOrMore oneOrMore)
                return EmptyExpansionExists(oneOrMore.Expansion);
            else if (expansion is ZeroOrMore or ZeroOrOne)
                return true;
            else if (expansion is Lookahead)
                return true;
            else if (expansion is Choice choiceExpansion) {
                foreach (var choice in choiceExpansion.Choices) {
                    if (EmptyExpansionExists(choice))
                        return true;
                }
                return false;
            } else if (expansion is Sequence sequenceExpansion) {
                foreach (var unit in sequenceExpansion.Units) {
                    if (!EmptyExpansionExists(unit))
                        return false;
                }
                return true;
            } else if (expansion is TryBlock tryBlock)
                return EmptyExpansionExists(tryBlock.Expansion);
            else
                return false; // This should be dead code.
        }

        #region FixRJustNames

        private class FixRJustNames : ITreeWalkerOp {
            public RegularExpression? root;

            public bool GoDeeper(Expansion e) {
                return true;
            }

            public void Action(Expansion e) {
                if (e is RJustName jn) {
                    RegularExpression? rexp;
                    if (!CSharpCCGlobals.named_tokens_table.TryGetValue(jn.Label, out rexp))
                        CSharpCCErrors.SemanticError(e, "Undefined lexical token name \"" + jn.Label + "\".");
                    else if (jn == root && !jn.ProductionContext.IsExplicit && rexp.IsPrivate) {
                        CSharpCCErrors.SemanticError(e,
                            "Token name \"" + jn.Label + "\" refers to a private " +
                            "(with a #) regular expression.");
                    } else if (jn == root && !jn.ProductionContext.IsExplicit &&
                               rexp.ProductionContext.Kind != TokenProduction.TOKEN) {
                        CSharpCCErrors.SemanticError(e,
                            "Token name \"" + jn.Label + "\" refers to a non-token " +
                            "(SKIP, MORE, IGNORE_IN_BNF) regular expression.");
                    } else {
                        jn.Ordinal = rexp.Ordinal;
                        jn.RegularExpression = rexp;
                    }
                }
            }
        }

        #endregion

        #region LookaheadFixer

        private class LookaheadFixer : ITreeWalkerOp {
            public bool GoDeeper(Expansion e) {
                return e is not RegularExpression;
            }

            public void Action(Expansion e) {
                if (e is Sequence seq) {
                    if (e.Parent is Choice or ZeroOrMore or OneOrMore or ZeroOrOne)
                        return;
                    Lookahead la = (Lookahead) (seq.Units[0]);
                    if (!la.IsExplicit)
                        return;

                    // Create a singleton choice with an empty action.
                    Choice ch = new();
                    ch.Line = la.Line;
                    ch.Column = la.Column;
                    ch.Parent = seq;
                    Sequence seq1 = new();
                    seq1.Line = la.Line;
                    seq1.Column = la.Column;
                    seq1.Parent = ch;
                    seq1.Units.Add(la);
                    la.Parent = seq1;
                    Action act = new();
                    act.Line = la.Line;
                    act.Column = la.Column;
                    act.Parent = seq1;
                    seq1.Units.Add(act);
                    ch.Choices.Add(seq1);
                    if (la.Amount != 0) {
                        if (la.ActionTokens.Count != 0) {
                            CSharpCCErrors.Warning(la,
                                "Encountered LOOKAHEAD(...) at a non-choice location.  " +
                                "Only semantic lookahead will be considered here.");
                        } else
                            CSharpCCErrors.Warning(la, "Encountered LOOKAHEAD(...) at a non-choice location.  This will be ignored.");
                    }
                    // Now we have moved the lookahead into the singleton choice.  Now create
                    // a new dummy lookahead node to replace this one at its original location.
                    Lookahead la1 = new();
                    la1.IsExplicit = false;
                    la1.Line = la.Line;
                    la1.Column = la.Column;
                    la1.Parent = seq;
                    // Now set the la_expansion field of la and la1 with a dummy expansion (we use EOF).
                    la.Expansion = new REndOfFile();
                    la1.Expansion = new REndOfFile();
                    seq.Units[0] = la1;
                    seq.Units[1] = ch;
                }
            }
        }

        #endregion

        #region ProductionDefinedChecker

        private class ProductionDefinedChecker : ITreeWalkerOp {
            public bool GoDeeper(Expansion e) {
                return e is not RegularExpression;
            }

            public void Action(Expansion e) {
                if (e is NonTerminal nt) {
                    NormalProduction? prod;
                    if (!CSharpCCGlobals.production_table.TryGetValue(nt.RequiredName, out prod))
                        CSharpCCErrors.SemanticError(e, "Non-terminal " + nt.Name + " has not been defined.");
                    else {
                        nt.Production = prod;
                        prod.Parents.Add(nt);
                    }
                }
            }
        }

        #endregion

        #region EmptyChecker

        private class EmptyChecker : ITreeWalkerOp {
            public bool GoDeeper(Expansion e) {
                return e is not RegularExpression;
            }

            public void Action(Expansion e) {
                if (e is OneOrMore oneOrMore) {
                    if (Semanticize.EmptyExpansionExists(oneOrMore.Expansion))
                        CSharpCCErrors.SemanticError(e, "Expansion within \"(...)+\" can be matched by empty string.");
                } else if (e is ZeroOrMore zeroOrMore) {
                    if (Semanticize.EmptyExpansionExists(zeroOrMore.Expansion))
                        CSharpCCErrors.SemanticError(e, "Expansion within \"(...)*\" can be matched by empty string.");
                } else if (e is ZeroOrOne zeroOrOne) {
                    if (Semanticize.EmptyExpansionExists(zeroOrOne.Expansion))
                        CSharpCCErrors.SemanticError(e, "Expansion within \"(...)?\" can be matched by empty string.");
                }
            }
        }

        #endregion

        #region LookaheadChecker

        private class LookaheadChecker : ITreeWalkerOp {
            public bool GoDeeper(Expansion e) {
                return e is not (RegularExpression or Lookahead);
            }

            public void Action(Expansion e) {
                if (e is Choice choiceExpansion) {
                    if (Options.getLookahead() == 1 || Options.getForceLaCheck())
                        LookaheadCalc.choiceCalc(choiceExpansion);
                } else if (e is OneOrMore oneOrMore) {
                    if (Options.getForceLaCheck() || (implicitLA(oneOrMore.Expansion) && Options.getLookahead() == 1))
                        LookaheadCalc.ebnfCalc(oneOrMore, oneOrMore.Expansion);
                } else if (e is ZeroOrMore zeroOrMore) {
                    if (Options.getForceLaCheck() || (implicitLA(zeroOrMore.Expansion) && Options.getLookahead() == 1))
                        LookaheadCalc.ebnfCalc(zeroOrMore, zeroOrMore.Expansion);
                } else if (e is ZeroOrOne zeroOrOne) {
                    if (Options.getForceLaCheck() || (implicitLA(zeroOrOne.Expansion) && Options.getLookahead() == 1))
                        LookaheadCalc.ebnfCalc(zeroOrOne, zeroOrOne.Expansion);
                }
            }

            private static bool implicitLA(Expansion? exp) {
                if (exp is not Sequence seq)
                    return true;
                return seq.Units[0] is not Lookahead { IsExplicit: true };
            }
        }

        #endregion
    }
}