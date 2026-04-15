using System.Diagnostics;
using System.IO.Pipes;
using Radiance.Utils;

namespace Radiance.Interpreter;

/// <summary>
/// Manages spawning and executing external processes (non-builtin commands).
/// Supports single-command execution, file redirections, and pipe connections
/// for multi-command pipelines.
/// </summary>
public sealed class ProcessManager
{
    /// <summary>
    /// Executes an external command by name with the given arguments.
    /// Automatically detects whether Console.Out has been redirected (e.g., during
    /// command substitution) and uses piped mode to capture output. Otherwise,
    /// the process inherits the terminal directly for TTY support (btop, vim, etc.).
    /// </summary>
    /// <param name="commandName">The command name or path.</param>
    /// <param name="args">The arguments to pass.</param>
    /// <param name="context">The current execution context.</param>
    /// <returns>The exit code of the process.</returns>
    public int Execute(string commandName, string[] args, ShellContext context)
    {
        var resolved = ResolveCommand(commandName);
        if (resolved is null)
        {
            // Check if the command exists as a file but is not executable (permission denied)
            if (commandName.Contains('/') || commandName.Contains('\\'))
            {
                if (File.Exists(commandName))
                {
                    ColorOutput.WriteError($"{commandName}: Permission denied");
                    return 126;
                }
            }
            else
            {
                // Check if found on PATH but not executable
                var pathResult = PathResolver.ResolveWithExecutability(commandName);
                if (pathResult is not null && pathResult.Value.FoundButNotExecutable)
                {
                    ColorOutput.WriteError($"{commandName}: Permission denied");
                    return 126;
                }
            }

            ColorOutput.WriteError($"{commandName}: command not found");
            return 127;
        }

        try
        {
            // When Console.Out has been redirected (e.g., command substitution
            // via Console.SetOut), we need piped mode so external command output
            // gets captured. Otherwise, let the child inherit the terminal directly
            // for full TTY support (interactive apps like btop, vim, htop).
            //
            // Note: Console.IsOutputRedirected only detects OS-level stream redirection.
            // Console.SetOut() changes the TextWriter but does NOT set the OS flag.
            // The default Console.Out is a SyncTextWriter (not StreamWriter), so checking
            // "is not StreamWriter" would always be true. Instead, we check if Console.Out
            // is a StringWriter, which is the actual type set during command substitution
            // via Console.SetOut(new StringWriter()).
            if (Console.IsOutputRedirected || OutputCapture.IsCapturing)
            {
                var startInfo = BuildCapturedStartInfo(resolved, args, context);
                using var process = new Process { StartInfo = startInfo };
                WireOutput(process);
                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.StandardInput.Close();
                process.WaitForExit();
                return process.ExitCode;
            }
            else
            {
                var startInfo = BuildTerminalStartInfo(resolved, args, context);
                using var process = new Process { StartInfo = startInfo };
                process.Start();
                process.WaitForExit();
                return process.ExitCode;
            }
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            // Common for permission denied, command not found at OS level
            var msg = ex.NativeErrorCode == 13  // EACCES on Unix
                ? $"{commandName}: Permission denied"
                : $"{commandName}: {ex.Message}";
            ColorOutput.WriteError(msg);
            return 126;
        }
        catch (Exception ex)
        {
            ColorOutput.WriteError($"{commandName}: {ex.Message}");
            return 126;
        }
    }

