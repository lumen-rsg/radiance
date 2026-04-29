using Radiance.Interpreter;

namespace Radiance.Builtins;

/// <summary>
/// Built-in <c>bg</c> command — resumes a stopped job in the background.
/// Currently a placeholder as full job suspension (Ctrl+Z) is not yet implemented.
/// </summary>
public sealed class BgCommand : IBuiltinCommand
{
    public string Name => "bg";

    public int Execute(string[] args, ShellContext context)
    {
        if (args.Length <= 1)
        {
            // Resume the current job (most recent stopped)
            var job = context.JobManager.GetCurrentJob();
            if (job is null)
            {
                Console.Error.WriteLine("radiance: bg: current: no such job");
                return 1;
            }

            return ResumeJob(job);
        }

        if (!int.TryParse(args[1], out var jobNum))
        {
            Console.Error.WriteLine($"radiance: bg: {args[1]}: no such job");
            return 1;
        }

        var targetJob = context.JobManager.GetJob(jobNum);
        if (targetJob is null)
        {
            Console.Error.WriteLine($"radiance: bg: {jobNum}: no such job");
            return 1;
        }

        return ResumeJob(targetJob);
    }

    private static int ResumeJob(Job job)
    {
        if (job.State != JobState.Stopped)
        {
            Console.Error.WriteLine($"radiance: bg: job {job.JobNumber} not stopped");
            return 1;
        }

        // Send SIGCONT to the process
        if (job.Process is not null && !job.Process.HasExited)
        {
            try
            {
                System.Diagnostics.Process.GetProcessById(job.Process.Id);
                // On Unix, we'd send SIGCONT via kill(pid, SIGCONT)
                // For now, mark as running
                job.State = JobState.Running;
                Console.WriteLine($"[{job.JobNumber}]+ {job.CommandText} &");
                return 0;
            }
            catch
            {
                Console.Error.WriteLine($"radiance: bg: job {job.JobNumber}: could not resume");
                return 1;
            }
        }

        Console.Error.WriteLine($"radiance: bg: job {job.JobNumber}: no process");
        return 1;
    }
}
