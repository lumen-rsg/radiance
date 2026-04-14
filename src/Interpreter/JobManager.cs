using System.Diagnostics;

namespace Radiance.Interpreter;

/// <summary>
/// Represents a background job managed by the shell.
/// </summary>
public sealed class Job
{
    /// <summary>
    /// The job number (unique per session, assigned sequentially).
    /// </summary>
    public int JobNumber { get; init; }

    /// <summary>
    /// The process associated with this job.
    /// </summary>
    public Process? Process { get; init; }

    /// <summary>
    /// The command string that started this job.
    /// </summary>
    public string CommandText { get; init; } = string.Empty;

    /// <summary>
    /// The current state of the job.
    /// </summary>
    public JobState State { get; set; } = JobState.Running;

    /// <summary>
    /// The PID of the process (0 if not started).
    /// </summary>
    public int Pid => Process?.Id ?? 0;

    /// <summary>
    /// When the job was started.
    /// </summary>
    public DateTime StartTime { get; init; } = DateTime.Now;

    /// <summary>
    /// The exit code of the job (valid only when State is Done).
    /// </summary>
    public int ExitCode { get; set; } = 0;

    /// <summary>
    /// Signal that is set when the job completes. Used for thread-pool background jobs.
    /// </summary>
    internal ManualResetEventSlim CompletedEvent { get; } = new(false);
}

/// <summary>
/// Represents the state of a background job.
/// </summary>
public enum JobState
{
    /// <summary>The job is currently running.</summary>
    Running,

    /// <summary>The job has been stopped (e.g., Ctrl+Z).</summary>
    Stopped,

    /// <summary>The job has completed.</summary>
    Done
}

/// <summary>
/// Manages background jobs for the shell. Tracks job numbers, processes,
/// and state transitions. Provides notifications when jobs complete.
/// </summary>
public sealed class JobManager
{
    private readonly Dictionary<int, Job> _jobs = new();
    private int _nextJobNumber = 1;

    /// <summary>
    /// Registers a new background job and returns the assigned job number.
    /// </summary>
    /// <param name="process">The process for this job.</param>
    /// <param name="commandText">The command string.</param>
    /// <returns>The assigned job number.</returns>
    public int AddJob(Process process, string commandText)
    {
        var jobNum = _nextJobNumber++;
        var job = new Job
        {
            JobNumber = jobNum,
            Process = process,
            CommandText = commandText,
            State = JobState.Running
        };
        _jobs[jobNum] = job;
        return jobNum;
    }

    /// <summary>
    /// Registers a job without a process (for function/compound commands run in background).
    /// The job starts in Running state and must be completed via <see cref="CompleteJob"/>.
    /// </summary>
    /// <param name="commandText">The command string.</param>
    /// <returns>The created <see cref="Job"/>.</returns>
    public Job AddJob(string commandText)
    {
        var jobNum = _nextJobNumber++;
        var job = new Job
        {
            JobNumber = jobNum,
            Process = null,
            CommandText = commandText,
            State = JobState.Running,
            ExitCode = 0
        };
        _jobs[jobNum] = job;
        return job;
    }

    /// <summary>
    /// Marks a background job as completed with the given exit code.
    /// </summary>
    /// <param name="jobNumber">The job number.</param>
    /// <param name="exitCode">The exit code.</param>
    public void CompleteJob(int jobNumber, int exitCode)
    {
        if (_jobs.TryGetValue(jobNumber, out var job))
        {
            job.ExitCode = exitCode;
            job.State = JobState.Done;
            job.CompletedEvent.Set();
        }
    }

    /// <summary>
    /// Gets a job by its job number.
    /// </summary>
    /// <param name="jobNumber">The job number.</param>
    /// <returns>The job, or null if not found.</returns>
    public Job? GetJob(int jobNumber) =>
        _jobs.TryGetValue(jobNumber, out var job) ? job : null;

    /// <summary>
    /// Gets all tracked jobs.
    /// </summary>
    public IEnumerable<Job> AllJobs => _jobs.Values;

    /// <summary>
    /// Checks if any jobs are currently tracked.
    /// </summary>
    public bool HasJobs => _jobs.Count > 0;

    /// <summary>
    /// Updates the state of all tracked jobs by checking if processes have exited.
    /// Returns jobs that have newly completed.
    /// </summary>
    /// <returns>A list of jobs that just transitioned to Done state.</returns>
    public List<Job> UpdateAndCollectCompleted()
    {
        var completed = new List<Job>();

        foreach (var job in _jobs.Values)
        {
            if (job.State == JobState.Done)
                continue;

            if (job.Process is not null && job.Process.HasExited)
            {
                job.ExitCode = job.Process.ExitCode;
                job.State = JobState.Done;
                completed.Add(job);
            }
        }

        return completed;
    }

    /// <summary>
    /// Removes completed jobs from the job table.
    /// </summary>
    public void CleanupCompleted()
    {
        var toRemove = _jobs.Where(kvp => kvp.Value.State == JobState.Done)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in toRemove)
        {
            try { _jobs[key].Process?.Dispose(); } catch { /* ignored */ }
            _jobs.Remove(key);
        }
    }

    /// <summary>
    /// Gets the job marked with '+' (most recent job).
    /// </summary>
    /// <returns>The current job, or null.</returns>
    public Job? GetCurrentJob()
    {
        return _jobs.Values
            .Where(j => j.State != JobState.Done)
            .MaxBy(j => j.JobNumber);
    }

    /// <summary>
    /// Formats a job status line for display (e.g., "[1]+ Running    sleep 5").
    /// </summary>
    /// <param name="job">The job to format.</param>
    /// <returns>The formatted status string.</returns>
    public static string FormatJobStatus(Job job)
    {
        var current = GetCurrentJobMarker(job);
        var state = job.State.ToString().ToLowerInvariant();
        return $"[{job.JobNumber}]{current} {state,-10} {job.CommandText}";
    }

    /// <summary>
    /// Returns "+" for the current job, "-" for the previous job, " " otherwise.
    /// </summary>
    private static string GetCurrentJobMarker(Job job) => "  ";

    /// <summary>
    /// Waits for a specific job to complete and returns its exit code.
    /// </summary>
    /// <param name="job">The job to wait for.</param>
    /// <returns>The exit code of the job.</returns>
    public int WaitForJob(Job job)
    {
        if (job.Process is not null)
        {
            job.Process.WaitForExit();
            job.ExitCode = job.Process.ExitCode;
            job.State = JobState.Done;
            return job.ExitCode;
        }

        // Thread-pool background job — wait for the completion signal
        job.CompletedEvent.Wait();
        return job.ExitCode;
    }
}