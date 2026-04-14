using Radiance.Interpreter;

namespace Radiance.Builtins;

/// <summary>
/// Built-in <c>cd</c> command — changes the current working directory.
/// Supports <c>cd ~</c>, <c>cd -</c>, and relative/absolute paths.
/// </summary>
public sealed class CdCommand : IBuiltinCommand
{
    public string Name => "cd";

    public int Execute(string[] args, ShellContext context)
    {
        var target = args.Length > 1 ? args[1] : null;

        // No argument: go to HOME
        if (string.IsNullOrEmpty(target) || target == "~")
        {
            var home = context.GetVariable("HOME");
            if (string.IsNullOrEmpty(home))
            {
                Console.Error.WriteLine("cd: HOME not set");
                return 1;
            }

            target = home;
        }

        // cd - : go to previous directory (OLDPWD)
        if (target == "-")
        {
            var oldPwd = context.GetVariable("OLDPWD");
            if (string.IsNullOrEmpty(oldPwd))
            {
                Console.Error.WriteLine("cd: OLDPWD not set");
                return 1;
            }

            target = oldPwd;
            Console.WriteLine(target);
        }

        // Handle tilde expansion
        if (target.StartsWith("~/"))
        {
            var home = context.GetVariable("HOME");
            target = Path.Combine(home, target[2..]);
        }

        // Resolve relative paths against the current directory
        var resolvedPath = Path.IsPathRooted(target)
            ? target
            : Path.GetFullPath(Path.Combine(context.CurrentDirectory, target));

        if (!Directory.Exists(resolvedPath))
        {
            Console.Error.WriteLine($"cd: no such file or directory: {target}");
            return 1;
        }

        // Update OLDPWD and PWD
        var oldDirectory = context.CurrentDirectory;
        context.SetVariable("OLDPWD", oldDirectory);
        context.CurrentDirectory = resolvedPath;
        context.SetVariable("PWD", resolvedPath);

        return 0;
    }
}