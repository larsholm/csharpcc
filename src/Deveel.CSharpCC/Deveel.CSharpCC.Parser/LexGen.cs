using System;
using System.Collections.Generic;
using System.IO;

namespace Deveel.CSharpCC.Parser {
	public class LexGen {
        private static TextWriter? outputWriter;
        private static TextWriter ostr {
            get => outputWriter ?? throw new InvalidOperationException("The lexer output writer has not been initialized.");
            set => outputWriter = value;
        }
        private static string staticString = "";
        private static string tokMgrClassName = "";
	    private static bool namespaceInserted;

        // Token productions grouped by lexical state.
        private static IDictionary<string, IList<TokenProduction>> allTpsForState = new Dictionary<string, IList<TokenProduction>>();
        public static int lexStateIndex = 0;
        private static int[] kinds = [];
        public static int maxOrdinal = 1;
        public static string lexStateSuffix = "";
        internal static string?[] newLexState = [];
        public static int[] lexStates = [];
        public static bool[] ignoreCase = [];
        public static Action?[] actions = [];
        public static IDictionary<string, NfaState> initStates = new Dictionary<string, NfaState>();
        public static int stateSetSize;
        public static int maxLexStates;
        public static String[] lexStateName = [];
        private static NfaState?[] singlesToSkip = [];
        public static long[] toSkip = [];
        public static long[] toSpecial = [];
        public static long[] toMore = [];
        public static long[] toToken = [];
        public static int defaultLexState;
        public static RegularExpression?[] rexprs = [];
        public static int[] maxLongsReqd = [];
        public static int[] initMatch = [];
        public static int[] canMatchAnyChar = [];
        public static bool hasEmptyMatch;
        public static bool[] canLoop = [];
        public static bool[] stateHasActions = [];
        public static bool hasLoop = false;
        public static bool[] canReachOnMore = [];
        public static bool[] hasNfa = [];
        public static bool[] mixed = [];
        public static NfaState? initialState;
        internal static NfaState RequiredInitialState => initialState ??
            throw new InvalidOperationException("The lexical state has not been initialized.");
        public static int curKind;
        private static bool hasSkipActions = false;
        private static bool hasMoreActions = false;
        private static bool hasTokenActions = false;
        private static bool hasSpecial = false;
        private static bool hasSkip = false;
        private static bool hasMore = false;
        public static RegularExpression? curRE;
        public static bool keepLineCol;

        internal static RegularExpression RequiredTokenExpression(int kind) => rexprs[kind] ??
            throw new InvalidOperationException("No regular expression exists for token kind " + kind + ".");

        // Assumes l != 0L
        static int MaxChar(long l)
        {
            for (int i = 64; i-- > 0; )
                if ((l & (1L << i)) != 0L)
                    return i;

            return 0xffff;
        }

        public static void AddCharToSkip(char c, int kind) {
            var skipState = singlesToSkip[lexStateIndex] ?? throw new InvalidOperationException("The skip state has not been initialized.");
            skipState.AddChar(c);
            skipState.kind = kind;
        }

        private static void PrintClassHead() {
            int i, j;

            try {
                string tmp = Path.Combine(Options.getOutputDirectory().FullName, tokMgrClassName + ".cs");
                ostr = new StreamWriter(tmp);
                List<string> tn = new List<string>(CSharpCCGlobals.ToolNames);
                tn.Add(CSharpCCGlobals.ToolName);

                ostr.WriteLine("/* " + CSharpCCGlobals.GetIdString(tn, tokMgrClassName + ".cs") + " */");
                if (Options.ModernCSharp) ostr.WriteLine("#nullable enable");

                if (CSharpCCGlobals.CompilationLayout is { } layout) {
                    ostr.Write(layout.SupportHeader);
                } else {
                int l = 0, kind;
                i = 1;
                for (;;) {
                    if (CSharpCCGlobals.cu_to_insertion_point_1.Count <= l)
                        break;

                    // TODO: write the namespace ...
                    kind = CSharpCCGlobals.cu_to_insertion_point_1[l].kind;
					int gKind = kind;
                    if (kind == CSharpCCParserConstants.NAMESPACE ||
                        kind == CSharpCCParserConstants.USING) {
                        for (; i < CSharpCCGlobals.cu_to_insertion_point_1.Count; i++) {
                            kind = CSharpCCGlobals.cu_to_insertion_point_1[i].kind;
                            if (kind == CSharpCCParserConstants.SEMICOLON ||
                                kind == CSharpCCParserConstants.ABSTRACT ||
                                kind == CSharpCCParserConstants.SEALED ||
                                kind == CSharpCCParserConstants.PUBLIC ||
                                kind == CSharpCCParserConstants.CLASS ||
                                kind == CSharpCCParserConstants.INTERFACE) {
                                CSharpCCGlobals.cline = CSharpCCGlobals.cu_to_insertion_point_1[l].beginLine;
                                CSharpCCGlobals.ccol = CSharpCCGlobals.cu_to_insertion_point_1[l].beginColumn;
                                for (j = l; j < i; j++) {
                                    CSharpCCGlobals.PrintToken(CSharpCCGlobals.cu_to_insertion_point_1[j], ostr);
                                }
	                            if (kind == CSharpCCParserConstants.SEMICOLON) {
									if (gKind == CSharpCCParserConstants.USING) {
										CSharpCCGlobals.PrintToken((Token)(CSharpCCGlobals.cu_to_insertion_point_1[j]), ostr);
									} else if (gKind == CSharpCCParserConstants.NAMESPACE) {
										ostr.WriteLine(" {");
									    namespaceInserted = true;
									}
	                            }
	                            ostr.WriteLine("");
                                break;
                            }
                        }
                        l = ++i;
                    } else
                        break;
                }

                }

                ostr.WriteLine("");
                ostr.WriteLine("// Token Manager.");
                if (Options.getSupportClassVisibilityPublic()) {
                    ostr.Write("public ");
                }
                ostr.WriteLine("class " + tokMgrClassName + " : " + CSharpCCGlobals.cu_name + "Constants {");
            } catch (IOException) {
                CSharpCCErrors.SemanticError("Could not create file : " + tokMgrClassName + ".cs\n");
                throw new InvalidOperationException();
            }

            if (CSharpCCGlobals.token_mgr_decls != null &&
                CSharpCCGlobals.token_mgr_decls.Count > 0) {
                Token t = CSharpCCGlobals.token_mgr_decls[0];
                bool commonTokenActionSeen = CSharpCCGlobals.TokenManagerHasCommonAction;
                bool commonTokenActionNeeded = Options.getCommonTokenAction();

                CSharpCCGlobals.PrintTokenSetup(CSharpCCGlobals.token_mgr_decls[0]);
                CSharpCCGlobals.ccol = 1;

                for (j = 0; j < CSharpCCGlobals.token_mgr_decls.Count; j++) {
                    t = CSharpCCGlobals.token_mgr_decls[j];
                    if (t.kind == CSharpCCParserConstants.IDENTIFIER &&
                        commonTokenActionNeeded &&
                        !commonTokenActionSeen)
                        commonTokenActionSeen = t.image.Equals("CommonTokenAction");

                    CSharpCCGlobals.PrintToken(t, ostr);
                }

                ostr.WriteLine("");
                if (commonTokenActionNeeded && !commonTokenActionSeen)
                    CSharpCCErrors.Warning("You have the COMMON_TOKEN_ACTION option set. " +
                                           "But it appears you have not defined the method :\n" +
                                           "      " + staticString + "void CommonTokenAction(Token t)\n" +
                                           "in your TOKEN_MGR_DECLS. The generated token manager will not compile.");

            } else if (Options.getCommonTokenAction()) {
                CSharpCCErrors.Warning("You have the COMMON_TOKEN_ACTION option set. " +
                                       "But you have not defined the method :\n" +
                                       "      " + staticString + "void CommonTokenAction(Token t)\n" +
                                       "in your TOKEN_MGR_DECLS. The generated token manager will not compile.");
            }

            ostr.WriteLine("");
            ostr.WriteLine("  // Debug output.");
            ostr.WriteLine("  public {0} System.IO.TextWriter debugStream = Console.Out;", staticString);
            ostr.WriteLine("  // Set debug output.");
            ostr.Write("  public {0} void SetDebugStream(System.IO.TextWriter ds)", staticString);
            ostr.WriteLine(" { debugStream = ds; }");

            if (Options.getTokenManagerUsesParser() && !Options.getStatic()) {
                ostr.WriteLine("");
                ostr.WriteLine("  // The parser.");
                ostr.WriteLine((Options.ModernCSharp ? "  public {0} parser;" : "  public {0} parser = null;"), CSharpCCGlobals.cu_name);
            }
        }

