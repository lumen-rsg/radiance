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
| 8 | Testing & Hardening | ⬜ Not Started | Unit/integration tests, POSIX compliance |

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