    /// <summary>
    /// Starts an external process with optional custom stdin/stdout/stderr streams
    /// for pipe plumbing and file redirections. Does NOT wait for exit — caller
    /// is responsible for waiting and disposal.
    /// </summary>
    /// <param name="commandName">The command name or path.</param>
    /// <param name="args">The arguments to pass.</param>
    /// <param name="context">The current execution context.</param>
    /// <param name="stdinStream">Optional stream for stdin (e.g., pipe reader).</param>
    /// <param name="stdoutStream">Optional stream for stdout (e.g., pipe writer or file).</param>
    /// <param name="stderrStream">Optional stream for stderr redirection.</param>
    /// <returns>The started <see cref="Process"/>, or null if command not found.</returns>
    public Process? StartProcess(
        string commandName,
        string[] args,
        ShellContext context,
        Stream? stdinStream = null,
        Stream? stdoutStream = null,
        Stream? stderrStream = null,
        bool wireStdoutAsync = true)
    {
        var resolved = ResolveCommand(commandName);
        if (resolved is null)
            return null;

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = resolved,
                WorkingDirectory = context.CurrentDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            // Add arguments (skip args[0] which is the command name)
            for (var i = 1; i < args.Length; i++)
            {
                startInfo.ArgumentList.Add(args[i]);
            }

            // Forward environment variables
            foreach (var name in context.ExportedVariableNames)
            {
                var value = context.GetVariable(name);
                startInfo.EnvironmentVariables[name] = value;
            }

            // Always redirect stdin so we can close it
            startInfo.RedirectStandardInput = true;

            // Configure stdout — redirect when there's a stream or we're in a pipeline
            startInfo.RedirectStandardOutput = stdoutStream is not null || stdinStream is not null;

            // Configure stderr
            startInfo.RedirectStandardError = true;

            var process = new Process { StartInfo = startInfo };

            process.Start();

            // Wire up stdin
            if (stdinStream is MemoryStream ms)
            {
                // MemoryStream data is already available — copy synchronously to avoid race conditions
                process.StandardInput.BaseStream.Write(ms.ToArray(), 0, (int)ms.Length);
                process.StandardInput.BaseStream.Flush();
                process.StandardInput.Close();
            }
            else if (stdinStream is not null)
            {
                _ = CopyStreamToWriterAsync(stdinStream, process.StandardInput.BaseStream);
            }
            else
            {
                process.StandardInput.Close();
            }

            // Wire up stdout
            if (stdoutStream is MemoryStream)
            {
                // MemoryStream — copy synchronously so all data is available when we return
                try
                {
                    process.StandardOutput.BaseStream.CopyTo(stdoutStream);
                }
                catch (IOException) { /* broken pipe */ }
                catch (ObjectDisposedException) { /* disposed */ }
            }
            else if (stdoutStream is not null)
            {
                _ = CopyReaderToStreamAsync(process.StandardOutput.BaseStream, stdoutStream);
            }
            else if (!wireStdoutAsync)
            {
                // Caller will read stdout synchronously — don't wire up async events
            }
            else
            {
                // Let stdout go to console via async events
                process.BeginOutputReadLine();
                process.OutputDataReceived += (_, e) =>
                {
                    if (e.Data is not null)
                        Console.WriteLine(e.Data);
                };
            }

            // Wire up stderr — merge with stdout stream if requested (2>&1), otherwise forward to console
            if (stderrStream is MemoryStream stderrMs)
            {
                // Merge stderr into the MemoryStream — read synchronously
                try
                {
                    process.StandardError.BaseStream.CopyTo(stderrMs);
                }
                catch (IOException) { /* broken pipe */ }
                catch (ObjectDisposedException) { /* disposed */ }
            }
            else if (stderrStream is not null)
            {
                _ = CopyReaderToStreamAsync(process.StandardError.BaseStream, stderrStream);
            }
            else
            {
                process.BeginErrorReadLine();
                process.ErrorDataReceived += (_, e) =>
                {
                    if (e.Data is not null)
                        Console.Error.WriteLine(e.Data);
                };
            }

            return process;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            var msg = ex.NativeErrorCode == 13
                ? $"{commandName}: Permission denied"
                : $"{commandName}: {ex.Message}";
            ColorOutput.WriteError(msg);
            return null;
        }
        catch (Exception ex)
        {
            ColorOutput.WriteError($"{commandName}: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Checks if a command exists (is a builtin or can be resolved on PATH).
    /// </summary>
    /// <param name="commandName">The command name to check.</param>
    /// <returns>True if the command can be resolved.</returns>
    public bool CommandExists(string commandName)
    {
        return ResolveCommand(commandName) is not null;
    }

    // ──── Internal Helpers ────

    /// <summary>
    /// Resolves a command name to a full executable path.
    /// Returns the input directly if it contains a path separator.
    /// </summary>
    internal static string? ResolveCommand(string commandName)
    {
        var executablePath = commandName.Contains('/') || commandName.Contains('\\')
            ? commandName
            : PathResolver.Resolve(commandName);

        return executablePath;
    }

    /// <summary>
    /// Builds a <see cref="ProcessStartInfo"/> for terminal-inherited execution.
    /// No streams are redirected, so the child process gets direct terminal (TTY) access.
    /// This is essential for interactive applications like btop, vim, htop, etc.
    /// </summary>
    private static ProcessStartInfo BuildTerminalStartInfo(string executablePath, string[] args, ShellContext context)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = context.CurrentDirectory,
            UseShellExecute = false,
            // Do NOT redirect any streams — child inherits the terminal directly
            RedirectStandardOutput = false,
            RedirectStandardError = false,
            RedirectStandardInput = false,
        };

        for (var i = 1; i < args.Length; i++)
        {
            startInfo.ArgumentList.Add(args[i]);
        }

        foreach (var name in context.ExportedVariableNames)
        {
            var value = context.GetVariable(name);
            startInfo.EnvironmentVariables[name] = value;
        }

        return startInfo;
    }

    /// <summary>
    /// Builds a <see cref="ProcessStartInfo"/> for captured-output execution (redirected streams).
    /// Used when output needs to be captured (e.g., command substitution).
    /// </summary>
    private static ProcessStartInfo BuildCapturedStartInfo(string executablePath, string[] args, ShellContext context)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = executablePath,
            WorkingDirectory = context.CurrentDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            CreateNoWindow = true,
        };

        for (var i = 1; i < args.Length; i++)
        {
            startInfo.ArgumentList.Add(args[i]);
        }

        foreach (var name in context.ExportedVariableNames)
        {
            var value = context.GetVariable(name);
            startInfo.EnvironmentVariables[name] = value;
        }

        return startInfo;
    }

    /// <summary>
    /// Wires up stdout/stderr forwarding for simple execution.
    /// </summary>
    private static void WireOutput(Process process)
    {
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                Console.WriteLine(e.Data);
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data is not null)
                Console.Error.WriteLine(e.Data);
        };
    }

    /// <summary>
    /// Asynchronously copies from an input stream to a process's stdin writer stream.
    /// Closes the writer when the input is exhausted.
    /// </summary>
    private static async Task CopyStreamToWriterAsync(Stream source, Stream destination)
    {
        try
        {
            await source.CopyToAsync(destination);
        }
        catch (IOException)
        {
            // Pipe was closed — ignore (broken pipe)
        }
        catch (ObjectDisposedException)
        {
            // Already disposed — ignore
        }
        finally
        {
            try { destination.Close(); } catch { /* ignored */ }
        }
    }

    /// <summary>
    /// Asynchronously copies from a process's stdout reader stream to a destination stream.
    /// </summary>
    private static async Task CopyReaderToStreamAsync(Stream source, Stream destination)
    {
        try
        {
            await source.CopyToAsync(destination);
        }
        catch (IOException)
        {
            // Pipe was closed — ignore (broken pipe)
        }
        catch (ObjectDisposedException)
        {
            // Already disposed — ignore
        }
        finally
        {
            try { destination.Close(); } catch { /* ignored */ }
        }
    }
}