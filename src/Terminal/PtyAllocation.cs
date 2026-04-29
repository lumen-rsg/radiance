using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Radiance.Interop;

namespace Radiance.Terminal;

/// <summary>
/// Manages the lifecycle of a PTY (pseudo-terminal) pair allocated via openpty().
/// Provides the master/slave file descriptors and a Stream wrapping the master fd
/// for reading/writing by the parent process.
/// </summary>
public sealed class PtyAllocation : IDisposable
{
    /// <summary>Master file descriptor — used by the parent process to read/write.</summary>
    public int MasterFd { get; }

    /// <summary>Slave file descriptor — passed to the child process via dup2.</summary>
    public int SlaveFd { get; }

    private bool _disposed;
    private bool _slaveFdTransferred; // true after SpawnProcess closes it

    private PtyAllocation(int masterFd, int slaveFd)
    {
        MasterFd = masterFd;
        SlaveFd = slaveFd;
    }

    /// <summary>
    /// Creates a new PTY pair inheriting the current terminal's window size.
    /// Falls back to 80x24 if the current terminal size cannot be queried.
    /// </summary>
    public static PtyAllocation? Create()
    {
        var ws = new Winsize();
        if (PosixPty.ioctl(0, PosixPty.TIOCGWINSZ, ref ws) != 0)
        {
            ws.ws_col = 80;
            ws.ws_row = 24;
        }

        var result = PosixPty.openpty(out int masterFd, out int slaveFd, IntPtr.Zero, IntPtr.Zero, ref ws);
        if (result != 0)
            return null;

        return new PtyAllocation(masterFd, slaveFd);
    }

    /// <summary>
    /// Creates a new PTY pair with explicit dimensions (for multiplexer panes).
    /// </summary>
    public static PtyAllocation? Create(int rows, int cols)
    {
        var ws = new Winsize
        {
            ws_row = (ushort)rows,
            ws_col = (ushort)cols
        };

        var result = PosixPty.openpty(out int masterFd, out int slaveFd, IntPtr.Zero, IntPtr.Zero, ref ws);
        if (result != 0)
            return null;

        return new PtyAllocation(masterFd, slaveFd);
    }

    /// <summary>
    /// Sets the terminal window size on the PTY slave.
    /// </summary>
    public void SetWindowSize(int rows, int cols)
    {
        var ws = new Winsize
        {
            ws_row = (ushort)rows,
            ws_col = (ushort)cols
        };
        PosixPty.ioctl(SlaveFd, PosixPty.TIOCSWINSZ, ref ws);
    }

    /// <summary>
    /// Gets the current terminal window size from the PTY.
    /// </summary>
    public (int rows, int cols) GetWindowSize()
    {
        var ws = new Winsize();
        PosixPty.ioctl(SlaveFd, PosixPty.TIOCGWINSZ, ref ws);
        return (ws.ws_row, ws.ws_col);
    }

    /// <summary>
    /// Creates a Stream wrapping the master file descriptor for reading/writing.
    /// The Stream takes ownership of the fd and will close it on disposal.
    /// </summary>
    public Stream CreateMasterStream()
    {
        var handle = new SafeFileHandle((IntPtr)MasterFd, ownsHandle: true);
        return new FileStream(handle, FileAccess.ReadWrite, bufferSize: 4096);
    }

    /// <summary>
    /// Creates a Stream wrapping the slave file descriptor.
    /// Does NOT take ownership — the slave fd is typically passed to
    /// posix_spawn file actions and closed by the parent after spawn.
    /// </summary>
    public Stream CreateSlaveStream()
    {
        var handle = new SafeFileHandle((IntPtr)SlaveFd, ownsHandle: false);
        return new FileStream(handle, FileAccess.ReadWrite, bufferSize: 4096);
    }

    /// <summary>
    /// Spawns a child process with the PTY slave as its stdin/stdout/stderr.
    /// Returns the child PID. The slave fd is closed in the parent after spawn.
    /// </summary>
    public int SpawnProcess(string file, string[] argv, string[]? envp = null)
    {
        if (envp is null)
        {
            // Inherit current process environment
            var envList = new List<string>();
            foreach (System.Collections.DictionaryEntry entry in Environment.GetEnvironmentVariables())
                envList.Add($"{entry.Key}={entry.Value}");
            envp = new string[envList.Count + 1];
            for (var i = 0; i < envList.Count; i++)
                envp[i] = envList[i];
            envp[envList.Count] = null!;
        }
        else
        {
            // Ensure null-terminated
            Array.Resize(ref envp, envp.Length + 1);
        }

        // Build null-terminated argv
        var argvBuf = new string[argv.Length + 1];
        Array.Copy(argv, argvBuf, argv.Length);
        argvBuf[argv.Length] = null!;

        // File actions: dup2 slave → 0/1/2, close both fds in child
        PosixSpawn.posix_spawn_file_actions_init(out var actions);
        PosixSpawn.posix_spawn_file_actions_adddup2(actions, SlaveFd, 0);
        PosixSpawn.posix_spawn_file_actions_adddup2(actions, SlaveFd, 1);
        PosixSpawn.posix_spawn_file_actions_adddup2(actions, SlaveFd, 2);
        PosixSpawn.posix_spawn_file_actions_addclose(actions, SlaveFd);
        PosixSpawn.posix_spawn_file_actions_addclose(actions, MasterFd);

        // Set process group so we can signal the child's group
        PosixSpawn.posix_spawnattr_init(out var attr);
        PosixSpawn.posix_spawnattr_setflags(attr, PosixSpawn.POSIX_SPAWN_SETPGROUP);

        var rc = PosixSpawn.posix_spawnp(out int pid, file, actions, attr, argvBuf, envp);

        PosixSpawn.posix_spawn_file_actions_destroy(actions);
        PosixSpawn.posix_spawnattr_destroy(attr);

        if (rc != 0)
            return -1;

        // Close slave fd in parent — child owns its copy via dup2
        PosixPty.close(SlaveFd);
        _slaveFdTransferred = true;

        // Put child in its own process group
        PosixPty.setpgid(pid, pid);

        return pid;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try { PosixPty.close(MasterFd); } catch { }
        if (!_slaveFdTransferred)
        {
            try { PosixPty.close(SlaveFd); } catch { }
        }
    }
}
