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