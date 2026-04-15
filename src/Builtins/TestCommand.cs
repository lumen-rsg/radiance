using Radiance.Interpreter;

namespace Radiance.Builtins;

/// <summary>
/// Built-in <c>test</c> and <c>[</c> commands — evaluates conditional expressions.
/// Returns exit code 0 (true) or 1 (false).
/// <para>
/// Supports:
/// <list type="bullet">
/// <item>String tests: <c>=</c>, <c>!=</c></item>
/// <item>Integer tests: <c>-eq</c>, <c>-ne</c>, <c>-lt</c>, <c>-le</c>, <c>-gt</c>, <c>-ge</c></item>
/// <item>Unary string tests: <c>-z</c> (empty), <c>-n</c> (non-empty)</item>
/// <item>File tests: <c>-f</c> (regular file), <c>-d</c> (directory), <c>-e</c> (exists)</item>
/// <item>Logical: <c>!</c> (not), <c>-a</c> (and), <c>-o</c> (or)</item>
/// </list>
/// </para>
/// <para>
/// The <c>[</c> command is identical but requires <c>]</c> as the last argument.
/// </para>
/// </summary>
public sealed class TestCommand : IBuiltinCommand
{
    public string Name => "test";

    public int Execute(string[] args, ShellContext context)
    {
        // args[0] is the command name ("test" or "[")
        var isBracket = args.Length > 0 && args[0] == "[";

        if (isBracket)
        {
            // For `[`, the last argument must be `]`
            if (args.Length < 2 || args[^1] != "]")
            {
                Console.Error.WriteLine("radiance: [: missing ']'");
                return 2;
            }

            // Remove the leading `[` and trailing `]`
            var exprArgs = new string[args.Length - 2];
            for (var i = 1; i < args.Length - 1; i++)
                exprArgs[i - 1] = args[i];

            return EvaluateTest(exprArgs) ? 0 : 1;
        }
        else
        {
            // For `test`, skip args[0]
            if (args.Length <= 1)
                return 1; // test with no args returns false

            var exprArgs = new string[args.Length - 1];
            for (var i = 1; i < args.Length; i++)
                exprArgs[i - 1] = args[i];

            return EvaluateTest(exprArgs) ? 0 : 1;
        }
    }

    /// <summary>
    /// Creates a bracket alias command (<c>[</c>) that delegates to <see cref="TestCommand"/>.
    /// </summary>
    public static BracketCommand CreateBracketAlias() => new();

    /// <summary>
    /// Evaluates a test expression and returns true if the condition holds.
    /// </summary>
    private static bool EvaluateTest(string[] args)
    {
        if (args.Length == 0)
            return false;

        return EvaluateExpression(args, 0, out _);
    }

    /// <summary>
    /// Evaluates an expression starting at the given index, handling -o (or) operator.
    /// Returns the result and the index after the consumed tokens.
    /// </summary>
    private static bool EvaluateExpression(string[] args, int start, out int endIndex)
    {
        var result = EvaluateAndExpression(args, start, out endIndex);

        while (endIndex < args.Length && args[endIndex] == "-o")
        {
            endIndex++; // consume -o
            var right = EvaluateAndExpression(args, endIndex, out endIndex);
            result = result || right;
        }

        return result;
    }

    /// <summary>
    /// Evaluates an and-expression, handling -a (and) operator.
    /// </summary>
    private static bool EvaluateAndExpression(string[] args, int start, out int endIndex)
    {
        var result = EvaluatePrimary(args, start, out endIndex);

        while (endIndex < args.Length && args[endIndex] == "-a")
        {
            endIndex++; // consume -a
            var right = EvaluatePrimary(args, endIndex, out endIndex);
            result = result && right;
        }

        return result;
    }

