using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Radiance.Interpreter;

namespace Radiance.Agent;

/// <summary>
/// Defines and executes tools available to the Lira agent.
/// Tools include: run_command, read_file, write_file, list_directory.
/// </summary>
public sealed class AgentTools
{
    private readonly ShellContext _context;

    /// <summary>
    /// Destructive tool names that require user confirmation before execution.
    /// </summary>
    private static readonly HashSet<string> DestructiveTools = new(StringComparer.Ordinal)
    {
        "run_command", "write_file"
    };

    public AgentTools(ShellContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Gets the tool definitions in OpenAI function calling format.
    /// </summary>
    /// <returns>A list of tool definitions.</returns>
    public static List<ToolDefinition> GetToolDefinitions()
    {
        return
        [
            new ToolDefinition
            {
                Function = new ToolFunction
                {
                    Name = "run_command",
                    Description = "Execute a shell command and return its output. Use this to run any shell command, inspect system state, install packages, run scripts, etc. The command runs in the current working directory.",
                    Parameters = new ToolParameters
                    {
                        Properties = new Dictionary<string, ToolProperty>
                        {
                            ["command"] = new()
                            {
                                Type = "string",
                                Description = "The shell command to execute"
                            }
                        },
                        Required = ["command"]
                    }
                }
            },
            new ToolDefinition
            {
                Function = new ToolFunction
                {
                    Name = "read_file",
                    Description = "Read the contents of a file. Returns the file content with line numbers. Supports reading specific line ranges.",
                    Parameters = new ToolParameters
                    {
                        Properties = new Dictionary<string, ToolProperty>
                        {
                            ["path"] = new()
                            {
                                Type = "string",
                                Description = "The path to the file to read (absolute or relative to CWD)"
                            },
                            ["start_line"] = new()
                            {
                                Type = "integer",
                                Description = "Optional starting line number (1-based). If omitted, reads from the beginning."
                            },
                            ["end_line"] = new()
                            {
                                Type = "integer",
                                Description = "Optional ending line number (1-based, inclusive). If omitted, reads to the end."
                            }
                        },
                        Required = ["path"]
                    }
                }
            },
            new ToolDefinition
            {
                Function = new ToolFunction
                {
                    Name = "write_file",
                    Description = "Write content to a file. Creates the file if it doesn't exist, overwrites if it does. Use with caution — this is a destructive operation.",
                    Parameters = new ToolParameters
                    {
                        Properties = new Dictionary<string, ToolProperty>
                        {
                            ["path"] = new()
                            {
                                Type = "string",
                                Description = "The path to the file to write (absolute or relative to CWD)"
                            },
                            ["content"] = new()
                            {
                                Type = "string",
                                Description = "The content to write to the file"
                            }
                        },
                        Required = ["path", "content"]
                    }
                }
            },
            new ToolDefinition
            {
                Function = new ToolFunction
                {
                    Name = "list_directory",
                    Description = "List the contents of a directory. Shows files and subdirectories with their types. Defaults to the current working directory.",
                    Parameters = new ToolParameters
                    {
                        Properties = new Dictionary<string, ToolProperty>
                        {
                            ["path"] = new()
                            {
                                Type = "string",
                                Description = "The directory path to list. Defaults to the current working directory."
                            },
                            ["recursive"] = new()
                            {
                                Type = "boolean",
                                Description = "Whether to list contents recursively. Default: false."
                            }
                        },
                        Required = []
                    }
                }
            }
        ];
    }

    /// <summary>
    /// Checks if a tool requires user confirmation before execution.
    /// </summary>
    /// <param name="toolName">The tool name.</param>
    /// <returns>True if the tool is destructive and needs confirmation.</returns>
    public static bool RequiresConfirmation(string toolName) => DestructiveTools.Contains(toolName);

    /// <summary>
    /// Executes a tool call and returns the result string.
    /// </summary>
    /// <param name="toolName">The name of the tool to execute.</param>
    /// <param name="arguments">The JSON arguments string.</param>
    /// <returns>The tool execution result.</returns>
    public string Execute(string toolName, string arguments)
    {
        try
        {
            return toolName switch
            {
                "run_command" => ExecuteCommand(arguments),
                "read_file" => ReadFile(arguments),
                "write_file" => WriteFile(arguments),
                "list_directory" => ListDirectory(arguments),
                _ => $"Error: Unknown tool '{toolName}'"
            };
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    /// <summary>
    /// Formats a tool call for display to the user.
    /// </summary>
    /// <param name="toolName">The tool name.</param>
    /// <param name="arguments">The JSON arguments string.</param>
    /// <returns>A human-readable description of the tool call.</returns>
    public static string FormatToolCall(string toolName, string arguments)
    {
        try
        {
            return toolName switch
            {
                "run_command" => $"🔧 run_command({ExtractArg(arguments, "command")})",
                "read_file" => FormatReadFile(arguments),
                "write_file" => $"📝 write_file({ExtractArg(arguments, "path")})",
                "list_directory" => $"📂 list_directory({(ExtractArg(arguments, "path") is { Length: > 0 } p ? p : ".")})",
                _ => $"🔧 {toolName}({arguments})"
            };
        }
        catch
        {
            return $"🔧 {toolName}({arguments})";
        }
    }

    // ──── Tool Implementations ────

    /// <summary>
    /// Executes a shell command and captures its output.
    /// </summary>
    private string ExecuteCommand(string arguments)
    {
        var cmd = ExtractArg(arguments, "command");
        if (string.IsNullOrEmpty(cmd))
            return "Error: 'command' argument is required";

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "/bin/bash",
                Arguments = $"-c {cmd.EscapeForShell()}",
                WorkingDirectory = _context.CurrentDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = false,
                CreateNoWindow = true
            };

            // Forward environment variables
            foreach (var name in _context.ExportedVariableNames)
            {
                var value = _context.GetVariable(name);
                if (!string.IsNullOrEmpty(value))
                    psi.EnvironmentVariables[name] = value;
            }

            using var process = new Process { StartInfo = psi };
            var stdout = new StringBuilder();
            var stderr = new StringBuilder();

            process.OutputDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                    stdout.AppendLine(e.Data);
            };
            process.ErrorDataReceived += (_, e) =>
            {
                if (e.Data is not null)
                    stderr.AppendLine(e.Data);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            // Timeout after 30 seconds
            if (!process.WaitForExit(30_000))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return $"Error: Command timed out after 30 seconds\n{stdout}";
            }

            var result = new StringBuilder();

            if (stdout.Length > 0)
                result.Append(stdout.ToString().TrimEnd());

            if (stderr.Length > 0)
            {
                if (result.Length > 0)
                    result.AppendLine();
                result.Append($"[stderr] {stderr.ToString().TrimEnd()}");
            }

            result.AppendLine($"\n[exit code: {process.ExitCode}]");

            return result.ToString();
        }
        catch (Exception ex)
        {
            return $"Error executing command: {ex.Message}";
        }
    }

