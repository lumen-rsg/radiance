using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;

namespace Radiance.Themes;

/// <summary>
/// A theme loaded from a JSON definition file.
/// Allows users to create custom themes without writing C# code.
/// </summary>
public sealed class JsonTheme : ITheme
{
    private readonly JsonThemeDefinition _def;

    public string Name => _def.Name;
    public string Description => _def.Description ?? $"Custom theme: {_def.Name}";
    public string Author => _def.Author ?? "User";

    private JsonTheme(JsonThemeDefinition definition)
    {
        _def = definition;
    }

    public string RenderPrompt(PromptContext ctx)
    {
        var sb = new StringBuilder();
        if (_def.LeftPrompt != null)
        {
            foreach (var seg in _def.LeftPrompt)
            {
                sb.Append(RenderSegment(seg, ctx));
            }
        }
        return sb.ToString();
    }

    public string RenderRightPrompt(PromptContext ctx)
    {
        var sb = new StringBuilder();
        if (_def.RightPrompt != null)
        {
            foreach (var seg in _def.RightPrompt)
            {
                sb.Append(RenderSegment(seg, ctx));
            }
        }
        return sb.ToString();
    }

    private string RenderSegment(JsonSegment seg, PromptContext ctx)
    {
        var text = seg.Type switch
        {
            "user" => ctx.User,
            "host" => ctx.Host,
            "cwd" => seg.FullPath == true ? ctx.TildeCwd : ctx.ShortCwd,
            "full_cwd" => ctx.TildeCwd,
            "git" => FormatGitSegment(ctx, seg),
            "git_branch" => ctx.GitBranch ?? "",
            "prompt_char" => ctx.LastExitCode == 0
                ? (seg.Text ?? "$")
                : (seg.ErrorText ?? seg.Text ?? "$"),
            "exit_code" => ctx.LastExitCode == 0 ? "" : ctx.LastExitCode.ToString(),
            "time" => string.IsNullOrEmpty(seg.Format) ? ctx.Time : ctx.Now.ToString(seg.Format),
            "date" => string.IsNullOrEmpty(seg.Format) ? ctx.Date : ctx.Now.ToString(seg.Format),
            "jobs" => ctx.JobCount > 0 ? $"[{ctx.JobCount} job{(ctx.JobCount > 1 ? "s" : "")}]" : "",
            "shell" => ctx.ShellName,
            "text" => seg.Text ?? "",
            "separator" => seg.Text ?? " ",
            "newline" => "\n",
            _ => ""
        };

        if (string.IsNullOrEmpty(text)) return "";

        // Determine color
        var fgColor = seg.Fg;
        if (seg.Type == "prompt_char" && ctx.LastExitCode != 0 && seg.ErrorFg != null)
            fgColor = seg.ErrorFg;
        if (seg.Type == "git" && ctx.GitDirty && seg.DirtyFg != null)
            fgColor = seg.DirtyFg;

        var result = new StringBuilder();

        if (seg.Bold == true) result.Append("\x1b[1m");
        if (seg.Italic == true) result.Append("\x1b[3m");
        if (fgColor != null) result.Append(ApplyAnsiColor(fgColor));
        if (seg.Bg != null) result.Append(ApplyBgColor(seg.Bg));

        result.Append(text);
        result.Append("\x1b[0m"); // Reset

        // Append suffix (separator)
        if (!string.IsNullOrEmpty(seg.Suffix)) result.Append(seg.Suffix);

        return result.ToString();
    }

    private static string FormatGitSegment(PromptContext ctx, JsonSegment seg)
    {
        if (string.IsNullOrEmpty(ctx.GitBranch)) return "";
        var branch = ctx.GitBranch;
        if (ctx.GitDirty) branch += seg.DirtyMarker ?? "*";
        var prefix = seg.Prefix ?? "";
        return $"{prefix}{branch}";
    }

