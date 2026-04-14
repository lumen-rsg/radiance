using Radiance.Builtins;
using Radiance.Interpreter;
using Radiance.Lexer;
using Radiance.Parser;

namespace Radiance.Tests.Infrastructure;

/// <summary>
/// Test harness that provides methods to execute shell commands through the
/// full Lexer → Parser → Interpreter pipeline and capture output.
/// </summary>
public sealed class ShellTestHarness : IDisposable
{
    public ShellContext Context { get; }
    public BuiltinRegistry Builtins { get; }
    public ProcessManager ProcessManager { get; }
    public ShellInterpreter Interpreter { get; }

    public ShellTestHarness()
    {
        Context = new ShellContext();
        Builtins = BuiltinRegistry.CreateDefault();
        ProcessManager = new ProcessManager();
        Interpreter = new ShellInterpreter(Context, Builtins, ProcessManager);
    }

    /// <summary>
    /// Executes a command string through the full pipeline and captures stdout.
    /// </summary>
    /// <param name="input">The shell command(s) to execute.</param>
    /// <returns>The captured stdout output and exit code.</returns>
    public (string output, int exitCode) Execute(string input)
    {
        var sw = new StringWriter();
        var originalOut = Console.Out;
        var originalError = Console.Error;
        try
        {
            Console.SetOut(sw);
            // Keep stderr going to a separate capture so it doesn't mix with stdout
            var errorSw = new StringWriter();
            Console.SetError(errorSw);

            var exitCode = ExecuteInternal(input);
            return (sw.ToString(), exitCode);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    /// <summary>
    /// Executes a command string through the full pipeline and captures both stdout and stderr.
    /// </summary>
    public (string stdout, string stderr, int exitCode) ExecuteWithStderr(string input)
    {
        var outSw = new StringWriter();
        var errSw = new StringWriter();
        var originalOut = Console.Out;
        var originalError = Console.Error;
        try
        {
            Console.SetOut(outSw);
            Console.SetError(errSw);

            var exitCode = ExecuteInternal(input);
            return (outSw.ToString(), errSw.ToString(), exitCode);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    /// <summary>
    /// Executes a command and returns only the exit code.
    /// </summary>
    public int ExecuteReturnCode(string input)
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        try
        {
            Console.SetOut(TextWriter.Null);
            Console.SetError(TextWriter.Null);

            return ExecuteInternal(input);
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
    }

    /// <summary>
    /// Executes a multi-line script (separated by newlines).
    /// </summary>
    public (string output, int exitCode) ExecuteScript(string script)
    {
        return Execute(script);
    }

    /// <summary>
    /// Sets a variable in the context.
    /// </summary>
    public void SetVar(string name, string value)
    {
        Context.SetVariable(name, value);
    }

    /// <summary>
    /// Gets a variable from the context.
    /// </summary>
    public string GetVar(string name)
    {
        return Context.GetVariable(name);
    }

    private int ExecuteInternal(string input)
    {
        var lexer = new Radiance.Lexer.Lexer(input);
        var tokens = lexer.Tokenize();
        var parser = new Radiance.Parser.Parser(tokens);
        var ast = parser.Parse();

        if (ast is null)
            return 0;

        var exitCode = Interpreter.Execute(ast);
        Context.LastExitCode = exitCode;
        return exitCode;
    }

    public void Dispose()
    {
    }
}

/// <summary>
/// Base class for test classes that need the shell test harness.
/// </summary>
public abstract class TestBase : IDisposable
{
    protected ShellTestHarness Harness { get; }

    protected TestBase()
    {
        Harness = new ShellTestHarness();
    }

    /// <summary>
    /// Executes a command and returns the trimmed stdout output.
    /// </summary>
    protected string Execute(string input)
    {
        var (output, _) = Harness.Execute(input);
        return output.Trim();
    }

    /// <summary>
    /// Executes a command and returns the raw (untrimmed) stdout output.
    /// </summary>
    protected string ExecuteRaw(string input)
    {
        var (output, _) = Harness.Execute(input);
        return output;
    }

    /// <summary>
    /// Executes a command and returns stdout and stderr.
    /// </summary>
    protected (string stdout, string stderr, int exitCode) ExecuteFull(string input)
    {
        return Harness.ExecuteWithStderr(input);
    }

    /// <summary>
    /// Executes a command and returns only the exit code.
    /// </summary>
    protected int ExecuteExitCode(string input)
    {
        return Harness.ExecuteReturnCode(input);
    }

    public void Dispose()
    {
        Harness.Dispose();
    }
}