        private static void DumpDebugMethods() {

            ostr.WriteLine("  {0} int kindCnt = 0;", staticString);
            ostr.Write("  internal {0} string ccKindsForBitVector(int i, long vec) ", staticString);
            ostr.WriteLine("{");
            ostr.WriteLine("    string retVal = \"\";");
            ostr.WriteLine("    if (i == 0)");
            ostr.WriteLine("       kindCnt = 0;");
            ostr.WriteLine("    for (int j = 0; j < 64; j++) {");
            ostr.WriteLine("       if ((vec & (1L << j)) != 0L) {");
            ostr.WriteLine("          if (kindCnt++ > 0)");
            ostr.WriteLine("             retVal += \", \";");
            ostr.WriteLine("          if (kindCnt % 5 == 0)");
            ostr.WriteLine("             retVal += \"\\n     \";");
            ostr.WriteLine("          retVal += TokenImage[i * 64 + j];");
            ostr.WriteLine("       }");
            ostr.WriteLine("    }");
            ostr.WriteLine("    return retVal;");
            ostr.WriteLine("  }");
            ostr.WriteLine("");

            ostr.Write("  internal {0} string ccKindsForStateVector(int lexState, int[] vec, int start, int end)", staticString);
            ostr.WriteLine("{");
            ostr.WriteLine("    bool[] kindDone = new bool[{0}];", maxOrdinal);
            ostr.WriteLine("    string retVal = \"\";");
            ostr.WriteLine("    int cnt = 0;");
            ostr.WriteLine("    for (int i = start; i < end; i++) {");
            ostr.WriteLine("     if (vec[i] == -1)");
            ostr.WriteLine("       continue;");
            ostr.WriteLine("     int[] stateSet = statesForState[curLexState][vec[i]];");
            ostr.WriteLine("     for (int j = 0; j < stateSet.Length; j++) {");
            ostr.WriteLine("       int state = stateSet[j];");
            ostr.WriteLine("       if (!kindDone[kindForState[lexState][state]]) {");
            ostr.WriteLine("          kindDone[kindForState[lexState][state]] = true;");
            ostr.WriteLine("          if (cnt++ > 0)");
            ostr.WriteLine("             retVal += \", \";");
            ostr.WriteLine("          if (cnt % 5 == 0)");
            ostr.WriteLine("             retVal += \"\\n     \";");
            ostr.WriteLine("          retVal += TokenImage[kindForState[lexState][state]];");
            ostr.WriteLine("       }");
            ostr.WriteLine("     }");
            ostr.WriteLine("    }");
            ostr.WriteLine("    if (cnt == 0)");
            ostr.WriteLine("       return \"{  }\";");
            ostr.WriteLine("    else");
            ostr.WriteLine("       return \"{ \" + retVal + \" }\";");
            ostr.WriteLine("  }");
            ostr.WriteLine("");
        }

        private static void BuildLexStatesTable() {
            int i;

            String[] tmpLexStateName = new String[CSharpCCGlobals.lexstate_I2S.Count];
            foreach (var tp in CSharpCCGlobals.rexprlist) {
                IList<RegExprSpec> respecs = tp.RegexSpecs;
                IList<TokenProduction>? tps;

                for (i = 0; i < tp.ResolvedLexStates.Length; i++) {
                    if (!allTpsForState.TryGetValue(tp.ResolvedLexStates[i], out tps)) {
                        tmpLexStateName[maxLexStates++] = tp.ResolvedLexStates[i];
                        allTpsForState[tp.ResolvedLexStates[i]] = tps = new List<TokenProduction>();
                    }

                    tps.Add(tp);
                }

                if (respecs == null || respecs.Count == 0)
                    continue;

                RegularExpression re;
                for (i = 0; i < respecs.Count; i++)
                    if (maxOrdinal <= (re = respecs[i].RequiredExpression).Ordinal)
                        maxOrdinal = re.Ordinal + 1;
            }

            // Even an EOF-only grammar needs a lexer state and its empty transition tables.
            foreach (var stateName in CSharpCCGlobals.lexstate_I2S.Values) {
                if (!allTpsForState.ContainsKey(stateName)) {
                    tmpLexStateName[maxLexStates++] = stateName;
                    allTpsForState[stateName] = new List<TokenProduction>();
                }
            }

            kinds = new int[maxOrdinal];
            toSkip = new long[maxOrdinal/64 + 1];
            toSpecial = new long[maxOrdinal/64 + 1];
            toMore = new long[maxOrdinal/64 + 1];
            toToken = new long[maxOrdinal/64 + 1];
            toToken[0] = 1L;
            actions = new Action?[maxOrdinal];
            actions[0] = CSharpCCGlobals.actForEof;
            hasTokenActions = CSharpCCGlobals.actForEof != null;
            initStates = new Dictionary<string, NfaState>();
            canMatchAnyChar = new int[maxLexStates];
            canLoop = new bool[maxLexStates];
            stateHasActions = new bool[maxLexStates];
            lexStateName = new String[maxLexStates];
            singlesToSkip = new NfaState?[maxLexStates];
            Array.Copy(tmpLexStateName, 0, lexStateName, 0, maxLexStates);

            for (i = 0; i < maxLexStates; i++)
                canMatchAnyChar[i] = -1;

            hasNfa = new bool[maxLexStates];
            mixed = new bool[maxLexStates];
            maxLongsReqd = new int[maxLexStates];
            initMatch = new int[maxLexStates];
            newLexState = new string?[maxOrdinal];
            newLexState[0] = CSharpCCGlobals.nextStateForEof;
            hasEmptyMatch = false;
            lexStates = new int[maxOrdinal];
            ignoreCase = new bool[maxOrdinal];
            rexprs = new RegularExpression?[maxOrdinal];
            RStringLiteral.allImages = new String[maxOrdinal];
            canReachOnMore = new bool[maxLexStates];
        }

        private static int GetIndex(String name) {
            for (int i = 0; i < lexStateName.Length; i++)
                if (lexStateName[i] != null && lexStateName[i].Equals(name))
                    return i;

            throw new InvalidOperationException(); // Should never come here
        }

		public static void start() {
			if (!Options.getBuildTokenManager() ||
			    Options.getUserTokenManager() ||
			    CSharpCCErrors.ErrorCount > 0)
				return;

            try {
                GenerateTokenManager();
            } finally {
                try {
                    outputWriter?.Dispose();
                } finally {
                    outputWriter = null;
                }
            }
        }

