using Radiance.Interpreter;

namespace Radiance.Builtins;

/// <summary>
/// Built-in <c>read</c> command — reads a line from standard input and assigns
/// it to one or more shell variables. Supports prompts, silent mode, and IFS splitting.
/// </summary>
public sealed class ReadCommand : IBuiltinCommand
{
    public string Name => "read";

    public int Execute(string[] args, ShellContext context)
    {
        var prompt = string.Empty;
        var silent = false;
        var varNames = new List<string>();
        var i = 1;

        // Parse flags
        while (i < args.Length && args[i].StartsWith('-') && args[i].Length > 1)
        {
            switch (args[i])
            {
                case "-p":
                    if (i + 1 < args.Length)
                    {
                        prompt = args[i + 1];
                        i++;
                    }
                    else
                    {
                        Console.Error.WriteLine("read: -p: option requires an argument");
                        return 2;
                    }
                    break;
                case "-s":
                    silent = true;
                    break;
                case "-r":
                    // Raw mode — don't handle backslashes (accept for compatibility)
                    break;
                case "--":
                    i++;
                    goto doneFlags;
                default:
                    // Unknown flag — treat remaining as variable names
                    goto doneFlags;
            }
            i++;
        }

        doneFlags:

        // Collect variable names
        while (i < args.Length)
        {
            varNames.Add(args[i]);
            i++;
        }

        // Default variable is REPLY
        if (varNames.Count == 0)
        {
            varNames.Add("REPLY");
        }

        // Display prompt if specified
        if (!string.IsNullOrEmpty(prompt))
        {
            Console.Write(prompt);
        }

        // Read input
        string? input;
        if (silent)
        {
            // Silent mode — don't echo characters
            var sb = new System.Text.StringBuilder();
            while (true)
            {
                var key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Enter)
                {
                    Console.WriteLine();
                    break;
                }
                if (key.Key == ConsoleKey.Backspace && sb.Length > 0)
                {
                    sb.Remove(sb.Length - 1, 1);
                }
                else if (key.Key == ConsoleKey.C && key.Modifiers.HasFlag(ConsoleModifiers.Control))
                {
                    Console.WriteLine("^C");
                    return 1;
                }
                else if (key.Key == ConsoleKey.D && key.Modifiers.HasFlag(ConsoleModifiers.Control))
                {
                    Console.WriteLine();
                    return 1;
                }
                else if (!char.IsControl(key.KeyChar))
                {
                    sb.Append(key.KeyChar);
                }
            }
            input = sb.ToString();
        }
        else
        {
            input = Console.ReadLine();
        }

        if (input is null)
        {
            // EOF
            return 1;
        }

        // Split input by IFS (default: whitespace)
        var ifs = context.GetVariable("IFS");
        if (string.IsNullOrEmpty(ifs))
        {
            ifs = " \t\n";
        }

        string[] parts;

        if (varNames.Count == 1)
        {
            // Single variable — assign the entire line
            parts = [input.TrimEnd()];
        }
        else
        {
            // Multiple variables — split by IFS
            parts = SplitByIfs(input, ifs, varNames.Count);
        }

        // Assign to variables
        for (var v = 0; v < varNames.Count; v++)
        {
            var value = v < parts.Length ? parts[v] : string.Empty;
            context.SetVariable(varNames[v], value);
        }

        return 0;
    }

    /// <summary>
    /// Splits input by IFS characters. The last variable gets the remainder
    /// of the input (like BASH behavior).
    /// </summary>
    private static string[] SplitByIfs(string input, string ifs, int maxParts)
    {
        var result = new List<string>();
        var current = new System.Text.StringBuilder();
        var ifsChars = ifs.ToCharArray();
        var inField = false;

        for (var i = 0; i < input.Length; i++)
        {
            var c = input[i];

            if (ifsChars.Contains(c))
            {
                if (inField)
                {
                    result.Add(current.ToString());
                    current.Clear();
                    inField = false;

                    if (result.Count >= maxParts - 1)
                    {
                        // Last part gets the remainder (trimmed trailing whitespace)
                        var remainder = input[(i + 1)..].TrimEnd(ifsChars);
                        result.Add(remainder);
                        return result.ToArray();
                    }
                }
                // Skip leading/consecutive IFS chars
            }
            else
            {
                current.Append(c);
                inField = true;
            }
        }

        // Add remaining field
        result.Add(current.ToString());

        return result.ToArray();
    }
}