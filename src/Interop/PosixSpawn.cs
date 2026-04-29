using System.Runtime.InteropServices;

namespace Radiance.Interop;

/// <summary>
/// P/Invoke declarations for posix_spawn — used to spawn child processes
/// with custom file descriptor wiring (e.g., PTY slave as stdin/stdout/stderr).
/// Unlike .NET's Process class, posix_spawn supports dup2 file actions for
/// arbitrary fd assignment in the child.
/// </summary>
internal static class PosixSpawn
{
    // ──── Spawn Functions ────

    [DllImport("libc", SetLastError = true)]
    internal static extern int posix_spawn(
        out int pid,
        string path,
        IntPtr file_actions,
        IntPtr attrp,
        string[] argv,
        string[] envp);

    [DllImport("libc", SetLastError = true)]
    internal static extern int posix_spawnp(
        out int pid,
        string file,
        IntPtr file_actions,
        IntPtr attrp,
        string[] argv,
        string[] envp);

    // ──── File Actions ────

    [DllImport("libc", SetLastError = true)]
    internal static extern int posix_spawn_file_actions_init(out IntPtr file_actions);

    [DllImport("libc")]
    internal static extern int posix_spawn_file_actions_adddup2(
        IntPtr file_actions,
        int fd,
        int newfd);

    [DllImport("libc")]
    internal static extern int posix_spawn_file_actions_addclose(
        IntPtr file_actions,
        int fd);

    [DllImport("libc")]
    internal static extern int posix_spawn_file_actions_destroy(IntPtr file_actions);

    // ──── Spawn Attributes ────

    [DllImport("libc", SetLastError = true)]
    internal static extern int posix_spawnattr_init(out IntPtr attrp);

    [DllImport("libc")]
    internal static extern int posix_spawnattr_setflags(IntPtr attrp, ushort flags);

    [DllImport("libc")]
    internal static extern int posix_spawnattr_destroy(IntPtr attrp);

    // ──── Wait ────

    [DllImport("libc", SetLastError = true)]
    internal static extern int waitpid(int pid, out int status, int options);

    // ──── Constants ────

    internal const ushort POSIX_SPAWN_SETPGROUP = 0x0001;
    internal const int WNOHANG = 1;
}
