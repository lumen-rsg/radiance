using Radiance.Interpreter;

namespace Radiance.Builtins;

/// <summary>
/// Built-in <c>eval</c> command — evaluates arguments as a shell command.
/// Concatenates all arguments with spaces, then runs the result through
/// the full lex → parse → interpret pipeline.
/// </summary>
public sealed class EvalCommand : IBuiltinCommand
{
    public string Name => "eval";

    public int Execute(string[] args, ShellContext context)
    {
        if (args.Length <= 1)
            return 0;

        // Concatenate all arguments with spaces (BASH behavior)
        var command = string.Join(" ", args[1..]);

        if (string.IsNullOrWhiteSpace(command))
            return 0;

        if (context.CommandLineExecutor is null)
        {
            Console.Error.WriteLine("radiance: eval: no command executor available");
            return 1;
        }

        return context.CommandLineExecutor(command);
    }
}
