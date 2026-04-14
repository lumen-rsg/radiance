using Radiance.Interpreter;
using Radiance.Utils;

namespace Radiance.Builtins;

/// <summary>
/// Built-in <c>source</c> (and <c>.</c>) command — reads and executes commands
/// from a file in the current shell context. Variables, functions, and aliases
/// defined in the sourced file persist in the current session.
/// </summary>
public sealed class SourceCommand : IBuiltinCommand
{
    private readonly string _name;

    public SourceCommand()
    {
        _name = "source";
    }

    private SourceCommand(string name)
    {
        _name = name;
    }

    public string Name => _name;

    /// <summary>
    /// Also registered under the alias <c>.</c> in the builtin registry.
    /// </summary>
    public const string DotAlias = ".";

    /// <summary>
    /// Allows overriding the command name (used for registering the <c>.</c> alias).
    /// </summary>
    internal string NameOverride { set => _ = value; }

    /// <summary>
    /// Creates a <c>.</c> alias instance of this command.
    /// </summary>
    internal static SourceCommand CreateDotAlias() => new(DotAlias);

    public int Execute(string[] args, ShellContext context)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("source: filename argument required");
            return 2;
        }

        var filename = args[1];

        // Resolve relative paths against CWD
        if (!Path.IsPathRooted(filename))
        {
            filename = Path.Combine(context.CurrentDirectory, filename);
        }

        if (!File.Exists(filename))
        {
            Console.Error.WriteLine($"source: {args[1]}: No such file or directory");
            return 1;
        }

        // Collect any additional arguments to set as positional parameters during sourcing
        var sourceArgs = new List<string> { args[1] };
        for (var i = 2; i < args.Length; i++)
        {
            sourceArgs.Add(args[i]);
        }

        try
        {
            var content = File.ReadAllText(filename);

            // Skip shebang line if present
            if (content.StartsWith("#!"))
            {
                var newlineIdx = content.IndexOf('\n');
                if (newlineIdx >= 0)
                    content = content[(newlineIdx + 1)..];
                else
                    content = string.Empty;
            }

            if (string.IsNullOrWhiteSpace(content))
                return 0;

            // Use the script executor callback to execute in the current context
            if (context.ScriptExecutor is not null)
            {
                return context.ScriptExecutor(content, sourceArgs.ToArray());
            }

            // Fallback: if no executor is set, we can't source
            Console.Error.WriteLine($"source: cannot execute: no script executor available");
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"source: {args[1]}: {ex.Message}");
            return 1;
        }
    }
}