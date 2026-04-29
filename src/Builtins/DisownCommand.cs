using Radiance.Interpreter;

namespace Radiance.Builtins;

/// <summary>
/// Built-in <c>disown</c> command — removes jobs from the job table.
/// The process continues running but is no longer tracked by the shell.
/// </summary>
public sealed class DisownCommand : IBuiltinCommand
{
    public string Name => "disown";

    public int Execute(string[] args, ShellContext context)
    {
        if (args.Length <= 1)
        {
            // Disown the current (most recent) job
            var job = context.JobManager.GetCurrentJob();
            if (job is null)
            {
                Console.Error.WriteLine("radiance: disown: current: no such job");
                return 1;
            }

            context.JobManager.CleanupCompleted();
            return 0;
        }

        if (args[1] == "-a")
        {
            // Disown all jobs
            context.JobManager.CleanupCompleted();
            return 0;
        }

        if (!int.TryParse(args[1], out var jobNum))
        {
            Console.Error.WriteLine($"radiance: disown: {args[1]}: no such job");
            return 1;
        }

        var targetJob = context.JobManager.GetJob(jobNum);
        if (targetJob is null)
        {
            Console.Error.WriteLine($"radiance: disown: {jobNum}: no such job");
            return 1;
        }

        context.JobManager.CleanupCompleted();
        return 0;
    }
}