    private static string ApplyAnsiColor(string color)
    {
        if (color.StartsWith('#') && color.Length == 7)
        {
            var r = int.Parse(color[1..3], System.Globalization.NumberStyles.HexNumber);
            var g = int.Parse(color[3..5], System.Globalization.NumberStyles.HexNumber);
            var b = int.Parse(color[5..7], System.Globalization.NumberStyles.HexNumber);
            return $"\x1b[38;2;{r};{g};{b}m";
        }

        var code = color.ToLowerInvariant() switch
        {
            "black" => 30, "red" => 31, "green" => 32, "yellow" => 33,
            "blue" => 34, "magenta" => 35, "cyan" => 36, "white" => 37,
            "bright_black" or "gray" or "grey" => 90,
            "bright_red" => 91, "bright_green" => 92, "bright_yellow" => 93,
            "bright_blue" => 94, "bright_magenta" => 95, "bright_cyan" => 96,
            "bright_white" => 97,
            "dark_gray" or "dark_grey" => 90,
            _ => 37
        };
        return $"\x1b[{code}m";
    }

    private static string ApplyBgColor(string color)
    {
        if (color.StartsWith('#') && color.Length == 7)
        {
            var r = int.Parse(color[1..3], System.Globalization.NumberStyles.HexNumber);
            var g = int.Parse(color[3..5], System.Globalization.NumberStyles.HexNumber);
            var b = int.Parse(color[5..7], System.Globalization.NumberStyles.HexNumber);
            return $"\x1b[48;2;{r};{g};{b}m";
        }

        var code = color.ToLowerInvariant() switch
        {
            "black" => 40, "red" => 41, "green" => 42, "yellow" => 43,
            "blue" => 44, "magenta" => 45, "cyan" => 46, "white" => 47,
            "bright_black" or "gray" or "grey" => 100,
            "bright_red" => 101, "bright_green" => 102, "bright_yellow" => 103,
            "bright_blue" => 104, "bright_magenta" => 105, "bright_cyan" => 106,
            "bright_white" => 107,
            _ => 49
        };
        return $"\x1b[{code}m";
    }

    /// <summary>
    /// Loads a JsonTheme from a file path.
    /// </summary>
    public static JsonTheme? LoadFromFile(string path)
    {
        try
        {
            var json = File.ReadAllText(path);
            var def = JsonSerializer.Deserialize<JsonThemeDefinition>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true
            });
            return def != null ? new JsonTheme(def) : null;
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>
/// JSON-serializable theme definition.
/// </summary>
internal sealed class JsonThemeDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "unnamed";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("author")]
    public string? Author { get; set; }

    [JsonPropertyName("left_prompt")]
    public List<JsonSegment>? LeftPrompt { get; set; }

    [JsonPropertyName("right_prompt")]
    public List<JsonSegment>? RightPrompt { get; set; }
}

/// <summary>
/// A single segment in a JSON theme prompt.
/// </summary>
internal sealed class JsonSegment
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "text";

    [JsonPropertyName("text")]
    public string? Text { get; set; }

    [JsonPropertyName("fg")]
    public string? Fg { get; set; }

    [JsonPropertyName("bg")]
    public string? Bg { get; set; }

    [JsonPropertyName("bold")]
    public bool? Bold { get; set; }

    [JsonPropertyName("italic")]
    public bool? Italic { get; set; }

    [JsonPropertyName("suffix")]
    public string? Suffix { get; set; }

    [JsonPropertyName("prefix")]
    public string? Prefix { get; set; }

    [JsonPropertyName("full_path")]
    public bool? FullPath { get; set; }

    [JsonPropertyName("error_fg")]
    public string? ErrorFg { get; set; }

    [JsonPropertyName("error_text")]
    public string? ErrorText { get; set; }

    [JsonPropertyName("dirty_fg")]
    public string? DirtyFg { get; set; }

    [JsonPropertyName("dirty_marker")]
    public string? DirtyMarker { get; set; }

    [JsonPropertyName("format")]
    public string? Format { get; set; }
}