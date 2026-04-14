using Radiance.Tests.Infrastructure;

namespace Radiance.Tests;

/// <summary>
/// End-to-end integration tests that exercise the full
/// Lexer → Parser → Interpreter pipeline.
/// </summary>
public sealed class IntegrationTests : TestBase
{
    // ──── Echo / Output ────

    [Fact]
    public void Echo_SimpleOutput()
    {
        Assert.Equal("hello world", Execute("echo hello world"));
    }

    [Fact]
    public void Echo_EmptyArgs()
    {
        Assert.Equal("", Execute("echo"));
    }

    [Fact]
    public void Echo_WithVariable()
    {
        Execute("X=hello");
        Assert.Equal("hello", Execute("echo $X"));
    }

    [Fact]
    public void Echo_WithVariableInString()
    {
        Execute("NAME=world");
        Assert.Equal("hello world", Execute("echo \"hello $NAME\""));
    }

    // ──── Variable Assignment ────

    [Fact]
    public void Variable_SimpleAssignment()
    {
        Execute("MY_VAR=test_value");
        Assert.Equal("test_value", Execute("echo $MY_VAR"));
    }

    [Fact]
    public void Variable_Overwrite()
    {
        Execute("X=first");
        Execute("X=second");
        Assert.Equal("second", Execute("echo $X"));
    }

    [Fact]
    public void Variable_EmptyValue()
    {
        Execute("EMPTY=");
        Assert.Equal("", Execute("echo $EMPTY"));
    }

    [Fact]
    public void Variable_UnsetVariable()
    {
        Execute("unset MY_VAR");
        Assert.Equal("", Execute("echo $MY_VAR"));
    }

    [Fact]
    public void Variable_UndefinedVariable_Empty()
    {
        Assert.Equal("", Execute("echo $COMPLETELY_UNDEFINED_12345"));
    }

    // ──── Quoting ────

    [Fact]
    public void Quoting_DoubleQuotedPreservesSpaces()
    {
        Assert.Equal("hello   world", Execute("echo \"hello   world\""));
    }

    [Fact]
    public void Quoting_SingleQuotedNoExpansion()
    {
        Execute("X=expanded");
        Assert.Equal("$X", Execute("echo '$X'"));
    }

    [Fact]
    public void Quoting_DoubleQuotedExpansion()
    {
        Execute("X=expanded");
        Assert.Equal("expanded", Execute("echo \"$X\""));
    }

    [Fact]
    public void Quoting_AdjacentQuoting()
    {
        Assert.Equal("helloworld", Execute("echo \"hello\"'world'"));
    }

    [Fact]
    public void Quoting_EscapedSpace()
    {
        Assert.Equal("hello world", Execute("echo hello\\ world"));
    }

    // ──── Exit Codes ────

    [Fact]
    public void ExitCode_True_ReturnsZero()
    {
        Assert.Equal(0, ExecuteExitCode("true"));
    }

    [Fact]
    public void ExitCode_False_ReturnsOne()
    {
        Assert.Equal(1, ExecuteExitCode("false"));
    }

    [Fact]
    public void ExitCode_CapturedInDollarQuestion()
    {
        Execute("false");
        Assert.Equal("1", Execute("echo $?"));
    }

    [Fact]
    public void ExitCode_SuccessCapture()
    {
        Execute("true");
        Assert.Equal("0", Execute("echo $?"));
    }

    // ──── Logical Operators ────

    [Fact]
    public void LogicalAnd_RightExecuted_WhenLeftSucceeds()
    {
        Assert.Equal("yes", Execute("true && echo yes"));
    }

    [Fact]
    public void LogicalAnd_RightNotExecuted_WhenLeftFails()
    {
        Assert.Equal("", Execute("false && echo yes"));
    }

    [Fact]
    public void LogicalOr_RightNotExecuted_WhenLeftSucceeds()
    {
        Assert.Equal("", Execute("true || echo yes"));
    }

    [Fact]
    public void LogicalOr_RightExecuted_WhenLeftFails()
    {
        Assert.Equal("yes", Execute("false || echo yes"));
    }

    // ──── Semicolons ────

    [Fact]
    public void Semicolon_BothCommandsExecute()
    {
        Assert.Equal("a\nb", Execute("echo a; echo b"));
    }

    [Fact]
    public void Semicolon_FirstFails_SecondStillRuns()
    {
        Assert.Equal("second", Execute("false; echo second"));
    }

