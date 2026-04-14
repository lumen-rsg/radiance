namespace Radiance.Builtins;

/// <summary>
/// The <c>jobs</c> builtin — lists active background jobs.
/// Usage: <c>jobs</c> or <c>jobs -l</c> (with PIDs)
/// </summary>
public sealed class JobsCommand : IBuiltinCommand
{
    /// <inheritdoc/>
    public string Name => "jobs";

    /// <inheritdoc/>
    public int Execute(string[] args, ShellContext context)
    {
        var showPids = args.Length > 1 && args[1] == "-l";

        if (!context.JobManager.HasJobs)
            return 0;

        // Update job states
        context.JobManager.UpdateAndCollectCompleted();

        foreach (var job in context.JobManager.AllJobs.OrderBy(j => j.JobNumber))
        {
            if (showPids)
            {
                Console.WriteLine($"[{job.JobNumber}]  {job.Pid}  {job.State.ToString().ToLowerInvariant(),-10} {job.CommandText}");
            }
            else
            {
                Console.WriteLine(JobManager.FormatJobStatus(job));
            }
        }

        return 0;
    }
}