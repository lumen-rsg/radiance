using System.Runtime.InteropServices;

namespace Radiance.Interop;

/// <summary>
/// P/Invoke declarations for POSIX PTY (pseudo-terminal) operations.
/// Provides openpty, terminal attributes, window size, and process group management.
/// </summary>
internal static class PosixPty
{
    // ──── PTY Creation ────

    [DllImport("libc", SetLastError = true)]
    internal static extern int openpty(
        out int amaster,
        out int aslave,
        IntPtr name,
        IntPtr termp,
        ref Winsize winp);

    [DllImport("libc", SetLastError = true)]
    internal static extern int openpty(
        out int amaster,
        out int aslave,
        IntPtr name,
        IntPtr termp,
        IntPtr winp);

    [DllImport("libc", SetLastError = true)]
    internal static extern int login_tty(int fd);

    [DllImport("libc", SetLastError = true)]
    internal static extern int close(int fd);

    // ──── Terminal Attributes ────

    [DllImport("libc", SetLastError = true)]
    internal static extern int tcgetattr(int fd, out Termios termios_p);

    [DllImport("libc", SetLastError = true)]
    internal static extern int tcsetattr(int fd, int optional_actions, ref Termios termios_p);

    // ──── Window Size ────

    [DllImport("libc", SetLastError = true)]
    internal static extern int ioctl(int fd, ulong request, ref Winsize ws);

    // ──── Process Groups ────

    [DllImport("libc", SetLastError = true)]
    internal static extern int setpgid(int pid, int pgid);

    [DllImport("libc", SetLastError = true)]
    internal static extern int tcsetpgrp(int fd, int pgid);

    [DllImport("libc")]
    internal static extern int getpgid(int pid);

    [DllImport("libc")]
    internal static extern int getpid();

    // ──── Signals ────

    [DllImport("libc", SetLastError = true)]
    internal static extern int killpg(int pgrp, int sig);

    [DllImport("libc", SetLastError = true)]
    internal static extern int kill(int pid, int sig);

    // ──── Constants ────

    internal const int TCSANOW = 0;

    // TIOCGWINSZ / TIOCSWINSZ differ between macOS and Linux
#if MACOS
    internal const ulong TIOCGWINSZ = 0x40087468;
    internal const ulong TIOCSWINSZ = 0x80087467;
#else
    internal const ulong TIOCGWINSZ = 0x5413;
    internal const ulong TIOCSWINSZ = 0x5414;
#endif

    internal const int SIGINT = 2;
    internal const int SIGTERM = 15;
    internal const int SIGTSTP = 18;
    internal const int SIGCHLD = 17;
    internal const int SIGTTIN = 21;
    internal const int SIGTTOU = 22;
    internal const int SIGPIPE = 13;
    internal const int SIGKILL = 9;
}

/// <summary>
/// POSIX termios structure for terminal attributes.
/// Layout differs between macOS and Linux.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct Termios
{
    public uint c_iflag;   // input flags
    public uint c_oflag;   // output flags
    public uint c_cflag;   // control flags
    public uint c_lflag;   // local flags

#if MACOS
    // macOS: c_cc is 20 bytes, then ispeed/ospeed as int
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
    public byte[] c_cc;
    public int c_ispeed;
    public int c_ospeed;
#else
    // Linux: c_cc is 19 bytes, then ispeed/ospeed as uint
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 19)]
    public byte[] c_cc;
    public uint c_ispeed;
    public uint c_ospeed;
#endif
}

/// <summary>
/// Window size structure for terminal dimensions.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct Winsize
{
    public ushort ws_row;
    public ushort ws_col;
    public ushort ws_xpixel;
    public ushort ws_ypixel;
}
