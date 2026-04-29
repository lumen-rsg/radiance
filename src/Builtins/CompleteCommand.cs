using Radiance.Interpreter;

namespace Radiance.Builtins;

/// <summary>
/// Built-in <c>complete</c> command — manages programmable tab completion.
/// Usage:
///   <c>complete -c command</c> — complete with commands
///   <c>complete -f command</c> — complete with filenames
///   <c>complete -d command</c> — complete with directories
///   <c>complete -F funcname command</c> — complete via function
///   <c>complete -W "word1 word2" command</c> — complete from word list
///   <c>complete -g "pattern" command</c> — complete from glob pattern
///   <c>complete -p [command]</c> — print completions
///   <c>complete -r command</c> — remove completion
/// </summary>
public sealed class CompleteCommand : IBuiltinCommand
{
    public string Name => "complete";

    public int Execute(string[] args, ShellContext context)
    {
        if (args.Length <= 1)
        {
            PrintAllCompletions(context);
            return 0;
        }

        var i = 1;
        CompletionType type = CompletionType.Default;
        string? functionName = null;
        List<string>? wordList = null;
        string? globPattern = null;
        var print = false;
        var remove = false;

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
                    case 'c':
                        type = CompletionType.Commands;
                        break;
                    case 'f':
                        type = CompletionType.Files;
                        break;
                    case 'd':
                        type = CompletionType.Directories;
                        break;
                    case 'F':
                        // Next arg is function name
                        if (i + 1 < args.Length)
                        {
                            i++;
                            functionName = args[i];
                            type = CompletionType.Function;
                        }
                        break;
                    case 'W':
                        // Next arg is word list
                        if (i + 1 < args.Length)
                        {
                            i++;
                            wordList = args[i].Split(' ', StringSplitOptions.RemoveEmptyEntries).ToList();
                            type = CompletionType.Words;
                        }
                        break;
                    case 'g':
                        // Next arg is glob pattern
                        if (i + 1 < args.Length)
                        {
                            i++;
                            globPattern = args[i];
                            type = CompletionType.GlobPattern;
                        }
                        break;
                    case 'p':
                        print = true;
                        break;
                    case 'r':
                        remove = true;
                        break;
                    default:
                        Console.Error.WriteLine($"radiance: complete: -{c}: invalid option");
                        return 2;
                }
            }

            i++;
        }

        if (print)
        {
            if (i < args.Length)
            {
                foreach (var name in args[i..])
                {
                    var spec = context.GetCompletion(name);
                    if (spec is not null)
                        PrintCompletion(spec);
                }
            }
            else
            {
                PrintAllCompletions(context);
            }
            return 0;
        }

        if (remove)
        {
            for (; i < args.Length; i++)
                context.RemoveCompletion(args[i]);
            return 0;
        }

        // Register completion for each command name
        for (; i < args.Length; i++)
        {
            var spec = new CompletionSpec
            {
                CommandName = args[i],
                Type = type,
                FunctionName = functionName,
                WordList = wordList,
                GlobPattern = globPattern
            };
            context.RegisterCompletion(args[i], spec);
        }

        return 0;
    }

    private static void PrintCompletion(CompletionSpec spec)
    {
        var typeFlag = spec.Type switch
        {
            CompletionType.Commands => "-c",
            CompletionType.Files => "-f",
            CompletionType.Directories => "-d",
            CompletionType.Function => $"-F {spec.FunctionName}",
            CompletionType.Words => $"-W \"{string.Join(" ", spec.WordList ?? [])}\"",
            CompletionType.GlobPattern => $"-g \"{spec.GlobPattern}\"",
            _ => ""
        };
        Console.WriteLine($"complete {typeFlag} {spec.CommandName}");
    }

    private static void PrintAllCompletions(ShellContext context)
    {
        foreach (var kvp in context.AllCompletions)
            PrintCompletion(kvp.Value);
    }
}
