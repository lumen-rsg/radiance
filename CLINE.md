# Radiance — BASH Interpreter & Shell

> A BASH-compatible shell and interpreter built from scratch in C# / .NET 10.0.

## Project Overview

Radiance is an interactive shell that implements BASH-style syntax, including command execution, pipelines, redirections, variables, control flow, functions, and more. It is built with a clean architecture separating lexing, parsing, interpretation, and shell interaction.

## Architecture

```
Input → Lexer → Tokens → Parser → AST → Interpreter → Execution
```

### Key Components

| Component | Location | Description |
|-----------|----------|-------------|
| **Shell** | `src/Shell/` | REPL loop, prompt rendering, command history |
| **Lexer** | `src/Lexer/` | Tokenizer — converts raw input into typed tokens |
| **Parser** | `src/Parser/` | Recursive-descent parser — builds an AST from tokens |
| **AST** | `src/Parser/Ast/` | Abstract Syntax Tree node definitions |
| **Interpreter** | `src/Interpreter/` | AST walker that executes commands |
| **Built-ins** | `src/Builtins/` | Built-in shell commands (echo, cd, pwd, export, etc.) |
| **Agent** | `src/Agent/` | Lira AI assistant — OpenAI-compatible API client, tools, agentic loop |
| **Plugins** | `src/Plugins/` | Plugin interface, manager, context, and `plugin` builtin |
| **Expansion** | `src/Expansion/` | Variable, glob, tilde, brace, and command substitution |
| **Utils** | `src/Utils/` | Path resolution, signal handling, helpers |

### Design Decisions

- **Parser type**: Recursive descent — natural fit for BASH grammar, easy to extend
- **Process spawning**: `System.Diagnostics.Process` — cross-platform .NET API
- **Pipeline I/O**: `AnonymousPipeServerStream` / `AnonymousPipeClientStream`
- **Test framework**: xUnit
- **Target framework**: .NET 10.0

## Roadmap

| Phase | Name | Status | Description |
|-------|------|--------|-------------|
| 1 | Foundation | ✅ Complete | REPL loop + basic command execution + builtins |
| 2 | Lexer & Parser | ✅ Complete | Proper tokenizer, AST, quoting |
| 3 | Pipelines & Redirections | ✅ Complete | Pipes (`\|`), file redirects (`>`, `<`, `>>`) |
| 4 | Variables & Expansion | ✅ Complete | `$VAR`, `$(cmd)`, `$((expr))`, tilde, glob |
| 5 | Control Flow | ✅ Complete | `if`, `for`, `while`, `case` |
| 6 | Advanced Features | ✅ Complete | Functions, aliases, job control, history, completion |
| 7 | Script Execution & Polish | ✅ Complete | `.sh` files, `source`, config, colorized output, bug fixes |
| 7.5 | QOL & Line Editing | ✅ Complete | Full line editing, Ctrl+R search, improved completion, TTY fix |
| 8 | Testing & Hardening | ✅ Complete | Unit/integration tests, POSIX compliance, 219/219 passing |

## Project Structure

```
Radiance/
├── Program.cs                     # Entry point
├── Radiance.csproj
├── CLINE.md                       # This file — agent context & changelog
├── src/
│   ├── Shell/
│   │   ├── RadianceShell.cs       # Main REPL loop
│   │   ├── Prompt.cs              # Prompt rendering
│   │   └── History.cs             # Command history
│   ├── Lexer/
│   │   ├── Lexer.cs               # Tokenizer
│   │   ├── Token.cs               # Token data class
│   │   └── TokenType.cs           # Token enum
│   ├── Parser/
│   │   ├── Parser.cs              # Recursive-descent parser → AST
│   │   └── Ast/                   # AST node definitions
│   ├── Interpreter/
│   │   ├── Interpreter.cs         # AST walker / executor
│   │   ├── ExecutionContext.cs    # Variables, env, CWD state
│   │   ├── ProcessManager.cs      # Spawn external processes
│   │   └── PipelineExecutor.cs    # Multi-process pipelines
│   ├── Builtins/
│   │   ├── IBuiltinCommand.cs     # Builtin command interface
│   │   ├── BuiltinRegistry.cs     # Command registry
│   │   └── *.cs                   # Individual builtin commands
│   ├── Plugins/
│   │   ├── IRadiancePlugin.cs     # Plugin interface
│   │   ├── PluginContext.cs       # Plugin API surface (register commands, access shell)
│   │   ├── PluginManager.cs       # Plugin discovery, loading, and lifecycle
│   │   └── PluginCommand.cs       # `plugin` builtin command
│   ├── Expansion/
│   │   └── *.cs                   # Variable, glob, tilde, brace expansion
│   └── Utils/
│       ├── PathResolver.cs        # $PATH lookup
│       └── SignalHandler.cs       # Signal handling
├── tests/
│   ├── Radiance.Tests.csproj
│   └── *.cs                       # Test files
└── README.md
```

## Changelog

### [1.3.4] — File Descriptor Redirection (`2>&1`) ✅

**Added:**

*File Descriptor Duplication (`>&`):*
- **Added support for `2>&1` and similar fd-duplication redirects** — the `>&` operator duplicates one file descriptor onto another (e.g., `2>&1` merges stderr into stdout)
- New `AmpersandGreaterThan` token type in lexer — `>&` is tokenized as a single operator (not `>` + `&`)
- Lexer recognizes `>` followed by `&` as `AmpersandGreaterThan` before checking standalone `>`
- Updated `RedirectNode` with `DuplicateTargetFd` parameter — stores the target fd number for `>&N` redirects, and `Target` is null for fd-dup redirects

*Parser fd-Prefix Detection:*
- New `ExtractFdPrefixFromWords()` method — detects when the last word in a command is actually an fd number prefix (e.g., `2` in `which dotnet 2>&1`). Removes it from the command words and passes it to `ParseRedirect` as the fd override
- `ParseRedirect` accepts `fdOverride` parameter — uses the extracted fd prefix (e.g., 2) instead of the default (0 for `<`, 1 for `>`)

*Stderr-to-Stdout Merging:*
- `PipelineExecutor.HasStderrToStdoutRedirect()` — detects `2>&1` redirects in command nodes
- External commands: creates a merged `MemoryStream` for both stdout and stderr, writes combined output to console after process exits
- Builtin commands: captures both `Console.Out` and `Console.Error` into the same `StringWriter`
- Function commands: redirects both `Console.Out` and `Console.Error` into the same `StringWriter`

**Modified files:**
- `src/Lexer/TokenType.cs` — added `AmpersandGreaterThan` token type
- `src/Lexer/Lexer.cs` — tokenize `>&` as `AmpersandGreaterThan`
- `src/Parser/Ast/RedirectNode.cs` — added `DuplicateTargetFd` parameter, made `Target` nullable
- `src/Parser/Parser.cs` — `ExtractFdPrefixFromWords()`, updated `ParseRedirect()` with fd override, `IsRedirectOperator()` includes `AmpersandGreaterThan`
- `src/Interpreter/PipelineExecutor.cs` — stderr/stdout merging for external/builtin/function commands, `HasStderrToStdoutRedirect()`
- `src/Interpreter/ProcessManager.cs` — `StartProcess` handles `stderrStream` parameter for fd-dup redirects

