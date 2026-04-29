# tmux Multiplexer Support — Progress Tracker

## Overview
Full tmux-compatible terminal multiplexing via `radiance --mux`. Ctrl+B prefix key. Sessions, windows, split panes, detach/reattach, copy mode, mouse support.

## Status: All Phases Complete

## Phases

### Phase 1: Foundation — PaneScreenBuffer + AnsiStreamParser
- [x] `src/Multiplexer/PaneScreenBuffer.cs` — Cell[,] grid, cursor, scroll regions, scrollback, resize, alt-screen swap
- [x] `src/Multiplexer/AnsiStreamParser.cs` — VT100/xterm CSI/OSC parser, state machine, partial sequence buffering

### Phase 2: MuxPane + PTY Lifecycle
- [x] Extend `PtyAllocation.Create(rows, cols)` — explicit-dimension PTY creation
- [x] Extract `PtyAllocation.SpawnProcess()` — reusable posix_spawnp + dup2 pattern from ProcessManager
- [x] `src/Multiplexer/MuxPane.cs` — PTY pair, child process, relay thread (master fd → AnsiStreamParser → PaneScreenBuffer), WriteInput()

### Phase 3: Rendering — MultiplexerRenderer + TerminalBuffer.Blit
- [x] `TerminalBuffer.Blit()` — sub-region cell copying method
- [x] `src/Multiplexer/MultiplexerRenderer.cs` — pane layout → Rect, border drawing, status bar (session name, window tabs, clock)

### Phase 4: Input — KeyRouter + TerminalKeyEncoder
- [x] `src/Multiplexer/TerminalKeyEncoder.cs` — ConsoleKeyInfo → terminal escape sequences
- [x] `src/Multiplexer/KeyRouter.cs` — Ctrl+B prefix state machine, command dispatch

### Phase 5: Window/Session Management + Main Loop
- [x] `src/Multiplexer/PaneLayout.cs` — binary tree (SplitV, SplitH, Leaf)
- [x] `src/Multiplexer/MuxWindow.cs` — tab: name, pane layout, active pane, zoom
- [x] `src/Multiplexer/MultiplexerSession.cs` — session: windows, DaVinciApp, 30fps render loop
- [x] `Program.cs` — add `--mux` flag handling, `--mux attach`, `--mux ls`, `--mux kill-session`

### Phase 6: Detach/Reattach (Session Persistence)
- [x] Socket-based daemon model or in-process detach
- [x] `radiance --mux attach` reconnects to live session

### Phase 7: Copy Mode + Mouse Support
- [x] `src/Multiplexer/CopyMode.cs` — vi-like scrollback browsing, search, selection, clipboard
- [x] Mouse tracking: click-to-select pane, drag-to-resize border, scroll wheel

### Phase 8: Resize + Polish
- [x] Terminal resize propagation to all panes (SIGWINCH)
- [x] Pane resize via Ctrl+B + Alt+Arrow
- [x] Configurable prefix key, status bar customization
- [x] Use user's default shell ($SHELL) instead of hardcoded /bin/bash

## Key Files to Modify
- `Program.cs` — `--mux` flag
- `src/Terminal/PtyAllocation.cs` — `Create(rows, cols)` + `SpawnProcess()`
- `DaVinci/Terminal/TerminalBuffer.cs` — `Blit()` method

## Reference Pattern
- `src/Interpreter/ProcessManager.cs:409-475` — PTY spawn + relay pattern to extract

## Plan File
- `/Users/cv2/.claude/plans/fancy-wishing-lobster.md`
