using System.Net.Sockets;
using System.Text;
using DaVinci.Terminal;

namespace Radiance.Multiplexer;

/// <summary>
/// Wire protocol message types for daemon ↔ client communication.
/// </summary>
internal enum MuxMsgType : byte
{
    Output = 0x01,   // daemon → client: ANSI bytes to render
    KeyInput = 0x02,  // client → daemon: encoded key bytes
    Resize = 0x03,    // client → daemon: 4 bytes (rows_be, cols_be)
    Detach = 0x04,    // client → daemon: client is disconnecting
    Attach = 0x05,    // client → daemon: client is attaching (payload = empty)
    Exit = 0x06,      // daemon → client: session has exited
}

/// <summary>
/// Session directory for socket files.
/// </summary>
public static class MuxSessionDir
{
    public static string Directory => Path.Combine(Path.GetTempPath(), "radiance-mux");

    public static string SocketPath(string sessionName) =>
        Path.Combine(Directory, $"{sessionName}.sock");

    public static string InfoPath(string sessionName) =>
        Path.Combine(Directory, $"{sessionName}.info");

    public static void EnsureDirectory()
    {
        System.IO.Directory.CreateDirectory(Directory);
    }

    /// <summary>
    /// List all session names that have socket files.
    /// </summary>
    public static List<string> ListSessions()
    {
        EnsureDirectory();
        var sessions = new List<string>();
        foreach (var file in System.IO.Directory.GetFiles(Directory, "*.sock"))
        {
            var name = Path.GetFileNameWithoutExtension(file);
            sessions.Add(name);
        }
        return sessions;
    }

    /// <summary>
    /// Write session metadata (PID, creation time) for discovery.
    /// </summary>
    public static void WriteInfo(string sessionName, MultiplexerSession session)
    {
        EnsureDirectory();
        var info = $"{Environment.ProcessId}\n{session.Name}\n{DateTime.UtcNow:O}";
        File.WriteAllText(InfoPath(sessionName), info);
    }

    /// <summary>
    /// Clean up socket and info files for a session.
    /// </summary>
    public static void Cleanup(string sessionName)
    {
        try { File.Delete(SocketPath(sessionName)); } catch { }
        try { File.Delete(InfoPath(sessionName)); } catch { }
    }
}

/// <summary>
/// Mux daemon: runs a MultiplexerSession headlessly, accepts client
/// connections over a Unix domain socket, relays rendered frames to
/// clients and key input from clients.
///
/// The daemon survives client detach — sessions continue running in
/// the background until explicitly killed or all panes exit.
/// </summary>
public sealed class MuxDaemon : IDisposable
{
    private readonly MultiplexerSession _session;
    private readonly HeadlessTerminal _headless;
    private readonly string _socketPath;
    private readonly string _sessionName;
    private readonly List<Socket> _clients = new();
    private readonly object _clientLock = new();

    private Thread? _renderThread;
    private Thread? _acceptThread;
    private volatile bool _running;
    private bool _disposed;

    // Frame timing
    private const int TargetFps = 30;
    private const int FrameIntervalMs = 1000 / TargetFps;

    public MuxDaemon(string sessionName, int width = 120, int height = 40, string? command = null)
    {
        _sessionName = sessionName;
        _socketPath = MuxSessionDir.SocketPath(sessionName);
        _headless = new HeadlessTerminal(width, height);
        _session = new MultiplexerSession(sessionName, _headless);
        _session.Initialize(command);
        _session.OnDetach += HandleDetach;
    }

    /// <summary>
    /// Start the daemon: begin accepting connections and rendering frames.
    /// Blocks until the session exits or is killed.
    /// </summary>
    public void Run()
    {
        _running = true;
        MuxSessionDir.EnsureDirectory();

        // Clean up stale socket
        try { File.Delete(_socketPath); } catch { }

        // Write session info
        MuxSessionDir.WriteInfo(_sessionName, _session);

        // Start render loop in background
        _renderThread = new Thread(RenderLoop)
        {
            Name = "mux-daemon-render",
            IsBackground = true
        };
        _renderThread.Start();

        // Accept clients on this thread (blocks)
        _acceptThread = new Thread(AcceptLoop)
        {
            Name = "mux-daemon-accept",
            IsBackground = true
        };
        _acceptThread.Start();

        // Wait for session to end
        _renderThread.Join();

        // Clean up
        Cleanup();
    }

    /// <summary>
    /// Stop the daemon and session.
    /// </summary>
    public void Stop()
    {
        _running = false;
        _session.Stop();
    }

    private void RenderLoop()
    {
        // Initial full render
        _session.RenderToBytes();

        while (_running)
        {
            var output = _session.RenderToBytes();

            // Broadcast to all connected clients
            if (output.Length > 0)
            {
                BroadcastMessage(MuxMsgType.Output, output);
            }

            Thread.Sleep(FrameIntervalMs);
        }
    }

    private void AcceptLoop()
    {
        var endpoint = new UnixDomainSocketEndPoint(_socketPath);
        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);

