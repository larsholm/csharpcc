using System;
using System.Globalization;
using System.IO;
using Deveel.CSharpCC.Parser;
using NUnit.Framework;

namespace Deveel.CSharpCC.NUnit;

[TestFixture]
[NonParallelizable]
public class OptionsTest {
    private TextWriter originalOutput;
    private TextWriter originalError;
    private StringWriter output;
    private StringWriter error;
    private CultureInfo originalCulture;

    [SetUp]
    public void SetUp() {
        originalCulture = CultureInfo.CurrentCulture;
        originalOutput = Console.Out;
        originalError = Console.Error;
        output = new StringWriter();
        error = new StringWriter();
        Console.SetOut(output);
        Console.SetError(error);
        Options.init();
        CSharpCCErrors.ReInit();
    }

    [TearDown]
    public void TearDown() {
        CultureInfo.CurrentCulture = originalCulture;
        Console.SetOut(originalOutput);
        Console.SetError(originalError);
        output.Dispose();
        error.Dispose();
        Options.init();
        CSharpCCErrors.ReInit();
    }

    [TestCase("en-US")]
    [TestCase("tr-TR")]
    public void KeysAndDuplicatesAreCaseInsensitiveAcrossCultures(string culture) {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(culture);
        Options.SetCmdLineOption("-static=false");
        Options.SetCmdLineOption("STATIC=true");
        Options.SetInputFileOption(null, null, "ignore_case", true);
        Options.SetInputFileOption(null, null, "IGNORE_CASE", false);
        Assert.That(Options.getStatic(), Is.False);
        Assert.That(Options.getIgnoreCase(), Is.True);
        Assert.That(output.ToString(), Is.EqualTo("Warning: Duplicate option setting \"STATIC=true\" will be ignored." + Environment.NewLine));
        Assert.That(error.ToString(), Is.EqualTo("Warning: Duplicate option setting for \"IGNORE_CASE\" will be ignored." + Environment.NewLine));
        Assert.That(CSharpCCErrors.WarningCount, Is.EqualTo(1));
        var snapshot = Options.getOptions();
        Assert.That(snapshot["static"], Is.False);
        snapshot["STATIC"] = true;
        Assert.That(Options.getStatic(), Is.False, "The options snapshot must remain a copy.");
        Assert.That(Options.GetOptionsString(["static", "ignore_case"]), Is.EqualTo("static=False,ignore_case=True"));
    }

    [TestCase("-nostatic", false)]
    [TestCase("STATIC:FaLsE", false)]
    [TestCase("-static=TRUE", true)]
    [TestCase("static", true)]
    public void BooleanFormsWorkInTurkishCulture(string argument, bool expected) {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
        Options.SetCmdLineOption(argument);
        Assert.That(Options.getStatic(), Is.EqualTo(expected));
        Assert.That(output.ToString(), Is.Empty);
    }

    [TestCase("OUTPUT_DIRECTORY=\"Mixed Case/with spaces\"", "Mixed Case/with spaces")]
    [TestCase("OUTPUT_DIRECTORY:\"C:\\Mixed Case=a\\out\"", "C:\\Mixed Case=a\\out")]
    [TestCase("OUTPUT_DIRECTORY=MiXeD/İstanbul", "MiXeD/İstanbul")]
    [TestCase("OUTPUT_DIRECTORY=\"\"", "")]
    [TestCase("OUTPUT_DIRECTORY=\"123\"", "123")]
    [TestCase("OUTPUT_DIRECTORY=\"true\"", "true")]
    [TestCase("OUTPUT_DIRECTORY=", "")]
    public void StringValuesPreserveTheirContent(string argument, string expected) {
        Options.SetCmdLineOption(argument);
        Assert.That(Options.getOptions()["OUTPUT_DIRECTORY"], Is.EqualTo(expected));
        Assert.That(output.ToString(), Is.Empty);
    }

