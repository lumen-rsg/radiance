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
| 5 | Control Flow | ⬜ Not Started | `if`, `for`, `while`, `case` |
| 6 | Advanced Features | ⬜ Not Started | Functions, aliases, job control, history, completion |
| 7 | Script Execution & Polish | ⬜ Not Started | `.sh` files, `source`, config, colorized output |
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