        private static void GenerateTokenManager() {
			keepLineCol = Options.getKeepLineColumn();
			List<RegularExpression> choices = new List<RegularExpression>();
			TokenProduction tp;
			int i, j;

			staticString = (Options.getStatic() ? "static " : "");
			tokMgrClassName = CSharpCCGlobals.cu_name + "TokenManager";

			PrintClassHead();
			BuildLexStatesTable();

			bool ignoring = false;

			foreach (var key in allTpsForState.Keys) {
				NfaState.ReInit();
				RStringLiteral.ReInit();

				lexStateIndex = GetIndex(key);
				lexStateSuffix = "_" + lexStateIndex;
				IList<TokenProduction> allTps = allTpsForState[key];
				initStates[key] = initialState = new NfaState();
				ignoring = false;

				singlesToSkip[lexStateIndex] = new NfaState { dummy = true };

				if (key.Equals("DEFAULT"))
					defaultLexState = lexStateIndex;

				for (i = 0; i < allTps.Count; i++) {
					tp = allTps[i];
					int kind = tp.Kind;
					bool ignore = tp.IgnoreCase;
					IList<RegExprSpec> rexps = tp.RegexSpecs;

					if (i == 0)
						ignoring = ignore;

					for (j = 0; j < rexps.Count; j++) {
						RegExprSpec respec = rexps[j];
						curRE = respec.RequiredExpression;

						rexprs[curKind = curRE.Ordinal] = curRE;
						lexStates[curRE.Ordinal] = lexStateIndex;
						ignoreCase[curRE.Ordinal] = ignore;

						if (curRE.IsPrivate) {
							kinds[curRE.Ordinal] = -1;
							continue;
						}

						if (curRE is RStringLiteral &&
						    !((RStringLiteral) curRE).Image.Equals("")) {
							((RStringLiteral) curRE).GenerateDfa(ostr, curRE.Ordinal);
							if (i != 0 && !mixed[lexStateIndex] && ignoring != ignore)
								mixed[lexStateIndex] = true;
						} else if (curRE.CanMatchAnyChar) {
							if (canMatchAnyChar[lexStateIndex] == -1 ||
							    canMatchAnyChar[lexStateIndex] > curRE.Ordinal)
								canMatchAnyChar[lexStateIndex] = curRE.Ordinal;
						} else {
							Nfa temp;

							if (curRE is RChoice)
								choices.Add(curRE);

							temp = curRE.GenerateRequiredNfa(ignore);
							temp.End.isFinal = true;
							temp.End.kind = curRE.Ordinal;
							initialState.AddMove(temp.Start);
						}

						if (kinds.Length < curRE.Ordinal) {
							int[] tmp = new int[curRE.Ordinal + 1];

							Array.Copy(kinds, 0, tmp, 0, kinds.Length);
							kinds = tmp;
						}
						//System.out.println("   ordina : " + curRE.ordinal);

						kinds[curRE.Ordinal] = kind;

						if (respec.NextState != null &&
						    !respec.NextState.Equals(lexStateName[lexStateIndex]))
							newLexState[curRE.Ordinal] = respec.NextState;

						if (respec.Action != null && respec.Action.ActionTokens != null &&
						    respec.Action.ActionTokens.Count > 0)
							actions[curRE.Ordinal] = respec.Action;

						switch (kind) {
							case TokenProduction.SPECIAL:
								hasSkipActions |= (actions[curRE.Ordinal] != null) ||
								                  (newLexState[curRE.Ordinal] != null);
								hasSpecial = true;
								toSpecial[curRE.Ordinal/64] |= 1L << (curRE.Ordinal%64);
								toSkip[curRE.Ordinal/64] |= 1L << (curRE.Ordinal%64);
								break;
							case TokenProduction.SKIP:
								hasSkipActions |= (actions[curRE.Ordinal] != null);
								hasSkip = true;
								toSkip[curRE.Ordinal/64] |= 1L << (curRE.Ordinal%64);
								break;
							case TokenProduction.MORE:
								hasMoreActions |= (actions[curRE.Ordinal] != null);
								hasMore = true;
								toMore[curRE.Ordinal/64] |= 1L << (curRE.Ordinal%64);

								if (newLexState[curRE.Ordinal] is string nextStateName)
									canReachOnMore[GetIndex(nextStateName)] = true;
								else
									canReachOnMore[lexStateIndex] = true;

								break;
							case TokenProduction.TOKEN:
								hasTokenActions |= (actions[curRE.Ordinal] != null);
								toToken[curRE.Ordinal/64] |= 1L << (curRE.Ordinal%64);
								break;
						}
					}
				}

				// Generate a static block for initializing the nfa transitions
				NfaState.ComputeClosures();

				for (i = 0; i < initialState.epsilonMoves.Count; i++)
					initialState.epsilonMoves[i].GenerateCode();

				if (hasNfa[lexStateIndex] = (NfaState.generatedStates != 0)) {
					initialState.GenerateCode();
					initialState.GenerateInitMoves(ostr);
				}

				if (initialState.kind != Int32.MaxValue && initialState.kind != 0) {
					if ((toSkip[initialState.kind/64] & (1L << initialState.kind)) != 0L ||
					    (toSpecial[initialState.kind/64] & (1L << initialState.kind)) != 0L)
						hasSkipActions = true;
					else if ((toMore[initialState.kind/64] & (1L << initialState.kind)) != 0L)
						hasMoreActions = true;
					else
						hasTokenActions = true;

					if (initMatch[lexStateIndex] == 0 ||
					    initMatch[lexStateIndex] > initialState.kind) {
						initMatch[lexStateIndex] = initialState.kind;
						hasEmptyMatch = true;
					}
				} else if (initMatch[lexStateIndex] == 0)
					initMatch[lexStateIndex] = Int32.MaxValue;

				RStringLiteral.FillSubString();

				if (hasNfa[lexStateIndex] && !mixed[lexStateIndex])
					RStringLiteral.GenerateNfaStartStates(ostr, initialState);

				RStringLiteral.DumpDfaCode(ostr);

				if (hasNfa[lexStateIndex])
					NfaState.DumpMoveNfa(ostr);

				if (stateSetSize < NfaState.generatedStates)
					stateSetSize = NfaState.generatedStates;
			}

			for (i = 0; i < choices.Count; i++)
				((RChoice) choices[i]).CheckUnmatchability();

			NfaState.DumpStateSets(ostr);
			CheckEmptyStringMatch();
			NfaState.DumpNonAsciiMoveMethods(ostr);
			RStringLiteral.DumpStrLiteralImages(ostr);
			DumpStaticVarDeclarations();
			DumpFillToken();
			DumpGetNextToken();

			if (Options.getDebugTokenManager()) {
				NfaState.DumpStatesForKind(ostr);
				DumpDebugMethods();
			}

			if (hasLoop) {
                ostr.WriteLine("{0}int[] ccEmptyLineNo = new int[{1}];", staticString, maxLexStates);
                ostr.WriteLine("{0}int[] ccEmptyColNo = new int[{1}];", staticString, maxLexStates);
				ostr.WriteLine("{0}bool[] ccBeenHere = new bool[{1}];", staticString, maxLexStates);
			}

			if (hasSkipActions)
				DumpSkipActions();
			if (hasMoreActions)
				DumpMoreActions();
			if (hasTokenActions)
				DumpTokenActions();

			NfaState.PrintBoilerPlate(ostr);
			ostr.WriteLine( /*{*/ "}");

			if (CSharpCCGlobals.CompilationLayout is { } ending)
                ostr.Write(ending.SupportFooter);
            else if (namespaceInserted)
				ostr.WriteLine("}");
				

		}

		private static void CheckEmptyStringMatch() {
            int i, j, k, len;
            bool[] seen = new bool[maxLexStates];
            bool[] done = new bool[maxLexStates];
            String cycle;
            String reList;

            for (i = 0; i < maxLexStates; i++) {
                if (done[i] || initMatch[i] == 0 || initMatch[i] == Int32.MaxValue ||
                    canMatchAnyChar[i] != -1)
                    continue;

                done[i] = true;
                len = 0;
                cycle = "";
                reList = "";

                for (k = 0; k < maxLexStates; k++)
                    seen[k] = false;

                j = i;
                seen[i] = true;
                cycle += lexStateName[j] + "-->";
                while (newLexState[initMatch[j]] is string nextStateName) {
                    cycle += newLexState[initMatch[j]];
                    if (seen[j = GetIndex(nextStateName)])
                        break;

                    cycle += "-->";
                    done[j] = true;
                    seen[j] = true;
                    if (initMatch[j] == 0 || initMatch[j] == Int32.MaxValue ||
                        canMatchAnyChar[j] != -1)
                        goto Outer;
                    if (len != 0)
                        reList += "; ";
                    reList += "line " + RequiredTokenExpression(initMatch[j]).Line + ", column " +
                              RequiredTokenExpression(initMatch[j]).Column;
                    len++;
                }

                if (newLexState[initMatch[j]] == null)
                    cycle += lexStateName[lexStates[initMatch[j]]];

                for (k = 0; k < maxLexStates; k++)
                    canLoop[k] |= seen[k];

                hasLoop = true;
                if (len == 0)
                    CSharpCCErrors.Warning(rexprs[initMatch[i]],
                        "Regular expression" + ((RequiredTokenExpression(initMatch[i]).Label.Equals(""))
                            ? ""
                            : (" for " + RequiredTokenExpression(initMatch[i]).Label)) +
                        " can be matched by the empty string (\"\") in lexical state " +
                        lexStateName[i] + ". This can result in an endless loop of " +
                        "empty string matches.");
                else {
                    CSharpCCErrors.Warning(rexprs[initMatch[i]],
                        "Regular expression" + ((RequiredTokenExpression(initMatch[i]).Label.Equals(""))
                            ? ""
                            : (" for " + RequiredTokenExpression(initMatch[i]).Label)) +
                        " can be matched by the empty string (\"\") in lexical state " +
                        lexStateName[i] + ". This regular expression along with the " +
                        "regular expressions at " + reList + " forms the cycle \n   " +
                        cycle + "\ncontaining regular expressions with empty matches." +
                        " This can result in an endless loop of empty string matches.");
                }

			Outer:
				;
            }
        }

