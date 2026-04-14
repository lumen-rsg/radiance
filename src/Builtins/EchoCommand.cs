using Radiance.Interpreter;

namespace Radiance.Builtins;

/// <summary>
/// Built-in <c>echo</c> command — prints arguments to stdout.
/// Supports <c>-n</c> (no trailing newline) and <c>-e</c> (interpret escape sequences).
/// </summary>
public sealed class EchoCommand : IBuiltinCommand
{
    public string Name => "echo";

    public int Execute(string[] args, ShellContext context)
    {
        if (args.Length <= 1)
        {
            Console.WriteLine();
            return 0;
        }

        var noNewline = false;
        var interpretEscapes = false;
        var startIdx = 1;

        // Parse flags
        while (startIdx < args.Length && args[startIdx].StartsWith('-') && args[startIdx].Length > 1)
        {
            var flag = args[startIdx];
            var isValidFlag = true;

            foreach (var c in flag[1..])
            {
                switch (c)
                {
                    case 'n':
                        noNewline = true;
                        break;
                    case 'e':
                        interpretEscapes = true;
                        break;
                    case 'E':
                        interpretEscapes = false;
                        break;
                    default:
                        isValidFlag = false;
                        break;
                }
            }

            if (!isValidFlag) break;
            startIdx++;
        }

        var parts = args[startIdx..];

        for (var i = 0; i < parts.Length; i++)
        {
            var text = interpretEscapes ? InterpretEscapes(parts[i]) : parts[i];
            Console.Write(text);
            if (i < parts.Length - 1)
                Console.Write(' ');
        }

        if (!noNewline)
            Console.WriteLine();

        return 0;
    }

    private static string InterpretEscapes(string input)
    {
        return input.Replace("\\n", "\n")
            .Replace("\\t", "\t")
            .Replace("\\r", "\r")
            .Replace("\\a", "\a")
            .Replace("\\b", "\b")
            .Replace("\\f", "\f")
            .Replace("\\v", "\v")
            .Replace("\\\\", "\\");
    }
}