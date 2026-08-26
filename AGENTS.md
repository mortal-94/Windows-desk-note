# Workspace engineering rules

## Data safety

- Treat every user's note, backup, settings file, and recovery file as irreplaceable user data.
- Before a change that runs, replaces, or publishes DeskNote, copy the real desktop user's current `note.txt`, settings, and newest backups to a new timestamped recovery directory. Never overwrite or prune the source while creating that snapshot.
- Tests and probes must use isolated temporary directories. They must never use the default `%LOCALAPPDATA%\WindowsDeskNote` path.
- Be aware that a sandboxed `%LOCALAPPDATA%` can differ from the real interactive desktop user's directory. For recovery or release verification, explicitly identify which context is being read and verify copies with a hash.
- Do not replace a non-empty note with empty content. Before any recovery write, preserve the destination when it exists and verify the restored file by hash, byte count, and line count.
- Use `NoteRepository`/`AtomicFileWriter` for application writes. A save or shutdown failure must keep the application open with the in-memory note intact.

## Performance and reliability

- Prefer event-driven Windows integration. A periodic health check is acceptable only when it is low-frequency, performs cheap read-only checks during healthy operation, and does expensive recovery work only after detecting an anomaly.
- Reject or explicitly raise any feature whose idle CPU, memory, I/O, startup time, or interaction latency cost is disproportionate to its user value.
- Never use busy loops, high-frequency desktop-window enumeration, global input hooks, or “always on top” as a desktop-layer substitute.
- Desktop recovery must be debounced and tolerate transient Explorer/display changes. After repeated failures, fall back to a visible editor instead of leaving the note inaccessible.

## Verification and release

- Keep warnings as errors, run formatting, Release build, and all automated tests before publishing.
- For desktop-layer changes, verify parent host, visibility, z-order, non-topmost behavior, and recovery after relevant Shell/display events where the environment permits.
- Stop only a process whose PID and executable path have been verified. Flush or snapshot user data first.
- Document what was automatically tested and what still requires manual Windows/IME/multi-monitor validation; do not report an unperformed GUI check as passed.
