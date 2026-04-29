using System.Diagnostics;

namespace Radiance.Interpreter;

/// <summary>
/// Manages signal handling for the shell: SIGINT forwarding to child processes,
/// and trap registration for user-defined signal handlers.
/// <para>
/// On Unix, uses <c>PosixSignalRegistration</c> for native signal handling.
/// On all platforms, uses <c>Console.CancelKeyPress</c> for Ctrl+C handling.
/// </para>
/// </summary>
public sealed class SignalHandler
{
    private Process? _foregroundProcess;
    private readonly Dictionary<string, string> _traps = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The shell context for executing trap actions.
    /// </summary>
    public ShellContext? Context { get; set; }

    /// <summary>
    /// Callback for executing a command string (used to run trap actions).
    /// </summary>
    public Func<string, int>? CommandExecutor { get; set; }

    /// <summary>
    /// Sets or removes a trap for the given signal.
    /// </summary>
    /// <param name="signal">The signal name (e.g., "INT", "TERM", "EXIT").</param>
    /// <param name="action">The command to run, or null/empty to reset the trap.</param>
    public void SetTrap(string signal, string? action)
    {
        var normalizedName = NormalizeSignalName(signal);

        if (string.IsNullOrEmpty(action))
        {
            _traps.Remove(normalizedName);
        }
        else
        {
            _traps[normalizedName] = action;
        }
    }

    /// <summary>
    /// Gets all registered traps.
    /// </summary>
    public IReadOnlyDictionary<string, string> Traps => _traps;

    /// <summary>
    /// Sets the current foreground child process.
    /// SIGINT will be forwarded to this process.
    /// Set to null when no child process is running in the foreground.
    /// </summary>
    public Process? ForegroundProcess
    {
        get => _foregroundProcess;
        set => _foregroundProcess = value;
    }

    /// <summary>
    /// Handles a SIGINT signal (Ctrl+C).
    /// Forwards the signal to the foreground child process if one is running.
    /// If no foreground process, the signal is handled by the shell (cancel current input).
    /// </summary>
    /// <returns>True if the signal was forwarded to a child process.</returns>
    public bool HandleSigInt()
    {
        // Check if there's a trap for SIGINT
        if (_traps.TryGetValue("SIGINT", out var trapAction) && CommandExecutor is not null)
        {
            _ = CommandExecutor(trapAction);
            return true;
        }

        // Forward SIGINT to the foreground child process
        if (_foregroundProcess is not null && !_foregroundProcess.HasExited)
        {
            try
            {
                _foregroundProcess.Kill();
            }
            catch (InvalidOperationException) { /* process already exited */ }
            catch (System.ComponentModel.Win32Exception) { /* access denied */ }

            return true;
        }

        return false;
    }

    /// <summary>
    /// Handles a SIGTERM signal.
    /// Runs the trap action if registered, otherwise exits the shell.
    /// </summary>
    public void HandleSigTerm()
    {
        if (_traps.TryGetValue("SIGTERM", out var trapAction) && CommandExecutor is not null)
        {
            _ = CommandExecutor(trapAction);
            return;
        }

        // Run EXIT trap if registered
        RunExitTrap();
    }

    /// <summary>
    /// Runs the EXIT trap action if one is registered.
    /// Called when the shell is about to exit.
    /// </summary>
    public void RunExitTrap()
    {
        if (_traps.TryGetValue("EXIT", out var trapAction) && CommandExecutor is not null)
        {
            try
            {
                _ = CommandExecutor(trapAction);
            }
            catch
            {
                // Trap errors don't prevent exit
            }
        }
    }

    /// <summary>
    /// Lists all registered traps for display.
    /// </summary>
    public IEnumerable<(string signal, string action)> ListTraps()
    {
        foreach (var kvp in _traps)
        {
            yield return (kvp.Key, kvp.Value);
        }
    }

    /// <summary>
    /// Normalizes a signal name: adds "SIG" prefix if missing, uppercases.
    /// Examples: "INT" → "SIGINT", "SIGTERM" → "SIGTERM", "exit" → "EXIT".
    /// </summary>
    private static string NormalizeSignalName(string signal)
    {
        var upper = signal.ToUpperInvariant();

        // Special case: EXIT is not a real signal but a pseudo-signal
        if (upper == "EXIT" || upper == "SIGEXIT")
            return "EXIT";

        if (!upper.StartsWith("SIG"))
            upper = "SIG" + upper;

        return upper;
    }
}
