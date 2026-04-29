using System.Net.Sockets;
using System.Text;
using Radiance.Terminal;

namespace Radiance.Multiplexer;

/// <summary>
/// Thin client that connects to a MuxDaemon via Unix domain socket.
/// Reads keys from the console, serializes them to the socket, and
/// writes ANSI output received from the daemon to the console.
/// </summary>
public sealed class MuxClient : IDisposable
{
    private readonly string _sessionName;
    private Socket? _socket;
    private volatile bool _running;
    private bool _disposed;

    public MuxClient(string sessionName)
    {
        _sessionName = sessionName;
    }

    /// <summary>
    /// Connect to the daemon and run the client loop.
    /// Blocks until the session detaches, exits, or the connection drops.
    /// Returns 0 on clean detach, 1 on error, 2 on connection failure.
    /// </summary>
    public int Run()
    {
        var socketPath = MuxSessionDir.SocketPath(_sessionName);
        if (!File.Exists(socketPath))
        {
            Console.Error.WriteLine($"No session '{_sessionName}' found.");
            return 2;
        }

        _socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            _socket.Connect(new UnixDomainSocketEndPoint(socketPath));
        }
        catch (SocketException ex)
        {
            Console.Error.WriteLine($"Failed to connect to session '{_sessionName}': {ex.Message}");
            return 2;
        }

        _running = true;

        // Send terminal size
        SendResize(Console.WindowWidth, Console.WindowHeight);

        // Enter raw terminal mode
        Console.Write("\x1b[?1000h"); // Mouse tracking
        Console.Write("\x1b[?25l");   // Hide cursor
        Console.Write("\x1b[2J\x1b[H"); // Clear screen

        try
        {
            // Start reader thread (daemon → console output)
            var readerThread = new Thread(ReadLoop)
            {
                Name = "mux-client-reader",
                IsBackground = true
            };
            readerThread.Start();

            // Writer loop: console keys → daemon
            while (_running)
            {
                if (Console.KeyAvailable)
                {
                    var key = Console.ReadKey(true);

                    // Encode ConsoleKeyInfo as: char (2) + keyCode (4) + modifiers (4)
                    var payload = new byte[10];
                    BitConverter.TryWriteBytes(payload.AsSpan(0), key.KeyChar);
                    BitConverter.TryWriteBytes(payload.AsSpan(2), (int)key.Key);
                    BitConverter.TryWriteBytes(payload.AsSpan(6), (int)key.Modifiers);

                    SendMessage(MuxMsgType.KeyInput, payload);
                }

                Thread.Sleep(8); // ~120Hz key polling
            }
        }
        finally
        {
            // Restore terminal
            Console.Write("\x1b[?1000l"); // Mouse tracking off
            Console.Write("\x1b[?25h");   // Show cursor
            Console.Write("\x1b[2J\x1b[H"); // Clear screen
            Console.Out.Flush();
        }

        return 0;
    }

    /// <summary>
    /// Disconnect from the daemon.
    /// </summary>
    public void Detach()
    {
        _running = false;
        try
        {
            SendMessage(MuxMsgType.Detach, Array.Empty<byte>());
        }
        catch { }
    }

    private void ReadLoop()
    {
        try
        {
            while (_running && _socket?.Connected == true)
            {
                // Read message header: type (1) + length (4)
                var header = new byte[5];
                if (!ReceiveExact(header))
                {
                    _running = false;
                    break;
                }

                var msgType = (MuxMsgType)header[0];
                var payloadLen = BitConverter.ToInt32(header, 1);
                if (payloadLen < 0 || payloadLen > 4 * 1024 * 1024)
                {
                    _running = false;
                    break;
                }

                var payload = payloadLen > 0 ? new byte[payloadLen] : Array.Empty<byte>();
                if (payloadLen > 0 && !ReceiveExact(payload))
                {
                    _running = false;
                    break;
                }

                switch (msgType)
                {
                    case MuxMsgType.Output:
                        // Write ANSI bytes to console
                        Console.Write(Encoding.UTF8.GetString(payload));
                        Console.Out.Flush();
                        break;

                    case MuxMsgType.Exit:
                        // Session has exited
                        _running = false;
                        break;
                }
            }
        }
        catch
        {
            _running = false;
        }
    }

    private void SendResize(int cols, int rows)
    {
        var payload = new byte[4];
        BitConverter.TryWriteBytes(payload.AsSpan(0), (ushort)rows);
        BitConverter.TryWriteBytes(payload.AsSpan(2), (ushort)cols);
        SendMessage(MuxMsgType.Resize, payload);
    }

    private void SendMessage(MuxMsgType type, byte[] payload)
    {
        if (_socket is null || !_socket.Connected) return;

        var header = new byte[5];
        header[0] = (byte)type;
        BitConverter.TryWriteBytes(header.AsSpan(1), payload.Length);

        _socket.Send(header);
        if (payload.Length > 0)
            _socket.Send(payload);
    }

    private bool ReceiveExact(byte[] buffer)
    {
        if (_socket is null) return false;
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = _socket.Receive(buffer, offset, buffer.Length - offset, SocketFlags.None);
            if (read == 0) return false;
            offset += read;
        }
        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _running = false;
        try { _socket?.Close(); } catch { }
    }
}