    /// <summary>
    /// Evaluates a primary expression: negation, unary tests, or binary comparisons.
    /// </summary>
    private static bool EvaluatePrimary(string[] args, int start, out int endIndex)
    {
        // Handle ! (negation)
        if (start < args.Length && args[start] == "!")
        {
            var result = EvaluatePrimary(args, start + 1, out endIndex);
            return !result;
        }

        // Check for binary operators: look ahead for operator patterns
        if (start + 2 <= args.Length)
        {
            // Binary operators: arg1 op arg2
            if (start + 2 <= args.Length)
            {
                var op = args[start + 1];

                // String comparison operators
                if (op is "=" or "==" or "!=")
                {
                    if (start + 2 < args.Length)
                    {
                        endIndex = start + 3;
                        return op switch
                        {
                            "=" or "==" => args[start] == args[start + 2],
                            "!=" => args[start] != args[start + 2],
                            _ => false
                        };
                    }
                }

                // Integer comparison operators
                if (IsIntOperator(op) && start + 2 < args.Length)
                {
                    endIndex = start + 3;
                    return EvaluateIntComparison(args[start], op, args[start + 2]);
                }
            }
        }

        // Unary operators
        if (start < args.Length)
        {
            var arg = args[start];

            if (arg == "-z")
            {
                // -z: string is empty
                var val = start + 1 < args.Length ? args[start + 1] : "";
                endIndex = start + 2;
                return string.IsNullOrEmpty(val);
            }

            if (arg == "-n")
            {
                // -n: string is non-empty
                var val = start + 1 < args.Length ? args[start + 1] : "";
                endIndex = start + 2;
                return !string.IsNullOrEmpty(val);
            }

            if (arg == "-f")
            {
                // -f: file exists and is a regular file
                var path = start + 1 < args.Length ? args[start + 1] : "";
                endIndex = start + 2;
                return File.Exists(path);
            }

            if (arg == "-d")
            {
                // -d: file exists and is a directory
                var path = start + 1 < args.Length ? args[start + 1] : "";
                endIndex = start + 2;
                return Directory.Exists(path);
            }

            if (arg == "-e")
            {
                // -e: file exists
                var path = start + 1 < args.Length ? args[start + 1] : "";
                endIndex = start + 2;
                return File.Exists(path) || Directory.Exists(path);
            }

            if (arg == "-s")
            {
                // -s: file exists and has size > 0
                var path = start + 1 < args.Length ? args[start + 1] : "";
                endIndex = start + 2;
                if (!File.Exists(path)) return false;
                try { return new FileInfo(path).Length > 0; }
                catch { return false; }
            }

            if (arg == "-r")
            {
                // -r: file is readable
                var path = start + 1 < args.Length ? args[start + 1] : "";
                endIndex = start + 2;
                try { return File.Exists(path) && new FileInfo(path).Length >= 0; }
                catch { return false; }
            }

            if (arg == "-w")
            {
                // -w: file is writable
                var path = start + 1 < args.Length ? args[start + 1] : "";
                endIndex = start + 2;
                try { return File.Exists(path); }
                catch { return false; }
            }

            if (arg == "-x")
            {
                // -x: file is executable
                var path = start + 1 < args.Length ? args[start + 1] : "";
                endIndex = start + 2;
                if (!File.Exists(path)) return false;
                try
                {
                    // On Unix, check execute permission
                    if (!OperatingSystem.IsWindows())
                    {
                        var mode = System.IO.File.GetUnixFileMode(path);
                        return (mode & System.IO.UnixFileMode.UserExecute) != 0;
                    }
                    return true; // On Windows, all files are "executable"
                }
                catch { return false; }
            }
        }

        // Single argument: true if non-empty string
        endIndex = start + 1;
        return !string.IsNullOrEmpty(args[start]);
    }

    /// <summary>
    /// Checks if the operator is an integer comparison operator.
    /// </summary>
    private static bool IsIntOperator(string op) =>
        op is "-eq" or "-ne" or "-lt" or "-le" or "-gt" or "-ge";

    /// <summary>
    /// Evaluates an integer comparison between two string values.
    /// Returns false if either value is not a valid integer.
    /// </summary>
    private static bool EvaluateIntComparison(string left, string op, string right)
    {
        // Try to parse both as integers
        if (!TryParseInt(left, out var l) || !TryParseInt(right, out var r))
            return false;

        return op switch
        {
            "-eq" => l == r,
            "-ne" => l != r,
            "-lt" => l < r,
            "-le" => l <= r,
            "-gt" => l > r,
            "-ge" => l >= r,
            _ => false
        };
    }

    /// <summary>
    /// Tries to parse a string as an integer, handling negative numbers.
    /// </summary>
    private static bool TryParseInt(string s, out int value)
    {
        return int.TryParse(s, System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out value);
    }
}

/// <summary>
/// Bracket command (<c>[</c>) — alias for <see cref="TestCommand"/>.
/// Identical to <c>test</c> but requires <c>]</c> as the last argument.
/// </summary>
public sealed class BracketCommand : IBuiltinCommand
{
    public string Name => "[";

    public int Execute(string[] args, ShellContext context)
    {
        // Delegate to TestCommand which handles [ ... ] syntax
        var testCmd = new TestCommand();
        return testCmd.Execute(args, context);
    }
}