### [1.3.3] — TTY Fix for Interactive Commands ✅

**Bug Fixes:**

*Interactive TUI Applications (vim, btop, htop, nano, etc.):*
- **Fixed "Output is not to a terminal" / "Input is not from a terminal" warnings when launching vim and other interactive TUI applications** — these apps require direct terminal (TTY) access but were incorrectly getting piped/redirected streams instead
- Root cause: In v1.2.4, a check was added to detect command substitution context: `Console.Out is not StreamWriter`. The assumption was that the default `Console.Out` is a `StreamWriter`, but on .NET it's actually `TextWriter.Synchronized(new StreamWriter(...))` which wraps it in a `SyncTextWriter` (a private subclass of `TextWriter`, NOT `StreamWriter`). So `Console.Out is not StreamWriter` was **always true**, forcing every external command into captured-output mode with redirected streams
- Initial fix attempt (`Console.Out is StringWriter`) broke redirection tests — `Console.SetOut()` also wraps in `SyncTextWriter`, so the type check was always false, causing `$(cat file)` to return empty
- Final fix: Introduced `OutputCapture` utility with a `[ThreadStatic]` depth counter. `Push()`/`Pop()` called around every `Console.SetOut()` in `CommandSubstitution` and `PipelineExecutor`. `ProcessManager.Execute()` checks `OutputCapture.IsCapturing` instead of inspecting `Console.Out` type

**New files:**
- `src/Utils/OutputCapture.cs` — `[ThreadStatic]` depth counter for tracking Console.Out capture state

**Modified files:**
- `src/Interpreter/ProcessManager.cs` — checks `OutputCapture.IsCapturing` instead of `Console.Out` type
- `src/Expansion/CommandSubstitution.cs` — wraps `Console.SetOut()` with `OutputCapture.Push()`/`Pop()`
- `src/Interpreter/PipelineExecutor.cs` — wraps all 5 `Console.SetOut()` locations with `OutputCapture.Push()`/`Pop()`

### [1.3.2] — Command Output Formatting Fix ✅

**Bug Fixes:**

*Box Drawing Alignment in `radiance stats`, `radiance help`, `radiance fortune`:*
- **Fixed broken table borders in `radiance stats`** — box rows had mismatched widths because content lines overflowed the declared inner width by 5+ columns. The `│` right border was pushed out of alignment, producing a jagged right edge
- Root cause: All content lines used hardcoded space padding that didn't account for ANSI escape codes or variable-length data. The declared inner width was 38 but content was 43-44 chars
- **Fixed broken table borders in `radiance help`** — same issue with 2-3 column overflow on every content line
- **Fixed emoji display width in `radiance fortune`** — the 🍪 emoji is 2 display columns wide but was counted as 1, causing box misalignment

*New Utility Methods (`SparkleRenderer`):*
- `VisibleLength(string)` — computes display-column length of a string, properly ignoring ANSI escape sequences and counting surrogate pairs (emoji) as 2 columns
- `BoxLine(int width, string content)` — writes a single line inside `│` borders with automatic right-padding based on visible length. Made `internal` for reuse across builtin commands

*Approach:*
- `RenderStats()` — widened inner box to 44 columns, replaced all hardcoded padding with `BoxLine()` calls that dynamically compute trailing padding
- `RenderFortune()` — now uses `VisibleLength()` for display width calculations and dynamic padding
- `PrintHelp()` in `RadianceCommand` — widened inner box to 26 columns, switched to `BoxLine()` for all content lines

**Modified files:**
- `src/Utils/SparkleRenderer.cs` — rewrote `RenderStats()` with `BoxLine()`, updated `RenderFortune()` with `VisibleLength()`, added `BoxLine()` and `VisibleLength()` utility methods
- `src/Builtins/RadianceCommand.cs` — rewrote `PrintHelp()` with `BoxLine()` and proper header alignment

### [1.3.1] — Error Reporting Fix ✅

**Bug Fixes:**

*Command Not Found Error Reporting:*
- **Fixed missing "command not found" error message** — when launching a nonexistent command, the shell silently returned exit code 127 without printing any error to stderr
- Root cause: `ProcessManager.Execute()` returned 127 when `ResolveCommand()` returned null, but never wrote an error message to stderr. The error was only printed in the `PipelineExecutor` path (piped/redirected commands), not in the direct execution path (simple commands)
- Fix: Added proper error messages in `ProcessManager.Execute()` using `ColorOutput.WriteError()` for consistent, colorized output

*Permission Denied Detection:*
- **Added "Permission denied" error (exit code 126)** — when a file exists on PATH but is not executable, or when a direct path references a non-executable file
- New `PathResolver.ResolveWithExecutability()` method returns executability information alongside the resolved path
- New `PathResolver.IsExecutable()` helper checks Unix file mode execute bits (`UnixFileMode.UserExecute | GroupExecute | OtherExecute`) on macOS/Linux, file extension on Windows
- New `ResolveResult` record struct for returning path + executability info

*Consistent Error Formatting:*
- **Updated all error messages to use `ColorOutput.WriteError()`** — previously, `PipelineExecutor` and `ProcessManager` used raw `Console.Error.WriteLine()`, producing inconsistent formatting
- All command execution errors now display with red bold "radiance: error:" prefix
- Added `Win32Exception` catch with native error code 13 (`EACCES`) detection for permission-denied at the OS level

**Modified files:**
- `src/Interpreter/ProcessManager.cs` — added "command not found" / "Permission denied" messages, `ColorOutput` formatting, `Win32Exception` handling
- `src/Interpreter/PipelineExecutor.cs` — switched to `ColorOutput.WriteError()` for consistency
- `src/Utils/PathResolver.cs` — added `ResolveWithExecutability()`, `ResolveResult`, `IsExecutable()`

### [1.3.0] — Lira AI Agent ✅

**Added:**

**Lira AI Assistant (`src/Agent/`):**
- `AgentConfig` — Configuration model loaded from `~/.radiance/agent.json` or environment variables (`LIRA_API_KEY`, `LIRA_BASE_URL`, `LIRA_MODEL`). Supports any OpenAI-compatible API (OpenAI, Ollama, LM Studio, vLLM, etc.)
- `OpenAiClient` — Full OpenAI chat completions API client with streaming SSE support, function/tool calling, and typed data models for all request/response types
- `AgentTools` — 6 built-in tools the agent can invoke: `run_command` (CLI execution), `read_file`, `write_file`, `list_directory`, `create_directory`, `shell_info`. Includes JSON schema definitions, formatting, and confirmation gating for destructive operations
- `LiraAgent` — The main agentic loop with streaming text output, automatic tool call processing, user y/n confirmation for commands, conversation history management, and code block rendering with syntax highlighting and line numbers

