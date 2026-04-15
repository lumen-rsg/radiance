using System.Diagnostics;
using System.IO.Pipes;
using Radiance.Builtins;
using Radiance.Expansion;
using Radiance.Lexer;
using Radiance.Parser.Ast;
using Radiance.Utils;

namespace Radiance.Interpreter;

/// <summary>
/// Orchestrates the execution of multi-command pipelines and file redirections.
/// Connects processes via anonymous pipes and handles I/O redirection to files.
/// Supports compound commands (if, for, while, case) in pipelines by capturing
/// their console output.
/// </summary>
public sealed class PipelineExecutor
{
    private readonly ShellContext _context;
    private readonly BuiltinRegistry _builtins;
    private readonly ProcessManager _processManager;
    private readonly ShellInterpreter _interpreter;
    private readonly Expander _expander;

    /// <summary>
    /// Creates a new pipeline executor.
    /// </summary>
    /// <param name="context">The shell execution context.</param>
    /// <param name="builtins">The builtin command registry.</param>
    /// <param name="processManager">The external process manager.</param>
    /// <param name="interpreter">The shell interpreter (for executing builtins and compound commands).</param>
    /// <param name="expander">The expansion engine (for word expansion).</param>
    public PipelineExecutor(
        ShellContext context,
        BuiltinRegistry builtins,
        ProcessManager processManager,
        ShellInterpreter interpreter,
        Expander expander)
    {
        _context = context;
        _builtins = builtins;
        _processManager = processManager;
        _interpreter = interpreter;
        _expander = expander;
    }

    /// <summary>
    /// Executes a pipeline of commands connected by pipes, with optional file
    /// redirections on individual commands.
    /// </summary>
    /// <param name="node">The pipeline AST node.</param>
    /// <returns>The exit code of the last command in the pipeline.</returns>
    public int Execute(PipelineNode node)
    {
        var commands = node.Commands;
        if (commands.Count == 0)
            return 0;

        // Single command — no pipes needed, just handle redirects
        if (commands.Count == 1)
        {
            return ExecuteSingleWithRedirects(commands[0]);
        }

        // Multi-command pipeline
        return ExecutePipeline(commands);
    }

    /// <summary>
    /// Executes a single command with its file redirections applied.
    /// Handles both simple commands and compound commands.
    /// </summary>
    private int ExecuteSingleWithRedirects(AstNode cmd)
    {
        if (cmd is SimpleCommandNode simpleCmd)
        {
            return ExecuteSingleSimpleWithRedirects(simpleCmd);
        }

        // Compound command (if, for, while, case) with redirects
        // For now, execute normally — redirect support for compound commands
        // can be enhanced later
        return _interpreter.Execute(cmd);
    }

