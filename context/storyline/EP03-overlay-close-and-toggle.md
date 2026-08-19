# 🎬 Episode 3 — Overlay Close Button & Native Toggle

> **Date:** 2026-06-24 · **Branch:** `main` · **Commits:** `5d758eb`
> **One-liner:** Made the desktop overlay feel native — a visible ✕ close button and a real
> on/off toggle (bar hidden until Ctrl+Win+H), fixing the "stuck running / no window" feel.

## 🎯 Intent
Two complaints: the app had no close button (only the tray menu, so it felt stuck running), and the
hotkey didn't toggle like native Live Captions (the bar was always visible). Fix both.

## 🛠️ What changed
- **`Hark.App/OverlayWindow.xaml`** — added a header **✕ close button** (red hover, native-style)
  in a new header column.
- **`Hark.App/OverlayWindow.xaml.cs`** — `CloseRequested` event wired to the ✕; added
  `ShowAndActivate()` that re-asserts `Topmost` so the bar surfaces above other always-on-top
  windows (fixes "it opened behind the browser").
- **`Hark.App/App.xaml.cs`** — overlay now **starts hidden**; toggle ON → `ShowAndActivate()` +
  start capture, toggle OFF → stop capture + `Hide()`. ✕ calls `Shutdown()` (clean exit via
  `OnExit`). Tray simplified to Start/Stop + Exit; **double-click tray** also toggles. One-time
  startup balloon explains the hotkey.

## 🧠 Decisions
- **Hidden-until-toggled** mirrors native Live Captions and makes on/off obvious.
- **Re-assert Topmost on show** rather than fancier z-order hacks — simplest reliable surfacing.
- Kept ✕ = full app exit (not just hide), since discoverability of "how do I quit" was the problem.

## 🚧 Problems & resolutions
- **`CS0103: CloseButton does not exist`** after editing XAML → the generated partial regenerates on
  full build (per-file analyzer was stale). `run_build` resolved it.
- **Benign `WFO0003`** WinForms analyzer warning about DPI in `app.manifest` (optional cleanup;
  could move to `ApplicationHighDpiMode`).

## ✅ Verification
- Build green. User confirmed: app starts with no visible window, **Ctrl+Win+H shows the bar**, and
  the **✕ close button is present**. Process exited **code 0** (clean shutdown).

## 🔓 Open threads
- Confirm toggle-off hides the bar in normal use (start/stop round-trip).
- If the hotkey is ever blocked on another machine: make the combo configurable / fall back.
- Optional: silence `WFO0003` via `ApplicationHighDpiMode`.
- (Carried) speaker diarization; permanent `az` ACL fix; credential-convention memory.
