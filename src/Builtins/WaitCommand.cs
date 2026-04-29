using Radiance.Interpreter;

namespace Radiance.Builtins;

/// <summary>
/// Built-in <c>wait</c> command — waits for background jobs to complete.
/// Without arguments, waits for all background jobs.
/// With a job number or PID, waits for that specific job.
/// </summary>
public sealed class WaitCommand : IBuiltinCommand
{
    public string Name => "wait";

    public int Execute(string[] args, ShellContext context)
    {
        if (args.Length <= 1)
        {
            // Wait for all background jobs
            var exitCode = 0;
            foreach (var job in context.JobManager.AllJobs.ToList())
            {
                if (job.State == JobState.Done)
                    continue;

                var code = context.JobManager.WaitForJob(job);
                if (code != 0)
                    exitCode = code;
            }

            return exitCode;
        }

        // Wait for a specific job
        var target = args[1];

        if (!int.TryParse(target, out var id))
        {
            Console.Error.WriteLine($"radiance: wait: {target}: not a valid job number");
            return 1;
        }

        // Try to find by job number first
        var targetJob = context.JobManager.GetJob(id);

        if (targetJob is null)
        {
            // Try to find by PID
            targetJob = context.JobManager.AllJobs.FirstOrDefault(j => j.Pid == id);

            if (targetJob is null)
            {
                Console.Error.WriteLine($"radiance: wait: {id}: no such job");
                return 127;
            }
        }

        if (targetJob.State == JobState.Done)
            return targetJob.ExitCode;

        return context.JobManager.WaitForJob(targetJob);
    }
}