    /// <summary>
    /// Executes a single simple command with its file redirections applied.
    /// Supports file redirects, fd-duplication redirects (2>&1), and combinations.
    /// </summary>
    private int ExecuteSingleSimpleWithRedirects(SimpleCommandNode cmd)
    {
        // Resolve redirects (expand redirect target paths)
        var stdinFile = GetStdinRedirect(cmd);
        var stdoutRedirect = GetStdoutRedirect(cmd);
        var hasStderrToStdout = HasStderrToStdoutRedirect(cmd);

        if (stdinFile is null && stdoutRedirect is null && !hasStderrToStdout)
        {
            // No redirects — delegate to interpreter directly
            return _interpreter.VisitSimpleCommand(cmd);
        }

        // Open redirect streams
        Stream? stdinStream = null;
        Stream? stdoutStream = null;
        var exitCode = 0;

        try
        {
            if (stdinFile is not null)
            {
                stdinStream = new FileStream(stdinFile, FileMode.Open, FileAccess.Read, FileShare.Read);
            }

            if (stdoutRedirect is not null)
            {
                var (file, append) = stdoutRedirect.Value;
                stdoutStream = new FileStream(
                    file,
                    append ? FileMode.Append : FileMode.Create,
                    FileAccess.Write,
                    FileShare.None);
            }

            // Expand words using the full expansion pipeline
            var expandedWords = ExpandWords(cmd);

            if (expandedWords.Count == 0)
            {
                // No command, just assignments
                foreach (var assignment in cmd.Assignments)
                {
                    var value = _expander.ExpandString(assignment.Value);
                    _context.SetVariable(assignment.Name, value);
                }

                return 0;
            }

            var commandName = expandedWords[0];

            // Check if it's a builtin
            if (_builtins.IsBuiltin(commandName))
            {
                exitCode = ExecuteBuiltinWithRedirects(cmd, expandedWords.ToArray(), stdinStream, stdoutStream, hasStderrToStdout);
            }
            else if (_context.HasFunction(commandName))
            {
                // Function with redirects — capture output via Console.SetOut
                var captured = new StringWriter();
                var originalOut = Console.Out;
                var originalError = Console.Error;
                OutputCapture.Push();
                try
                {
                    Console.SetOut(captured);
                    if (hasStderrToStdout)
                        Console.SetError(captured);

                    exitCode = _interpreter.ExecuteFunction(commandName, expandedWords);

                    var output = captured.ToString();
                    if (stdoutStream is not null)
                    {
                        var bytes = System.Text.Encoding.UTF8.GetBytes(output);
                        stdoutStream.Write(bytes, 0, bytes.Length);
                        stdoutStream.Flush();
                    }
                    else
                    {
                        Console.Write(output);
                    }
                }
                finally
                {
                    Console.SetOut(originalOut);
                    Console.SetError(originalError);
                    OutputCapture.Pop();
                }
            }
            else
            {
                exitCode = ExecuteExternalWithRedirects(
                    commandName, expandedWords.ToArray(), stdinStream, stdoutStream, hasStderrToStdout);
            }
        }
        catch (FileNotFoundException ex)
        {
            Console.Error.WriteLine($"radiance: {ex.Message}");
            exitCode = 1;
        }
        catch (DirectoryNotFoundException ex)
        {
            Console.Error.WriteLine($"radiance: {ex.Message}");
            exitCode = 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"radiance: redirection error: {ex.Message}");
            exitCode = 1;
        }
        finally
        {
            stdinStream?.Dispose();
            stdoutStream?.Dispose();
        }

