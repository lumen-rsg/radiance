using System.Text;
using Radiance.Interpreter;

namespace Radiance.Builtins;

/// <summary>
/// Built-in <c>set</c> command — manages shell variables and options.
/// Without arguments, prints all shell variables.
/// With <c>-e</c>, <c>-x</c>, <c>-u</c>, etc., sets shell options.
/// With <c>+e</c>, <c>+x</c>, etc., clears shell options.
/// With <c>--</c>, sets positional parameters.
/// </summary>
public sealed class SetCommand : IBuiltinCommand
{
    public string Name => "set";

    public int Execute(string[] args, ShellContext context)
    {
        if (args.Length <= 1)
        {
            // Print all shell variables
            foreach (var name in context.ShellVariableNames.OrderBy(n => n))
            {
                var value = context.GetVariable(name);
                Console.WriteLine($"{name}={value}");
            }

            return 0;
        }

        for (var i = 1; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg == "--")
            {
                // Set positional parameters to remaining args
                var newParams = new List<string>();
                for (var j = i + 1; j < args.Length; j++)
                    newParams.Add(args[j]);
                context.SetPositionalParams(newParams);
                return 0;
            }

            if (arg == "-o")
            {
                // set -o option_name
                if (i + 1 >= args.Length)
                {
                    PrintOptions(context);
                    return 0;
                }
                i++;
                if (!SetNamedOption(context, args[i], true))
                {
                    Console.Error.WriteLine($"radiance: set: -o: {args[i]}: unknown option");
                    return 1;
                }
                continue;
            }

            if (arg == "+o")
            {
                // set +o option_name
                if (i + 1 >= args.Length)
                {
                    PrintOptionsResetCommands(context);
                    return 0;
                }
                i++;
                if (!SetNamedOption(context, args[i], false))
                {
                    Console.Error.WriteLine($"radiance: set: +o: {args[i]}: unknown option");
                    return 1;
                }
                continue;
            }

            // Handle flag arguments: -exu, +exu, etc.
            if (arg.Length > 1 && (arg[0] == '-' || arg[0] == '+'))
            {
                var enable = arg[0] == '-';
                var flags = arg[1..];

                foreach (var c in flags)
                {
                    if (!SetFlag(context, c, enable))
                    {
                        Console.Error.WriteLine($"radiance: set: {arg[0]}{c}: invalid option");
                        return 1;
                    }
                }
                continue;
            }

            // Unknown argument
            Console.Error.WriteLine($"radiance: set: {arg}: invalid argument");
            return 1;
        }

        return 0;
    }

    private static bool SetFlag(ShellContext context, char flag, bool enable)
    {
        switch (flag)
        {
            case 'e':
                context.Options.ExitOnError = enable;
                return true;
            case 'x':
                context.Options.TraceCommands = enable;
                return true;
            case 'u':
                context.Options.ErrorOnUnset = enable;
                return true;
            case 'n':
                context.Options.NoExecute = enable;
                return true;
            case 'f':
                context.Options.NoGlob = enable;
                return true;
            case 'v':
                context.Options.Verbose = enable;
                return true;
            default:
                return false;
        }
    }

    private static bool SetNamedOption(ShellContext context, string name, bool enable)
    {
        switch (name)
        {
            case "errexit":
                context.Options.ExitOnError = enable;
                return true;
            case "xtrace":
                context.Options.TraceCommands = enable;
                return true;
            case "nounset":
                context.Options.ErrorOnUnset = enable;
                return true;
            case "noexec":
                context.Options.NoExecute = enable;
                return true;
            case "noglob":
                context.Options.NoGlob = enable;
                return true;
            case "verbose":
                context.Options.Verbose = enable;
                return true;
            default:
                return false;
        }
    }

    private static void PrintOptions(ShellContext context)
    {
        var options = context.Options;
        Console.WriteLine($"errexit    {(options.ExitOnError ? "on" : "off")}");
        Console.WriteLine($"xtrace     {(options.TraceCommands ? "on" : "off")}");
        Console.WriteLine($"nounset    {(options.ErrorOnUnset ? "on" : "off")}");
        Console.WriteLine($"noexec     {(options.NoExecute ? "on" : "off")}");
        Console.WriteLine($"noglob     {(options.NoGlob ? "on" : "off")}");
        Console.WriteLine($"verbose    {(options.Verbose ? "on" : "off")}");
    }

    private static void PrintOptionsResetCommands(ShellContext context)
    {
        var options = context.Options;
        Console.WriteLine($"set {(options.ExitOnError ? "-o" : "+o")} errexit");
        Console.WriteLine($"set {(options.TraceCommands ? "-o" : "+o")} xtrace");
        Console.WriteLine($"set {(options.ErrorOnUnset ? "-o" : "+o")} nounset");
        Console.WriteLine($"set {(options.NoExecute ? "-o" : "+o")} noexec");
        Console.WriteLine($"set {(options.NoGlob ? "-o" : "+o")} noglob");
        Console.WriteLine($"set {(options.Verbose ? "-o" : "+o")} verbose");
    }
}
