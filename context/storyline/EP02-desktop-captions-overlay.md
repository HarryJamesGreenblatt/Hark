# 🎬 Episode 2 — Desktop Captions Overlay (`Hark.App`)

> **Date:** 2026-06-24 · **Branch:** `main` · **Commits:** `bf56eaf`
> **One-liner:** Added a WPF tray app with a global hotkey and a Live-Captions-style overlay that —
> unlike native Live Captions — is selectable, copyable, resizable, and scrollable.

## 🎯 Intent
Turn the CLI MVP into "what Live Captions should have been": an integrated, hotkey-launchable
desktop captions overlay that doesn't functionally suck. WPF chosen over WinUI; CLI retained.

## 🛠️ What changed
- **`Hark.Core/HarkSession.cs`** (new) — shared pipeline orchestrator (Capture → Convert →
  Recognize → sinks) with `StartAsync`/`StopAsync` + `Interim`/`Final`/`Error` events, enabling
  clean toggling.
- **`Hark.Cli/Program.cs`** — refactored onto `HarkSession`; **behavior identical** (verified live).
- **`Hark.App/`** (new WPF project, already in `Hark.slnx`):
  - `App.xaml.cs` — tray `NotifyIcon`, `Ctrl+Win+H` toggle, session lifecycle, `AzureCliCredential`.
  - `OverlayWindow.xaml(.cs)` — borderless topmost translucent bar; **read-only `RichTextBox`**
	(selectable + `Ctrl+C` + Copy/Copy all), `CanResizeWithGrip`, 200-line scrollback, auto-scroll
	that pauses during selection; draggable via the header.
  - `OverlaySink.cs` — `ITranscriptSink` marshaling segments to the WPF dispatcher.
  - `GlobalHotkey.cs` — Win32 `RegisterHotKey` on a message-only window; `NoRepeat`; graceful
	fallback if reserved.
  - `GlobalUsings.cs` — aliases (`Application`, `MessageBox`, `Brush`, `Color`, ...) to resolve
	WPF vs WinForms ambiguity (WinForms enabled only for the tray icon).

## 🧠 Decisions
- **WPF** over WinUI — fastest path to a click-through-capable translucent overlay; stays in
  `net9.0-windows`.
- **No new NuGet** — tray via WinForms `NotifyIcon`, hotkey via P/Invoke.
- **Extract `HarkSession`** so CLI + app share one pipeline (vs duplicating wiring).
- **Selectable text via `RichTextBox`** is the core "doesn't suck" differentiator; tradeoff: window
  drags from the header strip (not the text area) so selection works.
- **Draggable, not click-through** for MVP.

## 🚧 Problems & resolutions
- **`CS0104` ambiguous `Application`/`Brush`/`Color`** (WPF vs WinForms) → **Fix:** global using
  aliases in `GlobalUsings.cs`.
- **"App frozen, no window, hotkey dead"** → **Root cause:** overlay was z-ordered behind other
  topmost windows; `dotnet run` blocking is normal for a GUI. Not a crash.
- **Live-Captions parity gaps** (text not selectable; window fixed-size) → **Fix:** `TextBlock` →
  read-only `RichTextBox` with copy menu; `NoResize` → `CanResizeWithGrip` + scrollback.
- **`replace_string_in_file` repeatedly failed on `OverlayWindow.xaml`** → worked around by deleting
  and recreating the file.

## ✅ Verification
- Solution builds green; CLI re-verified live via `HarkSession` (interims, finals, graceful stop).
- App runs tray-resident; user confirmed live captions render, and **text selection + expanded/
  resized window** work (screenshot evidence).
- Committed as `bf56eaf` (13 files); no artifacts/`context/` staged.

## 🔓 Open threads
- Optional: drag-from-anywhere (except while selecting); click-through toggle.
- Persistent settings (region/resource, overlay position/opacity, font size) vs env vars.
- Speaker diarization (`ConversationTranscriber`) — still deferred.
- Permanent `az` ACL fix vs running elevated.
- Approve the pending credential-convention memory.