    /// <summary>
    /// Reads a file's contents, optionally with line range.
    /// </summary>
    private string ReadFile(string arguments)
    {
        var path = ResolvePath(ExtractArg(arguments, "path"));
        var startLine = ExtractIntArg(arguments, "start_line") ?? 1;
        var endLine = ExtractIntArg(arguments, "end_line") ?? -1;

        if (!File.Exists(path))
            return $"Error: File not found: {path}";

        try
        {
            var lines = File.ReadAllLines(path);

            // Clamp range
            startLine = Math.Max(1, startLine);
            var effectiveEnd = endLine < 0 ? lines.Length : Math.Min(endLine, lines.Length);

            if (startLine > lines.Length)
                return $"Error: File has {lines.Length} lines, but start_line is {startLine}";

            var maxLines = 500; // Safety limit
            var totalLines = effectiveEnd - startLine + 1;
            var truncated = false;

            if (totalLines > maxLines)
            {
                effectiveEnd = startLine + maxLines - 1;
                truncated = true;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"File: {path} ({lines.Length} lines total)");

            for (var i = startLine - 1; i < effectiveEnd && i < lines.Length; i++)
            {
                var lineNum = (i + 1).ToString().PadLeft(4);
                sb.AppendLine($"{lineNum} │ {lines[i]}");
            }

            if (truncated)
            {
                sb.AppendLine($"  ... ({totalLines - maxLines} more lines, use start_line/end_line to read more)");
            }

            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            return $"Error reading file: {ex.Message}";
        }
    }

    /// <summary>
    /// Writes content to a file.
    /// </summary>
    private string WriteFile(string arguments)
    {
        var path = ResolvePath(ExtractArg(arguments, "path"));
        var content = ExtractArg(arguments, "content");

        if (string.IsNullOrEmpty(path))
            return "Error: 'path' argument is required";

        try
        {
            // Create directory if it doesn't exist
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var lines = content.Split('\n').Length;
            File.WriteAllText(path, content);
            return $"Successfully wrote {lines} lines to {path}";
        }
        catch (Exception ex)
        {
            return $"Error writing file: {ex.Message}";
        }
    }