        try
        {
            listener.Bind(endpoint);
            listener.Listen(4);

            while (_running)
            {
                listener.AcceptAsync(new SocketAsyncEventArgs());
                // Use polling accept with timeout
                if (listener.Poll(500000, SelectMode.SelectRead))
                {
                    var client = listener.Accept();
                    lock (_clientLock)
                    {
                        _clients.Add(client);
                    }

                    // Start reading from this client in a background thread
                    var clientThread = new Thread(() => ClientReadLoop(client))
                    {
                        Name = $"mux-client-{client.Handle}",
                        IsBackground = true
                    };
                    clientThread.Start();
                }
            }
        }
        catch (SocketException)
        {
            // Socket closed — daemon is shutting down
        }
    }

    private void ClientReadLoop(Socket client)
    {
        try
        {
            while (_running && client.Connected)
            {
                // Read message header: type (1 byte) + length (4 bytes)
                var header = new byte[5];
                if (!ReceiveExact(client, header))
                    break;

                var msgType = (MuxMsgType)header[0];
                var payloadLen = BitConverter.ToInt32(header, 1);
                if (payloadLen < 0 || payloadLen > 1024 * 1024) break;

                var payload = payloadLen > 0 ? new byte[payloadLen] : Array.Empty<byte>();
                if (payloadLen > 0 && !ReceiveExact(client, payload))
                    break;

                switch (msgType)
                {
                    case MuxMsgType.KeyInput:
                        // Decode and feed to session
                        FeedKeysToSession(payload);
                        break;

                    case MuxMsgType.Resize when payload.Length >= 4:
                        var rows = BitConverter.ToUInt16(payload, 0);
                        var cols = BitConverter.ToUInt16(payload, 2);
                        _session.ResizeTerminal(cols, rows);
                        break;

                    case MuxMsgType.Detach:
                        // Client wants to detach — just disconnect them
                        return;
                }
            }
        }
        catch (SocketException)
        {
            // Client disconnected
        }
        finally
        {
            RemoveClient(client);
            try { client.Close(); } catch { }
        }
    }

    private void FeedKeysToSession(byte[] payload)
    {
        // Payload contains raw terminal bytes. We need to convert them back
        // to ConsoleKeyInfo. For simplicity, we handle the common cases:
        // - Single printable byte → ConsoleKeyInfo with that char
        // - ESC sequences → we parse and reconstruct
        //
        // Actually, for the daemon, the simpler approach is to have the client
        // send ConsoleKeyInfo fields directly (char, key, modifiers).
        // Format: char (2 bytes UTF-16 LE) + keyCode (4 bytes int) + modifiers (4 bytes int)

        if (payload.Length < 10) return;

        var ch = BitConverter.ToChar(payload, 0);
        var keyCode = (ConsoleKey)BitConverter.ToInt32(payload, 2);
        var modifiers = (ConsoleModifiers)BitConverter.ToInt32(payload, 6);

        var key = new ConsoleKeyInfo(ch, keyCode,
            modifiers.HasFlag(ConsoleModifiers.Shift),
            modifiers.HasFlag(ConsoleModifiers.Alt),
            modifiers.HasFlag(ConsoleModifiers.Control));

        _session.FeedKey(key);
    }

    private void BroadcastMessage(MuxMsgType type, byte[] payload)
    {
        var header = new byte[5];
        header[0] = (byte)type;
        BitConverter.TryWriteBytes(header.AsSpan(1), payload.Length);

        lock (_clientLock)
        {
            for (var i = _clients.Count - 1; i >= 0; i--)
            {
                try
                {
                    _clients[i].Send(header);
                    if (payload.Length > 0)
                        _clients[i].Send(payload);
                }
                catch
                {
                    // Client disconnected — remove
                    try { _clients[i].Close(); } catch { }
                    _clients.RemoveAt(i);
                }
            }
        }
    }

    private void RemoveClient(Socket client)
    {
        lock (_clientLock)
        {
            _clients.Remove(client);
        }
    }

    private static bool ReceiveExact(Socket socket, byte[] buffer)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = socket.Receive(buffer, offset, buffer.Length - offset, SocketFlags.None);
            if (read == 0) return false;
            offset += read;
        }
        return true;
    }

    private void HandleDetach(MultiplexerSession session)
    {
        // Don't stop the daemon on detach — just let the client disconnect.
        // The session keeps running for reattach.
    }

    private void Cleanup()
    {
        // Close all clients
        lock (_clientLock)
        {
            foreach (var client in _clients)
            {
                try
                {
                    // Send exit message
                    var header = new byte[5];
                    header[0] = (byte)MuxMsgType.Exit;
                    BitConverter.TryWriteBytes(header.AsSpan(1), 0);
                    client.Send(header);
                }
                catch { }
                try { client.Close(); } catch { }
            }
            _clients.Clear();
        }

        MuxSessionDir.Cleanup(_sessionName);
        _session.Dispose();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        Cleanup();
    }
}
