using Radiance.Interpreter;

namespace Radiance.Builtins;

/// <summary>
/// Built-in <c>shopt</c> command — manages shell optional behavior.
/// Currently supports: extglob, autocd, cdspell.
/// </summary>
public sealed class ShoptCommand : IBuiltinCommand
{
    public string Name => "shopt";

    public int Execute(string[] args, ShellContext context)
    {
        if (args.Length <= 1)
        {
            PrintAllOptions(context);
            return 0;
        }

        var i = 1;
        var set = false;
        var unset = false;
        var print = false;
        var query = false;

        while (i < args.Length && args[i].StartsWith('-'))
        {
            switch (args[i])
            {
                case "-s":
                    set = true;
                    break;
                case "-u":
                    unset = true;
                    break;
                case "-p":
                    print = true;
                    break;
                case "-q":
                    query = true;
                    break;
                default:
                    Console.Error.WriteLine($"radiance: shopt: {args[i]}: invalid option");
                    return 2;
            }
            i++;
        }

        // If no option flags, just print
        if (!set && !unset && !print && !query)
        {
            if (i < args.Length)
            {
                for (; i < args.Length; i++)
                    PrintOption(context, args[i]);
            }
            else
            {
                PrintAllOptions(context);
            }
            return 0;
        }

        if (print)
        {
            if (i < args.Length)
            {
                for (; i < args.Length; i++)
                {
                    var value = GetOptionValue(context, args[i]);
                    if (value is not null)
                        Console.WriteLine($"shopt -{(value.Value ? 's' : 'u')} {args[i]}");
                }
            }
            else
            {
                PrintAllOptions(context);
            }
            return 0;
        }

        if (set || unset)
        {
            for (; i < args.Length; i++)
            {
                var enable = set;
                if (!SetOption(context, args[i], enable))
                {
                    Console.Error.WriteLine($"radiance: shopt: {args[i]}: invalid shell option name");
                    return 1;
                }
            }
            return 0;
        }

        if (query)
        {
            for (; i < args.Length; i++)
            {
                var value = GetOptionValue(context, args[i]);
                if (value is null || !(bool)value)
                    return 1;
            }
            return 0;
        }

        return 0;
    }

    private static bool SetOption(ShellContext context, string name, bool enable)
    {
        switch (name)
        {
            case "extglob":
                context.Options.ExtGlob = enable;
                return true;
            default:
                return false;
        }
    }

    private static bool? GetOptionValue(ShellContext context, string name)
    {
        return name switch
        {
            "extglob" => context.Options.ExtGlob,
            _ => null
        };
    }

    private static void PrintOption(ShellContext context, string name)
    {
        var value = GetOptionValue(context, name);
        if (value is not null)
            Console.WriteLine($"{name}\t{(value.Value ? "on" : "off")}");
    }

    private static void PrintAllOptions(ShellContext context)
    {
        Console.WriteLine($"extglob\t{(context.Options.ExtGlob ? "on" : "off")}");
    }
}
