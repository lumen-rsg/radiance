<div align="center">

# ✨ Radiance

**A BASH-compatible shell and interpreter built from scratch in C# / .NET 10**

[![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Tests](https://img.shields.io/badge/tests-231%20passing-brightgreen)](tests/)
[![License](https://img.shields.io/badge/license-MIT-blue)](LICENSE)
[![Platform](https://img.shields.io/badge/platform-macOS%20%7C%20Linux%20%7C%20Windows-lightgrey)](Radiance.csproj)

[Features](#-features) • [Getting Started](#-getting-started) • [Usage](#-usage) • [Theming](#-theming-system) • [Plugins](#-plugin-system) • [Wiki](#-wiki) • [Contributing](#-contributing)

</div>

---

## 🌟 Features

- **BASH-compatible syntax** — pipelines, redirections, variables, control flow, functions, and more
- **Interactive REPL** — full line editing, cursor navigation, keyboard shortcuts
- **Reverse history search** — `Ctrl+R` incremental search through command history
- **Tab completion** — commands, file paths, directories, tilde/variable expansion
- **Theming system** — 6 built-in themes + custom JSON themes with RPROMPT support
- **Plugin system** — extend the shell with .NET DLLs loaded at runtime
- **Job control** — background jobs (`&`), `jobs`, `fg`
- **Persistent history** — saved to `~/.radiance_history`
- **Config file** — auto-sources `~/.radiancerc` on startup
- **Script execution** — run `.sh` files, inline `-c` commands, or `source` scripts
- **Full expansion pipeline** — `$VAR`, `$(cmd)`, `$((expr))`, `~`, globs, parameter expansion

---

## 🚀 Getting Started

### Prerequisites

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) or newer

### Build from Source

```bash
git clone https://github.com/lumen-rsg/radiance.git
cd radiance
dotnet build
```

### Run

```bash
# Interactive mode
dotnet run

# Or publish a standalone binary
dotnet publish -c Release
./bin/Release/net10.0/radiance
```

---

## 📖 Usage

```bash
radiance                        # Launch interactive REPL
radiance script.sh [args...]    # Execute a shell script
radiance -c "command" [args...] # Execute an inline command
radiance --help                 # Show help
radiance --version              # Show version
```

### Quick Examples

```bash
# Variables and expansion
name="World"
echo "Hello, $name!"

# Pipelines
ls -la | grep ".cs" | wc -l

# Redirections
echo "log entry" >> output.txt
sort < input.txt > sorted.txt

# Command substitution
files=$(ls *.cs)
echo "Found: $files"

# Arithmetic
echo "2 + 2 = $((2 + 2))"

# Control flow
if [ -f "README.md" ]; then
    echo "README exists"
fi

for file in *.cs; do
    echo "Found: $file"
done

# Functions
greet() {
    echo "Hello, $1!"
}
greet "Radiance"

# Aliases
alias ll="ls -la"
```

---

## 🐚 Shell Features

### Line Editing

| Shortcut | Action |
|----------|--------|
| `←` / `→` | Move cursor left/right |
| `Home` / `Ctrl+A` | Move to beginning of line |
| `End` / `Ctrl+E` | Move to end of line |
| `Delete` | Delete character at cursor |
| `Ctrl+D` | Delete at cursor (or EOF on empty line) |
| `Ctrl+K` | Kill from cursor to end of line |
| `Ctrl+U` | Kill from beginning of line to cursor |
| `Ctrl+W` | Delete word backward |
| `Ctrl+C` | Cancel current line |
| `Ctrl+L` | Clear screen |
| `Esc` | Clear entire line |
| `Ctrl+R` | Reverse history search |
| `Tab` | Autocomplete |
| `↑` / `↓` | Navigate history |

### Pipelines & Redirections

```bash
ls | grep foo | wc -l          # Pipe commands together
echo "hello" > file.txt        # Write to file
echo "world" >> file.txt       # Append to file
sort < input.txt               # Read from file
```

### Variables & Expansion

```bash
echo $VAR                      # Variable expansion
echo ${VAR:-default}           # Parameter expansion (default value)
echo ${#VAR}                   # String length
echo $(command)                # Command substitution
echo $((2 + 2))                # Arithmetic expansion
echo ~                         # Tilde expansion
echo *.cs                      # Glob expansion
```

### Control Flow

```bash
# If/elif/else
if [ condition ]; then ...; elif [ condition ]; then ...; else ...; fi

# For loop
for var in word1 word2; do ...; done

# While/Until
while [ condition ]; do ...; done
until [ condition ]; do ...; done

# Case
case $var in
    pattern1) ... ;;
    pattern2) ... ;;
    *) ... ;;
esac
```

### Functions & Aliases

```bash
# Functions (two syntaxes)
my_func() { echo "args: $@"; }
function my_func { echo "args: $@"; }

# Local variables
my_func() { local x=10; echo $x; }

# Aliases
alias ll="ls -la"
alias gs="git status"
```

### Job Control

```bash
sleep 10 &                     # Run in background
jobs                           # List background jobs
fg %1                          # Bring job to foreground
```

---

## 📋 Built-in Commands

| Command | Description |
|---------|-------------|
| `echo` | Print text to stdout |
| `cd` | Change directory |
| `pwd` | Print working directory |
| `exit` | Exit the shell |
| `export` | Export environment variables |
| `unset` | Remove variables |
| `set` | Set/unset shell options |
| `env` | Print environment |
| `type` | Show command type (builtin/function/alias/external) |
| `alias` / `unalias` | Manage aliases |
| `source` / `.` | Execute a script in current context |
| `read` | Read input into variables |
| `history` | Manage command history |
| `jobs` | List background jobs |
| `fg` | Bring job to foreground |
| `return` | Return from function |
| `local` | Declare local variables in functions |
| `break` / `continue` | Loop control |
| `true` / `false` | Return success/failure exit codes |
| `theme` | Manage shell themes |
| `plugin` | Manage plugins |

---

## 🎨 Theming System

Radiance includes a powerful theming system with 6 built-in themes and support for custom JSON themes.

### Built-in Themes

| Theme | Style |
|-------|-------|
| `default` | Classic `user@host cwd $` with git support |
| `minimal` | Clean arrow `❯` + directory name only |
| `powerline` | Powerline-style segments with colored backgrounds |
| `rainbow` | Vibrant multi-color with 2-line layout |
| `dark` | Optimized for dark terminal backgrounds |
| `light` | Optimized for light terminal backgrounds |

### Theme Command

```bash
theme list                     # List all available themes
theme set <name>               # Switch theme (persists to config)
theme current                  # Show current theme info
theme info <name>              # Show theme details and preview
theme path                     # Show custom themes directory
```

### Custom JSON Themes

Create custom themes by placing `.json` files in `~/.radiance/themes/`:

```json
{
  "name": "my-theme",
  "description": "My custom theme",
  "author": "Me",
  "left_prompt": [
    { "type": "user", "fg": "bright_green", "bold": true, "suffix": "@" },
    { "type": "host", "fg": "bright_cyan", "suffix": " " },
    { "type": "cwd", "fg": "bright_blue", "bold": true, "suffix": " " },
    { "type": "git", "fg": "bright_magenta", "dirty_fg": "bright_red" },
    { "type": "prompt_char", "fg": "bright_green", "error_fg": "bright_red" }
  ],
  "right_prompt": [
    { "type": "time", "fg": "dark_gray" }
  ]
}
```

**Available segment types:** `user`, `host`, `cwd`, `git`, `prompt_char`, `time`, `date`, `jobs`, `exit_code`, `text`

**Colors:** Named ANSI colors (`bright_green`, `bright_red`, `dark_gray`, etc.) or hex RGB (`#ff0000`)

**Style properties:** `bold`, `italic`, `fg`, `bg`, `prefix`, `suffix`, `error_fg`, `dirty_fg`

See [`themes/example.json`](themes/example.json) for a full example.

### C# Themes

For advanced theming, implement the `ITheme` interface:

```csharp
public class MyTheme : ITheme
{
    public string Name => "my-theme";
    public string Description => "My custom theme";
    public string Author => "Me";

    public string RenderPrompt(PromptContext ctx)
    {
        // Build prompt string with ANSI codes
        return $"{ctx.User}@{ctx.Host} {ctx.Cwd} $ ";
    }

    public string RenderRightPrompt(PromptContext ctx)
    {
        return ctx.Time;  // Right-aligned prompt
    }
}
```

---

## 🔌 Plugin System

Extend Radiance with .NET plugins. Place compiled DLLs in `~/.radiance/plugins/`.

### Creating a Plugin

```csharp
using Radiance.Plugins;
using Radiance.Builtins;
using Radiance.Interpreter;

public class HelloPlugin : IRadiancePlugin
{
    public string Name => "hello";
    public string Version => "1.0.0";
    public string Description => "Adds a 'hello' greeting command";

    public void OnLoad(PluginContext context)
    {
        context.RegisterCommand(new HelloCommand());
    }

    public void OnUnload() { }
}

public class HelloCommand : IBuiltinCommand
{
    public string Name => "hello";

    public int Execute(string[] args, ShellContext context)
    {
        Console.WriteLine("Hello from the plugin!");
        return 0;
    }
}
```

### Plugin Command

```bash
plugin list                    # List loaded plugins
plugin load /path/to/plugin.dll  # Load a plugin at runtime
plugin unload <name>           # Unload a plugin by name
plugin help                    # Show plugin usage
```

---

## ⚙️ Configuration

| File | Purpose |
|------|---------|
| `~/.radiancerc` | Auto-sourced on startup — set aliases, functions, env vars |
| `~/.radiance/config.json` | Persistent settings (theme preference) |
| `~/.radiance/themes/*.json` | Custom theme files |
| `~/.radiance/plugins/*.dll` | Plugin DLLs (auto-loaded on startup) |
| `~/.radiance_history` | Persistent command history |

### Example `~/.radiancerc`

```bash
# Aliases
alias ll="ls -la"
alias gs="git status"

# Environment
export EDITOR="vim"

# Functions
mkcd() {
    mkdir -p "$1" && cd "$1"
}
```

---

## 🏗️ Architecture

```
┌──────────┐    ┌───────┐    ┌────────┐    ┌─────────────┐    ┌──────────┐
│  Input   │───▶│ Lexer │───▶│ Tokens │───▶│   Parser    │───▶│   AST    │
│ (string) │    │       │    │        │    │ (recursive  │    │  (nodes) │
│          │    │       │    │        │    │  descent)   │    │          │
└──────────┘    └───────┘    └────────┘    └─────────────┘    └────┬─────┘
                                                                    │
                                                                    ▼
                                                             ┌──────────────┐
                                                             │ Interpreter  │
                                                             │  (AST walker)│
                                                             └──────┬───────┘
                                                                    │
                                                          ┌─────────┼─────────┐
                                                          ▼         ▼         ▼
                                                     Builtins   Plugins   External
                                                                          Processes
```

### Key Components

| Component | Directory | Description |
|-----------|-----------|-------------|
| **Shell** | `src/Shell/` | REPL loop, prompt rendering, command history |
| **Lexer** | `src/Lexer/` | Tokenizer — converts raw input into typed tokens |
| **Parser** | `src/Parser/` | Recursive-descent parser — builds AST from tokens |
| **AST** | `src/Parser/Ast/` | Abstract Syntax Tree node definitions |
| **Interpreter** | `src/Interpreter/` | AST walker that executes commands |
| **Built-ins** | `src/Builtins/` | Built-in shell commands |
| **Plugins** | `src/Plugins/` | Plugin interface, manager, and lifecycle |
| **Expansion** | `src/Expansion/` | Variable, glob, tilde, and command substitution |
| **Themes** | `src/Themes/` | Theme engine with JSON and C# theme support |
| **Utils** | `src/Utils/` | Path resolution, color output, helpers |

### Project Structure

```
Radiance/
├── Program.cs                     # Entry point (CLI flag handling)
├── Radiance.csproj                # Project file (.NET 10.0)
├── Radiance.sln                   # Solution file
├── CLINE.md                       # Agent context & detailed changelog
├── themes/
│   └── example.json               # Example custom JSON theme
├── scripts/
│   └── stress_test.sh             # Stress test script
├── src/
│   ├── Shell/                     # REPL, prompt, history
│   ├── Lexer/                     # Tokenizer
│   ├── Parser/                    # Parser + AST nodes
│   ├── Interpreter/               # Interpreter, pipelines, jobs
│   ├── Builtins/                  # Built-in commands
│   ├── Plugins/                   # Plugin system
│   ├── Expansion/                 # Expansion/substitution
│   ├── Themes/                    # Theming engine + built-in themes
│   └── Utils/                     # Utilities
└── tests/                         # xUnit test suite (231 tests)
    ├── Infrastructure/            # Test harness
    ├── Expansion/                 # Expansion tests
    ├── LexerTests.cs
    ├── ParserTests.cs
    ├── ShellContextTests.cs
    ├── ThemeTests.cs
    └── IntegrationTests.cs
```

---

## 🧪 Development

### Building

```bash
dotnet build                    # Debug build
dotnet build -c Release         # Release build
```

### Running Tests

```bash
dotnet test                     # Run all 231 tests
dotnet test --filter "FullyQualifiedName~LexerTests"  # Run specific tests
```

### Code Style

- **C# conventions** — PascalCase for public members, ` camelCase` for locals
- **XML doc comments** on all public APIs
- **Nullable reference types** enabled — handle nulls properly
- **Implicit usings** enabled — no need for common `using` statements

---

## 🤝 Contributing

Contributions are welcome! Here's how to get started:

1. **Fork** the repository
2. **Create a feature branch** (`git checkout -b feature/my-feature`)
3. **Make your changes** — follow existing code style and add XML doc comments
4. **Add tests** — ensure new features are covered by tests
5. **Run the test suite** (`dotnet test`) — all tests must pass
6. **Commit** with a descriptive message
7. **Open a Pull Request**

### Guidelines

- Keep the architecture clean — respect the Lexer → Parser → Interpreter pipeline
- New builtins should implement `IBuiltinCommand` and be registered in `BuiltinRegistry.CreateDefault()`
- New themes should implement `ITheme` or use the JSON format
- Run `dotnet build` after changes to verify compilation
- Update `CLINE.md` with any architectural or changelog changes

---

## 📚 Wiki

Detailed documentation is available on the [GitHub Wiki](https://github.com/lumen-rsg/radiance/wiki):

| Page | Description |
|------|-------------|
| [Architecture](https://github.com/lumen-rsg/radiance/wiki/Architecture) | Deep dive into Lexer → Parser → Interpreter pipeline |
| [BASH Compatibility](https://github.com/lumen-rsg/radiance/wiki/BASH-Compatibility) | Supported syntax, known differences from BASH |
| [Theme Development](https://github.com/lumen-rsg/radiance/wiki/Theme-Development) | Creating custom themes (JSON and C#) |
| [Plugin Development](https://github.com/lumen-rsg/radiance/wiki/Plugin-Development) | Building and distributing plugins |
| [Configuration](https://github.com/lumen-rsg/radiance/wiki/Configuration) | Config files, environment, startup behavior |
| [Keyboard Shortcuts](https://github.com/lumen-rsg/radiance/wiki/Keyboard-Shortcuts) | Full reference for line editing shortcuts |
| [Testing](https://github.com/lumen-rsg/radiance/wiki/Testing) | Writing and running tests |

---

## 📄 License

This project is licensed under the MIT License. See the [LICENSE](LICENSE) file for details.

---

<div align="center">

**[Report a Bug](https://github.com/lumen-rsg/radiance/issues) · [Request a Feature](https://github.com/lumen-rsg/radiance/issues) · [Ask a Question](https://github.com/lumen-rsg/radiance/issues)**

</div>