        private static void DumpStaticVarDeclarations() {
            int i;
            String charStreamName;

            ostr.WriteLine("");
            ostr.WriteLine("// Lexer state names.");
            ostr.WriteLine("public static readonly string[] lexStateNames = {");
            for (i = 0; i < maxLexStates; i++) {
                ostr.WriteLine("   \"{0}\"", lexStateName[i]);
                if (i < maxLexStates - 1)
                    ostr.WriteLine(", ");
            }
            ostr.WriteLine("};");

            if (maxLexStates > 1) {
                ostr.WriteLine("");
                ostr.WriteLine("// Lex State array.");
                ostr.Write("public static readonly int[] ccNewLexState = {");

                for (i = 0; i < maxOrdinal; i++) {
                    if (i%25 == 0)
                        ostr.WriteLine("   ");

                    if (newLexState[i] is not string nextStateName)
                        ostr.Write("-1, ");
                    else
                        ostr.Write(GetIndex(nextStateName) + ", ");
                }
                ostr.WriteLine("};");
            }

            if (hasSkip || hasMore || hasSpecial) {
                // Bit vector for TOKEN
                ostr.Write("static readonly long[] ccToToken = {");
                for (i = 0; i < maxOrdinal/64 + 1; i++) {
                    if (i%4 == 0)
                        ostr.WriteLine("   ");

                    ostr.Write(toToken[i] + "L, ");
                }
                ostr.WriteLine("};");
            }

            if (hasSkip || hasSpecial) {
                // Bit vector for SKIP
                ostr.Write("static readonly long[] ccToSkip = {");
                for (i = 0; i < maxOrdinal/64 + 1; i++) {
                    if (i%4 == 0)
                        ostr.WriteLine("   ");

                    ostr.Write(toSkip[i] + "L, ");
                }
                ostr.WriteLine("};");
            }

            if (hasSpecial) {
                // Bit vector for SPECIAL
                ostr.Write("static readonly long[] ccToSpecial = {");
                for (i = 0; i < maxOrdinal/64 + 1; i++) {
                    if (i%4 == 0)
                        ostr.WriteLine("   ");

                    ostr.Write(toSpecial[i] + "L, ");
                }
                ostr.WriteLine("};");
            }

            if (hasMore) {
                // Bit vector for MORE
                ostr.Write("static readonly long[] ccToMore = {");
                for (i = 0; i < maxOrdinal/64 + 1; i++) {
                    if (i%4 == 0)
                        ostr.WriteLine("   ");

                    ostr.Write(toMore[i] + "L, ");
                }

                ostr.WriteLine("};");
            }

            if (Options.getUserCharStream())
                charStreamName = "ICharStream";
            else {
                if (Options.getUnicodeEscape())
                    charStreamName = "UnicodeCharStream";
                else
                    charStreamName = "SimpleCharStream";
            }

            ostr.WriteLine((Options.ModernCSharp ? "internal {0}{1}? inputStream;" : "internal {0}{1} inputStream;"), staticString, charStreamName);
            if (Options.ModernCSharp)
                ostr.WriteLine("private {0}{1} RequiredInputStream => inputStream ?? throw new System.InvalidOperationException(\"The token manager has not been initialized.\");", staticString, charStreamName);

            ostr.WriteLine("private {0}readonly int[] ccRounds = new int[{1}];", staticString, stateSetSize);
            ostr.WriteLine("private {0}readonly int[] ccStateSet = new int[{1}];", staticString, (2 * stateSetSize));

            if (hasMoreActions || hasSkipActions || hasTokenActions) {
                ostr.WriteLine("private {0}{1} image = new {1}();", staticString, Options.stringBufOrBuild());
                ostr.WriteLine("private {0}int ccImageLen;", staticString);
                if (!Options.ModernCSharp || hasMoreActions || Array.Exists(actions, action => action?.ActionTokens.Count > 0))
                    ostr.WriteLine("private {0}int lengthOfMatch;", staticString);
            }

            ostr.WriteLine("{0}protected char curChar;", staticString);

            if (Options.getTokenManagerUsesParser() && !Options.getStatic()) {
                ostr.WriteLine("");
                ostr.WriteLine("// Constructor with parser.");
                ostr.Write("public {0} ({1} parser, {2} stream) ", tokMgrClassName, CSharpCCGlobals.cu_name, charStreamName);
                ostr.WriteLine("{");
                ostr.WriteLine("   this.parser = parser;");
            } else {
                ostr.WriteLine("// Constructor.");
                ostr.Write("public {0} ({1} stream)", tokMgrClassName, charStreamName);
                ostr.WriteLine("{");
            }

            if (Options.getStatic() && !Options.getUserCharStream()) {
                ostr.WriteLine("   if (inputStream != null)");
                ostr.WriteLine("      throw new TokenManagerError(\"ERROR: Second call to constructor of static lexer. " +
                               "You must use ReInit() to initialize the static variables.\", TokenManagerError.STATIC_LEXER_ERROR);");
            } else if (!Options.getUserCharStream()) {
                if (Options.getUnicodeEscape())
                    ostr.WriteLine("   if (UnicodeCharStream.staticFlag)");
                else
                    ostr.WriteLine("   if (SimpleCharStream.staticFlag)");

                ostr.WriteLine("      throw new InvalidOperationException(\"ERROR: Cannot use a static ICharStream class with a " +
                               "non-static lexical analyzer.\");");
            }

            ostr.WriteLine("   inputStream = stream;");

            ostr.WriteLine("}");

            if (Options.getTokenManagerUsesParser() && !Options.getStatic()) {
                ostr.WriteLine("");
                ostr.WriteLine("// Constructor with parser.");
                ostr.WriteLine("public {0} ({1} parser, {2} stream, int lexState)", tokMgrClassName, CSharpCCGlobals.cu_name, charStreamName);
                ostr.WriteLine("   : this(parser, stream) {");
            } else {
                ostr.WriteLine("");
                ostr.WriteLine("// Constructor.");
                ostr.WriteLine("public {0} ({1} stream, int lexState)", tokMgrClassName, charStreamName);
                ostr.WriteLine("   : this(stream) {");
            }
            ostr.WriteLine("   SwitchTo(lexState);");
            ostr.WriteLine("}");

            // Reinit method for reinitializing the parser (for static parsers).
            ostr.WriteLine("");
            ostr.WriteLine("/** Reinitialise parser. */");
            ostr.Write("public {0}void ReInit({1} stream)", staticString, charStreamName);
            ostr.WriteLine("{");
            ostr.WriteLine("   ccMatchedPos = ccNewStateCnt = 0;");
            ostr.WriteLine("   curLexState = defaultLexState;");
            ostr.WriteLine("   inputStream = stream;");
            ostr.WriteLine("   ReInitRounds();");
            ostr.WriteLine("}");

            // Method to reinitialize the jjrounds array.
            ostr.Write("private {0}void ReInitRounds()", staticString);
            ostr.WriteLine("{");
            ostr.WriteLine("   int i;");
            ostr.WriteLine("   ccRound = {0};", (Int32.MinValue + 1));
            ostr.WriteLine("   for (i = {0}; i-- > 0;)", stateSetSize);
            ostr.WriteLine("      ccRounds[i] = Int32.MinValue;");
            ostr.WriteLine("}");

            // Reinit method for reinitializing the parser (for static parsers).
            ostr.WriteLine("");
            ostr.WriteLine("// Reinitialise parser.");
            ostr.Write("public {0}void ReInit({1} stream, int lexState)", staticString, charStreamName);
            ostr.WriteLine("{");
            ostr.WriteLine("   ReInit(stream);");
            ostr.WriteLine("   SwitchTo(lexState);");
            ostr.WriteLine("}");

            ostr.WriteLine("");
            ostr.WriteLine("// Switch to specified lex state.");
            ostr.Write("public {0}void SwitchTo(int lexState)", staticString);
            ostr.WriteLine("{");
            ostr.WriteLine("   if (lexState >= {0} || lexState < 0)", lexStateName.Length);
            ostr.WriteLine("      throw new TokenManagerError(\"Error: Ignoring invalid lexical state : \"" +
                           " + lexState + \". State unchanged.\", TokenManagerError.INVALID_LEXICAL_STATE);");
            ostr.WriteLine("   else");
            ostr.WriteLine("      curLexState = lexState;");
            ostr.WriteLine("}");

            ostr.WriteLine();
        }

