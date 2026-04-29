using System.Text;
using Radiance.Interpreter;

namespace Radiance.Builtins;

/// <summary>
/// Built-in <c>trap</c> command — manages signal handlers.
/// <list type="bullet">
/// <item><c>trap 'command' SIGNAL</c> — set a trap</item>
/// <item><c>trap SIGNAL</c> — reset a trap to default</item>
/// <item><c>trap -p</c> — print all traps</item>
/// <item><c>trap -p SIGNAL</c> — print a specific trap</item>
/// <item><c>trap -l</c> — list signal names</item>
/// </list>
/// </summary>
public sealed class TrapCommand : IBuiltinCommand
{
    public string Name => "trap";

    /// <summary>
    /// The signal handler instance, set by the shell during initialization.
    /// </summary>
    public SignalHandler? SignalHandler { get; set; }

    public int Execute(string[] args, ShellContext context)
    {
        if (SignalHandler is null)
            return 0;

        if (args.Length <= 1)
        {
            PrintTraps();
            return 0;
        }

        var i = 1;

        // Handle flags
        if (args[i] == "-l")
        {
            ListSignals();
            return 0;
        }

        if (args[i] == "-p")
        {
            if (args.Length > 2)
            {
                // Print specific trap
                for (var j = 2; j < args.Length; j++)
                {
                    var trapAction = GetTrapAction(args[j]);
                    if (trapAction is not null)
                        Console.WriteLine($"trap -- '{trapAction}' {args[j]}");
                }
            }
            else
            {
                PrintTraps();
            }
            return 0;
        }

        // trap 'command' SIGNAL [SIGNAL...]
        // trap SIGNAL (reset to default)
        var action = args[i];

        // If the first arg looks like a signal name (no spaces, starts with SIG or is a number),
        // treat it as a signal to reset
        if (IsSignalName(action) && args.Length == 2)
        {
            SignalHandler.SetTrap(action, null);
            return 0;
        }

        // Otherwise, it's trap 'command' SIGNAL...
        i++;
        if (i >= args.Length)
        {
            Console.Error.WriteLine("radiance: trap: missing signal specification");
            return 1;
        }

        for (; i < args.Length; i++)
        {
            SignalHandler.SetTrap(args[i], action);
        }

        return 0;
    }

    private string? GetTrapAction(string signal)
    {
        var normalizedName = NormalizeSignalName(signal);
        foreach (var (sig, action) in SignalHandler!.ListTraps())
        {
            if (sig == normalizedName)
                return action;
        }
        return null;
    }

    private void PrintTraps()
    {
        foreach (var (signal, action) in SignalHandler!.ListTraps())
        {
            Console.WriteLine($"trap -- '{action}' {signal}");
        }
    }

    private static void ListSignals()
    {
        var signals = new[]
        {
            "EXIT", "HUP", "INT", "QUIT", "ILL", "TRAP", "ABRT", "BUS",
            "FPE", "KILL", "USR1", "SEGV", "USR2", "PIPE", "ALRM", "TERM",
            "STKFLT", "CHLD", "CONT", "STOP", "TSTP", "TTIN", "TTOU", "URG",
            "XCPU", "XFSZ", "VTALRM", "PROF", "WINCH", "IO", "PWR", "SYS"
        };

        for (var i = 0; i < signals.Length; i++)
        {
            Console.Write($"{signals[i],-10}");
            if ((i + 1) % 6 == 0)
                Console.WriteLine();
        }
        if (signals.Length % 6 != 0)
            Console.WriteLine();
    }

    private static bool IsSignalName(string s)
    {
        if (int.TryParse(s, out _))
            return true;
        var upper = s.ToUpperInvariant();
        return upper.StartsWith("SIG") || upper == "EXIT" || upper == "HUP"
            || upper == "INT" || upper == "TERM" || upper == "QUIT"
            || upper == "KILL" || upper == "USR1" || upper == "USR2"
            || upper == "CHLD" || upper == "TSTP" || upper == "CONT";
    }

    private static string NormalizeSignalName(string signal)
    {
        var upper = signal.ToUpperInvariant();
        if (upper == "EXIT" || upper == "SIGEXIT")
            return "EXIT";
        if (!upper.StartsWith("SIG"))
            upper = "SIG" + upper;
        return upper;
    }
}
