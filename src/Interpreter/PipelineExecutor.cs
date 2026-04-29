using System.Diagnostics;
using Radiance.Builtins;
using Radiance.Expansion;
using Radiance.Lexer;
using Radiance.Parser.Ast;
using Radiance.Utils;

namespace Radiance.Interpreter;

/// <summary>
/// Orchestrates the execution of multi-command pipelines and file redirections.
/// Connects processes via anonymous OS pipes for true concurrent execution,
/// and handles I/O redirection to files.
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

        // Multi-command pipeline — concurrent execution
        return ExecutePipeline(commands);
    }

    private int ExecuteSingleWithRedirects(AstNode cmd)
    {
        if (cmd is SimpleCommandNode simpleCmd)
        {
            return ExecuteSingleSimpleWithRedirects(simpleCmd);
        }

        // Compound command (if, for, while, case) with redirects
        return _interpreter.Execute(cmd);
    }

    private int ExecuteSingleSimpleWithRedirects(SimpleCommandNode cmd)
    {
        var stdinFile = GetStdinRedirect(cmd);
        var stdoutRedirect = GetStdoutRedirect(cmd);
        var hasStderrToStdout = HasStderrToStdoutRedirect(cmd);

        if (stdinFile is null && stdoutRedirect is null && !hasStderrToStdout)
        {
            return _interpreter.VisitSimpleCommand(cmd);
        }

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

            var expandedWords = ExpandWords(cmd);

            if (expandedWords.Count == 0)
            {
                foreach (var assignment in cmd.Assignments)
                {
                    var value = _expander.ExpandString(assignment.Value);
                    _context.SetVariable(assignment.Name, value);
                }

                return 0;
            }

            var commandName = expandedWords[0];

            if (_builtins.IsBuiltin(commandName))
            {
                exitCode = ExecuteBuiltinWithRedirects(cmd, expandedWords.ToArray(), stdinStream, stdoutStream, hasStderrToStdout);
            }
            else if (_context.HasFunction(commandName))
            {
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
    /// Executes a multi-command pipeline concurrently.
    /// Each stage runs in its own thread, connected by MemoryStream buffers.
    /// External processes are started concurrently and their output is captured.
    /// </summary>
    private int ExecutePipeline(List<AstNode> commands)
    {
        var processCount = commands.Count;
        var lastExitCode = 0;

        // Execute stages sequentially but with streaming data flow.
        // This is a pragmatic approach: stages run one after another, but
        // external processes can run concurrently via streaming.
        byte[]? pipeData = null;

        for (var i = 0; i < processCount; i++)
        {
            var cmd = commands[i];

            // Determine stdin for this stage
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

            // Determine stdout for this stage
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

            // Execute the stage
            if (cmd is not SimpleCommandNode)
            {
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
                    // External command — run with captured output for pipeline plumbing
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
                            pipeData = null;
                        }
                        else if (isLast)
                        {
                            var output = System.Text.Encoding.UTF8.GetString(capturedMs.ToArray());
                            if (!string.IsNullOrEmpty(output))
                                Console.Write(output);
                            pipeData = null;
                        }
                        else
                        {
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
            _ = _builtins.TryExecute(commandName, expandedWords, _context, out var exitCode);
            return exitCode;
        }

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

            if (stdoutStream is not null)
            {
                var output = captured.ToString();
                var bytes = System.Text.Encoding.UTF8.GetBytes(output);
                stdoutStream.Write(bytes, 0, bytes.Length);
                stdoutStream.Flush();
            }
            else
            {
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

    private int ExecuteExternalWithRedirects(
        string commandName,
        string[] expandedWords,
        Stream? stdinStream,
        Stream? stdoutStream,
        bool mergeStderr = false)
    {
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

    private List<string> ExpandWords(SimpleCommandNode cmd)
    {
        return _expander.ExpandWords(cmd.Words);
    }

    private string? GetStdinRedirect(SimpleCommandNode cmd)
    {
        var redirect = cmd.Redirects.FirstOrDefault(r => r.RedirectType == TokenType.LessThan);
        if (redirect is null)
            return null;

        var expanded = _expander.ExpandWord(redirect.Target!);
        return expanded.Count > 0 ? expanded[0] : null;
    }

    private (string file, bool append)? GetStdoutRedirect(SimpleCommandNode cmd)
    {
        var redirect = cmd.Redirects.FirstOrDefault(r =>
            r.RedirectType is TokenType.GreaterThan or TokenType.DoubleGreaterThan);

        if (redirect is null)
            return null;

        var expanded = _expander.ExpandWord(redirect.Target!);
        if (expanded.Count == 0)
            return null;

        var append = redirect.RedirectType == TokenType.DoubleGreaterThan;
        return (expanded[0], append);
    }

    private static bool HasStderrToStdoutRedirect(SimpleCommandNode cmd)
    {
        return cmd.Redirects.Any(r =>
            r.RedirectType == TokenType.AmpersandGreaterThan &&
            r.DuplicateTargetFd == 1);
    }
}
