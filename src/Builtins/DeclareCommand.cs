using Radiance.Interpreter;

namespace Radiance.Builtins;

/// <summary>
/// Built-in <c>declare</c> and <c>typeset</c> commands — declares variables with attributes.
/// Supports flags: -r (readonly), -x (export), -i (integer), -a (array), -p (print).
/// </summary>
public sealed class DeclareCommand : IBuiltinCommand
{
    private readonly string _name;

    public string Name => _name;

    public DeclareCommand(string name = "declare")
    {
        _name = name;
    }

    public int Execute(string[] args, ShellContext context)
    {
        var setReadOnly = false;
        var setExport = false;
        var setInteger = false;
        var setArray = false;
        var printOnly = false;
        var i = 1;

        // Parse flags
        while (i < args.Length && args[i].StartsWith('-') && args[i].Length > 1)
        {
            var flag = args[i];
            if (flag == "--")
            {
                i++;
                break;
            }

            foreach (var c in flag[1..])
            {
                switch (c)
                {
                    case 'r':
                        setReadOnly = true;
                        break;
                    case 'x':
                        setExport = true;
                        break;
                    case 'i':
                        setInteger = true;
                        break;
                    case 'a':
                        setArray = true;
                        break;
                    case 'p':
                        printOnly = true;
                        break;
                    default:
                        Console.Error.WriteLine($"radiance: {Name}: -{c}: invalid option");
                        return 2;
                }
            }

            i++;
        }

        // -p: print variables with declare syntax
        if (printOnly)
        {
            if (i < args.Length)
            {
                // Print specific variables
                for (; i < args.Length; i++)
                {
                    var variable = context.GetShellVariable(args[i]);
                    if (variable is not null)
                        PrintVariable(args[i], variable);
                }
            }
            else
            {
                // Print all variables
                foreach (var name in context.ShellVariableNames.OrderBy(n => n))
                {
                    var variable = context.GetShellVariable(name);
                    if (variable is not null)
                        PrintVariable(name, variable);
                }
            }

            return 0;
        }

        // No more arguments after flags — print all if any flags were set, else do nothing
        if (i >= args.Length)
        {
            if (setReadOnly || setExport || setInteger || setArray)
            {
                // Show variables matching the given attributes
                foreach (var name in context.ShellVariableNames.OrderBy(n => n))
                {
                    var variable = context.GetShellVariable(name);
                    if (variable is null) continue;
                    if (setReadOnly && !variable.IsReadOnly) continue;
                    if (setExport && !variable.IsExported) continue;
                    if (setInteger && !variable.IsInteger) continue;
                    if (setArray && !variable.IsArray) continue;
                    PrintVariable(name, variable);
                }
            }
            return 0;
        }

        // Process variable assignments/declarations
        for (; i < args.Length; i++)
        {
            var arg = args[i];
            var eqIdx = arg.IndexOf('=');

            string name;
            string value;
            List<string>? arrayElements = null;

            if (eqIdx >= 0)
            {
                name = arg[..eqIdx];
                value = arg[(eqIdx + 1)..];
            }
            else
            {
                name = arg;
                value = context.GetVariable(name);
            }

            // Check for array assignment in remaining args: NAME=(...)
            // This is handled by the parser for inline syntax, but declare can also use:
            // declare -a ARR="(val1 val2 val3)"
            if (setArray && value.StartsWith('(') && value.EndsWith(')'))
            {
                var inner = value[1..^1].Trim();
                arrayElements = string.IsNullOrEmpty(inner)
                    ? new List<string>()
                    : inner.Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
                value = inner;
            }

            // Find existing variable or create new
            var existing = context.GetShellVariable(name);
            var variable = existing?.Clone() ?? new ShellVariable();

            variable.Value = value;
            if (arrayElements is not null)
                variable.ArrayElements = arrayElements;
            if (setReadOnly) variable.IsReadOnly = true;
            if (setExport) variable.IsExported = true;
            if (setInteger) variable.IsInteger = true;
            if (setArray && variable.ArrayElements is null)
                variable.ArrayElements = new List<string>();

            context.SetShellVariable(name, variable);
        }

        return 0;
    }

    private static void PrintVariable(string name, ShellVariable variable)
    {
        var flags = new System.Text.StringBuilder();
        if (variable.IsReadOnly) flags.Append('r');
        if (variable.IsExported) flags.Append('x');
        if (variable.IsInteger) flags.Append('i');
        if (variable.IsArray) flags.Append('a');

        var flagStr = flags.Length > 0 ? $" -{flags}" : "";
        if (variable.IsArray && variable.ArrayElements is not null)
        {
            var elements = string.Join(" ", variable.ArrayElements);
            Console.WriteLine($"declare{flagStr} {name}=({elements})");
        }
        else
        {
            Console.WriteLine($"declare{flagStr} {name}=\"{variable.Value}\"");
        }
    }
}
