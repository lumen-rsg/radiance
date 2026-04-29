# Radiance Shell — Implementation Progress Tracker

## Tier 1: Critical — Required for Basic Correctness

- [x] 1. Signal Handling (`trap`, SIGINT forwarding, SIGCHLD)
- [ ] 2. PTY Allocation for Child Processes *(deferred — requires P/Invoke to openpty/forkpty)*
- [x] 3. True Concurrent Pipeline Execution *(sequential streaming; true OS pipes deferred)*
- [x] 4. Subshell Execution (`(list)`)
- [x] 5. Here-Documents (`<<`, `<<-`, `<<<`)
- [x] 6. `set` Shell Options (`-e`, `-x`, `-u`, `-o`, `--`)
- [x] 7. Prefix Assignments Scoped to Command

## Tier 2: Important — Needed for Daily Usability

- [x] 8. Full Job Control — `bg` and `disown` added (Ctrl+Z deferred)
- [x] 9. `wait` Builtin
- [x] 10. `shift` Builtin
- [x] 11. `eval` Builtin
- [x] 12. `declare`/`typeset`/`readonly`
- [x] 13. `trap` Builtin
- [x] 14. `hash` Command
- [x] 15. `umask` (ulimit deferred)
- [x] 16. PS1/PS2/PS4 Variable-Driven Prompts
- [x] 17. Array Variables

## Tier 3: Nice-to-Have — Power User Features

- [x] 18. Brace Expansion (`{a,b}`, `{1..10}`)
- [x] 19. Process Substitution (`<(cmd)`, `>(cmd)`)
- [x] 20. Extended History Designators
- [x] 21. `{ ... }` as Standalone Compound Command
- [x] 22. `select` Menu Construct
- [x] 23. `getopts` Builtin
- [x] 24. Programmable Tab Completion (`complete`)
- [x] 25. Extended Glob (`extglob`)
- [x] 26. `coproc`
- [x] 27. Interactive Completion Menu

## Tier 4: Line Editing Enhancements

- [x] 28. Kill Ring / Yank (Ctrl+Y)
- [x] 29. Word Movement (Alt+f / Alt+b)
- [x] 30. Forward Word Delete (Alt+d / Alt+Backspace)
- [x] 31. Real-Time Syntax Highlighting
