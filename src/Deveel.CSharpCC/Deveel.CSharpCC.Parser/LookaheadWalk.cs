#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;

namespace Deveel.CSharpCC.Parser {
	public static class LookaheadWalk {
		public static bool considerSemanticLA;
		public static List<MatchInfo>? sizeLimitedMatches;
		public static void reInit() {
			considerSemanticLA = false;
			sizeLimitedMatches = null;
		}

		public static IList<MatchInfo> genFirstSet(IList<MatchInfo> partialMatches, Expansion? exp) {
			if (exp is RegularExpression regularExpression) {
				IList<MatchInfo> retval = [];
				for (int i = 0; i < partialMatches.Count; i++) {
					MatchInfo m = partialMatches[i];
					MatchInfo mnew = new();
					for (int j = 0; j < m.firstFreeLoc; j++) {
						mnew.match[j] = m.match[j];
					}
					mnew.firstFreeLoc = m.firstFreeLoc;
					mnew.match[mnew.firstFreeLoc++] = regularExpression.Ordinal;
					if (mnew.firstFreeLoc == MatchInfo.laLimit) {
						(sizeLimitedMatches ?? throw new InvalidOperationException("Lookahead matches have not been initialized.")).Add(mnew);
					} else {
						retval.Add(mnew);
					}
				}
				return retval;
			} else if (exp is NonTerminal nonTerminal) {
				NormalProduction prod = nonTerminal.ResolvedProduction;
				if (prod is CodeProduction) {
					return [];
				} else {
					return genFirstSet(partialMatches, prod.Expansion);
				}
			} else if (exp is Choice ch) {
				IList<MatchInfo> retval = [];
				foreach (Expansion e in ch.Choices) {
					IList<MatchInfo> v = genFirstSet(partialMatches, e);
					listAppend(retval, v);
				}
				return retval;
			} else if (exp is Sequence seq) {
				IList<MatchInfo> v = partialMatches;
				foreach (Expansion unit in seq.Units) {
					v = genFirstSet(v, unit);
					if (v.Count == 0)
						break;
				}
				return v;
			} else if (exp is OneOrMore om) {
				IList<MatchInfo> retval = [];
				IList<MatchInfo> v = partialMatches;
				while (true) {
					v = genFirstSet(v, om.Expansion);
					if (v.Count == 0)
						break;

					listAppend(retval, v);
				}
				return retval;
			} else if (exp is ZeroOrMore zm) {
				IList<MatchInfo> retval = [];
				listAppend(retval, partialMatches);
				IList<MatchInfo> v = partialMatches;
				while (true) {
					v = genFirstSet(v, zm.Expansion);
					if (v.Count == 0)
						break;

					listAppend(retval, v);
				}
				return retval;
			} else if (exp is ZeroOrOne zeroOrOne) {
				IList<MatchInfo> retval = [];
				listAppend(retval, partialMatches);
				listAppend(retval, genFirstSet(partialMatches, zeroOrOne.Expansion));
				return retval;
			} else if (exp is TryBlock tryBlock) {
				return genFirstSet(partialMatches, tryBlock.Expansion);
			} else if (considerSemanticLA &&
			           exp is Lookahead { ActionTokens.Count: not 0 }) {
				return [];
			} else {
				IList<MatchInfo> retval = [];
				listAppend(retval, partialMatches);
				return retval;
			}
		}

		public static IList<MatchInfo> genFollowSet(IList<MatchInfo> partialMatches, Expansion exp, long generation) {
			if (exp.MyGeneration == generation) {
				return [];
			}

			exp.MyGeneration = generation;
			object? parent = exp.Parent;
			if (parent == null) {
				IList<MatchInfo> retval = [];
				listAppend(retval, partialMatches);
				return retval;
			} else if (parent is NormalProduction parentNormalProduction) {
				IList<NonTerminal> parents = parentNormalProduction.Parents;
				IList<MatchInfo> retval = [];
				//System.out.println("1; gen: " + generation + "; exp: " + exp);
				for (int i = 0; i < parents.Count; i++) {
					IList<MatchInfo> v = genFollowSet(partialMatches, parents[i], generation);
					listAppend(retval, v);
				}
				return retval;
			} else if (parent is Sequence seq) {
				IList<MatchInfo> v = partialMatches;
				for (int i = exp.Ordinal + 1; i < seq.Units.Count; i++) {
					v = genFirstSet(v, seq.Units[i]);
					if (v.Count == 0)
						return v;
				}

				IList<MatchInfo> v1 = [];
				IList<MatchInfo> v2 = [];
				listSplit(v, partialMatches, v1, v2);
				if (v1.Count != 0) {
					//System.out.println("2; gen: " + generation + "; exp: " + exp);
					v1 = genFollowSet(v1, seq, generation);
				}
				if (v2.Count != 0) {
					//System.out.println("3; gen: " + generation + "; exp: " + exp);
					v2 = genFollowSet(v2, seq, Expansion.NextGenerationIndex++);
				}
				listAppend(v2, v1);
				return v2;
			} else if (parent is OneOrMore or ZeroOrMore) {
				IList<MatchInfo> moreMatches = [];
				listAppend(moreMatches, partialMatches);
				IList<MatchInfo> v = partialMatches;
				while (true) {
					v = genFirstSet(v, exp);
					if (v.Count == 0)
						break;
					listAppend(moreMatches, v);
				}

				IList<MatchInfo> v1 = [];
				IList<MatchInfo> v2 = [];
				listSplit(moreMatches, partialMatches, v1, v2);
				if (v1.Count != 0) {
					//System.out.println("4; gen: " + generation + "; exp: " + exp);
					v1 = genFollowSet(v1, (Expansion) parent, generation);
				}
				if (v2.Count != 0) {
					//System.out.println("5; gen: " + generation + "; exp: " + exp);
					v2 = genFollowSet(v2, (Expansion) parent, Expansion.NextGenerationIndex++);
				}
				listAppend(v2, v1);
				return v2;
			} else {
				//System.out.println("6; gen: " + generation + "; exp: " + exp);
				return genFollowSet(partialMatches, (Expansion) parent, generation);
			}
		}


		private static void listSplit(IList<MatchInfo> toSplit, IList<MatchInfo> mask, IList<MatchInfo> partInMask, IList<MatchInfo> rest) {
			for (int i = 0; i < toSplit.Count; i++) {
				for (int j = 0; j < mask.Count; j++) {
					if (toSplit[i] == mask[j]) {
						partInMask.Add(toSplit[i]);
						goto OuterLoop;
					}
				}
				rest.Add(toSplit[i]);

			OuterLoop:
				;
			}
		}

		private static void listAppend(IList<MatchInfo> vToAppendTo, IList<MatchInfo> vToAppend) {
			for (int i = 0; i < vToAppend.Count; i++) {
				vToAppendTo.Add(vToAppend[i]);
			}
		}

	}
}