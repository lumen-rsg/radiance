using System.Diagnostics;
using System.IO.Pipes;
using Radiance.Builtins;
using Radiance.Expansion;
using Radiance.Lexer;
using Radiance.Parser.Ast;

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
    /// </summary>
    private int ExecuteSingleSimpleWithRedirects(SimpleCommandNode cmd)
    {
        // Resolve redirects (expand redirect target paths)
        var stdinFile = GetStdinRedirect(cmd);
        var stdoutRedirect = GetStdoutRedirect(cmd);

        if (stdinFile is null && stdoutRedirect is null)
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
                exitCode = ExecuteBuiltinWithRedirects(cmd, expandedWords.ToArray(), stdinStream, stdoutStream);
            }
            else
            {
                exitCode = ExecuteExternalWithRedirects(commandName, expandedWords.ToArray(), stdinStream, stdoutStream);
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
    /// Executes a multi-command pipeline using anonymous pipes to connect processes.
    /// Supports simple commands, compound commands, and builtins.
    /// </summary>
    private int ExecutePipeline(List<AstNode> commands)
    {
        var processCount = commands.Count;
        var processes = new Process?[processCount];
        var pipePairs = new (AnonymousPipeServerStream writer, AnonymousPipeClientStream reader)?[processCount - 1];
        var builtinResults = new (bool isBuiltin, int exitCode, string? capturedOutput)[processCount];
        var redirectFiles = new List<Stream>();

        try
        {
            // 1. Create anonymous pipe pairs between each pair of adjacent commands
            for (var i = 0; i < processCount - 1; i++)
            {
                var writer = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
                var reader = new AnonymousPipeClientStream(PipeDirection.In, writer.GetClientHandleAsString());
                pipePairs[i] = (writer, reader);
            }

            // 2. Start all processes / execute builtins / execute compound commands
            for (var i = 0; i < processCount; i++)
            {
                var cmd = commands[i];

                // Determine stdin stream for this command
                Stream? stdinStream = null;
                if (i == 0)
                {
                    // First command: check for file redirect (only for simple commands)
                    if (cmd is SimpleCommandNode simpleCmd0)
                    {
                        var stdinFile = GetStdinRedirect(simpleCmd0);
                        if (stdinFile is not null)
                        {
                            stdinStream = new FileStream(stdinFile, FileMode.Open, FileAccess.Read, FileShare.Read);
                            redirectFiles.Add(stdinStream);
                        }
                    }
                }
                else
                {
                    // Read from the previous pipe
                    stdinStream = pipePairs[i - 1]!.Value.reader;
                }

                // Determine stdout stream for this command
                Stream? stdoutStream = null;
                if (i == processCount - 1)
                {
                    // Last command: check for file redirect (only for simple commands)
                    if (cmd is SimpleCommandNode simpleCmdN)
                    {
                        var stdoutRedirect = GetStdoutRedirect(simpleCmdN);
                        if (stdoutRedirect is not null)
                        {
                            var (file, append) = stdoutRedirect.Value;
                            stdoutStream = new FileStream(
                                file,
                                append ? FileMode.Append : FileMode.Create,
                                FileAccess.Write,
                                FileShare.None);
                            redirectFiles.Add(stdoutStream);
                        }
                    }
                }
                else
                {
                    // Write to the next pipe
                    stdoutStream = pipePairs[i]!.Value.writer;
                }

                // Handle compound commands (if, for, while, case) — capture output like builtins
                if (cmd is not SimpleCommandNode)
                {
                    var capturedOutput = new StringWriter();
                    var originalOut = Console.Out;
                    try
                    {
                        Console.SetOut(capturedOutput);
                        var exitCode = _interpreter.Execute(cmd);
                        builtinResults[i] = (true, exitCode, capturedOutput.ToString());
                    }
                    finally
                    {
                        Console.SetOut(originalOut);
                    }

                    // Write captured output to the stdout stream (pipe or file)
                    if (stdoutStream is not null && builtinResults[i].capturedOutput is not null)
                    {
                        var bytes = System.Text.Encoding.UTF8.GetBytes(builtinResults[i].capturedOutput!);
                        stdoutStream.Write(bytes, 0, bytes.Length);
                        stdoutStream.Flush();
                    }

                    // Close stdin if it came from a pipe
                    if (i > 0) stdinStream?.Close();
                    // Close stdout writer so next process gets EOF
                    if (i < processCount - 1 && stdoutStream is not null) stdoutStream.Close();
                    else if (stdoutStream is not null) stdoutStream.Flush();

                    continue;
                }

                // Simple command path
                var simpleCommand = (SimpleCommandNode)cmd;
                var expandedWords = ExpandWords(simpleCommand);

                if (expandedWords.Count == 0)
                {
                    builtinResults[i] = (true, 0, null);
                    continue;
                }

                var commandName = expandedWords[0];

                // Execute as builtin or external process
                if (_builtins.IsBuiltin(commandName))
                {
                    // Capture builtin output
                    var capturedOutput = new StringWriter();
                    var originalOut = Console.Out;
                    try
                    {
                        Console.SetOut(capturedOutput);
                        _ = _builtins.TryExecute(commandName, expandedWords.ToArray(), _context, out var exitCode);
                        builtinResults[i] = (true, exitCode, capturedOutput.ToString());
                    }
                    finally
                    {
                        Console.SetOut(originalOut);
                    }

                    // Write captured output to the stdout stream (pipe or file)
                    if (stdoutStream is not null && builtinResults[i].capturedOutput is not null)
                    {
                        var bytes = System.Text.Encoding.UTF8.GetBytes(builtinResults[i].capturedOutput!);
                        stdoutStream.Write(bytes, 0, bytes.Length);
                        stdoutStream.Flush();
                    }

                    if (i > 0) stdinStream?.Close();
                    if (i < processCount - 1 && stdoutStream is not null) stdoutStream.Close();
                    else if (stdoutStream is not null) stdoutStream.Flush();
                }
                else
                {
                    // External command
                    var process = _processManager.StartProcess(
                        commandName,
                        expandedWords.ToArray(),
                        _context,
                        stdinStream,
                        stdoutStream);

                    if (process is null)
                    {
                        Console.Error.WriteLine($"radiance: command not found: {commandName}");
                        builtinResults[i] = (true, 127, null);
                    }
                    else
                    {
                        processes[i] = process;
                    }

                    // Close our copy of the pipe handles
                    if (i > 0) stdinStream?.Close();
                    if (i < processCount - 1) stdoutStream?.Close();
                    else if (stdoutStream is not null) stdoutStream.Close();
                }
            }

            // 3. Wait for all external processes to complete (in reverse order)
            for (var i = processCount - 1; i >= 0; i--)
            {
                processes[i]?.WaitForExit();
            }

            // 4. Determine exit code — last command's exit code
            var lastIdx = processCount - 1;
            var finalExitCode = 0;

            if (processes[lastIdx] is not null)
            {
                finalExitCode = processes[lastIdx]!.ExitCode;
            }
            else if (builtinResults[lastIdx].isBuiltin)
            {
                finalExitCode = builtinResults[lastIdx].exitCode;
            }

            return finalExitCode;
        }
        finally
        {
            // Cleanup all pipe handles and redirect files
            foreach (var pipePair in pipePairs)
            {
                if (pipePair is not null)
                {
                    try { pipePair.Value.writer.Dispose(); } catch { /* ignored */ }
                    try { pipePair.Value.reader.Dispose(); } catch { /* ignored */ }
                }
            }

            foreach (var stream in redirectFiles)
            {
                try { stream.Dispose(); } catch { /* ignored */ }
            }

            // Dispose processes
            foreach (var process in processes)
            {
                try { process?.Dispose(); } catch { /* ignored */ }
            }
        }
    }

    /// <summary>
    /// Executes a builtin command with optional file redirections by capturing
    /// its console output and writing to the redirect streams.
    /// </summary>
    private int ExecuteBuiltinWithRedirects(
        SimpleCommandNode cmd,
        string[] expandedWords,
        Stream? stdinStream,
        Stream? stdoutStream)
    {
        var commandName = expandedWords[0];

        if (stdoutStream is null && stdinStream is null)
        {
            // No redirects — execute normally
            _ = _builtins.TryExecute(commandName, expandedWords, _context, out var exitCode);
            return exitCode;
        }

        // Capture output via Console.SetOut
        var captured = new StringWriter();
        var originalOut = Console.Out;
        try
        {
            Console.SetOut(captured);

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
                Console.SetOut(originalOut);
                Console.Write(captured.ToString());
            }

            return exitCode;
        }
        finally
        {
            Console.SetOut(originalOut);
        }
    }

    /// <summary>
    /// Executes an external command with optional file redirections.
    /// </summary>
    private int ExecuteExternalWithRedirects(
        string commandName,
        string[] expandedWords,
        Stream? stdinStream,
        Stream? stdoutStream)
    {
        var process = _processManager.StartProcess(
            commandName,
            expandedWords,
            _context,
            stdinStream,
            stdoutStream);

        if (process is null)
        {
            Console.Error.WriteLine($"radiance: command not found: {commandName}");
            return 127;
        }

        process.WaitForExit();
        var exitCode = process.ExitCode;
        process.Dispose();
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
        var expanded = _expander.ExpandWord(redirect.Target);
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
        var expanded = _expander.ExpandWord(redirect.Target);
        if (expanded.Count == 0)
            return null;

        var append = redirect.RedirectType == TokenType.DoubleGreaterThan;
        return (expanded[0], append);
    }
}