**Agent Features:**
- Streaming API responses with real-time text display
- Fenced code block detection and rendering with ANSI syntax highlighting (keywords, strings, comments, numbers, operators)
- Line numbers and colored borders for code blocks
- Collapsed view for long code blocks (>20 lines, shows first/last 8 lines)
- Tool calling with agentic loop (API can call tools, see results, and continue responding)
- User confirmation required before executing CLI commands (y/n prompt)
- Conversation history with configurable max length
- In-chat slash commands: `/help`, `/exit`, `/quit`, `/reset`, `/config`, `/model`, `/history`

**`agent` Builtin Command (`src/Builtins/AgentCommand.cs`):**
- `agent` — Launch interactive Lira chat session
- `agent config` — Show current API configuration
- `agent setup` — Show setup instructions
- `agent reset` — Reset conversation and start fresh

**Shell Integration:**
- Registered as a builtin command in `BuiltinRegistry.CreateDefault()`
- Welcome banner updated: "Type 'agent' for AI help!"
- Passes `ShellContext` to the agent for working directory awareness

**New files:**
- `src/Agent/AgentConfig.cs`
- `src/Agent/OpenAiClient.cs`
- `src/Agent/AgentTools.cs`
- `src/Agent/LiraAgent.cs`
- `src/Builtins/AgentCommand.cs`

**Modified files:**
- `src/Builtins/BuiltinRegistry.cs` — register `AgentCommand`
- `src/Shell/RadianceShell.cs` — updated welcome message

### [1.2.5] — Theme Subcommand Bug Fix ✅

**Bug Fixes:**