        private static void DumpFillToken() {
            double tokenVersion = CSharpFiles.GetVersion("Token.cs");
            bool hasBinaryNewToken = tokenVersion > 4.09;

            ostr.Write("internal {0}Token ccFillToken()", staticString);
            ostr.WriteLine("{");
            ostr.WriteLine("   Token t;");
            ostr.WriteLine("   string curTokenImage;");
            if (keepLineCol) {
                ostr.WriteLine("   int beginLine;");
                ostr.WriteLine("   int endLine;");
                ostr.WriteLine("   int beginColumn;");
                ostr.WriteLine("   int endColumn;");
            }

            if (hasEmptyMatch) {
                ostr.WriteLine("   if (ccMatchedPos < 0) {");
                ostr.WriteLine("      if (image == null)");
                ostr.WriteLine("         curTokenImage = \"\";");
                ostr.WriteLine("      else");
                ostr.WriteLine("         curTokenImage = image.ToString();");

                if (keepLineCol) {
                    ostr.WriteLine("      beginLine = endLine = " + CSharpCCGlobals.CharStreamReference + ".BeginLine;");
                    ostr.WriteLine("      beginColumn = endColumn = " + CSharpCCGlobals.CharStreamReference + ".BeginColumn;");
                }

                ostr.WriteLine("   } else {");
                ostr.WriteLine((Options.ModernCSharp ? "      string? im = ccStrLiteralImages[ccMatchedKind];" : "      string im = ccStrLiteralImages[ccMatchedKind];"));
                ostr.WriteLine("      curTokenImage = (im == null) ? " + CSharpCCGlobals.CharStreamReference + ".GetImage() : im;");

                if (keepLineCol) {
                    ostr.WriteLine("      beginLine = " + CSharpCCGlobals.CharStreamReference + ".BeginLine;");
                    ostr.WriteLine("      beginColumn = " + CSharpCCGlobals.CharStreamReference + ".BeginColumn;");
                    ostr.WriteLine("      endLine = " + CSharpCCGlobals.CharStreamReference + ".EndLine;");
                    ostr.WriteLine("      endColumn = " + CSharpCCGlobals.CharStreamReference + ".EndColumn;");
                }

                ostr.WriteLine("   }");
            } else {
                ostr.WriteLine((Options.ModernCSharp ? "   string? im = ccStrLiteralImages[ccMatchedKind];" : "   string im = ccStrLiteralImages[ccMatchedKind];"));
                ostr.WriteLine("   curTokenImage = (im == null) ? " + CSharpCCGlobals.CharStreamReference + ".GetImage() : im;");
                if (keepLineCol) {
                    ostr.WriteLine("   beginLine = " + CSharpCCGlobals.CharStreamReference + ".BeginLine;");
                    ostr.WriteLine("   beginColumn = " + CSharpCCGlobals.CharStreamReference + ".BeginColumn;");
                    ostr.WriteLine("   endLine = " + CSharpCCGlobals.CharStreamReference + ".EndLine;");
                    ostr.WriteLine("   endColumn = " + CSharpCCGlobals.CharStreamReference + ".EndColumn;");
                }
            }

            if (Options.getTokenFactory().Length > 0) {
                ostr.WriteLine("   t = {0}.NewToken(ccMatchedKind, curTokenImage);", Options.getTokenFactory());
            } else if (hasBinaryNewToken) {
                ostr.WriteLine("   t = Token.NewToken(ccMatchedKind, curTokenImage);");
            } else {
                ostr.WriteLine("   t = Token.NewToken(ccMatchedKind);");
                ostr.WriteLine("   t.Kind = ccMatchedKind;");
                ostr.WriteLine("   t.Image = curTokenImage;");
            }

            if (keepLineCol) {
                ostr.WriteLine("");
                ostr.WriteLine("   t.BeginLine = beginLine;");
                ostr.WriteLine("   t.EndLine = endLine;");
                ostr.WriteLine("   t.BeginColumn = beginColumn;");
                ostr.WriteLine("   t.EndColumn = endColumn;");
            }

            ostr.WriteLine("");
            ostr.WriteLine("   return t;");
            ostr.WriteLine("}");
        }

