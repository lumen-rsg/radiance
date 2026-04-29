using System.Runtime.InteropServices;
using Radiance.Interpreter;

namespace Radiance.Builtins;

/// <summary>
/// Built-in <c>umask</c> command — sets or prints the file mode creation mask.
/// <list type="bullet">
/// <item><c>umask</c> — print current mask (octal)</item>
/// <item><c>umask NNN</c> — set mask to octal value</item>
/// <item><c>umask -S</c> — print mask in symbolic form</item>
/// </list>
/// </summary>
public sealed class UmaskCommand : IBuiltinCommand
{
    public string Name => "umask";

    public int Execute(string[] args, ShellContext context)
    {
        if (args.Length <= 1)
        {
            PrintCurrentUmask();
            return 0;
        }

        if (args[1] == "-S")
        {
            PrintCurrentUmaskSymbolic();
            return 0;
        }

        // Set umask
        var maskStr = args[1];

        if (maskStr.StartsWith('+') || maskStr.StartsWith('-') || maskStr.Contains(','))
        {
            Console.Error.WriteLine("radiance: umask: symbolic modes not yet supported");
            return 1;
        }

        if (!int.TryParse(maskStr, System.Globalization.NumberStyles.AllowLeadingSign, null, out var mask) &&
            !int.TryParse(maskStr, System.Globalization.NumberStyles.Integer, null, out mask))
        {
            Console.Error.WriteLine($"radiance: umask: {maskStr}: invalid mask");
            return 1;
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var result = umask((uint)mask);
            Console.WriteLine(Convert.ToString(result, 8).PadLeft(4, '0'));
        }

        return 0;
    }

    [System.Runtime.InteropServices.DllImport("libc", SetLastError = true)]
    private static extern uint umask(uint mask);

    private static void PrintCurrentUmask()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            // Get current umask by setting and restoring
            var old = umask(0);
            umask(old);
            Console.WriteLine(Convert.ToString((int)old, 8).PadLeft(4, '0'));
        }
    }

    private static void PrintCurrentUmaskSymbolic()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) ||
            RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            var old = umask(0);
            umask(old);
            var mask = (int)old;
            Console.WriteLine(UmaskToSymbolic(mask));
        }
    }

    private static string UmaskToSymbolic(int mask)
    {
        var owner = Perms((mask >> 6) & 7);
        var group = Perms((mask >> 3) & 7);
        var other = Perms(mask & 7);
        return $"u={owner},g={group},o={other}";
    }

    private static string Perms(int bits)
    {
        var s = "";
        if ((bits & 4) == 0) s += 'r';
        if ((bits & 2) == 0) s += 'w';
        if ((bits & 1) == 0) s += 'x';
        return s.Length == 0 ? "---" : s;
    }
}
