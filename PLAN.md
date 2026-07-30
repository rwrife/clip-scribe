# clip-scribe — Project Plan

## Scope

Build a fast, private, persistent **clipboard history manager for Windows 10/11**:

- Background capture of clipboard text (v1) and images (stretch), stored locally.
- Persistent, deduplicated, searchable history that survives reboots.
- Global-hotkey overlay to search and paste any past clip.
- Pinned favorites and a reusable snippet library.
- Paste-as-plain-text and multi-clip collect/paste.
- **Optional** local-AI text transforms via an OpenAI-compatible local endpoint.
- Runs entirely offline; no account, cloud sync, or telemetry.

### In scope (v1)
- Text clip capture + history with instant substring/fuzzy search.
- Tray-resident app, global hotkey overlay, keyboard-driven paste-back.
- Pins/snippets, plain-text paste, configurable history size & retention.
- Optional AI transform menu with graceful fallback when unconfigured.

## Architecture / tech approach

- **Language/UI:** C# / .NET 8, **WPF** (WinUI/System.Windows) for the overlay + settings; tray icon via `NotifyIcon`/`H.NotifyIcon`.
- **Clipboard capture:** Win32 clipboard-format listener (`AddClipboardFormatListener` / `WM_CLIPBOARDUPDATE`) via a hidden message window; read text (and later `CF_BITMAP`).
- **Paste-back:** set clipboard content, then synthesize `Ctrl+V` via `SendInput` to the previously focused window.
- **Global hotkey:** `RegisterHotKey` (default `Ctrl+Shift+V`, configurable).
- **Storage:** SQLite (`Microsoft.Data.Sqlite`) with **FTS5** for full-text search; clips table (hash for dedupe, timestamp, type, pinned flag, source app). WAL mode; DB under `%LOCALAPPDATA%\clip-scribe`.
- **Search:** FTS5 for text; in-memory fuzzy ranking for short queries.
- **Local-AI:** thin `HttpClient` calling an OpenAI-compatible `/v1/chat/completions` (Ollama/llama.cpp). Endpoint + model + presets configurable; feature-flagged off by default. All calls local; timeouts + graceful failure.
- **Settings:** JSON config in `%LOCALAPPDATA%\clip-scribe\config.json`.
- **Security/privacy:** optional exclude-list (e.g., password managers / `CF_CLIPBOARD_VIEWER_IGNORE`), "ignore sensitive" heuristic, clear-history + pause-capture controls.
- **Testing:** xUnit for storage/dedupe/search/transform-client (endpoint mocked); UI kept thin.

## Milestones

1. **M1 — Capture + storage:** clipboard listener, dedupe, SQLite+FTS5 persistence, retention/size limits. Headless-testable core.
2. **M2 — Overlay UX:** tray app, global hotkey, search overlay, keyboard navigation, paste-back to prior window.
3. **M3 — Pins & snippets:** pin/unpin, snippet library, plain-text paste, multi-clip collect/paste.
4. **M4 — Local-AI transforms:** transform client, preset menu, settings, graceful fallback.
5. **M5 — Packaging & polish:** portable self-contained x64 build, MSIX, settings UI, privacy/exclude controls, first-run onboarding.

## Non-goals

- No cloud sync, accounts, or cross-device history.
- No telemetry or external network calls beyond a user-configured **local** AI endpoint.
- Not a full snippet-expansion/text-expander macro engine (basic snippets only).
- No macOS/Linux port in v1 (Windows-first).
- No bundled/managed AI model — users bring their own local model.

## Packaging target for Windows

- **Primary:** portable, self-contained single-folder **x64** build (`dotnet publish -r win-x64 --self-contained`), zipped for Releases; no admin install required.
- **Secondary:** **MSIX** package for Start-menu install and clean uninstall.
- Windows 10 (1809+) and Windows 11 supported. Runs from the system tray, optional launch-at-login.