        private static void DumpGetNextToken() {
            int i;
            bool usesEofLabel = false;

            ostr.WriteLine("");
            ostr.WriteLine(staticString + "int curLexState = " + defaultLexState + ";");
            ostr.WriteLine(staticString + "int defaultLexState = " + defaultLexState + ";");
            ostr.WriteLine(staticString + "int ccNewStateCnt;");
            ostr.WriteLine(staticString + "int ccRound;");
            ostr.WriteLine(staticString + "int ccMatchedPos;");
            ostr.WriteLine(staticString + "int ccMatchedKind;");
            ostr.WriteLine("");
            ostr.WriteLine("// Get the next Token.");
            ostr.WriteLine("public " + staticString + "Token GetNextToken() ");
            ostr.WriteLine("{");
            if (hasSpecial) {
                ostr.WriteLine((Options.ModernCSharp ? "  Token? specialToken = null;" : "  Token specialToken = null;"));
            }
            ostr.WriteLine("  Token matchedToken;");
            ostr.WriteLine("  int curPos = 0;");
            ostr.WriteLine("");
            // OLD: ostr.WriteLine("  EOFLoop :\n  for (;;)");
            ostr.WriteLine("  while (true) {");
            ostr.WriteLine("   try {");
            ostr.WriteLine("      curChar = " + CSharpCCGlobals.CharStreamReference + ".BeginToken();");
            ostr.WriteLine((Options.ModernCSharp ? "   } catch(System.IO.IOException) {" : "   }  catch(System.IO.IOException e) {"));

            if (Options.getDebugTokenManager())
                ostr.WriteLine("      debugStream.WriteLine(\"Returning the <EOF> token.\");");

            ostr.WriteLine("      ccMatchedKind = 0;");
            ostr.WriteLine("      matchedToken = ccFillToken();");

            if (hasSpecial)
                ostr.WriteLine("      matchedToken.SpecialToken = specialToken;");

            if (CSharpCCGlobals.nextStateForEof != null ||
                CSharpCCGlobals.actForEof != null)
                ostr.WriteLine("      TokenLexicalActions(matchedToken);");

            if (Options.getCommonTokenAction())
                ostr.WriteLine("      CommonTokenAction(matchedToken);");

            ostr.WriteLine("      return matchedToken;");
            ostr.WriteLine("   }");

            if (hasMoreActions || hasSkipActions || hasTokenActions) {
                ostr.WriteLine("   image.Length = 0;");
                ostr.WriteLine("   ccImageLen = 0;");
            }

            ostr.WriteLine("");

            String prefix = "";
            if (hasMore) {
                ostr.WriteLine("   while (true)");
                ostr.WriteLine("   {");
                prefix = "  ";
            }

            String endSwitch = "";
            String caseStr = "";
            // this also sets up the start state of the nfa
            if (maxLexStates > 1) {
                ostr.WriteLine(prefix + "   switch(curLexState)");
                ostr.WriteLine(prefix + "   {");
                endSwitch = prefix + "   }";
                caseStr = prefix + "     case ";
                prefix += "    ";
            }

            prefix += "   ";
            for (i = 0; i < maxLexStates; i++) {
                if (maxLexStates > 1)
                    ostr.WriteLine(caseStr + i + ":");

                var skipState = singlesToSkip[i] ?? throw new InvalidOperationException("The skip state has not been initialized.");
                if (skipState.HasTransitions()) {
                    // added the backup(0) to make JIT happy
                    ostr.WriteLine(prefix + "try { " + CSharpCCGlobals.CharStreamReference + ".Backup(0);");
                    if (skipState.asciiMoves[0] != 0L &&
                        skipState.asciiMoves[1] != 0L) {
                        ostr.WriteLine(prefix + "   while ((curChar < 64 && ({0}L & (1L << curChar)) != 0L) ||",
                            (skipState.asciiMoves[0]));
                        ostr.WriteLine(prefix + "          (curChar >> 6) == 1 && ({0}L & (1L << (curChar & 63))) != 0L)",
                            (skipState.asciiMoves[1]));
                    } else if (skipState.asciiMoves[1] == 0L) {
                        ostr.WriteLine(prefix + "   while (curChar <= {0} && ({1}L & (1L << curChar)) != 0L)",
                            MaxChar(skipState.asciiMoves[0]),
                            skipState.asciiMoves[0]);
                    } else if (skipState.asciiMoves[0] == 0L) {
                        ostr.WriteLine(prefix + "   while (curChar > 63 && curChar <= {0}  && ({1}L & (1L << (curChar & 63))) != 0L)",
                            (MaxChar(skipState.asciiMoves[1]) + 64),
                            (skipState.asciiMoves[1]));
                    }

                    if (Options.getDebugTokenManager()) {
                        ostr.WriteLine(prefix + "{");
                        ostr.WriteLine("      debugStream.WriteLine(" +
                                       (maxLexStates > 1
                                           ? "\"<\" + lexStateNames[curLexState] + \">\" + "
                                           : "") +
                                       "\"Skipping character : \" + " +
                                       "TokenManagerError.AddEscapes(curChar.ToString()) + \" (\" + (int)curChar + \")\");");
                    }
                    ostr.WriteLine(prefix + "      curChar = " + CSharpCCGlobals.CharStreamReference + ".BeginToken();");

                    if (Options.getDebugTokenManager())
                        ostr.WriteLine(prefix + "}");

                    ostr.WriteLine(prefix + "}");
                    usesEofLabel = true;
                    ostr.WriteLine(prefix + "catch (System.IO.IOException) { goto EOFLoop; }");
                }

                if (initMatch[i] != Int32.MaxValue && initMatch[i] != 0) {
                    if (Options.getDebugTokenManager())
                        ostr.WriteLine("      debugStream.WriteLine(\"   Matched the empty string as \" + TokenImage[" + initMatch[i] +"] + \" token.\");");

                    ostr.WriteLine(prefix + "ccMatchedKind = " + initMatch[i] + ";");
                    ostr.WriteLine(prefix + "ccMatchedPos = -1;");
                    ostr.WriteLine(prefix + "curPos = 0;");
                } else {
                    ostr.WriteLine(prefix + "ccMatchedKind = Int32.MaxValue;");
                    ostr.WriteLine(prefix + "ccMatchedPos = 0;");
                }

                if (Options.getDebugTokenManager())
                    ostr.WriteLine("      debugStream.WriteLine(" +
                                   (maxLexStates > 1 ? "\"<\" + lexStateNames[curLexState] + \">\" + " : "") +
                                   "\"Current character : \" + " +
                                   "TokenManagerError.AddEscapes(curChar.ToString()) + \" (\" + (int)curChar + \") " +
                                   "at line \" + " + CSharpCCGlobals.CharStreamReference + ".EndLine + \" column \" + " + CSharpCCGlobals.CharStreamReference + ".EndColumn);");

                ostr.WriteLine(prefix + "curPos = ccMoveStringLiteralDfa0_" + i + "();");

                if (canMatchAnyChar[i] != -1) {
                    if (initMatch[i] != Int32.MaxValue && initMatch[i] != 0)
                        ostr.WriteLine(prefix + "if (ccMatchedPos < 0 || (ccMatchedPos == 0 && ccMatchedKind > " + canMatchAnyChar[i] + "))");
                    else
                        ostr.WriteLine(prefix + "if (ccMatchedPos == 0 && ccMatchedKind > " + canMatchAnyChar[i] + ")");
                    ostr.WriteLine(prefix + "{");

                    if (Options.getDebugTokenManager())
                        ostr.WriteLine("           debugStream.WriteLine(\"   Current character matched as a \" + TokenImage[" +
                                       canMatchAnyChar[i] + "] + \" token.\");");
                    ostr.WriteLine(prefix + "   ccMatchedKind = " + canMatchAnyChar[i] + ";");

                    if (initMatch[i] != Int32.MaxValue && initMatch[i] != 0)
                        ostr.WriteLine(prefix + "   ccMatchedPos = 0;");

                    ostr.WriteLine(prefix + "}");
                }

                if (maxLexStates > 1)
                    ostr.WriteLine(prefix + "break;");
            }

            if (maxLexStates > 1)
                ostr.WriteLine(endSwitch);
            else if (maxLexStates == 0)
                ostr.WriteLine("       ccMatchedKind = Int32.MaxValue;");

            if (maxLexStates > 1)
                prefix = "  ";
            else
                prefix = "";

            if (maxLexStates > 0) {
                ostr.WriteLine(prefix + "   if (ccMatchedKind != Int32.MaxValue)");
                ostr.WriteLine(prefix + "   {");
                ostr.WriteLine(prefix + "      if (ccMatchedPos + 1 < curPos)");

                if (Options.getDebugTokenManager()) {
                    ostr.WriteLine(prefix + "      {");
                    ostr.WriteLine(prefix + "         debugStream.WriteLine(" +
                                   "\"   Putting back \" + (curPos - ccMatchedPos - 1) + \" characters into the input stream.\");");
                }

                ostr.WriteLine(prefix + "         " + CSharpCCGlobals.CharStreamReference + ".Backup(curPos - ccMatchedPos - 1);");

                if (Options.getDebugTokenManager())
                    ostr.WriteLine(prefix + "      }");

                if (Options.getDebugTokenManager()) {
                    if (Options.getUnicodeEscape() ||
                        Options.getUserCharStream())
                        ostr.WriteLine("    debugStream.WriteLine(" +
                                       "\"****** FOUND A \" + TokenImage[ccMatchedKind] + \" MATCH " +
                                       "(\" + TokenManagerError.AddEscapes(new String(" + CSharpCCGlobals.CharStreamReference + ".GetSuffix(ccMatchedPos + 1))) + " +
                                       "\") ******\\n\");");
                    else
                        ostr.WriteLine("    debugStream.WriteLine(" +
                                       "\"****** FOUND A \" + TokenImage[ccMatchedKind] + \" MATCH " +
                                       "(\" + TokenManagerError.AddEscapes(new String(" + CSharpCCGlobals.CharStreamReference + ".GetSuffix(ccMatchedPos + 1))) + " +
                                       "\") ******\\n\");");
                }

                if (hasSkip || hasMore || hasSpecial) {
                    ostr.WriteLine(prefix + "      if ((ccToToken[ccMatchedKind >> 6] & " + "(1L << (ccMatchedKind & 63))) != 0L)");
                    ostr.WriteLine(prefix + "      {");
                }

                ostr.WriteLine(prefix + "         matchedToken = ccFillToken();");

                if (hasSpecial)
                    ostr.WriteLine(prefix + "         matchedToken.SpecialToken = specialToken;");

                if (hasTokenActions)
                    ostr.WriteLine(prefix + "         TokenLexicalActions(matchedToken);");

                if (maxLexStates > 1) {
                    ostr.WriteLine("       if (ccNewLexState[ccMatchedKind] != -1)");
                    ostr.WriteLine(prefix + "       curLexState = ccNewLexState[ccMatchedKind];");
                }

                if (Options.getCommonTokenAction())
                    ostr.WriteLine(prefix + "         CommonTokenAction(matchedToken);");

                ostr.WriteLine(prefix + "         return matchedToken;");

                if (hasSkip || hasMore || hasSpecial) {
                    ostr.WriteLine(prefix + "      }");

                    if (hasSkip || hasSpecial) {
                        if (hasMore) {
                            ostr.WriteLine(prefix + "      else if ((ccToSkip[ccMatchedKind >> 6] & " +
                                           "(1L << (ccMatchedKind & 63))) != 0L)");
                        } else
                            ostr.WriteLine(prefix + "      else");

                        ostr.WriteLine(prefix + "      {");

                        if (hasSpecial) {
                            ostr.WriteLine(prefix + "         if ((ccToSpecial[ccMatchedKind >> 6] & " +
                                           "(1L << (ccMatchedKind & 63))) != 0L)");
                            ostr.WriteLine(prefix + "         {");

                            ostr.WriteLine(prefix + "            matchedToken = ccFillToken();");

                            ostr.WriteLine(prefix + "            if (specialToken == null)");
                            ostr.WriteLine(prefix + "               specialToken = matchedToken;");
                            ostr.WriteLine(prefix + "            else");
                            ostr.WriteLine(prefix + "            {");
                            ostr.WriteLine(prefix + "               matchedToken.SpecialToken = specialToken;");
                            ostr.WriteLine(prefix + "               specialToken = (specialToken.Next = matchedToken);");
                            ostr.WriteLine(prefix + "            }");

                            if (hasSkipActions)
                                ostr.WriteLine(prefix + "            SkipLexicalActions(matchedToken);");

                            ostr.WriteLine(prefix + "         }");

                            if (hasSkipActions) {
                                ostr.WriteLine(prefix + "         else");
                                ostr.WriteLine(prefix + "            SkipLexicalActions(null);");
                            }
                        } else if (hasSkipActions)
                            ostr.WriteLine(prefix + "         SkipLexicalActions(null);");

                        if (maxLexStates > 1) {
                            ostr.WriteLine("         if (ccNewLexState[ccMatchedKind] != -1)");
                            ostr.WriteLine(prefix + "         curLexState = ccNewLexState[ccMatchedKind];");
                        }

                        usesEofLabel = true;
                        ostr.WriteLine(prefix + "         goto EOFLoop;");
                        ostr.WriteLine(prefix + "      }");
                    }

                    if (hasMore) {
                        if (hasMoreActions)
                            ostr.WriteLine(prefix + "      MoreLexicalActions();");
                        else if (hasSkipActions || hasTokenActions)
                            ostr.WriteLine(prefix + "      ccImageLen += ccMatchedPos + 1;");

                        if (maxLexStates > 1) {
                            ostr.WriteLine("      if (ccNewLexState[ccMatchedKind] != -1)");
                            ostr.WriteLine(prefix + "      curLexState = ccNewLexState[ccMatchedKind];");
                        }
                        ostr.WriteLine(prefix + "      curPos = 0;");
                        ostr.WriteLine(prefix + "      ccMatchedKind = Int32.MaxValue;");

                        ostr.WriteLine(prefix + "      try {");
                        ostr.WriteLine(prefix + "         curChar = " + CSharpCCGlobals.CharStreamReference + ".ReadChar();");

                        if (Options.getDebugTokenManager())
                            ostr.WriteLine("   debugStream.WriteLine(" +
                                           (maxLexStates > 1 ? "\"<\" + lexStateNames[curLexState] + \">\" + " : "") +
                                           "\"Current character : \" + " +
                                           "TokenManagerError.AddEscapes(curChar.ToString()) + \" (\" + (int)curChar + \") " +
                                           "at line \" + " + CSharpCCGlobals.CharStreamReference + ".EndLine + \" column \" + " + CSharpCCGlobals.CharStreamReference + ".EndColumn);");
                        ostr.WriteLine(prefix + "         continue;");
                        ostr.WriteLine(prefix + "      }");
                        ostr.WriteLine(prefix + "      catch (System.IO.IOException) { }");
                    }
                }

                ostr.WriteLine(prefix + "   }");
                ostr.WriteLine(prefix + "   int errorLine = " + CSharpCCGlobals.CharStreamReference + ".EndLine;");
                ostr.WriteLine(prefix + "   int errorColumn = " + CSharpCCGlobals.CharStreamReference + ".EndColumn;");
                ostr.WriteLine(prefix + (Options.ModernCSharp ? "   string errorAfter = \"\";" : "   string errorAfter = null;"));
                ostr.WriteLine(prefix + "   bool EOFSeen = false;");
                ostr.WriteLine(prefix + "   try { " + CSharpCCGlobals.CharStreamReference + ".ReadChar(); " + CSharpCCGlobals.CharStreamReference + ".Backup(1); }");
                ostr.WriteLine(prefix + "   catch (System.IO.IOException) {");
                ostr.WriteLine(prefix + "      EOFSeen = true;");
                ostr.WriteLine(prefix + "      errorAfter = curPos <= 1 ? \"\" : " + CSharpCCGlobals.CharStreamReference + ".GetImage();");
                ostr.WriteLine(prefix + "      if (curChar == '\\n' || curChar == '\\r') {");
                ostr.WriteLine(prefix + "         errorLine++;");
                ostr.WriteLine(prefix + "         errorColumn = 0;");
                ostr.WriteLine(prefix + "      }");
                ostr.WriteLine(prefix + "      else");
                ostr.WriteLine(prefix + "         errorColumn++;");
                ostr.WriteLine(prefix + "   }");
                ostr.WriteLine(prefix + "   if (!EOFSeen) {");
                ostr.WriteLine(prefix + "      " + CSharpCCGlobals.CharStreamReference + ".Backup(1);");
                ostr.WriteLine(prefix + "      errorAfter = curPos <= 1 ? \"\" : " + CSharpCCGlobals.CharStreamReference + ".GetImage();");
                ostr.WriteLine(prefix + "   }");
                ostr.WriteLine(prefix +
                               "   throw new TokenManagerError(EOFSeen, curLexState, errorLine, errorColumn, errorAfter, curChar, TokenManagerError.LEXICAL_ERROR);");
            }

            if (hasMore)
                ostr.WriteLine(prefix + " }");

            if (!Options.ModernCSharp || usesEofLabel)
                ostr.WriteLine("  EOFLoop: ;");

            ostr.WriteLine("  }");
            ostr.WriteLine("}");
            ostr.WriteLine("");
        }

