# clip-scribe

> Windows clipboard history manager — searchable clip history, pinned favorites & snippets, with optional local-AI text transforms. **Offline & privacy-first.**

## Overview

`clip-scribe` is a lightweight Windows 10/11 desktop utility that remembers everything you copy so you never lose a clip again. It keeps a rolling, searchable history of text (and optionally images) you copy, lets you pin frequently used clips and snippets, and pastes any past item back with a global hotkey. An optional local-AI layer can transform clip text on demand (clean up formatting, summarize, change tone, translate) using tiny local models via Ollama or llama.cpp — but the core clipboard-manager value works fully offline with zero AI.

Everything stays on your machine. No account, no cloud, no telemetry.

## Motivation

Windows' built-in clipboard history (`Win+V`) is limited: it caps entries, has weak search, loses history on reboot unless synced to the cloud, and can't organize snippets. Power users, developers, writers, and support staff constantly re-copy the same strings, lose an important clip under a flurry of new copies, or want to reshape pasted text. `clip-scribe` fills that gap with a fast, persistent, searchable, private clipboard manager that optionally adds small local-AI conveniences without shipping your clipboard to a server.

## Use cases

- **Never lose a clip:** scroll or search back through hundreds of past copies.
- **Snippet library:** pin boilerplate (addresses, email templates, code stubs, commands) and paste with a hotkey.
- **Developer workflow:** keep the last N tokens/paths/commands you copied and re-paste any of them.
- **Writing/support:** transform a clip with local AI — "make this a polite reply", "summarize", "fix grammar", "to markdown" — without cloud services.
- **Paste as plain text:** strip rich formatting from any clip on paste.
- **Multi-item collect & paste:** gather several clips, then paste them in sequence.

## How to use (Windows-first quickstart)

> Status: early scaffold. Steps below describe the intended v1 experience; see **Current status** and `PLAN.md`.

1. Download the latest portable build (`clip-scribe-win-x64.zip`) from Releases, or the MSIX installer.
2. Run `clip-scribe.exe`. It starts in the system tray and begins recording clipboard history.
3. Press the global hotkey (default **`Ctrl+Shift+V`**) to open the history overlay.
4. Type to **search** your history, use arrow keys to select, and press **Enter** to paste.
5. Right-click any clip to **Pin**, **Delete**, **Copy as plain text**, or **Transform with AI** (if enabled).
6. Manage pinned snippets and settings from the tray menu → **Settings**.

### Example workflow

```text
1. Copy a messy paragraph from a PDF.
2. Press Ctrl+Shift+V → the clip is at the top of history.
3. Right-click → Transform → "Fix formatting & grammar".
4. clip-scribe sends the text to your local model, replaces the preview.
5. Press Enter to paste the cleaned-up version into your editor.
```

```text
Snippet reuse:
1. Pin your standard git remote command once.
2. Ctrl+Shift+V → type "remote" → Enter → pasted instantly.
```

## Local-AI integration (optional)

`clip-scribe` can call a **local** OpenAI-compatible endpoint (Ollama or llama.cpp `server`) for text transforms. It is **off by default** and never required.

- Point clip-scribe at `http://localhost:11434` (Ollama) or your llama.cpp server URL.
- Recommended tiny models: `llama3.2:1b`/`3b`, `qwen2.5:1.5b`, `phi3.5-mini`, or MiniCPM-family small models — anything that runs comfortably on CPU/iGPU.
- Built-in transform presets: *Fix grammar*, *Summarize*, *Make polite/formal/casual*, *To Markdown*, *Extract action items*, *Translate*.
- If no model/endpoint is configured, AI menu items are hidden and clip-scribe runs as a pure clipboard manager.

Nothing is sent anywhere except the local endpoint you explicitly configure.

## Current status / milestones

🚧 **Early scaffold.** Docs and backlog are in place; implementation tracked issue-by-issue.

- [ ] M1 — Clipboard capture engine + persistent SQLite history
- [ ] M2 — Tray app + searchable history overlay + paste-back
- [ ] M3 — Pins, snippets, and plain-text paste
- [ ] M4 — Optional local-AI transforms
- [ ] M5 — Windows packaging (portable x64 + MSIX) and settings

See [`PLAN.md`](./PLAN.md) for scope, architecture, and packaging details.

## License

MIT (see repository).