        return exitCode;
    }

    /// <summary>
    /// Executes a multi-command pipeline by running each stage sequentially and
    /// passing data through MemoryStreams. Simple, reliable approach that avoids
    /// anonymous pipe race conditions.
    /// </summary>
    private int ExecutePipeline(List<AstNode> commands)
    {
        var processCount = commands.Count;
        var lastExitCode = 0;
        byte[]? pipeData = null; // carries stdout from one stage to the next stage's stdin

        for (var i = 0; i < processCount; i++)
        {
            var cmd = commands[i];

            // ── Determine stdin for this stage ──
            Stream? stdinStream = null;
            if (i == 0 && cmd is SimpleCommandNode sc0)
            {
                var stdinFile = GetStdinRedirect(sc0);
                if (stdinFile is not null)
                    stdinStream = new FileStream(stdinFile, FileMode.Open, FileAccess.Read, FileShare.Read);
            }
            else if (pipeData is not null && pipeData.Length > 0)
            {
                stdinStream = new MemoryStream(pipeData);
            }

            // ── Determine stdout for this stage ──
            var isLast = i == processCount - 1;
            Stream? stdoutFile = null;
            if (isLast && cmd is SimpleCommandNode scN)
            {
                var stdoutRedirect = GetStdoutRedirect(scN);
                if (stdoutRedirect is not null)
                {
                    var (file, append) = stdoutRedirect.Value;
                    stdoutFile = new FileStream(file, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None);
                }
            }

            // ── Execute the stage ──
            if (cmd is not SimpleCommandNode)
            {
                // Compound command (if, for, while, case)
                var captured = new StringWriter();
                var origOut = Console.Out;
                OutputCapture.Push();
                try
                {
                    Console.SetOut(captured);
                    lastExitCode = _interpreter.Execute(cmd);
                }
                finally
                {
                    Console.SetOut(origOut);
                    OutputCapture.Pop();
                }

                var output = captured.ToString();
                if (stdoutFile is not null)
                {
                    var bytes = System.Text.Encoding.UTF8.GetBytes(output);
                    stdoutFile.Write(bytes, 0, bytes.Length);
                    pipeData = null;
                }
                else if (isLast)
                {
                    Console.Write(output);
                    pipeData = null;
                }
                else
                {
                    pipeData = System.Text.Encoding.UTF8.GetBytes(output);
                }
            }
            else
            {
                var simpleCommand = (SimpleCommandNode)cmd;
                var expandedWords = ExpandWords(simpleCommand);

                if (expandedWords.Count == 0)
                {
                    lastExitCode = 0;
                    pipeData = null;
                    stdinStream?.Dispose();
                    stdoutFile?.Dispose();
                    continue;
                }

                var commandName = expandedWords[0];

                if (_builtins.IsBuiltin(commandName))
                {
                    // Builtin — capture output via Console.SetOut
                    var captured = new StringWriter();
                    var origOut = Console.Out;
                    OutputCapture.Push();
                    try
                    {
                        Console.SetOut(captured);
                        _ = _builtins.TryExecute(commandName, expandedWords.ToArray(), _context, out lastExitCode);
                    }
                    finally
                    {
                        Console.SetOut(origOut);
                        OutputCapture.Pop();
                    }

                    var output = captured.ToString();
                    if (stdoutFile is not null)
                    {
                        var bytes = System.Text.Encoding.UTF8.GetBytes(output);
                        stdoutFile.Write(bytes, 0, bytes.Length);
                        pipeData = null;
                    }
                    else if (isLast)
                    {
                        Console.Write(output);
                        pipeData = null;
                    }
                    else
                    {
                        pipeData = System.Text.Encoding.UTF8.GetBytes(output);
                    }
                }
                else if (_context.HasFunction(commandName))
                {
                    // Function — capture output via Console.SetOut (same pattern as builtin)
                    var captured = new StringWriter();
                    var origOut = Console.Out;
                    OutputCapture.Push();
                    try
                    {
                        Console.SetOut(captured);
                        lastExitCode = _interpreter.ExecuteFunction(commandName, expandedWords);
                    }
                    finally
                    {
                        Console.SetOut(origOut);
                        OutputCapture.Pop();
                    }

                    var output = captured.ToString();
                    if (stdoutFile is not null)
                    {
                        var bytes = System.Text.Encoding.UTF8.GetBytes(output);
                        stdoutFile.Write(bytes, 0, bytes.Length);
                        pipeData = null;
                    }
                    else if (isLast)
                    {
                        Console.Write(output);
                        pipeData = null;
                    }
                    else
                    {
                        pipeData = System.Text.Encoding.UTF8.GetBytes(output);
                    }
                }
                else
                {
                    // External command — run the process, capture stdout to byte[]
                    var capturedMs = new MemoryStream();
                    var process = _processManager.StartProcess(
                        commandName,
                        expandedWords.ToArray(),
                        _context,
                        stdinStream,
                        isLast && stdoutFile is not null ? stdoutFile : capturedMs,
                        wireStdoutAsync: false);

                    if (process is null)
                    {
                        ColorOutput.WriteError($"{commandName}: command not found");
                        lastExitCode = 127;
                        pipeData = null;
                    }
                    else
                    {
                        process.WaitForExit();
                        lastExitCode = process.ExitCode;

                        if (stdoutFile is not null)
                        {
                            // Output was written directly to file
                            pipeData = null;
                        }
                        else if (isLast)
                        {
                            // Last command — write captured stdout to console
                            var output = System.Text.Encoding.UTF8.GetString(capturedMs.ToArray());
                            if (!string.IsNullOrEmpty(output))
                                Console.Write(output);
                            pipeData = null;
                        }
                        else
                        {
                            // Intermediate — pass stdout bytes to next stage
                            pipeData = capturedMs.ToArray();
                        }

                        process.Dispose();
                    }
                }
            }

            stdinStream?.Dispose();
            stdoutFile?.Dispose();
        }

        return lastExitCode;
    }

    /// <summary>
    /// Executes a builtin command with optional file redirections by capturing
    /// its console output and writing to the redirect streams.
    /// </summary>
    private int ExecuteBuiltinWithRedirects(
        SimpleCommandNode cmd,
        string[] expandedWords,
        Stream? stdinStream,
        Stream? stdoutStream,
        bool mergeStderr = false)
    {
        var commandName = expandedWords[0];

        if (stdoutStream is null && stdinStream is null && !mergeStderr)
        {
            // No redirects — execute normally
            _ = _builtins.TryExecute(commandName, expandedWords, _context, out var exitCode);
            return exitCode;
        }

        // Capture output via Console.SetOut
        var captured = new StringWriter();
        var originalOut = Console.Out;
        var originalError = Console.Error;
        OutputCapture.Push();
        try
        {
            Console.SetOut(captured);
            if (mergeStderr)
                Console.SetError(captured);

            _ = _builtins.TryExecute(commandName, expandedWords, _context, out var exitCode);

            // Write captured output to the redirect stream
            if (stdoutStream is not null)
            {
                var output = captured.ToString();
                var bytes = System.Text.Encoding.UTF8.GetBytes(output);
                stdoutStream.Write(bytes, 0, bytes.Length);
                stdoutStream.Flush();
            }
            else
            {
                // No stdout redirect — write to console
                Console.Write(captured.ToString());
            }

            return exitCode;
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
            OutputCapture.Pop();
        }
    }

    /// <summary>
    /// Executes an external command with optional file redirections.
    /// When <paramref name="mergeStderr"/> is true, stderr is redirected to the
    /// same destination as stdout (2>&1).
    /// </summary>
    private int ExecuteExternalWithRedirects(
        string commandName,
        string[] expandedWords,
        Stream? stdinStream,
        Stream? stdoutStream,
        bool mergeStderr = false)
    {
        // When 2>&1 is requested but there's no file redirect, we need to
        // capture both stdout and stderr into a single MemoryStream, then
        // write the merged output to the console.
        Stream? effectiveStdout = stdoutStream;
        Stream? effectiveStderr = null;
        MemoryStream? mergedStream = null;

        if (mergeStderr && stdoutStream is null)
        {
            mergedStream = new MemoryStream();
            effectiveStdout = mergedStream;
            effectiveStderr = mergedStream;
        }
        else if (mergeStderr)
        {
            effectiveStderr = stdoutStream;
        }

        var process = _processManager.StartProcess(
            commandName,
            expandedWords,
            _context,
            stdinStream,
            effectiveStdout,
            stderrStream: effectiveStderr,
            wireStdoutAsync: effectiveStdout is null);

        if (process is null)
        {
            ColorOutput.WriteError($"{commandName}: command not found");
            mergedStream?.Dispose();
            return 127;
        }

        process.WaitForExit();
        var exitCode = process.ExitCode;
        process.Dispose();

        // If we merged stderr into a MemoryStream (2>&1 with no file redirect),
        // write the combined output to the console
        if (mergedStream is not null)
        {
            var output = System.Text.Encoding.UTF8.GetString(mergedStream.ToArray());
            if (!string.IsNullOrEmpty(output))
                Console.Write(output);
            mergedStream.Dispose();
        }

        return exitCode;
    }

    // ──── Helper Methods ────

    /// <summary>
    /// Expands all words in a command node using the full expansion pipeline
    /// (tilde, variable, command substitution, arithmetic, glob).
    /// Also processes prefix assignments.
    /// </summary>
    private List<string> ExpandWords(SimpleCommandNode cmd)
    {
        // Process assignments first
        foreach (var assignment in cmd.Assignments)
        {
            var value = _expander.ExpandString(assignment.Value);
            _context.SetVariable(assignment.Name, value);
        }

        return _expander.ExpandWords(cmd.Words);
    }

    /// <summary>
    /// Expands the redirect target and gets the stdin redirect filename, if any.
    /// </summary>
    private string? GetStdinRedirect(SimpleCommandNode cmd)
    {
        var redirect = cmd.Redirects.FirstOrDefault(r => r.RedirectType == TokenType.LessThan);
        if (redirect is null)
            return null;

        // Expand the redirect target word parts
        var expanded = _expander.ExpandWord(redirect.Target!);
        return expanded.Count > 0 ? expanded[0] : null;
    }

    /// <summary>
    /// Expands the redirect target and gets the stdout redirect details, if any.
    /// </summary>
    private (string file, bool append)? GetStdoutRedirect(SimpleCommandNode cmd)
    {
        var redirect = cmd.Redirects.FirstOrDefault(r =>
            r.RedirectType is TokenType.GreaterThan or TokenType.DoubleGreaterThan);

        if (redirect is null)
            return null;

        // Expand the redirect target word parts
        var expanded = _expander.ExpandWord(redirect.Target!);
        if (expanded.Count == 0)
            return null;

        var append = redirect.RedirectType == TokenType.DoubleGreaterThan;
        return (expanded[0], append);
    }

    /// <summary>
    /// Checks if the command has a 2>&1 redirect (stderr to stdout).
    /// </summary>
    private static bool HasStderrToStdoutRedirect(SimpleCommandNode cmd)
    {
        return cmd.Redirects.Any(r =>
            r.RedirectType == TokenType.AmpersandGreaterThan &&
            r.DuplicateTargetFd == 1);
    }
}