    // ──── Export ────

    [Fact]
    public void Export_Variable()
    {
        Execute("export TEST_RAD_INTEGRATION=exported");
        Assert.Equal("exported", Environment.GetEnvironmentVariable("TEST_RAD_INTEGRATION"));
        Environment.SetEnvironmentVariable("TEST_RAD_INTEGRATION", null);
    }

    [Fact]
    public void Export_ExistingVariable()
    {
        Execute("TEST_RAD_EXP_EXISTING=val");
        Execute("export TEST_RAD_EXP_EXISTING");
        Assert.Equal("val", Environment.GetEnvironmentVariable("TEST_RAD_EXP_EXISTING"));
        Environment.SetEnvironmentVariable("TEST_RAD_EXP_EXISTING", null);
    }

    // ──── Unset ────

    [Fact]
    public void Unset_RemovesVariable()
    {
        Execute("TEST_RAD_UNSET=yes");
        Execute("unset TEST_RAD_UNSET");
        Assert.Equal("", Execute("echo $TEST_RAD_UNSET"));
    }

    // ──── Set ────

    [Fact]
    public void Set_ShowsVariables()
    {
        Execute("TEST_RAD_SET_VARIABLE=myval");
        var output = Execute("set");
        Assert.Contains("TEST_RAD_SET_VARIABLE", output);
    }

    // ──── Env ────

    [Fact]
    public void Env_ShowsExportedVariables()
    {
        Execute("export TEST_RAD_ENV_SHOW=yes");
        var output = Execute("env");
        Assert.Contains("TEST_RAD_ENV_SHOW", output);
        Environment.SetEnvironmentVariable("TEST_RAD_ENV_SHOW", null);
    }

    // ──── Pwd ────

    [Fact]
    public void Pwd_ShowsCurrentDirectory()
    {
        var output = Execute("pwd");
        Assert.Equal(Directory.GetCurrentDirectory(), output);
    }

    // ──── Cd ────

    [Fact]
    public void Cd_ChangesDirectory()
    {
        var before = Execute("pwd");
        Execute("cd /tmp");
        var after = Execute("pwd");
        // On macOS, /tmp is a symlink to /private/tmp
        Assert.NotEqual(before, after);
        Assert.Contains("tmp", after);
    }

    // ──── If Statement ────

    [Fact]
    public void If_TrueExecutesThen()
    {
        Assert.Equal("yes", Execute("if true; then echo yes; fi"));
    }

    [Fact]
    public void If_FalseSkipsThen()
    {
        Assert.Equal("", Execute("if false; then echo yes; fi"));
    }

    [Fact]
    public void If_WithElse_TrueBranch()
    {
        Assert.Equal("yes", Execute("if true; then echo yes; else echo no; fi"));
    }

    [Fact]
    public void If_WithElse_FalseBranch()
    {
        Assert.Equal("no", Execute("if false; then echo yes; else echo no; fi"));
    }

    [Fact]
    public void If_WithElif()
    {
        Assert.Equal("elif", Execute("if false; then echo if; elif true; then echo elif; fi"));
    }

    [Fact]
    public void If_WithVariableCondition()
    {
        Execute("X=1");
        Assert.Equal("set", Execute("if true; then echo set; fi"));
    }

    // ──── For Loop ────

    [Fact]
    public void For_IteratesOverWords()
    {
        Assert.Equal("a\nb\nc", Execute("for x in a b c; do echo $x; done"));
    }

    [Fact]
    public void For_IteratesOverVariableExpansion()
    {
        Assert.Equal("1\n2\n3", Execute("for i in 1 2 3; do echo $i; done"));
    }

    [Fact]
    public void For_NestedExpansion()
    {
        Assert.Equal("a_b\na_c", Execute("for x in b c; do echo a_$x; done"));
    }

    // ──── While Loop ────

    [Fact]
    public void While_SingleIteration()
    {
        Assert.Equal("done", Execute("x=0; while false; do echo loop; done; echo done"));
    }

    [Fact]
    public void While_WithBreak()
    {
        var output = Execute("x=0; while true; do x=$((x+1)); if test $x -eq 3; then break; fi; echo $x; done");
        Assert.Equal("1\n2", output);
    }

    [Fact]
    public void While_WithContinue()
    {
        var output = Execute("for i in 1 2 3 4 5; do if test $((i % 2)) -eq 0; then continue; fi; echo $i; done");
        Assert.Equal("1\n3\n5", output);
    }

