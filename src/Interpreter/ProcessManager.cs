using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using Radiance.Interop;
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
    /// Optional signal handler for forwarding SIGINT to child processes.
    /// Set by the shell during initialization.
    /// </summary>
    public SignalHandler? SignalHandler { get; set; }
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
            // Smart PTY detection: when output is captured but the command is known
            // to be interactive (vim, ssh, sudo, etc.), allocate a PTY so isatty()
            // returns true for the child process.
            if (Console.IsOutputRedirected || OutputCapture.IsCapturing)
            {
                // Smart detection: use PTY for known interactive commands in captured context
                if (NeedsPty(commandName))
                {
                    return ExecuteWithPty(resolved, args, context);
                }

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

                // Set as foreground process for SIGINT forwarding
                SignalHandler?.ForegroundProcess = process;
                try
                {
                    process.Start();
                    process.WaitForExit();
                    return process.ExitCode;
                }
                finally
                {
                    if (SignalHandler is not null)
                        SignalHandler.ForegroundProcess = null;
                }
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

    /// <summary>
    /// Starts an external process for use in a concurrent pipeline.
    /// Unlike StartProcess(), this always uses async CopyToAsync for stream
    /// bridging and does NOT wait for the process to exit.
    /// The caller is responsible for WaitForExit() and disposal.
    /// Returns the Process and any stdout/stderr copy tasks that must be
    /// awaited after WaitForExit().
    /// </summary>
    public (Process? process, Task? stdoutTask, Task? stderrTask) StartConcurrent(
        string commandName,
        string[] args,
        ShellContext context,
        Stream? stdinSource,
        Stream? stdoutSink,
        Stream? stderrSink)
    {
        var resolved = ResolveCommand(commandName);
        if (resolved is null)
            return (null, null, null);

        try
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = resolved,
                WorkingDirectory = context.CurrentDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = stdinSource is not null,
                RedirectStandardOutput = stdoutSink is not null,
                RedirectStandardError = stderrSink is not null,
            };

            for (var i = 1; i < args.Length; i++)
                startInfo.ArgumentList.Add(args[i]);

            foreach (var name in context.ExportedVariableNames)
            {
                var value = context.GetVariable(name);
                startInfo.EnvironmentVariables[name] = value;
            }

            var process = new Process { StartInfo = startInfo };
            Task? stdoutTask = null;
            Task? stderrTask = null;

            process.Start();

            // Wire stdin — async copy from source to process stdin
            if (stdinSource is not null)
            {
                _ = CopyAndCloseAsync(stdinSource, process.StandardInput.BaseStream);
            }

            // Wire stdout — async copy from process stdout to sink
            if (stdoutSink is not null)
            {
                stdoutTask = CopyReaderToStreamAsync(process.StandardOutput.BaseStream, stdoutSink);
            }

            // Wire stderr — async copy from process stderr to sink
            if (stderrSink is not null)
            {
                stderrTask = CopyReaderToStreamAsync(process.StandardError.BaseStream, stderrSink);
            }

            return (process, stdoutTask, stderrTask);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            var msg = ex.NativeErrorCode == 13
                ? $"{commandName}: Permission denied"
                : $"{commandName}: {ex.Message}";
            ColorOutput.WriteError(msg);
            return (null, null, null);
        }
        catch (Exception ex)
        {
            ColorOutput.WriteError($"{commandName}: {ex.Message}");
            return (null, null, null);
        }
    }

    /// <summary>
    /// Determines whether a command should be executed with PTY allocation.
    /// Uses smart detection: known interactive programs get a PTY when their
    /// stdout would otherwise be a pipe (captured context), so isatty() returns true.
    /// When stdout is a real terminal, the existing terminal-inherited mode suffices.
    /// </summary>
    private static bool NeedsPty(string commandName)
    {
        // Only allocate PTY when output is being captured (inside pipeline/substitution)
        // AND the command is known to need a TTY
        if (!Console.IsOutputRedirected && !OutputCapture.IsCapturing)
            return false;

        // Known interactive commands that require a TTY
        return IsInteractiveCommand(commandName);
    }

    /// <summary>
    /// Checks if a command is known to be interactive (needs a TTY).
    /// </summary>
    internal static bool IsInteractiveCommand(string commandName)
    {
        var name = Path.GetFileName(commandName);
        return name is
            "vim" or "vi" or "nano" or "emacs" or
            "ssh" or "sudo" or "su" or
            "htop" or "btop" or "top" or
            "less" or "more" or "most" or
            "man" or
            "tmux" or "screen" or
            "gdb" or "lldb" or
            "ftp" or "sftp" or "telnet" or
            "mc" or "ranger" or
            "tui" or "dialog" or "whiptail";
    }

    /// <summary>
    /// Executes an external command with PTY allocation using posix_spawn.
    /// The child process gets a real PTY slave as its stdin/stdout/stderr,
    /// making isatty() return true. The parent relays data between the
    /// master PTY fd and the console.
    /// </summary>
    private int ExecuteWithPty(string resolved, string[] args, ShellContext context)
    {
        using var pty = Terminal.PtyAllocation.Create();
        if (pty is null)
        {
            ColorOutput.WriteError("radiance: failed to allocate PTY");
            return 126;
        }

        // Set window size from current terminal
        var ws = new Winsize();
        if (PosixPty.ioctl(0, PosixPty.TIOCGWINSZ, ref ws) == 0)
            PosixPty.ioctl(pty.SlaveFd, PosixPty.TIOCSWINSZ, ref ws);

        // Build argv (null-terminated)
        var argv = new string[args.Length + 1];
        argv[0] = resolved;
        for (var i = 1; i < args.Length; i++)
            argv[i] = args[i];
        argv[args.Length] = null!;

        // Build envp from context
        var envp = BuildEnvp(context);

        // Set up file actions: dup2 slave to 0/1/2, close both fds in child
        PosixSpawn.posix_spawn_file_actions_init(out var actions);
        PosixSpawn.posix_spawn_file_actions_adddup2(actions, pty.SlaveFd, 0);
        PosixSpawn.posix_spawn_file_actions_adddup2(actions, pty.SlaveFd, 1);
        PosixSpawn.posix_spawn_file_actions_adddup2(actions, pty.SlaveFd, 2);
        PosixSpawn.posix_spawn_file_actions_addclose(actions, pty.SlaveFd);
        PosixSpawn.posix_spawn_file_actions_addclose(actions, pty.MasterFd);

        var rc = PosixSpawn.posix_spawnp(out int pid, resolved, actions, IntPtr.Zero, argv, envp);
        PosixSpawn.posix_spawn_file_actions_destroy(actions);

        if (rc != 0)
        {
            ColorOutput.WriteError($"radiance: posix_spawn failed: {StrError(rc)}");
            return 126;
        }

        // Close slave fd in parent — child has its own copy via dup2
        PosixPty.close(pty.SlaveFd);

        // Create master stream for I/O relay
        using var masterStream = pty.CreateMasterStream();

        // Relay: master → Console.Out, Console.In → master
        var readTask = Task.Run(() => RelayMasterToConsole(masterStream));
        var writeTask = Task.Run(() => RelayConsoleToMaster(masterStream));

        // Set as foreground process for signal forwarding
        SignalHandler?.GetType().GetProperty("ForegroundProcess")?.SetValue(SignalHandler, null);

        // Wait for child to exit
        PosixSpawn.waitpid(pid, out int status, 0);

        // Close master to stop relay threads
        masterStream.Close();

        try { Task.WaitAll(readTask, writeTask); } catch { }

        // Extract exit code from wait status
        if ((status & 0x7f) == 0)
            return (status >> 8) & 0xff;
        return 128 + (status & 0x7f);
    }

    /// <summary>
    /// Builds a null-terminated envp array from the shell context's exported variables.
    /// </summary>
    private static string[] BuildEnvp(ShellContext context)
    {
        var env = new List<string>();
        foreach (var name in context.ExportedVariableNames)
        {
            var value = context.GetVariable(name);
            env.Add($"{name}={value}");
        }

        // Inherit current process environment for anything not explicitly exported
        foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            var key = (string)entry.Key!;
            if (!context.ExportedVariableNames.Contains(key))
                env.Add($"{key}={entry.Value}");
        }

        var envp = new string[env.Count + 1];
        for (var i = 0; i < env.Count; i++)
            envp[i] = env[i];
        envp[env.Count] = null!;
        return envp;
    }

    /// <summary>
    /// Relays data from the PTY master to Console.Out (child → terminal).
    /// </summary>
    private static void RelayMasterToConsole(Stream masterStream)
    {
        var buffer = new byte[4096];
        try
        {
            while (true)
            {
                int n = masterStream.Read(buffer, 0, buffer.Length);
                if (n <= 0) break;
                Console.Out.Write(System.Text.Encoding.UTF8.GetString(buffer, 0, n));
                Console.Out.Flush();
            }
        }
        catch (IOException) { /* master closed */ }
        catch (ObjectDisposedException) { /* disposed */ }
    }

    /// <summary>
    /// Relays data from Console.In to the PTY master (terminal → child).
    /// </summary>
    private static void RelayConsoleToMaster(Stream masterStream)
    {
        try
        {
            while (true)
            {
                int ch = Console.In.Read();
                if (ch < 0) break;
                var buf = System.Text.Encoding.UTF8.GetBytes([(char)ch]);
                masterStream.Write(buf, 0, buf.Length);
                masterStream.Flush();
            }
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
    }

    /// <summary>
    /// Returns a string description for a POSIX errno value.
    /// </summary>
    private static string StrError(int errno)
    {
        return errno switch
        {
            2 => "No such file or directory",
            13 => "Permission denied",
            22 => "Invalid argument",
            _ => $"error {errno}"
        };
    }

    /// <summary>
    /// Asynchronously copies from source to destination, then closes the destination.
    /// </summary>
    private static async Task CopyAndCloseAsync(Stream source, Stream destination)
    {
        try
        {
            await source.CopyToAsync(destination);
        }
        catch (IOException) { /* broken pipe */ }
        catch (ObjectDisposedException) { /* disposed */ }
        finally
        {
            try { destination.Close(); } catch { }
        }
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