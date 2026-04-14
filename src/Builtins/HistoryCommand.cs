using Radiance.Interpreter;
using Radiance.Shell;

namespace Radiance.Builtins;

/// <summary>
/// The <c>history</c> builtin — displays or manipulates the command history.
/// Usage:
///   <c>history</c> — list all history entries
///   <c>history N</c> — list last N entries
///   <c>history -c</c> — clear all history
///   <c>history -d N</c> — delete entry at offset N
/// </summary>
public sealed class HistoryCommand : IBuiltinCommand
{
    /// <inheritdoc/>
    public string Name => "history";

    /// <summary>
    /// The history instance from the shell. Must be set before use.
    /// </summary>
    internal History? HistoryInstance { get; set; }

    /// <inheritdoc/>
    public int Execute(string[] args, ShellContext context)
    {
        if (HistoryInstance is null)
        {
            Console.Error.WriteLine("radiance: history: not available");
            return 1;
        }

        if (args.Length <= 1)
        {
            // List all entries
            PrintEntries(HistoryInstance.GetAll(), 1);
            return 0;
        }

        var flag = args[1];

        if (flag == "-c")
        {
            HistoryInstance.Clear();
            return 0;
        }

        if (flag == "-d")
        {
            if (args.Length <= 2)
            {
                Console.Error.WriteLine("radiance: history: -d: offset required");
                return 1;
            }

            if (int.TryParse(args[2], out var offset))
            {
                if (!HistoryInstance.Delete(offset))
                {
                    Console.Error.WriteLine($"radiance: history: {offset}: offset out of range");
                    return 1;
                }
                return 0;
            }

            Console.Error.WriteLine($"radiance: history: {args[2]}: numeric offset required");
            return 1;
        }

        // Numeric argument — show last N entries
        if (int.TryParse(flag, out var count))
        {
            var entries = HistoryInstance.GetAll().ToList();
            var start = Math.Max(0, entries.Count - count);
            PrintEntries(entries.Skip(start), start + 1);
            return 0;
        }

        Console.Error.WriteLine($"radiance: history: {flag}: invalid option");
        return 1;
    }

    /// <summary>
    /// Prints history entries with their indices.
    /// </summary>
    private static void PrintEntries(IEnumerable<string> entries, int startIndex)
    {
        var i = startIndex;
        foreach (var entry in entries)
        {
            Console.WriteLine($"{i,5}  {entry}");
            i++;
        }
    }
}