    /// <summary>
    /// Lists directory contents.
    /// </summary>
    private string ListDirectory(string arguments)
    {
        var path = ResolvePath(ExtractArg(arguments, "path"));
        if (string.IsNullOrEmpty(path))
            path = _context.CurrentDirectory;

        var recursive = ExtractBoolArg(arguments, "recursive");

        if (!Directory.Exists(path))
            return $"Error: Directory not found: {path}";

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Contents of {path}:");

            ListDirContents(path, sb, recursive, 0);

            return sb.ToString().TrimEnd();
        }
        catch (Exception ex)
        {
            return $"Error listing directory: {ex.Message}";
        }
    }

    /// <summary>
    /// Recursively lists directory contents with indentation.
    /// </summary>
    private static void ListDirContents(string path, StringBuilder sb, bool recursive, int depth)
    {
        var indent = new string(' ', depth * 2);
        var maxEntries = 200; // Safety limit
        var count = 0;

        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(path))
            {
                if (count++ >= maxEntries)
                {
                    sb.AppendLine($"{indent}  ... (more entries, listing truncated)");
                    break;
                }

                var name = Path.GetFileName(entry);
                var isDir = Directory.Exists(entry);

                if (isDir)
                {
                    sb.AppendLine($"{indent}  📁 {name}/");

                    if (recursive && depth < 5) // Max recursion depth
                    {
                        ListDirContents(entry, sb, recursive, depth + 1);
                    }
                }
                else
                {
                    try
                    {
                        var info = new FileInfo(entry);
                        var size = FormatFileSize(info.Length);
                        sb.AppendLine($"{indent}  📄 {name} ({size})");
                    }
                    catch
                    {
                        sb.AppendLine($"{indent}  📄 {name}");
                    }
                }
            }
        }
        catch (UnauthorizedAccessException)
        {
            sb.AppendLine($"{indent}  (access denied)");
        }
    }

    // ──── Helpers ────

    /// <summary>
    /// Resolves a path relative to the current working directory.
    /// Handles ~ expansion.
    /// </summary>
    private string ResolvePath(string? path)
    {
        if (string.IsNullOrEmpty(path))
            return _context.CurrentDirectory;

        // Tilde expansion
        if (path.StartsWith("~/"))
        {
            var home = Environment.GetEnvironmentVariable("HOME")
                ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            path = home + path[1..];
        }

        // Make absolute
        if (!Path.IsPathRooted(path))
        {
            path = Path.GetFullPath(Path.Combine(_context.CurrentDirectory, path));
        }

        return path;
    }

    /// <summary>
    /// Extracts a string argument from the JSON arguments.
    /// </summary>
    private static string ExtractArg(string json, string name)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty(name, out var prop))
            {
                return prop.ValueKind == JsonValueKind.String ? prop.GetString() ?? "" : prop.ToString();
            }
            return "";
        }
        catch
        {
            return "";
        }
    }

    /// <summary>
    /// Extracts an integer argument from the JSON arguments.
    /// </summary>
    private static int? ExtractIntArg(string json, string name)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Number)
            {
                return prop.GetInt32();
            }
            return null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Extracts a boolean argument from the JSON arguments.
    /// </summary>
    private static bool ExtractBoolArg(string json, string name)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.True)
            {
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Formats a read_file tool call for display.
    /// </summary>
    private static string FormatReadFile(string arguments)
    {
        var path = ExtractArg(arguments, "path");
        var startLine = ExtractIntArg(arguments, "start_line");
        var endLine = ExtractIntArg(arguments, "end_line");

        var range = startLine.HasValue ? $" (lines {startLine}-{(endLine.HasValue ? endLine : "end")})" : "";
        return $"📖 read_file({path}{range})";
    }

    /// <summary>
    /// Formats a file size in human-readable form.
    /// </summary>
    private static string FormatFileSize(long bytes)
    {
        return bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{bytes / 1024} KB",
            < 1024 * 1024 * 1024 => $"{bytes / (1024 * 1024)} MB",
            _ => $"{bytes / (1024 * 1024 * 1024)} GB"
        };
    }
}

/// <summary>
/// Extension methods for the Agent namespace.
/// </summary>
internal static class AgentExtensions
{
    /// <summary>
    /// Escapes a string for use in a shell command argument.
    /// </summary>
    public static string EscapeForShell(this string s)
    {
        if (!s.Any(c => char.IsWhiteSpace(c) || c is '\'' or '"' or '\\' or '$' or '`' or '!' or '*' or '?' or '(' or ')' or '[' or ']' or '{' or '}'))
            return s;

        return $"\"{s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("$", "\\$").Replace("`", "\\`")}\"";
    }
}