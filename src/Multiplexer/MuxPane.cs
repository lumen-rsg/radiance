using System.Text;
using Radiance.Interop;
using Radiance.Terminal;

namespace Radiance.Multiplexer;

/// <summary>
/// A single multiplexer pane: owns a PTY, child process, relay thread,
/// and a PaneScreenBuffer that holds the virtual terminal state.
/// </summary>
public sealed class MuxPane : IDisposable
{
    private readonly PtyAllocation _pty;
    private readonly Stream _masterStream;
    private readonly PaneScreenBuffer _buffer;
    private readonly AnsiStreamParser _parser;
    private readonly Thread _relayThread;
    private readonly object _writeLock = new();

    private volatile bool _running;
    private bool _disposed;

    /// <summary>Child process ID. -1 if not spawned.</summary>
    public int Pid { get; }

    /// <summary>Unique pane ID within the window (assigned by MuxWindow).</summary>
    public int PaneId { get; internal set; }

    /// <summary>True while the child process is still running.</summary>
    public bool IsAlive { get; private set; }

    /// <summary>Exit code of the child process, or null if still running.</summary>
    public int? ExitCode { get; private set; }

    /// <summary>The virtual terminal buffer for this pane.</summary>
    public PaneScreenBuffer Buffer => _buffer;

    /// <summary>Fired when the child process exits.</summary>
    public event Action<MuxPane, int>? OnExit;

    /// <summary>
    /// Create a pane running a child process inside a PTY with the given dimensions.
    /// </summary>
    public MuxPane(string command, int rows, int cols, string[]? argv = null, string[]? envp = null)
    {
        argv ??= Array.Empty<string>();

        _pty = PtyAllocation.Create(rows, cols)
            ?? throw new InvalidOperationException("Failed to allocate PTY for mux pane");

        _buffer = new PaneScreenBuffer(cols, rows);
        _parser = new AnsiStreamParser(_buffer);

        // Resolve command path
        var resolved = ResolveCommand(command);

        Pid = _pty.SpawnProcess(resolved, argv, envp);
        if (Pid < 0)
        {
            _pty.Dispose();
            throw new InvalidOperationException($"Failed to spawn process: {command}");
        }

        IsAlive = true;
        _masterStream = _pty.CreateMasterStream();

        // Relay thread: master fd → parser → buffer
        _relayThread = new Thread(RelayLoop)
        {
            Name = $"mux-relay-{Pid}",
            IsBackground = true
        };
        _running = true;
        _relayThread.Start();
    }

    /// <summary>
    /// Write input bytes to the child process's stdin (via PTY master).
    /// Thread-safe.
    /// </summary>
    public void WriteInput(ReadOnlySpan<byte> data)
    {
        if (!_running || data.IsEmpty) return;

        lock (_writeLock)
        {
            try
            {
                _masterStream.Write(data);
                _masterStream.Flush();
            }
            catch
            {
                // Broken pipe — child likely exited
            }
        }
    }

    /// <summary>
    /// Write a UTF-8 string to the child process's stdin.
    /// </summary>
    public void WriteInput(string text)
    {
        WriteInput(Encoding.UTF8.GetBytes(text));
    }

    /// <summary>
    /// Resize the PTY and the screen buffer to the new dimensions.
    /// </summary>
    public void Resize(int rows, int cols)
    {
        _pty.SetWindowSize(rows, cols);
        _buffer.Resize(cols, rows);
    }

    /// <summary>
    /// Send a signal to the child process group.
    /// </summary>
    public void SendSignal(int sig)
    {
        if (!IsAlive) return;
        try { PosixPty.killpg(Pid, sig); } catch { }
    }

    private void RelayLoop()
    {
        var buf = new byte[4096];

        while (_running)
        {
            int bytesRead;
            try
            {
                bytesRead = _masterStream.Read(buf, 0, buf.Length);
            }
            catch
            {
                // Stream closed or error
                break;
            }

            if (bytesRead == 0)
                break;

            // Feed raw bytes through the ANSI parser into the screen buffer
            _parser.Feed(new ReadOnlySpan<byte>(buf, 0, bytesRead));
        }

        // Child process output ended — reap it
        ReapChild();
    }

    private void ReapChild()
    {
        _running = false;
        IsAlive = false;

        if (Pid > 0)
        {
            try
            {
                PosixSpawn.waitpid(Pid, out int status, 0);
                ExitCode = (status & 0x7f) == 0
                    ? (status >> 8) & 0xff
                    : 128 + (status & 0x7f);
            }
            catch
            {
                ExitCode = -1;
            }
        }

        OnExit?.Invoke(this, ExitCode ?? -1);
    }

    /// <summary>
    /// Kill the child process and clean up resources.
    /// </summary>
    public void Close()
    {
        _running = false;

        if (IsAlive)
        {
            SendSignal(PosixPty.SIGTERM);

            // Give it a moment, then force kill
            Thread.Sleep(50);
            if (IsAlive)
                SendSignal(PosixPty.SIGKILL);
        }

        try { _masterStream.Close(); } catch { }

        // Wait for relay thread to finish
        _relayThread.Join(1000);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Close();
        _pty.Dispose();
    }

    private static string ResolveCommand(string command)
    {
        // If it's an absolute path, use as-is
        if (command.StartsWith('/'))
            return command;

        // Search PATH
        var path = Environment.GetEnvironmentVariable("PATH") ?? "/usr/bin:/bin:/usr/sbin:/sbin";
        foreach (var dir in path.Split(':'))
        {
            var fullPath = Path.Combine(dir, command);
            if (File.Exists(fullPath))
                return fullPath;
        }

        // Fall back to the command name — posix_spawnp will search PATH itself
        return command;
    }
}