*Theme Command Subcommand Resolution:*
- **Fixed `theme` command always reporting "unknown subcommand 'theme'"** — running `theme`, `theme help`, `theme list`, or any `theme` subcommand would produce `theme: unknown subcommand 'theme'`
- Root cause: `ThemeCommand.Execute()` assumed `args[0]` was the subcommand, but the builtin registry passes the full argv including the command name as `args[0]` (like C's `argv`). So `args[0]` was always `"theme"`, and the actual subcommand (`"help"`, `"list"`, etc.) was in `args[1]`
- Fix: Updated all argument indexing in `ThemeCommand`:
  - `Execute()`: check `args.Length <= 1` for no-subcommand case, use `args[1]` for subcommand
  - `SetTheme()`: theme name moved from `args[1]` to `args[2]`
  - `ShowInfo()`: theme name moved from `args[1]` to `args[2]`
- Matches the convention already used by `RadianceCommand` (which correctly uses `args.Length > 1 ? args[1]`)

**Modified files:**
- `src/Builtins/ThemeCommand.cs` — fixed args indexing to account for command name in args[0]

### [1.2.4] — Redirection Bug Fix ✅

**Bug Fixes:**

*External Command Output Capture in Command Substitution:*
- **Fixed `$(cat file)` and similar command substitutions returning empty string** — when using external commands (like `cat`, `head`, `tail`) inside `$(...)` to read back file contents written by shell redirections, the output was going directly to the terminal instead of being captured
- Root cause: `ProcessManager.Execute()` used `Console.IsOutputRedirected` to decide between terminal-inherited mode and captured-output mode. However, `Console.IsOutputRedirected` only reflects OS-level stream redirection, NOT `Console.SetOut()` (which is what command substitution uses to capture output via `new StringWriter()`)
- Fix: Added `Console.Out is not StreamWriter` check — the default `Console.Out` is a `StreamWriter`, but after `Console.SetOut(new StringWriter())` it becomes a `StringWriter`, so the type check correctly detects command substitution context
- This fix ensures external command output is properly captured in all contexts: command substitution, pipelines, and script execution
- Interactive TTY support (vim, btop, etc.) is unaffected — terminal-inherited mode still works when `Console.Out` is the default `StreamWriter`
- Fixes stress test failures in Section 12 (Redirections): tests 12.1, 12.2, 12.4, and 12.5 now pass
- Stress test: **198/198 tests passing** (was 194/198)
- Unit tests: **261/261 tests passing**

**Modified files:**
- `src/Interpreter/ProcessManager.cs` — output capture detection now checks `Console.Out is not StreamWriter` in addition to `Console.IsOutputRedirected`

### [1.2.3] — Login Shell Support ✅

**Added:**

**Login Shell Mode:**
- `radiance -l` / `radiance --login` — launches Radiance as a login shell
- Combined flags supported: `radiance -il` (interactive login)
- Login shell detection via `argv[0]` starting with `-` (standard UNIX convention)
- BASH-compatible profile sourcing on login:
  1. `/etc/profile` (system-wide profile)
  2. First found of: `~/.bash_profile`, `~/.bash_login`, `~/.profile` (user-level profile)
  3. Then `~/.radiance_rc` (Radiance-specific config, always sourced)
- Sets `SHELL` environment variable to the Radiance executable path
- Shebang lines (`#!`) are automatically skipped in profile files
- Errors in profile files produce warnings but don't abort the shell

**`/etc/shells` Registration:**
- To register Radiance as a standard shell: `echo /opt/homebrew/bin/Radiance | sudo tee -a /etc/shells`
- Then change shell with: `chsh -s /opt/homebrew/bin/Radiance`

**Modified files:**
- `Program.cs` — `-l` / `--login` flag parsing, login shell argv[0] detection, version bump
- `src/Shell/RadianceShell.cs` — `RadianceShell(bool isLoginShell)` constructor, `SourceLoginProfiles()`, `SourceFileIfExists()`, login profile sourcing in `Run()`

### [1.2.1] — ✨ The `radiance` Command — Sparkle Mode ✅

**Added:**

**`radiance` Builtin Command (`src/Builtins/RadianceCommand.cs`):**
- `radiance` — Displays a gorgeous gradient-colored ASCII art logo with the Radiance banner
- `radiance spark` — Triggers a sparkle cascade animation with ✦ ✧ ⋆ characters in random ANSI colors across the terminal
- `radiance fortune` — Shows a random nerdy developer fortune cookie in a decorative box border (24 curated quotes)
- `radiance stats` — Displays a stylish session dashboard: uptime, commands run, unique commands, top 5 commands with bar charts
- `radiance matrix` — Matrix-style green digital rain animation (2 seconds) — "Wake up, Radiance..."
- `radiance help` — Styled usage information

**Sparkle Renderer (`src/Utils/SparkleRenderer.cs`):**
- `SparkleRenderer` — static utility class with ANSI art rendering, sparkle animations, matrix rain, fortune display, stats dashboard
- `SessionStats` — session statistics tracker (command count, frequency map, top commands, uptime)
- Logo rendering with gradient ANSI colors (cyan → aqua → pink → gold)
- Word-wrapping and box-drawing utilities

**Session Statistics Tracking:**
- `RadianceShell` tracks every command executed via `SessionStats.RecordCommand()`
- First word of each input line extracted as command name for frequency tracking
- Stats wired to `RadianceCommand` for the `stats` subcommand

**Welcome Message Update:**
- Welcome banner now hints at the `radiance` command: "Try 'radiance' for something fun!"

**New files:**
- `src/Utils/SparkleRenderer.cs`
- `src/Builtins/RadianceCommand.cs`

**Modified files:**
- `src/Builtins/BuiltinRegistry.cs` — register `RadianceCommand`
- `src/Shell/RadianceShell.cs` — session stats tracking, wire radiance command, updated welcome message

### [1.2.0] — Theming System ✅

**Added:**

**Theme Engine (`src/Themes/`):**
- `ITheme` interface — contract for themes with `RenderPrompt()`, `RenderRightPrompt()`, metadata
- `PromptContext` — rich context object (user, host, cwd, git branch/dirty, exit code, jobs, time, etc.)
- `ThemeBase` abstract class — ANSI color helpers, segment builders, text width calculation, `AnsiColor` enum
- `ThemeManager` — registry, loading, switching, persistence to `~/.radiance/config.json`
- `JsonTheme` — declarative JSON theme loader for custom themes without writing C# code

**6 Built-in Themes (`src/Themes/Builtins/`):**
- `DefaultTheme` — classic user@host cwd $ with git support
- `MinimalTheme` — clean arrow + directory name only
- `PowerlineTheme` — Powerline-style segments with colored backgrounds and separators
- `RainbowTheme` — vibrant multi-color with 2-line layout
- `DarkTheme` — optimized for dark terminal backgrounds
- `LightTheme` — optimized for light terminal backgrounds

**`theme` Built-in Command (`src/Builtins/ThemeCommand.cs`):**
- `theme list` — list all available themes
- `theme set <name>` — switch theme (persists to config)
- `theme current` — show current theme info
- `theme info <name>` — show theme details and preview
- `theme path` — show custom themes directory

**Custom JSON Themes (`~/.radiance/themes/*.json`):**
- Declarative segment-based format with color/style customization
- Supports: user, host, cwd, git, prompt_char, time, date, jobs, exit_code, text segments
- Colors: named ANSI colors or hex RGB (#ff0000)
- Style: bold, italic, fg, bg, suffix, prefix, error_fg, dirty_fg
- Example theme: `themes/example.json`

**RPROMPT Support:**
- Right-aligned prompt via ANSI cursor manipulation
- Used by Powerline, Rainbow, Dark, and Light themes

**Shell Integration:**
- `RadianceShell` uses `ThemeManager` for all prompt rendering
- `Prompt.GetGitBranch()` / `Prompt.IsGitDirty()` — public git helpers for themes
- Theme initialized in shell constructor, registered as builtin

**Tests:**
- 12 new tests covering themes, context, JSON loading, manager operations
- All 231 tests passing

**New files:**
- `src/Themes/ITheme.cs`, `PromptContext.cs`, `ThemeBase.cs`, `ThemeManager.cs`, `JsonTheme.cs`
- `src/Themes/Builtins/DefaultTheme.cs`, `MinimalTheme.cs`, `PowerlineTheme.cs`, `RainbowTheme.cs`, `DarkTheme.cs`, `LightTheme.cs`
- `src/Builtins/ThemeCommand.cs`
- `themes/example.json`
- `tests/ThemeTests.cs`

**Modified files:**
- `src/Shell/RadianceShell.cs` — theme manager integration
- `src/Shell/Prompt.cs` — public git helper methods

### [1.1.1] — Script Execution Bug Fix ✅

**Bug Fixes:**

*Shebang Script Execution:*
- **Fixed `./script.sh` failing with "No such file or directory"** — when running a script with `#!/bin/bash` (or similar shebang referencing `bash`/`sh`) from within the Radiance shell, the shell correctly detects and executes it internally instead of trying to spawn `bash` as an external process (which may not exist at `/bin/bash` on macOS)
- Root cause: `ProcessManager` used `Process.Start()` with `UseShellExecute=true`, which failed because the shebang interpreter (`/bin/bash`) didn't exist as a runnable process. Radiance is a BASH-compatible shell and should handle `#!/bin/bash` scripts internally
- Fix: Added shebang detection in `ShellInterpreter.VisitSimpleCommand()` — when a command contains `/` (indicating a path), the interpreter reads the shebang line. If it references `radiance`, `bash`, or `/sh`, the script is executed internally via the new `ScriptFileExecutor` callback
- New `ShellContext.ScriptFileExecutor` callback property — wired to `RadianceShell.ExecuteScript()` for in-process script execution
- New `TryReadShebang()` helper in `ShellInterpreter` — efficiently reads the first line of a file to extract the shebang interpreter path

**Modified files:**
- `src/Interpreter/ExecutionCtx.cs` — added `ScriptFileExecutor` callback property
- `src/Interpreter/Interpreter.cs` — shebang detection in `VisitSimpleCommand()`, `TryReadShebang()` helper
- `src/Shell/RadianceShell.cs` — wired `ScriptFileExecutor` callback in constructor

### [1.1.0] — Plugin System ✅

**Added:**

**Plugin System (`src/Plugins/`):**
- `IRadiancePlugin` interface — contract for plugins with `Name`, `Version`, `Description`, `OnLoad()`, `OnUnload()` lifecycle methods
- `PluginContext` — safe API surface passed to plugins on load:
  - `RegisterCommand(IBuiltinCommand)` — register custom commands that become available as builtins
  - `UnregisterCommand(string)` — remove a previously registered command
  - `Shell` property — access to `ShellContext` for reading/writing variables, setting aliases, etc.
  - Tracks all registered commands per-plugin for automatic cleanup on unload
- `PluginManager` — discovers, loads, and manages plugin lifecycle:
  - Scans `~/.radiance/plugins/` for `.dll` files on startup (creates directory if missing)
  - Uses `Assembly.LoadFrom()` + reflection to find `IRadiancePlugin` implementations
  - Supports runtime loading via `plugin load <path>` and unloading via `plugin unload <name>`
  - `UnloadAll()` called on shell exit for clean shutdown
  - Prevents duplicate plugin loading (by name)
  - Rollback of registered commands if `OnLoad()` fails
- `PluginCommand` — `plugin` builtin command for managing plugins at runtime:
  - `plugin list` — display all loaded plugins with name, version, description
  - `plugin load <path>` — load a plugin DLL at runtime
  - `plugin unload <name>` — unload a plugin by name (removes its commands)
  - `plugin help` — usage information

**Registry Extension:**
- Added `BuiltinRegistry.Unregister(string)` — removes a registered command by name, used by plugin cleanup

**Shell Integration (`RadianceShell`):**
- `PluginManager` initialized in constructor alongside `BuiltinRegistry`
- `LoadPlugins()` called after config sourcing, before welcome banner
- `UnloadAll()` called on exit after history save
- Plugin count displayed on startup if any plugins loaded

**New files:**
- `src/Plugins/IRadiancePlugin.cs`
- `src/Plugins/PluginContext.cs`
- `src/Plugins/PluginManager.cs`
- `src/Plugins/PluginCommand.cs`

**Modified files:**
- `src/Builtins/BuiltinRegistry.cs` — added `Unregister()` method
- `src/Shell/RadianceShell.cs` — plugin manager initialization, loading, and unloading

### [1.0.1] — Tab Completion Fix & QoL ✅

**Bug Fixes:**

*Tab Completion:*
- **Fixed path completion replacing entire line** — `ls /opt/` + TAB now correctly produces `/opt/angara/`, `/opt/homebrew/` etc. instead of just `angara/`, `homebrew/` which replaced the `/opt/` prefix
- Rewrote `CompletePath()` to split prefix at last `/`, preserve the original directory part (with `~`, `$VAR` etc.), and return full completion paths
- Fixed `startLeft` not updating after multiple-match display — `ApplyCompletion()` now passes `startLeft` by `ref` so prompt re-render propagates back to `ReadLine()`

*Line Editing:*
- Fixed display artifacts on large deletions (Ctrl+U, Ctrl+K, backspace) — `RedisplayLine()` now uses ANSI escape `\x1b[K` (clear to end of line) instead of clearing just one character

*Completion Behavior:*
- Hidden files (dot-files) are now excluded from completion unless the prefix starts with `.` (standard BASH behavior)

*Cleanup:*
- Removed unused `ClearCurrentLine()` method and unused `FindWordEnd()` method
- Removed unused `wordEnd` variable and `toAdd` variable in completion code

**Modified files:**
- `src/Shell/RadianceShell.cs` — all fixes above

### [1.0.0] — Phase 8: Testing & Hardening ✅

**Test Suite (219 tests, all passing):**
- `tests/Radiance.Tests.csproj` — xUnit test project targeting .NET 10.0
- `tests/Infrastructure/ShellTestHarness.cs` — Full lexer→parser→interpreter pipeline test harness with Console.Out capture
- Unit tests:
  - `tests/LexerTests.cs` — Lexer tokenization tests (word splitting, quoting, operators, assignments)
  - `tests/ParserTests.cs` — Parser AST construction tests (commands, pipelines, if/for/while/case, functions)
  - `tests/ShellContextTests.cs` — Variable scoping, positional parameters, aliases, functions
  - `tests/Expansion/VariableExpanderTests.cs` — `$VAR`, `${VAR}`, `$?`, positional params, parameter expansion
  - `tests/Expansion/ArithmeticExpanderTests.cs` — `$((expr))` arithmetic operations
  - `tests/Expansion/TildeExpanderTests.cs` — `~` and `~user` expansion
- Integration tests (`tests/IntegrationTests.cs`):
  - Echo, variables, quoting, exit codes, logical operators, semicolons
  - Export/unset/set/env, pwd/cd, if/elif/else, for/while/until loops
  - Case statements (with `*` wildcard default), functions with args/return
  - Command substitution, arithmetic expansion, aliases, type command
  - File redirections (>, >>, <), pipelines (builtin-to-external, external-to-external)
  - Parameter expansion (`${VAR:-default}`, `${#VAR}`), break/continue
  - Multi-line scripts (counter, fibonacci)

**Shell Bug Fixes:**

*Lexer:*
- Fixed `${VAR}` inside words — `${` now correctly detected even when preceded by regular characters
- Fixed double-quoted strings in word context (e.g., `alias name="value"`) — no longer treated as string-end

*Parser:*
- Fixed `ParseList` terminator handling — condition terminator detection rewritten to avoid false matches
- Fixed `AssignmentWord` handling in command position — `X=hello` as first word no longer stops word collection
- Fixed keyword-in-command-position — keywords (`done`, `elif`, etc.) only terminate word collection when no words collected yet (e.g., `echo done` now works correctly)

*Interpreter:*
- Fixed if/elif/else execution — was broken due to parser not producing correct AST for `if` condition bodies
- Fixed `break`/`continue` in compound commands — `VisitList` now stops executing remaining pipelines when break/continue/return is requested
- Fixed `$?` not updated between commands in a list — `LastExitCode` is now set after each pipeline
- Fixed function positional parameters — `$1` is now the first argument (not the function name)
- Fixed case `*` pattern — glob expansion is now skipped for case patterns via `skipGlob` parameter on `ExpandWord`
- Fixed alias expansion — added alias lookup in `VisitSimpleCommand` before function/builtin/external dispatch
- Fixed `TypeCommand` NullReferenceException — `BuiltinRegistry` is now set on `ShellContext` before type lookup

*Pipeline:*
- Rewrote `PipelineExecutor.ExecutePipeline` — replaced anonymous pipe approach with sequential MemoryStream-based data passing
- Fixed race condition: builtin output is captured as string→bytes, external process stdin/stdout uses synchronous MemoryStream copies
- Fixed `ProcessManager.StartProcess` — MemoryStream stdin/stdout handled synchronously to avoid fire-and-forget async race

*Expansion:*
- Added `skipGlob` parameter to `Expander.ExpandWord` — used by case patterns to prevent `*` from being expanded to file list

**Test Bug Fixes:**
- `ShellContextTests.PushScope_IsolatesInnerScope` — uses `SetLocalVariable` for proper scope isolation testing
- `VariableExpanderTests.Expand_PositionalParam` — positional params are 1-based; test data corrected
- `ParserTests.Assignment_Standalone` — parser returns `SimpleCommandNode` (not `AssignmentNode`); test updated
- `IntegrationTests.Cd_ChangesDirectory` — macOS `/tmp` is symlink; test checks for change, not exact path
- `IntegrationTests.Pipeline_Grep` — uses `printf` instead of `echo -e` (builtin echo doesn't support `-e`)

**Version bump:** 0.7.5 → 1.0.0

### [0.7.5] — Phase 7.5: QOL & Line Editing ✅

**Added:**

**TTY Support for Interactive Commands:**
- Updated `ProcessManager.Execute()` — simple (non-piped) commands now inherit the terminal directly instead of using redirected streams
- Auto-detects `Console.IsOutputRedirected` to choose terminal-inherited mode vs. piped mode (for command substitution)
- New `BuildTerminalStartInfo()` — no stream redirection, child gets raw TTY access
- Renamed `BuildStartInfo()` → `BuildCapturedStartInfo()` for clarity
- Fixes: `btop`, `vim`, `htop`, `top`, `nano`, and other interactive TUI apps now work correctly

**Full Line Editing (ReadLine rewrite):**
- Complete rewrite of `RadianceShell.ReadLine()` with cursor position tracking (`cursorPos`, `startLeft`)
- `RedisplayLine()` — efficient line redisplay with proper cursor positioning
- `SetCursorPosition()` — handles line wrapping for long input lines
- **Navigation shortcuts:**
  - `←` / `→` — move cursor left/right by character
  - `Home` / `Ctrl+A` — move to beginning of line
  - `End` / `Ctrl+E` — move to end of line
- **Editing shortcuts:**
  - `Delete` — delete character at cursor
  - `Ctrl+D` — delete at cursor, or EOF on empty line
  - `Ctrl+K` — kill from cursor to end of line
  - `Ctrl+U` — kill from beginning of line to cursor
  - `Ctrl+W` — delete word backward
  - `Esc` — clear entire line
  - `Ctrl+C` — cancel line with `^C` display
  - `Ctrl+L` — clear screen and re-render prompt + input
- Insert characters at cursor position (not just append)

**Reverse History Search (Ctrl+R):**
- `HandleReverseSearch()` — incremental reverse search through history
- Shows `(reverse-i-search)'query': match` prompt with ANSI colors
- Type to search, `Ctrl+R` to cycle to older matches, `Enter` to accept, `Esc`/`Ctrl+C` to cancel
- `Backspace` shrinks query and re-searches
- Updated `History.SearchEntries()` — searches entries containing query, returns newest first

**Improved Tab Completion:**
- **Tilde expansion**: `~/` and `~user/` complete correctly by expanding to home directory for search
- **Variable expansion**: `$HOME/`, `$PWD/` etc. expand for path completion search
- **Absolute paths**: `/opt/`, `/usr/` etc. now complete correctly
- **Directory-only mode**: `cd`, `pushd`, `popd`, `rmdir`, `mkdir` only show directory completions
- **PATH caching**: Executable list cached for 5 seconds (`PathCacheTimeout`) to avoid rescanning PATH on every Tab press
- **Smart path resolution**: `ExpandPathPrefix()` handles `~`, `~user`, `$VAR`, `/absolute`, and relative paths

**Version bump:** 0.7.0 → 0.7.5

**Modified files:**
- `src/Interpreter/ProcessManager.cs` — TTY support, terminal-inherited execution
- `src/Shell/RadianceShell.cs` — full line editing rewrite, Ctrl+R, improved completion
- `src/Shell/History.cs` — `SearchEntries()` method
- `Program.cs` — version bump

### [0.7.0] — Phase 7: Script Execution & Polish ✅

**Added:**

**Script Execution:**
- Updated `Program.cs` with full CLI flag handling:
  - `radiance` — launch interactive REPL
  - `radiance script.sh [args...]` — execute a shell script with positional parameters
  - `radiance -c "command"` — execute an inline command string
  - `radiance --help` / `radiance --version` — help and version output
- Updated `RadianceShell.cs`:
  - `RunScript()` — reads and executes a script file, sets positional parameters (`$1`–`$9`), `$0`, `$#`, `$@`
  - `RunCommand()` — executes a `-c` command string
  - Script execution shares the same context as interactive mode (variables, functions, aliases persist)

**New Builtins:**
- `source` / `.` (`src/Builtins/SourceCommand.cs`) — execute a script in the current shell context:
  - `source script.sh` / `. script.sh` — reads and executes commands from a file
  - Shares `ScriptExecutor` callback from `ShellContext` for execution
- `read` (`src/Builtins/ReadCommand.cs`) — read a line from stdin into variables:
  - `read VAR` — reads a line into `$VAR`
  - `read A B C` — splits input by IFS, assigns to variables, last variable gets remainder
  - `read -p "prompt" VAR` — displays a prompt before reading
  - `read -r` — raw mode (no backslash escape interpretation)
- `break` (`src/Builtins/BreakCommand.cs`) — exit loops (`break`, `break N` for N levels)
- `continue` (`src/Builtins/ContinueCommand.cs`) — skip to next loop iteration (`continue`, `continue N`)

**Control Flow Support:**
- Updated `ShellContext` with loop control flags:
  - `BreakRequested` / `BreakDepth` — for `break` builtin
  - `ContinueRequested` / `ContinueDepth` — for `continue` builtin
  - `ScriptExecutor` callback — for `source`/`.` builtin to delegate execution
- Updated `ShellInterpreter`:
  - `VisitFor` — checks `BreakRequested`/`ContinueRequested` after each iteration, supports depth for nested loops
  - `VisitWhile` — same break/continue support with depth propagation

**Persistent History:**
- Updated `History` class (`src/Shell/History.cs`):
  - `History(string filePath)` — constructor accepts a file path for persistent storage
  - History is automatically loaded on creation and saved after each command
  - Default path: `~/.radiance_history`
  - `Save()` — writes all entries to disk
  - `Load()` — reads entries from disk (called on startup)

**Config File Auto-Sourcing:**
- On startup, the shell checks for `~/.radiancerc` and auto-sources it if present
- Runs before the interactive prompt loop starts
- Allows users to set aliases, functions, environment variables, and prompt customizations

**Colorized Output:**
- New `ColorOutput` utility (`src/Utils/ColorOutput.cs`):
  - `WriteError()` — red error messages to stderr
  - `WriteWarning()` — yellow warning messages to stderr
  - `WriteInfo()` — cyan informational messages to stderr

**Critical Bug Fix — Argument Passing:**
- Fixed a critical bug where command arguments were silently merged (e.g., `echo hello` → `echohello`, `uname -a` → empty output)
- Root cause: The lexer skipped whitespace between tokens but didn't preserve this information, so the parser's `CollectWordParts()` incorrectly merged adjacent tokens into single words
- Fix: Added `HasLeadingWhitespace` flag to `Token` record, tracked in the lexer's `NextToken()`, and respected in `CollectWordParts()` to stop word collection when whitespace separates tokens
- Updated files: `Token.cs`, `Lexer.cs`, `Parser.cs`

**Version bump:** 0.6.0 → 0.7.0

**New files:**
- `src/Utils/ColorOutput.cs`
- `src/Builtins/BreakCommand.cs`
- `src/Builtins/ContinueCommand.cs`
- `src/Builtins/SourceCommand.cs`
- `src/Builtins/ReadCommand.cs`

### [0.6.0] — Phase 6: Advanced Features ✅

**Added:**

**Shell Functions:**
- New `FunctionNode` (`src/Parser/Ast/FunctionNode.cs`) — AST node for function definitions
- Updated `IAstVisitor<T>` with `VisitFunction` method
- Updated parser with two function definition syntaxes:
  - `function name { body; }` — via `ParseFunction()`
  - `name() { body; }` — via `ParseFunctionNameParens()`
  - `ParseBraceBody()` — parses command list inside `{ ... }`, used by function bodies
- Updated `ShellInterpreter`:
  - `VisitFunction` — registers function in `ShellContext`
  - `ExecuteFunction` — calls function with scope push/pop, positional parameter save/restore, `FUNCNAME` tracking
  - Function dispatch in `VisitSimpleCommand` — BASH order: function → builtin → external
  - `DescribePipeline()` — human-readable command text for job display
- Updated `ShellContext`:
  - Function storage (`SetFunction`, `GetFunction`, `HasFunction`, `UnsetFunction`, `FunctionNames`)
  - Variable scope stack (`PushScope`, `PopScope`, `ScopeDepth`) for function-local variables
  - Positional parameter scope stack (`PushPositionalParams`, `PopPositionalParams`) for function arguments
  - `ReturnRequested` / `ReturnExitCode` flags for `return` builtin support
  - `FunctionDef` record type
- New builtins:
  - `return` (`src/Builtins/ReturnCommand.cs`) — exits function with optional exit code
  - `local` (`src/Builtins/LocalCommand.cs`) — declares local variables in function scope

**Aliases:**
- Updated `ShellContext` with alias storage (`SetAlias`, `GetAlias`, `UnsetAlias`, `UnsetAllAliases`, `Aliases`)
- New builtins:
  - `alias` (`src/Builtins/AliasCommand.cs`) — define/display aliases (`alias name=value`, `alias`, `alias name`)
  - `unalias` (`src/Builtins/UnaliasCommand.cs`) — remove aliases (`unalias name`, `unalias -a`)
- Alias expansion in `RadianceShell.ExpandAliases()` — expands first word of each command if it matches an alias
- Updated `type` builtin to recognize aliases and functions in BASH resolution order

**Background Jobs & `&` Operator:**
- New `JobManager` (`src/Interpreter/JobManager.cs`) — tracks background jobs:
  - `Job` class with job number, process, state, exit code, completion signal
  - `JobState` enum (Running, Stopped, Done)
  - `AddJob` / `CompleteJob` / `GetJob` / `WaitForJob` / `UpdateAndCollectCompleted`
  - Thread-pool job support via `ManualResetEventSlim` for completion signaling
- Updated `ShellContext` — `JobManager` property
- Updated `ShellInterpreter.VisitList` — `&` separator triggers background execution via `ThreadPool.QueueUserWorkItem`
- New builtins:
  - `jobs` (`src/Builtins/JobsCommand.cs`) — list background jobs with status
  - `fg` (`src/Builtins/FgCommand.cs`) — bring background job to foreground
- Updated `RadianceShell` — `NotifyCompletedJobs()` prints notifications at each prompt

**Enhanced History:**
- New `history` builtin (`src/Builtins/HistoryCommand.cs`):
  - `history` — list all entries
  - `history N` — show last N entries
  - `history -c` — clear history
  - `history -d N` — delete entry at offset N
- Updated `History` class with `Clear()`, `Delete()`, `GetEntry()` methods
- Wired `HistoryCommand` to shell's `History` instance via `BuiltinRegistry.TryGetCommand()`
- Added `TryGetCommand()` to `BuiltinRegistry`

**Tab Completion:**
- Full tab completion in `RadianceShell.ReadLine()`:
  - Command position: completes builtins, functions, aliases, and PATH executables
  - Argument position: completes file/directory paths relative to CWD
  - Common prefix completion for multiple matches
  - Multiple match display in columns (like BASH)
  - `FindCommonPrefix()` / `ApplyCompletion()` / `HandleTabCompletion()` helpers

**Grammar additions:**
```
command         := simple_command | compound_command | function_definition
function_def    := 'function' WORD ('()')? '{' list '}' | WORD '()' '{' list '}'
```

**New files:**
- `src/Parser/Ast/FunctionNode.cs`
- `src/Builtins/ReturnCommand.cs`
- `src/Builtins/LocalCommand.cs`
- `src/Builtins/AliasCommand.cs`
- `src/Builtins/UnaliasCommand.cs`
- `src/Builtins/JobsCommand.cs`
- `src/Builtins/FgCommand.cs`
- `src/Builtins/HistoryCommand.cs`
- `src/Interpreter/JobManager.cs`

### [0.5.0] — Phase 5: Control Flow ✅

**Added:**
- New AST node types for compound commands:
  - `IfNode` (`src/Parser/Ast/IfNode.cs`) — `if/elif/else/fi` with condition, then-body, elif branches, else-body
  - `ForNode` (`src/Parser/Ast/ForNode.cs`) — `for VAR in words; do body; done` with variable name, iterable words, body
  - `WhileNode` (`src/Parser/Ast/WhileNode.cs`) — `while/until condition; do body; done` with `IsUntil` flag
  - `CaseNode` + `CaseItem` (`src/Parser/Ast/CaseNode.cs`) — `case WORD in pattern) body ;; esac` with glob-pattern matching
- Updated `IAstVisitor<T>` with 4 new visit methods: `VisitIf`, `VisitFor`, `VisitWhile`, `VisitCase`
- Updated `PipelineNode.Commands` from `List<SimpleCommandNode>` to `List<AstNode>` — compound commands can appear in pipelines
- New `TokenType.DoubleSemicolon` (`;;`) for case statement item separators
- Updated lexer to produce `DoubleSemicolon` tokens
- Major parser rewrite (`src/Parser/Parser.cs`):
  - `ParseCommand()` dispatches to compound command parsers based on keyword
  - `ParseIf()` — full `if/then/elif/then/else/fi` with nested condition/body parsing
  - `ParseFor()` — `for VAR in words; do body; done` with optional `in` clause
  - `ParseWhile()` — `while/until condition; do body; done`
  - `ParseCase()` — `case WORD in pattern(|pattern)...) body ;; esac` with multi-pattern items
  - `ParseCompoundList()` — terminates at specified keywords (then/fi/do/done/esac/;;)
  - Keyword detection via `IsKeyword()` helper — keywords are `Word` tokens detected contextually in the parser
  - `ParseSimpleCommand()` stops word collection at terminator keywords (then/fi/do/done/etc.)
- Updated `ShellInterpreter` (`src/Interpreter/Interpreter.cs`):
  - `VisitIf` — evaluate condition, execute matching branch (then/elif/else)
  - `VisitFor` — expand iterable words (with glob), loop setting variable + executing body
  - `VisitWhile` — `while` loops while exit code 0; `until` loops while exit code non-zero; safety limit of 1M iterations
  - `VisitCase` — expand word, match against patterns using glob-style regex matching, execute first matching body
  - `MatchCasePattern()` / `GlobToRegex()` — glob-to-regex conversion for case pattern matching
- Updated `PipelineExecutor` (`src/Interpreter/PipelineExecutor.cs`):
  - Handles `AstNode` entries in pipelines (not just `SimpleCommandNode`)
  - Compound commands in pipelines: captures Console.Out output and writes to pipe
- Updated `RadianceShell` (`src/Shell/RadianceShell.cs`):
  - Multi-line input support via block stack tracking
  - Unclosed `if/for/while/until/case` triggers PS2 continuation prompt (`> `)
  - `ComputeBlockStack()` — lexes each line, counts block openers/closers in command position
  - `IsInCommandPosition()` — determines if a token is a keyword in command position
  - Reads continuation lines until all blocks are closed

**Grammar additions:**
```
command      := simple_command | compound_command
compound_command := if_command | for_command | while_command | case_command
if_command   := 'if' list 'then' list ('elif' list 'then' list)* ['else' list] 'fi'
for_command  := 'for' WORD ['in' word*] separator 'do' list 'done'
while_command := ('while' | 'until') list 'do' list 'done'
case_command := 'case' WORD 'in' case_item* 'esac'
case_item    := pattern ('|' pattern)* ')' list ';;'
```

### [0.4.0] — Phase 4: Variables & Expansion ✅

**Added:**
- New `src/Expansion/` module with full BASH-compatible expansion pipeline:
  - `Expander.cs` — orchestrates expansion phases in correct BASH order (tilde → variable → command substitution → arithmetic → glob)
  - `TildeExpander.cs` — `~` and `~user` expansion to home directories
  - `VariableExpander.cs` — `$VAR`, `${VAR}`, special variables (`$?`, `$$`, `$!`, `$#`, `$@`, `$*`, `$0`–`$9`), parameter expansion (`${VAR:-default}`, `${VAR:=default}`, `${VAR:+alt}`, `${#VAR}`)
  - `CommandSubstitution.cs` — `$(command)` and `` `command` `` substitution with recursive expansion
  - `ArithmeticExpander.cs` — `$((expression))` with full integer arithmetic (comparison, bitwise, logical, shift operators) via recursive-descent expression parser
  - `GlobExpander.cs` — filename generation with `*`, `?`, `[...]` patterns, hidden dot-file rules, regex conversion
- New `WordPart` type (`src/Parser/Ast/WordPart.cs`) — tracks quoting context per word segment:
  - `WordQuoting.None` — all expansions apply
  - `WordQuoting.Double` — variable/command substitution/arithmetic only
  - `WordQuoting.Single` — no expansion (literal)
  - `WordQuoting.Escaped` — literal
- Updated `SimpleCommandNode.Words` from `List<string>` to `List<List<WordPart>>` for quoting-aware expansion
- Updated `RedirectNode.Target` from `string` to `List<WordPart>` for expansion in redirect filenames
- Updated `TokenType` — split `String` into `DoubleQuotedString` and `SingleQuotedString`
- Updated lexer:
  - Emits `DoubleQuotedString` and `SingleQuotedString` tokens instead of generic `String`
  - `$(...)` command substitution tracking with nested parenthesis/quote handling
  - Backtick `` `...` `` command substitution scanning
- Updated parser:
  - Adjacent quoted/unquoted tokens merged into single word (e.g., `hello"world"` → one word with two parts)
  - `WordPart` lists preserve quoting context for each segment
- Updated `ShellContext` (`ExecutionCtx.cs`):
  - Positional parameters (`$1`–`$9`) with `SetPositionalParams`/`GetPositionalParam`
  - `$#` (count), `$@`/`$*` (all params)
  - `$0` (shell name), `$!` (last background PID), `$-` (shell options)
- Updated `ShellInterpreter`:
  - Uses `Expander` for all word and string expansion instead of inline variable expansion
  - `ExpandVariables` method delegates to `Expander.ExpandString` for backward compatibility
- Updated `PipelineExecutor`:
  - Accepts `Expander` instance for full expansion in pipelines and redirects
  - Redirect target filenames are expanded through the full expansion pipeline

### [0.3.0] — Phase 3: Pipelines & Redirections ✅

**Added:**
- Refactored `ProcessManager` with new `StartProcess` method supporting custom stdin/stdout/stderr streams for pipe plumbing and file redirections
- Async stream copying (`CopyStreamToWriterAsync`, `CopyReaderToStreamAsync`) with proper broken-pipe handling
- New `PipelineExecutor` class (`src/Interpreter/PipelineExecutor.cs`) — orchestrates multi-command pipeline execution:
  - Connects processes via `AnonymousPipeServerStream` / `AnonymousPipeClientStream`
  - Supports builtins in pipelines by capturing console output and piping to next process
  - File redirections: `>` (write), `>>` (append), `<` (stdin from file)
  - Exit code of the last command in the pipeline is returned (standard BASH behavior)
  - Proper pipe handle cleanup in `finally` blocks to prevent resource leaks
- Updated `ShellInterpreter`:
  - `VisitPipeline` now delegates to `PipelineExecutor` instead of executing only the first command
  - `VisitSimpleCommand` delegates to `PipelineExecutor` when redirections are present
  - Made `ExpandVariables` internal so `PipelineExecutor` can reuse it
- Single-command redirects work for both builtins (via `Console.SetOut` capture) and external commands (via `FileStream`)
- Multi-command pipelines work for external processes (e.g., `ls | grep foo | wc -l`)
- Builtins in pipelines (e.g., `echo hello | cat`) supported via output capture

### [0.2.0] — Phase 2: Lexer & Parser ✅

**Added:**
- New `Lexer.cs` with position tracking (line, column), comments (`#`), assignment word detection, ANSI-C quoting support
- Updated `Token.cs` with line/column tracking for error reporting
- New `TokenType` entries: `AssignmentWord`, `LParen`, `RParen`, `Comment`
- AST node definitions with visitor pattern:
  - `AstNode` (abstract base with `Accept<T>()`)
  - `SimpleCommandNode` (command + args + assignments + redirects)
  - `PipelineNode` (commands connected by `|`)
  - `ListNode` (pipelines with `;`, `&&`, `||`, `&` separators)
  - `AssignmentNode` (variable assignments)
  - `RedirectNode` (I/O redirections)
- `IAstVisitor<T>` interface for visitor pattern dispatch
- Recursive-descent `Parser.cs` with grammar: `list → and_or → pipeline → simple_command`
- `ShellInterpreter` (AST walker) implementing `IAstVisitor<int>`:
  - `&&` / `||` short-circuit evaluation
  - `;` sequential execution
  - Variable expansion in command words
  - Assignment prefix handling
- Updated `RadianceShell.cs` to use Lexer → Parser → Interpreter pipeline
- Kept `SimpleTokenizer.cs` for reference (no longer in active pipeline)

**Architecture now follows:**
```
Input → Lexer → Tokens → Parser → AST → Interpreter → Execution
```

### [0.1.0] — Phase 1: Foundation ✅

**Added:**
- Project scaffolding with .NET 10.0 console app
- REPL loop with interactive prompt (`user@host:cwd$`)
- Basic tokenization (word splitting with quote handling)
- Builtin command registry and dispatch system
- Built-in commands: `echo`, `cd`, `pwd`, `exit`, `export`, `unset`, `type`
- External command execution via `System.Diagnostics.Process`
- Execution context tracking (CWD, environment variables, exit codes)
- Command history (in-memory, arrow key navigation)
- `$PATH` resolution for external executables

---

## Notes for Agents

- Always update this file after making changes to the codebase.
- Update the Roadmap status and Changelog when starting/finishing a phase.
- Follow the existing code style: PascalCase for public members, thorough XML doc comments.
- The project uses `ImplicitUsings` and `Nullable` enabled — account for nullable reference types.
- Run `dotnet build` after making changes to verify compilation.
- dotnet is located at ~/.dotnet/
- Please commit your changes and use .gitignore carefully. (create it if it does not exist.)