    [TestCase("LOOKAHEAD=2147483648")]
    [TestCase("LOOKAHEAD=-2147483649")]
    [TestCase("LOOKAHEAD=99999999999999999999999999999999")]
    [TestCase("LOOKAHEAD=0")]
    [TestCase("LOOKAHEAD=-1")]
    [TestCase("LOOKAHEAD=1.5")]
    [TestCase("LOOKAHEAD=garbage")]
    [TestCase("LOOKAHEAD=\"2\"")]
    [TestCase("LOOKAHEAD=true")]
    [TestCase("LOOKAHEAD=")]
    public void InvalidIntegerValuesWarnAndAllowALaterValidSetting(string argument) {
        Options.SetCmdLineOption(argument);
        Assert.That(Options.getLookahead(), Is.EqualTo(1));
        Assert.That(output.ToString(), Is.EqualTo($"Warning: Bad option value in \"{argument}\" will be ignored.{Environment.NewLine}"));
        Options.SetCmdLineOption("lookahead=3");
        Assert.That(Options.getLookahead(), Is.EqualTo(3));
        Assert.That(CSharpCCErrors.WarningCount, Is.Zero, "CLI warnings historically do not increment the grammar warning counter.");
    }

    [TestCase("LOOKAHEAD=2147483647", int.MaxValue)]
    [TestCase("LOOKAHEAD= +2 ", 2)]
    public void IntegerLimitsAndInvariantFormattingAreAccepted(string argument, int expected) {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
        Options.SetCmdLineOption(argument);
        Assert.That(Options.getLookahead(), Is.EqualTo(expected));
        Assert.That(output.ToString(), Is.Empty);
    }

    [TestCase(true)]
    [TestCase(false)]
    public void CommandLineWinsRegardlessOfAssignmentOrder(bool commandLineFirst) {
        if (commandLineFirst) Options.SetCmdLineOption("static=false");
        Options.SetInputFileOption(null, null, "STATIC", true);
        if (!commandLineFirst) Options.SetCmdLineOption("static=false");
        Assert.That(Options.getStatic(), Is.False);
        Assert.That(output.ToString(), Is.Empty);
        Assert.That(error.ToString(), Is.EqualTo(commandLineFirst
            ? "Warning: Command line setting of \"STATIC\" modifies option value in file." + Environment.NewLine : ""));
    }

    [Test]
    public void MatchingCommandLineAndGrammarValuesDoNotWarn() {
        Options.SetCmdLineOption("static=false");
        Options.SetInputFileOption(null, null, "STATIC", false);
        Assert.That(Options.getStatic(), Is.False);
        Assert.That(error.ToString(), Is.Empty);
    }

    [TestCase(0)]
    [TestCase(-1)]
    [TestCase("2")]
    [TestCase(null)]
    public void InvalidGrammarValuesWarnWithLocationAndDoNotReserveTheOption(object value) {
        var location = new Token { beginLine = 4, beginColumn = 7 };
        Options.SetInputFileOption(null, location, "LOOKAHEAD", value);
        Assert.That(Options.getLookahead(), Is.EqualTo(1));
        Assert.That(error.ToString(), Is.EqualTo($"Warning: Line 4, Column 7: Bad option value \"{value}\" for \"LOOKAHEAD\".  Option setting will be ignored.{Environment.NewLine}"));
        Options.SetInputFileOption(null, null, "lookahead", 2);
        Assert.That(Options.getLookahead(), Is.EqualTo(2));
    }