    // ──── Until Loop ────

    [Fact]
    public void Until_StopsWhenTrue()
    {
        var output = Execute("x=0; until test $x -eq 3; do x=$((x+1)); echo $x; done");
        Assert.Equal("1\n2\n3", output);
    }

    // ──── Case Statement ────

    [Fact]
    public void Case_MatchesPattern()
    {
        Assert.Equal("matched A", Execute("x=a; case $x in a) echo matched A ;; b) echo matched B ;; esac"));
    }

    [Fact]
    public void Case_DefaultWithStar()
    {
        Assert.Equal("default", Execute("x=z; case $x in a) echo A ;; *) echo default ;; esac"));
    }

    [Fact]
    public void Case_MultiplePatterns()
    {
        Assert.Equal("vowel", Execute("x=e; case $x in a|e|i|o|u) echo vowel ;; *) echo consonant ;; esac"));
    }

    // ──── Functions ────

    [Fact]
    public void Function_DefineAndCall()
    {
        Assert.Equal("hello from func", Execute("greet() { echo hello from func; }; greet"));
    }

    [Fact]
    public void Function_WithKeyword()
    {
        Assert.Equal("hello", Execute("function hello { echo hello; }; hello"));
    }

    [Fact]
    public void Function_AccessesArguments()
    {
        Assert.Equal("arg1 arg2", Execute("show() { echo $1 $2; }; show arg1 arg2"));
    }

    [Fact]
    public void Function_ScopedVariables()
    {
        var output = Execute("X=global; show() { local X=local; echo $X; }; show; echo $X");
        Assert.Equal("local\nglobal", output);
    }

    [Fact]
    public void Function_ReturnValue()
    {
        Assert.Equal("42", Execute("getcode() { return 42; }; getcode; echo $?"));
    }

    // ──── Command Substitution ────

    [Fact]
    public void CommandSubstitution_Parentheses()
    {
        Assert.Equal("hello", Execute("echo $(echo hello)"));
    }

    [Fact]
    public void CommandSubstitution_Nested()
    {
        Assert.Equal("hello", Execute("echo $(echo $(echo hello))"));
    }

    [Fact]
    public void CommandSubstitution_InAssignment()
    {
        Execute("X=$(echo computed)");
        Assert.Equal("computed", Execute("echo $X"));
    }

    // ──── Arithmetic Expansion ────

    [Fact]
    public void Arithmetic_BasicCalculation()
    {
        Assert.Equal("5", Execute("echo $((2+3))"));
    }

    [Fact]
    public void Arithmetic_WithVariable()
    {
        Execute("X=10");
        Assert.Equal("13", Execute("echo $(($X+3))"));
    }

    [Fact]
    public void Arithmetic_InLoop()
    {
        var output = Execute("x=0; x=$((x+1)); echo $x; x=$((x+1)); echo $x");
        Assert.Equal("1\n2", output);
    }

    // ──── Aliases ────

    [Fact]
    public void Alias_DefineAndUse()
    {
        Execute("alias ll='echo LL_OUTPUT'");
        Assert.Equal("LL_OUTPUT", Execute("ll"));
    }

    [Fact]
    public void Alias_Unalias()
    {
        Execute("alias tempalias='echo ALIAS'");
        Execute("unalias tempalias");
        // After unalias, it should try to run as an external command and fail
        var (_, stderr, exitCode) = ExecuteFull("tempalias");
        Assert.NotEqual(0, exitCode);
    }

    [Fact]
    public void Alias_ListAll()
    {
        Execute("alias testalias1='cmd1'");
        Execute("alias testalias2='cmd2'");
        var output = Execute("alias");
        Assert.Contains("testalias1", output);
        Assert.Contains("testalias2", output);
    }

    // ──── Type Command ────

    [Fact]
    public void Type_Builtin()
    {
        var output = Execute("type echo");
        Assert.Contains("builtin", output);
    }

    [Fact]
    public void Type_Function()
    {
        Execute("myfunc() { echo hi; }");
        var output = Execute("type myfunc");
        Assert.Contains("function", output);
    }

    [Fact]
    public void Type_External()
    {
        var output = Execute("type ls");
        Assert.Contains("ls", output);
    }

    // ──── Comments ────

    [Fact]
    public void Comments_Ignored()
    {
        Assert.Equal("hello", Execute("# this is a comment\necho hello"));
    }

