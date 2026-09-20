using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace Deveel.CSharpCC.Parser {
	public static class Options {
		private static IDictionary<string, object> optionValues = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

		private static int IntValue(String option) {
			object value;
			return !optionValues.TryGetValue(option, out value) ? 0 : (int) value;
		}

		private static bool BooleanValue(string option) {
			object value;
			return optionValues.TryGetValue(option, out value) && (bool)value;
		}

		private static String StringValue(String option) {
			object value;
			return !optionValues.TryGetValue(option, out value) ? null : (string)value;
		}

        public static IDictionary<string, object> getOptions() {
            return new Dictionary<string, object>(optionValues, StringComparer.OrdinalIgnoreCase);
        }

        private static HashSet<string> cmdLineSetting;
        private static HashSet<string> inputFileSetting;
		
        public static void init() {
            optionValues = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            cmdLineSetting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            inputFileSetting = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            optionValues.Add("LOOKAHEAD", 1);
            optionValues.Add("CHOICE_AMBIGUITY_CHECK", 2);
            optionValues.Add("OTHER_AMBIGUITY_CHECK", 1);

            optionValues.Add("STATIC", true);
            optionValues.Add("DEBUG_PARSER", false);
            optionValues.Add("DEBUG_LOOKAHEAD", false);
            optionValues.Add("DEBUG_TOKEN_MANAGER", false);
            optionValues.Add("ERROR_REPORTING", true);
            optionValues.Add("UNICODE_ESCAPE", false);
            optionValues.Add("UNICODE_INPUT", false);
            optionValues.Add("IGNORE_CASE", false);
            optionValues.Add("USER_TOKEN_MANAGER", false);
            optionValues.Add("USER_CHAR_STREAM", false);
            optionValues.Add("BUILD_PARSER", true);
            optionValues.Add("BUILD_TOKEN_MANAGER", true);
            optionValues.Add("TOKEN_MANAGER_USES_PARSER", false);
            optionValues.Add("SANITY_CHECK", true);
            optionValues.Add("FORCE_LA_CHECK", false);
            optionValues.Add("COMMON_TOKEN_ACTION", false);
            optionValues.Add("CACHE_TOKENS", false);
            optionValues.Add("KEEP_LINE_COLUMN", true);

            optionValues.Add("GENERATE_CHAINED_EXCEPTION", false);
            optionValues.Add("GENERATE_GENERICS", false);
            optionValues.Add("GENERATE_STRING_BUILDER", false);
            optionValues.Add("GENERATE_ANNOTATIONS", false);
            optionValues.Add("SUPPORT_CLASS_VISIBILITY_PUBLIC", true);

            optionValues.Add("OUTPUT_DIRECTORY", ".");
			optionValues.Add("CLR_VERSION", "2.0");
            optionValues.Add("TOKEN_EXTENDS", "");
            optionValues.Add("TOKEN_FACTORY", "");
            optionValues.Add("GRAMMAR_ENCODING", "");
        }
		
        public static String GetOptionsString(String[] interestingOptions) {
            StringBuilder sb = new StringBuilder();

            for (int i = 0; i < interestingOptions.Length; i++) {
                String key = interestingOptions[i];
                sb.Append(key);
                sb.Append('=');
                sb.Append(optionValues[key]);
                if (i != interestingOptions.Length - 1) {
                    sb.Append(',');
                }
            }

            return sb.ToString();
        }

		
        public static bool IsOption(String opt) {
            return opt != null && opt.Length > 1 && opt[0] == '-';
        }

        public static Object UpgradeValue(String name, Object value) {
            if (name.Equals("NODE_FACTORY", StringComparison.OrdinalIgnoreCase) && value is bool) {
                if ((bool) value) {
                    value = "*";
                } else {
                    value = "";
                }
            }

            return value;
        }

        public static void SetInputFileOption(object nameloc, object valueloc, string name, object value) {
            if (!optionValues.TryGetValue(name, out var existingValue)) {
                CSharpCCErrors.Warning(nameloc, "Bad option name \"" + name + "\".  Option setting will be ignored.");
                return;
            }

            value = UpgradeValue(name, value);

            if (!IsValidValue(name, value, existingValue)) {
                CSharpCCErrors.Warning(valueloc, "Bad option value \"" + value + "\" for \"" + name + "\".  Option setting will be ignored.");
                return;
            }

            if (inputFileSetting.Contains(name)) {
                CSharpCCErrors.Warning(nameloc, "Duplicate option setting for \"" + name + "\" will be ignored.");
                return;
            }

            if (cmdLineSetting.Contains(name)) {
                if (!existingValue.Equals(value)) {
                    CSharpCCErrors.Warning(nameloc, "Command line setting of \"" + name + "\" modifies option value in file.");
                }
                return;
            }

            optionValues[name] = value;
            inputFileSetting.Add(name);
        }

        public static void SetCmdLineOption(String arg) {
            if (string.IsNullOrEmpty(arg)) {
                Console.Out.WriteLine("Warning: Bad option \"" + arg + "\" will be ignored.");
                return;
            }

            string s = arg[0] == '-' ? arg[1..] : arg;

            String name;
            Object Val;

            // Look for the first ":" or "=", which will separate the option name
            // from its value (if any).
            int index1 = s.IndexOf('=');
            int index2 = s.IndexOf(':');
            int index;

            if (index1 < 0)
                index = index2;
            else if (index2 < 0)
                index = index1;
            else if (index1 < index2)
                index = index1;
            else
                index = index2;

            if (index < 0) {
                name = s;
                if (optionValues.ContainsKey(name)) {
                    Val = true;
                } else if (name.Length > 2 && name.StartsWith("NO", StringComparison.OrdinalIgnoreCase)) {
                    Val = false;
                    name = name[2..];
                } else {
                    Console.Out.WriteLine("Warning: Bad option \"" + arg
                                          + "\" will be ignored.");
                    return;
                }
            } else {
                name = s[..index];
                string valueText = s[(index + 1)..];
                if (valueText.Equals("TRUE", StringComparison.OrdinalIgnoreCase)) {
                    Val = true;
                } else if (valueText.Equals("FALSE", StringComparison.OrdinalIgnoreCase)) {
                    Val = false;
                } else if (int.TryParse(valueText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int number)) {
                    if (number <= 0) {
                        Console.Out.WriteLine("Warning: Bad option value in \"" + arg + "\" will be ignored.");
                        return;
                    }
                    Val = number;
                } else {
                    Val = valueText.Length >= 2 && valueText[0] == '"' && valueText[^1] == '"'
                        ? valueText[1..^1]
                        : valueText;
                }
            }

            if (!optionValues.TryGetValue(name, out var valOrig)) {
                Console.Out.WriteLine("Warning: Bad option \"" + arg
                                      + "\" will be ignored.");
                return;
            }

            if (!IsValidValue(name, Val, valOrig)) {
                Console.Out.WriteLine("Warning: Bad option value in \"" + arg
                                      + "\" will be ignored.");
                return;
            }
            if (cmdLineSetting.Contains(name)) {
                Console.Out.WriteLine("Warning: Duplicate option setting \"" + arg
                                      + "\" will be ignored.");
                return;
            }

            Val = UpgradeValue(name, Val);

            optionValues[name] = Val;
            cmdLineSetting.Add(name);
        }

        private static bool IsValidValue(string name, object value, object existingValue) {
            if (value == null || value.GetType() != existingValue.GetType() || value is int and <= 0)
                return false;

            return !name.Equals("CLR_VERSION", StringComparison.OrdinalIgnoreCase) ||
                TryParseClrVersion((string)value, out _);
        }

        private static bool TryParseClrVersion(string value, out double version) =>
            double.TryParse(value, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out version) &&
            double.IsFinite(version);

        public static void Normalize() {
            if (getDebugLookahead() && !getDebugParser()) {
                if (cmdLineSetting.Contains("DEBUG_PARSER")
                    || inputFileSetting.Contains("DEBUG_PARSER")) {
                    CSharpCCErrors.Warning("True setting of option DEBUG_LOOKAHEAD overrides " +
                                           "false setting of option DEBUG_PARSER.");
                }
                optionValues["DEBUG_PARSER"] = true;
            }

            // Now set the "GENERATE" options from the supplied (or default) JDK version.

            optionValues["GENERATE_CHAINED_EXCEPTION"] = clrVersionAtLeast(1.1);
            optionValues["GENERATE_GENERICS"] = clrVersionAtLeast(2.0);
            optionValues["GENERATE_STRING_BUILDER"] = clrVersionAtLeast(1.1);
        }

        public static int getLookahead() {
            return IntValue("LOOKAHEAD");
        }

        public static int getChoiceAmbiguityCheck() {
            return IntValue("CHOICE_AMBIGUITY_CHECK");
        }

        public static int getOtherAmbiguityCheck() {
            return IntValue("OTHER_AMBIGUITY_CHECK");
        }

        public static bool getStatic() {
            return BooleanValue("STATIC");
        }

        public static bool getDebugParser() {
            return BooleanValue("DEBUG_PARSER");
        }

        public static bool getDebugLookahead() {
            return BooleanValue("DEBUG_LOOKAHEAD");
        }

        public static bool getDebugTokenManager() {
            return BooleanValue("DEBUG_TOKEN_MANAGER");
        }

        public static bool getErrorReporting() {
            return BooleanValue("ERROR_REPORTING");
        }

        public static bool getUnicodeEscape() {
            return BooleanValue("UNICODE_ESCAPE");
        }

        public static bool getUnicodeInput() {
            return BooleanValue("UNICODE_INPUT");
        }

        public static bool getIgnoreCase() {
            return BooleanValue("IGNORE_CASE");
        }

        public static bool getUserTokenManager() {
            return BooleanValue("USER_TOKEN_MANAGER");
        }

        public static bool getUserCharStream() {
            return BooleanValue("USER_CHAR_STREAM");
        }

        public static bool getBuildParser() {
            return BooleanValue("BUILD_PARSER");
        }

        public static bool getBuildTokenManager() {
            return BooleanValue("BUILD_TOKEN_MANAGER");
        }

        /**
   * Find the token manager uses parser value.
   *
   * @return The requested token manager uses parser value;
   */

        public static bool getTokenManagerUsesParser() {
            return BooleanValue("TOKEN_MANAGER_USES_PARSER");
        }

        /**
   * Find the sanity check value.
   *
   * @return The requested sanity check value.
   */

        public static bool getSanityCheck() {
            return BooleanValue("SANITY_CHECK");
        }

        /**
   * Find the force lookahead check value.
   *
   * @return The requested force lookahead value.
   */

        public static bool getForceLaCheck() {
            return BooleanValue("FORCE_LA_CHECK");
        }

        /**
   * Find the common token action value.
   *
   * @return The requested common token action value.
   */

        public static bool getCommonTokenAction() {
            return BooleanValue("COMMON_TOKEN_ACTION");
        }

        /**
   * Find the cache tokens value.
   *
   * @return The requested cache tokens value.
   */

        public static bool getCacheTokens() {
            return BooleanValue("CACHE_TOKENS");
        }

        /**
   * Find the keep line column value.
   *
   * @return The requested keep line column value.
   */

        public static bool getKeepLineColumn() {
            return BooleanValue("KEEP_LINE_COLUMN");
        }

        /**
   * Find the JDK version.
   *
   * @return The requested jdk version.
   */

        public static String getClrVersion() {
            return StringValue("CLR_VERSION");
        }

        /**
   * Should the generated code create Exceptions using a constructor taking a nested exception?
   * @return
   */

        public static bool getGenerateChainedException() {
            return BooleanValue("GENERATE_CHAINED_EXCEPTION");
        }

        /**
   * Should the generated code contain Generics?
   * @return
   */

        public static bool getGenerateGenerics() {
            return BooleanValue("GENERATE_GENERICS");
        }

        /**
   * Should the generated code use StringBuilder rather than StringBuilder?
   * @return
   */

        public static bool getGenerateStringBuilder() {
            return BooleanValue("GENERATE_STRING_BUILDER");
        }

        /**
   * Should the generated code contain Annotations?
   * @return
   */

        public static bool getGenerateAnnotations() {
            return BooleanValue("GENERATE_ANNOTATIONS");
        }

        /**
   * Should the generated code class visibility public?
   * @return
   */

        public static bool getSupportClassVisibilityPublic() {
            return BooleanValue("SUPPORT_CLASS_VISIBILITY_PUBLIC");
        }

        /**
   * Determine if the output language is at least the specified
   * version.
   * @param version the version to check against. E.g. <code>1.5</code>
   * @return true if the output version is at least the specified version.
   */

        public static bool clrVersionAtLeast(double version) {
            return TryParseClrVersion(getClrVersion(), out double clrVersion) && clrVersion >= version;
        }

        /**
   * Return the Token's superclass.
   *
   * @return The required base class for Token.
   */

        public static String getTokenExtends() {
            return StringValue("TOKEN_EXTENDS");
        }

        /**
   * Return the Token's factory class.
   *
   * @return The required factory class for Token.
   */

        public static String getTokenFactory() {
            return StringValue("TOKEN_FACTORY");
        }

        /**
   * Return the file encoding; this will return the file.encoding system property if no value was explicitly set
   *
   * @return The file encoding (e.g., UTF-8, ISO_8859-1, MacRoman)
   */

        public static String getGrammarEncoding() {
            if (StringValue("GRAMMAR_ENCODING").Equals("")) {
                return Encoding.Default.BodyName;
            } else {
                return StringValue("GRAMMAR_ENCODING");
            }
        }

        /**
   * Find the output directory.
   *
   * @return The requested output directory.
   */

        public static DirectoryInfo getOutputDirectory() {
            return new DirectoryInfo(StringValue("OUTPUT_DIRECTORY"));
        }

        public static String stringBufOrBuild() {
            if (getGenerateStringBuilder()) {
                return "System.Text.StringBuilder";
            } else {
                return "System.Text.StringBuilder";
            }
        }

    }
}