    [TestCase("")]
    [TestCase(null)]
    [TestCase("-")]
    [TestCase("UNKNOWN=true")]
    [TestCase("NOUNKNOWN")]
    public void BadCommandLineOptionsWarn(string argument) {
        Options.SetCmdLineOption(argument);
        Assert.That(output.ToString(), Is.EqualTo($"Warning: Bad option \"{argument}\" will be ignored.{Environment.NewLine}"));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void LookaheadDebuggingEnablesParserDebuggingAndNormalizationIsIdempotent(bool explicitFalse) {
        Options.SetCmdLineOption("debug_lookahead=true");
        if (explicitFalse) Options.SetInputFileOption(null, null, "debug_parser", false);
        Options.Normalize();
        Options.Normalize();
        Assert.That(Options.getDebugParser(), Is.True);
        Assert.That(CSharpCCErrors.WarningCount, Is.EqualTo(explicitFalse ? 1 : 0));
        Assert.That(error.ToString(), Is.EqualTo(explicitFalse
            ? "Warning: True setting of option DEBUG_LOOKAHEAD overrides false setting of option DEBUG_PARSER." + Environment.NewLine : ""));
    }

    [TestCase("not-a-version")]
    [TestCase("")]
    [TestCase("NaN")]
    [TestCase("Infinity")]
    [TestCase("1e9999")]
    public void InvalidClrVersionsWarnInsteadOfFailingDuringGeneration(string value) {
        string argument = $"CLR_VERSION=\"{value}\"";
        Options.SetCmdLineOption(argument);
        Assert.That(Options.getClrVersion(), Is.EqualTo("2.0"));
        Assert.That(output.ToString(), Is.EqualTo($"Warning: Bad option value in \"{argument}\" will be ignored.{Environment.NewLine}"));
        Options.SetInputFileOption(null, null, "clr_version", value);
        Assert.That(Options.getClrVersion(), Is.EqualTo("2.0"));
        Assert.That(CSharpCCErrors.WarningCount, Is.EqualTo(1));
        Options.SetCmdLineOption("clr_version=1.0");
        Options.Normalize();
        Assert.That(Options.getGenerateGenerics(), Is.False);
        Assert.That(Options.getGenerateChainedException(), Is.False);
    }

    [TestCase("1.0", false, false)]
    [TestCase("1.1", false, true)]
    [TestCase("2.0", true, true)]
    [TestCase("10.0", true, true)]
    public void LegacyVersionThresholdsUseInvariantCulture(string version, bool generics, bool chainedException) {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("da-DK");
        Options.SetInputFileOption(null, null, "clr_version", version);
        Options.Normalize();
        Assert.That(Options.getGenerateGenerics(), Is.EqualTo(generics));
        Assert.That(Options.getGenerateChainedException(), Is.EqualTo(chainedException));
        Assert.That(Options.getGenerateStringBuilder(), Is.EqualTo(chainedException));
        Assert.That(error.ToString(), Is.Empty);
    }

    [Test]
    public void GrammarOptionsUseTheSameValidationAndPrecedence() {
        CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("tr-TR");
        Options.SetCmdLineOption("static=false");
        using var reader = new StringReader("""
            options {
                static = true;
                ignore_case = true;
                LOOKAHEAD = 2;
                lookahead = 3;
                TOKEN_FACTORY = "MiXeD.Factory";
            }
            """);
        var parser = new CSharpCCParser(reader);
        parser.csharpcc_options();
        Assert.That(Options.getStatic(), Is.False);
        Assert.That(Options.getIgnoreCase(), Is.True);
        Assert.That(Options.getLookahead(), Is.EqualTo(2));
        Assert.That(Options.getTokenFactory(), Is.EqualTo("MiXeD.Factory"));
        Assert.That(CSharpCCErrors.WarningCount, Is.EqualTo(2));
        Assert.That(error.ToString(), Does.Contain("Command line setting of \"static\" modifies option value in file."));
        Assert.That(error.ToString(), Does.Contain("Duplicate option setting for \"lookahead\" will be ignored."));
    }

    [Test]
    public void UnknownGrammarOptionsUseTheNameLocation() {
        var location = new Token { beginLine = 2, beginColumn = 5 };
        Options.SetInputFileOption(location, null, "UNKNOWN", true);
        Assert.That(error.ToString(), Is.EqualTo("Warning: Line 2, Column 5: Bad option name \"UNKNOWN\".  Option setting will be ignored." + Environment.NewLine));
        Assert.That(CSharpCCErrors.WarningCount, Is.EqualTo(1));
    }

    [Test]
    public void InitClearsAssignmentsAndDuplicateTracking() {
        Options.SetCmdLineOption("STATIC=false");
        Options.SetInputFileOption(null, null, "LOOKAHEAD", 2);
        Options.init();
        Assert.That(Options.getStatic(), Is.True);
        Assert.That(Options.getLookahead(), Is.EqualTo(1));
        Options.SetCmdLineOption("static=false");
        Options.SetInputFileOption(null, null, "lookahead", 3);
        Assert.That(Options.getStatic(), Is.False);
        Assert.That(Options.getLookahead(), Is.EqualTo(3));
        Assert.That(output.ToString(), Is.Empty);
        Assert.That(error.ToString(), Is.Empty);
    }
}