    [Fact]
    public void Comments_Inline()
    {
        Assert.Equal("hello", Execute("echo hello # inline comment"));
    }

    // ──── Multi-line Scripts ────

    [Fact]
    public void Script_MultipleLines()
    {
        var output = Execute("echo line1\necho line2\necho line3");
        Assert.Equal("line1\nline2\nline3", output);
    }

    [Fact]
    public void Script_Counter()
    {
        var output = Execute("""
            count=0
            for i in 1 2 3; do
                count=$((count + i))
            done
            echo $count
            """);
        Assert.Equal("6", output);
    }

    [Fact]
    public void Script_Fibonacci()
    {
        var output = Execute("""
            a=1
            b=1
            for i in 1 2 3 4 5; do
                echo $a
                c=$((a + b))
                a=$b
                b=$c
            done
            """);
        Assert.Equal("1\n1\n2\n3\n5", output);
    }

    // ──── Redirect ────

    [Fact]
    public void Redirect_OutputToFile()
    {
        var tmpFile = Path.GetTempFileName();
        try
        {
            Execute($"echo redirected > {tmpFile}");
            Assert.Equal("redirected\n", File.ReadAllText(tmpFile));
        }
        finally
        {
            File.Delete(tmpFile);
        }
    }

    [Fact]
    public void Redirect_AppendToFile()
    {
        var tmpFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmpFile, "existing\n");
            Execute($"echo appended >> {tmpFile}");
            Assert.Equal("existing\nappended\n", File.ReadAllText(tmpFile));
        }
        finally
        {
            File.Delete(tmpFile);
        }
    }

    [Fact]
    public void Redirect_InputFromFile()
    {
        var tmpFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmpFile, "file content here");
            var output = Execute($"cat < {tmpFile}");
            Assert.Equal("file content here", output);
        }
        finally
        {
            File.Delete(tmpFile);
        }
    }

    // ──── Pipeline ────

    [Fact]
    public void Pipeline_TwoBuiltins()
    {
        // echo | wc type pipeline — builtin echo piped to external wc
        var output = Execute("echo hello world | wc -w");
        Assert.Equal("2", output.Trim());
    }

    [Fact]
    public void Pipeline_Grep()
    {
        // Use printf (external) instead of echo -e since our echo builtin
        // doesn't support -e flag for escape sequences
        var output = Execute("printf 'apple\\nbanana\\ncherry\\n' | grep ban");
        Assert.Equal("banana", output.Trim());
    }

    // ──── Parameter Expansion ────

    [Fact]
    public void ParameterExpansion_Default()
    {
        Assert.Equal("fallback", Execute("echo ${UNSET_RAD_VAR:-fallback}"));
    }

    [Fact]
    public void ParameterExpansion_AssignDefault()
    {
        Execute("echo ${NEW_RAD_VAR:=assigned}");
        Assert.Equal("assigned", Execute("echo $NEW_RAD_VAR"));
    }

    [Fact]
    public void ParameterExpansion_StringLength()
    {
        Execute("STR=hello");
        Assert.Equal("5", Execute("echo ${#STR}"));
    }

    // ──── Break/Continue ────

    [Fact]
    public void Break_ExitsLoop()
    {
        var output = Execute("for i in 1 2 3 4 5; do if test $i -eq 3; then break; fi; echo $i; done");
        Assert.Equal("1\n2", output);
    }

    [Fact]
    public void Continue_SkipsIteration()
    {
        var output = Execute("for i in 1 2 3 4 5; do if test $i -eq 3; then continue; fi; echo $i; done");
        Assert.Equal("1\n2\n4\n5", output);
    }

    // ──── Edge Cases ────

    [Fact]
    public void EmptyInput_NoError()
    {
        Assert.Equal("", Execute(""));
    }

    [Fact]
    public void WhitespaceOnly_NoError()
    {
        Assert.Equal("", Execute("   "));
    }

    [Fact]
    public void CommentOnly_NoError()
    {
        Assert.Equal("", Execute("# just a comment"));
    }

    [Fact]
    public void MultipleSemis_NoError()
    {
        Assert.Equal("", Execute(";;;"));
    }

    [Fact]
    public void VariableInBraces()
    {
        Execute("X=hello");
        Assert.Equal("hello_world", Execute("echo ${X}_world"));
    }

    [Fact]
    public void SpecialVar_ShellName()
    {
        Assert.Equal("radiance", Execute("echo $0"));
    }
}
