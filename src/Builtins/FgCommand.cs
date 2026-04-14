using Radiance.Interpreter;

namespace Radiance.Builtins;

/// <summary>
/// The <c>fg</c> builtin — brings a background job to the foreground.
/// Usage: <c>fg [%n]</c> — if no job number specified, uses the current job.
/// </summary>
public sealed class FgCommand : IBuiltinCommand
{
    /// <inheritdoc/>
    public string Name => "fg";

    /// <inheritdoc/>
    public int Execute(string[] args, ShellContext context)
    {
        Job? job;

        if (args.Length > 1)
        {
            var arg = args[1];
            if (arg.StartsWith('%') && int.TryParse(arg[1..], out var jobNum))
            {
                job = context.JobManager.GetJob(jobNum);
            }
            else if (int.TryParse(arg, out jobNum))
            {
                job = context.JobManager.GetJob(jobNum);
            }
            else
            {
                Console.Error.WriteLine($"radiance: fg: {arg}: no such job");
                return 1;
            }
        }
        else
        {
            job = context.JobManager.GetCurrentJob();
        }

        if (job is null)
        {
            Console.Error.WriteLine("radiance: fg: current: no such job");
            return 1;
        }

        Console.WriteLine(job.CommandText);

        if (job.Process is null)
        {
            Console.Error.WriteLine($"radiance: fg: job {job.JobNumber} has no process");
            return 1;
        }

        // Bring to foreground — wait for the process
        var exitCode = context.JobManager.WaitForJob(job);
        context.LastExitCode = exitCode;
        return exitCode;
    }
}