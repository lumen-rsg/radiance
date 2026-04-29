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
/// Connects stages via OS-level anonymous pipes for true concurrent execution,
/// with automatic backpressure through kernel pipe buffers.
/// Handles I/O redirection to files and compound commands in pipelines.
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
    /// All stages start simultaneously, connected by OS anonymous pipes.
    /// External processes get async CopyToAsync bridging. Builtins/functions
    /// run on dedicated threads with Console.SetOut redirected to the output pipe.
    /// </summary>
    private int ExecutePipeline(List<AstNode> commands)
    {
        var stageCount = commands.Count;
        var lastExitCode = 0;

        // ── 1. Create inter-stage pipes ──
        // For N stages, create N-1 pipe pairs.
        // pipe[i] connects stage i's stdout to stage i+1's stdin.
        var pipeWriters = new AnonymousPipeServerStream?[stageCount]; // stdout for each stage
        var pipeReaders = new AnonymousPipeClientStream?[stageCount]; // stdin for each stage

        for (var i = 0; i < stageCount - 1; i++)
        {
            var server = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.None);
            var client = new AnonymousPipeClientStream(PipeDirection.In, server.GetClientHandleAsString());
            pipeWriters[i] = server;
            pipeReaders[i + 1] = client;
        }

        // ── 2. Apply file redirects for first/last stage ──
        Stream? firstStdinFile = null;
        Stream? lastStdoutFile = null;
        bool lastHasStderrToStdout = false;

        if (commands[0] is SimpleCommandNode sc0)
        {
            var stdinFile = GetStdinRedirect(sc0);
            if (stdinFile is not null)
                firstStdinFile = new FileStream(stdinFile, FileMode.Open, FileAccess.Read, FileShare.Read);
        }

        var lastCmd = commands[stageCount - 1];
        if (lastCmd is SimpleCommandNode scN)
        {
            var stdoutRedirect = GetStdoutRedirect(scN);
            if (stdoutRedirect is not null)
            {
                var (file, append) = stdoutRedirect.Value;
                lastStdoutFile = new FileStream(file, append ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None);
            }

            lastHasStderrToStdout = HasStderrToStdoutRedirect(scN);
        }

        // ── 3. Start all stages concurrently ──
        var processes = new Process?[stageCount];
        var stdoutTasks = new Task?[stageCount];
        var threads = new Thread?[stageCount];
        var stageErrors = new Exception?[stageCount];
        var stageExitCodes = new int[stageCount];
        var completionEvents = new ManualResetEventSlim?[stageCount];
        var lastStdoutCapture = new MemoryStream?[1]; // Capture for last external stage with no redirect

        try
        {
            for (var i = 0; i < stageCount; i++)
            {
                var cmd = commands[i];
                var isFirst = i == 0;
                var isLast = i == stageCount - 1;

                // Determine stdin and stdout for this stage
                Stream? stdin = isFirst ? firstStdinFile : pipeReaders[i];
                Stream? stdout = isLast ? lastStdoutFile : pipeWriters[i];

                if (cmd is not SimpleCommandNode)
                {
                    // ── Compound command (if/for/while/case/brace/subshell) ──
                    completionEvents[i] = new ManualResetEventSlim(false);
                    var idx = i;
                    threads[i] = new Thread(() =>
                    {
                        try
                        {
                            var writer = stdout is not null
                                ? new StreamWriter(stdout, System.Text.Encoding.UTF8) { AutoFlush = true }
                                : null;

                            var origOut = Console.Out;
                            OutputCapture.Push();
                            try
                            {
                                if (writer is not null)
                                    Console.SetOut(writer);
                                stageExitCodes[idx] = _interpreter.Execute(cmd);
                            }
                            finally
                            {
                                Console.SetOut(origOut);
                                OutputCapture.Pop();
                                writer?.Flush();
                                writer?.Dispose();
                            }
                        }
                        catch (IOException) { stageExitCodes[idx] = 1; }
                        catch (ObjectDisposedException) { stageExitCodes[idx] = 1; }
                        catch (Exception ex) { stageErrors[idx] = ex; stageExitCodes[idx] = 1; }
                        finally
                        {
                            completionEvents[idx]!.Set();
                        }
                    })
                    { IsBackground = true };
                    threads[i]!.Start();
                }
                else
                {
                    var simpleCommand = (SimpleCommandNode)cmd;
                    var expandedWords = ExpandWords(simpleCommand);

                    if (expandedWords.Count == 0)
                    {
                        // Assignment-only command in pipeline
                        foreach (var assignment in simpleCommand.Assignments)
                        {
                            var value = _expander.ExpandString(assignment.Value);
                            _context.SetVariable(assignment.Name, value);
                        }
                        stageExitCodes[i] = 0;

                        // Close the pipe writer so downstream stages see EOF
                        if (stdout is AnonymousPipeServerStream ps)
                            ps.Dispose();
                        continue;
                    }

                    var commandName = expandedWords[0];

                    if (_builtins.IsBuiltin(commandName))
                    {
                        // ── Builtin in pipeline ──
                        completionEvents[i] = new ManualResetEventSlim(false);
                        var idx = i;
                        threads[i] = new Thread(() =>
                        {
                            try
                            {
                                var writer = stdout is not null
                                    ? new StreamWriter(stdout, System.Text.Encoding.UTF8) { AutoFlush = true }
                                    : null;

                                var origOut = Console.Out;
                                OutputCapture.Push();
                                try
                                {
                                    if (writer is not null)
                                        Console.SetOut(writer);
                                    _ = _builtins.TryExecute(commandName, expandedWords.ToArray(), _context, out stageExitCodes[idx]);
                                }
                                finally
                                {
                                    Console.SetOut(origOut);
                                    OutputCapture.Pop();
                                    writer?.Flush();
                                    writer?.Dispose();
                                }
                            }
                            catch (IOException) { stageExitCodes[idx] = 1; }
                            catch (ObjectDisposedException) { stageExitCodes[idx] = 1; }
                            catch (Exception ex) { stageErrors[idx] = ex; stageExitCodes[idx] = 1; }
                            finally
                            {
                                completionEvents[idx]!.Set();
                            }
                        })
                        { IsBackground = true };
                        threads[i]!.Start();
                    }
                    else if (_context.HasFunction(commandName))
                    {
                        // ── Shell function in pipeline ──
                        completionEvents[i] = new ManualResetEventSlim(false);
                        var idx = i;
                        threads[i] = new Thread(() =>
                        {
                            try
                            {
                                var writer = stdout is not null
                                    ? new StreamWriter(stdout, System.Text.Encoding.UTF8) { AutoFlush = true }
                                    : null;

                                var origOut = Console.Out;
                                OutputCapture.Push();
                                try
                                {
                                    if (writer is not null)
                                        Console.SetOut(writer);
                                    stageExitCodes[idx] = _interpreter.ExecuteFunction(commandName, expandedWords);
                                }
                                finally
                                {
                                    Console.SetOut(origOut);
                                    OutputCapture.Pop();
                                    writer?.Flush();
                                    writer?.Dispose();
                                }
                            }
                            catch (IOException) { stageExitCodes[idx] = 1; }
                            catch (ObjectDisposedException) { stageExitCodes[idx] = 1; }
                            catch (Exception ex) { stageErrors[idx] = ex; stageExitCodes[idx] = 1; }
                            finally
                            {
                                completionEvents[idx]!.Set();
                            }
                        })
                        { IsBackground = true };
                        threads[i]!.Start();
                    }
                    else
                    {
                        // ── External command in pipeline ──
                        var stderrSink = (isLast && lastHasStderrToStdout) ? stdout : null;

                        // For the last stage with no stdout pipe (output to console),
                        // capture to a MemoryStream so we can write it to Console.Out
                        // (which may have been redirected by the test harness or command substitution)
                        Stream? effectiveStdout = stdout;
                        if (isLast && stdout is null)
                        {
                            var capturedMs = new MemoryStream();
                            lastStdoutCapture[0] = capturedMs;
                            effectiveStdout = capturedMs;
                        }

                        var (proc, stdoutTask, _) = _processManager.StartConcurrent(
                            commandName, expandedWords.ToArray(), _context,
                            stdin, effectiveStdout, stderrSink);

                        processes[i] = proc;
                        stdoutTasks[i] = stdoutTask;

                        if (proc is null)
                        {
                            ColorOutput.WriteError($"{commandName}: command not found");
                            stageExitCodes[i] = 127;

                            // Close pipe writer so downstream stages see EOF
                            if (stdout is AnonymousPipeServerStream ps)
                                ps.Dispose();
                        }
                    }
                }
            }

            // ── 4. Wait for all stages ──
            for (var i = 0; i < stageCount; i++)
            {
                if (processes[i] is not null)
                {
                    processes[i]!.WaitForExit();
                    stageExitCodes[i] = processes[i]!.ExitCode;

                    // Wait for stdout copy task to complete so all data is in the sink stream
                    if (stdoutTasks[i] is not null)
                    {
                        try { stdoutTasks[i]!.Wait(); } catch { }
                    }
                }
                else if (completionEvents[i] is not null)
                {
                    completionEvents[i]!.Wait();
                }
            }

            // ── 5. Handle last-stage console output ──
            // If the last stage was external with no stdout pipe, write captured
            // output to Console.Out (which may have been redirected).
            if (lastStdoutCapture[0] is { } capture)
            {
                var output = System.Text.Encoding.UTF8.GetString(capture.ToArray());
                if (!string.IsNullOrEmpty(output))
                    Console.Write(output);
            }

            lastExitCode = stageExitCodes[stageCount - 1];
        }
        finally
        {
            // ── 6. Cleanup ──
            for (var i = 0; i < stageCount; i++)
            {
                try { processes[i]?.Dispose(); } catch { }
                try { pipeWriters[i]?.Dispose(); } catch { }
                try { pipeReaders[i]?.Dispose(); } catch { }
                completionEvents[i]?.Dispose();
            }
            firstStdinFile?.Dispose();
            lastStdoutFile?.Dispose();
            lastStdoutCapture[0]?.Dispose();
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