        public static void DumpSkipActions() {
            Action? act;

            ostr.WriteLine(staticString + (Options.ModernCSharp ? "void SkipLexicalActions(Token? matchedToken)" : "void SkipLexicalActions(Token matchedToken)"));
            ostr.WriteLine("{");
            ostr.WriteLine("   switch(ccMatchedKind)");
            ostr.WriteLine("   {");

            for (int i = 0; i < maxOrdinal; i++) {
                if ((toSkip[i/64] & (1L << (i%64))) == 0L)
                    continue;

                for (;;) {
                    if (((act = actions[i]) == null ||
                         act.ActionTokens == null ||
                         act.ActionTokens.Count == 0) && !canLoop[lexStates[i]])
                        goto Outer;

                    ostr.WriteLine("      case " + i + " :");

                    if (initMatch[lexStates[i]] == i && canLoop[lexStates[i]]) {
                        ostr.WriteLine("         if (ccMatchedPos == -1)");
                        ostr.WriteLine("         {");
                        ostr.WriteLine("            if (ccBeenHere[" + lexStates[i] + "] &&");
                        ostr.WriteLine("                ccEmptyLineNo[" + lexStates[i] + "] == " + CSharpCCGlobals.CharStreamReference + ".BeginLine &&");
                        ostr.WriteLine("                ccEmptyColNo[" + lexStates[i] + "] == " + CSharpCCGlobals.CharStreamReference + ".BeginColumn)");
                        ostr.WriteLine("               throw new TokenManagerError(" +
                                       "(\"Error: Bailing out of infinite loop caused by repeated empty string matches " +
                                       "at line \" + " + CSharpCCGlobals.CharStreamReference + ".BeginLine + \", " +
                                       "column \" + " + CSharpCCGlobals.CharStreamReference + ".BeginColumn + \".\"), TokenManagerError.LOOP_DETECTED);");
                        ostr.WriteLine("            ccEmptyLineNo[" + lexStates[i] + "] = inpuStream.BeginLine;");
                        ostr.WriteLine("            ccEmptyColNo[" + lexStates[i] + "] = " + CSharpCCGlobals.CharStreamReference + ".BeginColumn;");
                        ostr.WriteLine("            ccBeenHere[" + lexStates[i] + "] = true;");
                        ostr.WriteLine("         }");
                    }

                    if ((act = actions[i]) == null ||
                        act.ActionTokens.Count == 0)
                        break;

                    ostr.Write("         image.Append");
                    if (RStringLiteral.allImages[i] != null) {
                        ostr.WriteLine("(ccStrLiteralImages[" + i + "]);");
                        ostr.WriteLine("        lengthOfMatch = ccStrLiteralImages[" + i + (Options.ModernCSharp ? "]?.Length ?? throw new InvalidOperationException(\"Missing literal image.\");" : "].Length;"));
                    } else {
                        ostr.WriteLine("(" + CSharpCCGlobals.CharStreamReference + ".GetSuffix(ccImageLen + (lengthOfMatch = ccMatchedPos + 1)));");
                    }

                    CSharpCCGlobals.PrintTokenSetup(act.ActionTokens[0]);
                    CSharpCCGlobals.ccol = 1;

                    for (int j = 0; j < act.ActionTokens.Count; j++)
                        CSharpCCGlobals.PrintToken(act.ActionTokens[j], ostr);
                    ostr.WriteLine("");

                    break;
                }

                ostr.WriteLine("         break;");
			Outer:
				;
            }

            ostr.WriteLine("      default :");
            ostr.WriteLine("         break;");
            ostr.WriteLine("   }");
            ostr.WriteLine("}");
        }

