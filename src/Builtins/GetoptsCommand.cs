using Radiance.Interpreter;

namespace Radiance.Builtins;

/// <summary>
/// Built-in <c>getopts</c> command — POSIX option parser for shell scripts.
/// Usage: <c>getopts optstring name [args...]</c>
/// Sets <paramref name="name"/> to the next option character, OPTARG to the option's argument,
/// and OPTIND to the next index.
/// </summary>
public sealed class GetoptsCommand : IBuiltinCommand
{
    public string Name => "getopts";

    public int Execute(string[] args, ShellContext context)
    {
        if (args.Length < 3)
        {
            Console.Error.WriteLine("radiance: getopts: usage: getopts optstring name [arg...]");
            return 2;
        }

        var optstring = args[1];
        var varName = args[2];

        // Get positional parameters or explicit args
        List<string> paramList;
        if (args.Length > 3)
        {
            paramList = args[3..].ToList();
        }
        else
        {
            paramList = context.PositionalParams.ToList();
        }

        // Read OPTIND (1-based index into paramList)
        var optindStr = context.GetVariable("OPTIND");
        if (!int.TryParse(optindStr, out var optind) || optind < 1)
            optind = 1;

        // optind is 1-based, but we use it to index into the args.
        // After processing an option, optind points to the next arg.
        if (optind > paramList.Count)
        {
            // No more arguments
            context.SetVariable(varName, "?");
            context.SetVariable("OPTIND", optind.ToString());
            return 1;
        }

        var currentArg = paramList[optind - 1];

        // Check if it's an option (starts with -)
        if (!currentArg.StartsWith('-') || currentArg.Length < 2 || currentArg == "--")
        {
            // Not an option or end of options
            context.SetVariable(varName, "?");
            if (currentArg == "--")
                context.SetVariable("OPTIND", (optind + 1).ToString());
            return 1;
        }

        var optChar = currentArg[1];

        // Check if this option requires an argument (indicated by : after the option char in optstring)
        var optIdx = optstring.IndexOf(optChar);
        if (optIdx < 0)
        {
            // Unknown option
            context.SetVariable(varName, "?");
            if (optstring.StartsWith(':'))
            {
                // Silent mode — set OPTARG to the unknown option char
                context.SetVariable("OPTARG", optChar.ToString());
            }
            else
            {
                Console.Error.WriteLine($"radiance: {varName}: illegal option -- {optChar}");
            }
            context.SetVariable("OPTIND", (optind + 1).ToString());
            return 0;
        }

        var requiresArg = optIdx + 1 < optstring.Length && optstring[optIdx + 1] == ':';

        if (requiresArg)
        {
            // Option requires an argument
            string? optarg = null;

            if (currentArg.Length > 2)
            {
                // Argument is attached: -xARG
                optarg = currentArg[2..];
                context.SetVariable("OPTIND", (optind + 1).ToString());
            }
            else if (optind < paramList.Count)
            {
                // Argument is the next parameter
                optarg = paramList[optind]; // optind is 1-based, next is optind (0-based = optind)
                context.SetVariable("OPTIND", (optind + 2).ToString());
            }
            else
            {
                // Missing argument
                if (optstring.StartsWith(':'))
                {
                    context.SetVariable(varName, ":");
                    context.SetVariable("OPTARG", optChar.ToString());
                }
                else
                {
                    context.SetVariable(varName, "?");
                    Console.Error.WriteLine($"radiance: option requires an argument -- {optChar}");
                }
                context.SetVariable("OPTIND", (optind + 1).ToString());
                return 1;
            }

            context.SetVariable(varName, optChar.ToString());
            context.SetVariable("OPTARG", optarg ?? "");
        }
        else
        {
            // Simple option (no argument)
            context.SetVariable(varName, optChar.ToString());
            context.SetVariable("OPTIND", (optind + 1).ToString());
        }

        return 0;
    }
}