        public static void DumpMoreActions() {
            Action? act;

            ostr.WriteLine(staticString + "void MoreLexicalActions()");
            ostr.WriteLine("{");
            ostr.WriteLine("   ccImageLen += (lengthOfMatch = ccMatchedPos + 1);");
            ostr.WriteLine("   switch(ccMatchedKind)");
            ostr.WriteLine("   {");


            for (int i = 0; i < maxOrdinal; i++) {
                if ((toMore[i/64] & (1L << (i%64))) == 0L)
                    continue;

                for (;;) {
                    if (((act = actions[i]) == null ||
                         act.ActionTokens == null ||
                         act.ActionTokens.Count == 0) && !canLoop[lexStates[i]])
                        goto Outer;

                    ostr.WriteLine("      case " + i + " :");

                    if (initMatch[lexStates[i]] == i && canLoop[lexStates[i]]) {
                        ostr.WriteLine("         if (ccMatchedPos == -1)");
                        ostr.WriteLine("         {");
                        ostr.WriteLine("            if (ccBeenHere[" + lexStates[i] + "] &&");
                        ostr.WriteLine("                ccEmptyLineNo[" + lexStates[i] + "] == " + CSharpCCGlobals.CharStreamReference + ".BeginLine &&");
                        ostr.WriteLine("                ccEmptyColNo[" + lexStates[i] + "] == " + CSharpCCGlobals.CharStreamReference + ".BeginColumn)");
                        ostr.WriteLine("               throw new TokenManagerError(" +
                                       "(\"Error: Bailing out of infinite loop caused by repeated empty string matches " +
                                       "at line \" + " + CSharpCCGlobals.CharStreamReference + ".BeginLine + \", " +
                                       "column \" + " + CSharpCCGlobals.CharStreamReference + ".BeginColumn + \".\"), TokenManagerError.LOOP_DETECTED);");
                        ostr.WriteLine("            ccEmptyLineNo[" + lexStates[i] + "] = " + CSharpCCGlobals.CharStreamReference + ".BeginLine;");
                        ostr.WriteLine("            ccEmptyColNo[" + lexStates[i] + "] = " + CSharpCCGlobals.CharStreamReference + ".BeginColumn;");
                        ostr.WriteLine("            ccBeenHere[" + lexStates[i] + "] = true;");
                        ostr.WriteLine("         }");
                    }

                    if ((act = actions[i]) == null ||
                        act.ActionTokens.Count == 0) {
                        break;
                    }

                    ostr.Write("         image.Append");

                    if (RStringLiteral.allImages[i] != null)
                        ostr.WriteLine("(ccStrLiteralImages[" + i + "]);");
                    else
                        ostr.WriteLine("(" + CSharpCCGlobals.CharStreamReference + ".GetSuffix(ccImageLen));");

                    ostr.WriteLine("         ccImageLen = 0;");
                    CSharpCCGlobals.PrintTokenSetup(act.ActionTokens[0]);
                    CSharpCCGlobals.ccol = 1;

                    for (int j = 0; j < act.ActionTokens.Count; j++)
                        CSharpCCGlobals.PrintToken(act.ActionTokens[j], ostr);
                    ostr.WriteLine("");

                    break;
                }

                ostr.WriteLine("         break;");

			Outer:
				;
            }

            ostr.WriteLine("      default :");
            ostr.WriteLine("         break;");

            ostr.WriteLine("   }");
            ostr.WriteLine("}");
        }

        public static void DumpTokenActions() {
            Action? act;
            int i;

            ostr.WriteLine(staticString + "void TokenLexicalActions(Token matchedToken)");
            ostr.WriteLine("{");
            ostr.WriteLine("   switch(ccMatchedKind)");
            ostr.WriteLine("   {");


            for (i = 0; i < maxOrdinal; i++) {
                if ((toToken[i/64] & (1L << (i%64))) == 0L)
                    continue;

                for (;;) {
                    if (((act = actions[i]) == null ||
                         act.ActionTokens == null ||
                         act.ActionTokens.Count == 0) && !canLoop[lexStates[i]])
                        goto Outer;

                    ostr.WriteLine("      case " + i + " :");

                    if (initMatch[lexStates[i]] == i && canLoop[lexStates[i]]) {
                        ostr.WriteLine("         if (ccMatchedPos == -1)");
                        ostr.WriteLine("         {");
                        ostr.WriteLine("            if (ccBeenHere[" + lexStates[i] + "] &&");
                        ostr.WriteLine("                ccEmptyLineNo[" + lexStates[i] + "] == " + CSharpCCGlobals.CharStreamReference + ".BeginLine &&");
                        ostr.WriteLine("                ccEmptyColNo[" + lexStates[i] + "] == " + CSharpCCGlobals.CharStreamReference + ".BeginColumn)");
                        ostr.WriteLine("               throw new TokenManagerError(" +
                                       "(\"Error: Bailing out of infinite loop caused by repeated empty string matches " +
                                       "at line \" + " + CSharpCCGlobals.CharStreamReference + ".BeginLine + \", " +
                                       "column \" + " + CSharpCCGlobals.CharStreamReference + ".BeginColumn + \".\"), TokenManagerError.LOOP_DETECTED);");
                        ostr.WriteLine("            ccEmptyLineNo[" + lexStates[i] + "] = " + CSharpCCGlobals.CharStreamReference + ".BeginLine;");
                        ostr.WriteLine("            ccEmptyColNo[" + lexStates[i] + "] = " + CSharpCCGlobals.CharStreamReference + ".BeginColumn;");
                        ostr.WriteLine("            ccBeenHere[" + lexStates[i] + "] = true;");
                        ostr.WriteLine("         }");
                    }

                    if ((act = actions[i]) == null ||
                        act.ActionTokens.Count == 0)
                        break;

                    if (i == 0) {
                        ostr.WriteLine("      image.Length = 0;"); // For EOF no image is there
                    } else {
                        ostr.Write("        image.Append");

                        if (RStringLiteral.allImages[i] != null) {
                            ostr.WriteLine("(ccStrLiteralImages[" + i + "]);");
                            ostr.WriteLine("        lengthOfMatch = ccStrLiteralImages[" + i + (Options.ModernCSharp ? "]?.Length ?? throw new InvalidOperationException(\"Missing literal image.\");" : "].Length;"));
                        } else {
                            ostr.WriteLine("(" + CSharpCCGlobals.CharStreamReference + ".GetSuffix(ccImageLen + (lengthOfMatch = ccMatchedPos + 1)));");
                        }
                    }

                    CSharpCCGlobals.PrintTokenSetup(act.ActionTokens[0]);
                    CSharpCCGlobals.ccol = 1;

                    for (int j = 0; j < act.ActionTokens.Count; j++)
                        CSharpCCGlobals.PrintToken(act.ActionTokens[j], ostr);
                    ostr.WriteLine("");

                    break;
                }

                ostr.WriteLine("         break;");

			Outer:
				;

            }

            ostr.WriteLine("      default :");
            ostr.WriteLine("         break;");
            ostr.WriteLine("   }");
            ostr.WriteLine("}");
        }

        public static void reInit() {
            outputWriter = null;
            staticString = "";
            tokMgrClassName = "";
            allTpsForState = new Dictionary<string, IList<TokenProduction>>();
            lexStateIndex = 0;
            kinds = [];
            maxOrdinal = 1;
            lexStateSuffix = "";
            newLexState = [];
            lexStates = [];
            ignoreCase = [];
            actions = [];
            initStates = new Dictionary<string, NfaState>();
            stateSetSize = 0;
            maxLexStates = 0;
            lexStateName = [];
            singlesToSkip = [];
            toSkip = [];
            toSpecial = [];
            toMore = [];
            toToken = [];
            defaultLexState = 0;
            rexprs = [];
            maxLongsReqd = [];
            initMatch = [];
            canMatchAnyChar = [];
            hasEmptyMatch = false;
            canLoop = [];
            stateHasActions = [];
            hasLoop = false;
            canReachOnMore = [];
            hasNfa = [];
            mixed = [];
            initialState = null;
            curKind = 0;
            hasSkipActions = false;
            hasMoreActions = false;
            hasTokenActions = false;
            hasSpecial = false;
            hasSkip = false;
            hasMore = false;
            curRE = null;
        }

    }
}
