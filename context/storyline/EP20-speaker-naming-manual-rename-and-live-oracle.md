# 🎬 Episode 20 — Putting Names to Voices: Manual Rename, Live Oracle Naming & the Alias-Map Fix

> **Date:** 2026-08-27 · **Branch:** `main` · **Commit:** `daa5a77`
> **One-liner:** Turned anonymous `Guest-N` speakers into **real identities** two convergent ways —
> a **manual right-click Rename** on the pills and an **autonomous "Oracle" naming pass** that infers
> names live from the transcript — both flowing through one `ConversationStore.Rename` path. The hard
> part wasn't the UI or the LLM; it was realizing a live rename must be a **persistent acoustic-label →
> name alias applied at commit time**, not a one-shot history rewrite, or the streaming engine's
> repeating `Guest-N` re-spawns the moment the speaker keeps talking.

## 🎯 Intent
Close the long-standing "diarization caveats — consider a rename/merge affordance" thread (open since
EP05) and take the deferred **name/role binding** (EP14's recognition-head roadmap, 0.5) off the shelf.
User framing across the session: *"update the Guest entities to reflect the appropriate identities"* →
*"does the API expose naming?"* (no) → *"formalize `ConversationStore`, add a VS-style right-click
rename"* → *"have the Oracle fill names in automatically"* → *"auto-apply, not suggest"* → *"it must run
DURING capture, not on Stop"* → *"why doesn't this ride the beat-detection loop?"*.

## 🛠️ What changed
- **Manual rename (`ConversationStore.cs` · `OverlayWindow.xaml(.cs)` · `SpeakerWindow.xaml.cs` ·
  `App.xaml.cs`)** — `ConversationStore.Rename(old, new)` re-tags every line and **rebuilds the target
  bucket in chronological order** (so renaming into an existing name **merges**). Right-clicking a
  speaker pill opens a **dark-styled `Popup`** (a `TextBox`; Enter saves, Esc / click-away cancels) —
  chosen over an inline edit (fights the left-click "filter to speaker" gesture) and over a heavyweight
  modal `Window` (wrong over a translucent overlay). `App.OnSpeakerRenameRequested` orchestrates:
  store rename → relabel/merge the pill → rebuild caption lines → **rebind (or merge-close) any open
  speaker page**. Pill handlers now read the button's **current `Content`**, so a rename only relabels
  (no rewiring).
- **Oracle auto-naming (`Hark.Core/Transcription/SpeakerNamingRefiner.cs` (new) · `App.xaml.cs`)** — a
  text-only, strict-JSON-schema pass that maps each `Guest-N` → a real name **only** when the transcript
  identifies the *speaker* (introduction / direct address / self-ID), returning `""` otherwise; it is
  told to **never** name a merely-mentioned third party and never invent. Runs **live** on a
  `DispatcherTimer` cadence that **mirrors the Vision beat loop**: revision-gate (skip when unchanged) +
  a 15 s floor measured from pass **start** + single-flight guard + supersede + an **"only while an
  anonymous label exists" gate** (goes quiet once everyone's named, reactivates on a new split) +
  **head+tail transcript cap** (first 24 + last 120 lines, so cost is O(n) not O(n²)). Results apply
  through the **same** `OnSpeakerRenameRequested` path, so a later streaming split (`Guest-3` for an
  already-named person) **merges** into the name. Also wired into the offline Stop-time refine as a
  belt-and-suspenders `NameAsync`.
- **The identity fix (`ConversationStore.cs` · `OverlaySink.cs`)** — renames are now a **persistent
  acoustic-label → display-name alias** (`_aliases` + `_seenRaw`) resolved inside `CommitFinal`, so
  **future** utterances of a renamed `Guest-N` land under the chosen name instead of re-spawning the old
  label. `Rename` re-points **every** seen raw label whose current display equals the old name, then
  rewrites history + merges. `OverlaySink` resolves the alias when prefixing live captions
  (`"Guest-2:"` → `"Don Rickles:"` immediately). Aliases clear on `Clear`/`Rebuild`.
- **UI cleanups (same commit)** — dropped the duplicative **"Open page"** context-menu item (left-click
  already opens); dark-styled the pill context menu to match the popup (`DarkContextMenuStyle` /
  `DarkMenuItemStyle`); renamed the captions scope toggle **LATEST/TRANSCRIPT → LATEST/ALL** ("Live
  Captions killer" framing); removed the **leftover diagnostic refine toast** (`4→3 speakers, N lines
  regrouped` + its `CanonicalLabels`/`DistinctSpeakers` diff), leaving only the error balloon.

## 🧠 Decisions
- **Manual and Oracle naming are complementary, converging on one path** — **because** one code path
  (`store.Rename`) means one merge behavior and one propagation to pills/pages/recaps. The Oracle is
  just an *automated caller* of the manual mechanism, so its output is never "locked in" — a wrong guess
  is a right-click away, and manual names always win.
- **Auto-apply, don't suggest** — **because** (user's reasoning) a confident auto-name at the end of a
  beat removes the *urge* to fiddle mid-stream; without it, users reach in and mess with it constantly.
  The manual override is the safety valve.
- **Live cadence, not a Stop-time pass** — **because** HARK's Stop path `Hide()`s the bar and the next
  Start `ResetConversation()`s it, so an offline naming result is **unreachable/uncopyable**. Naming has
  to resolve while the bar is up — which the EP18/19 Vision beat loop already proved out.
- **A live rename must be a persistent alias, not a history rewrite** — **because** the streaming
  diarizer keeps emitting the *same* `Guest-N` for a voice; rewriting only the past means the next
  utterance re-creates the label. The alias binds the acoustic identity for the rest of the session; the
  loop then only handles genuinely new splits.
- **Named labels are stable — no auto-correction** — **because** letting names re-evaluate mid-session
  is exactly the flip-flopping the user disliked. You can't have "stable once set" *and* "keeps
  re-guessing." Stable + one-click manual fix is the honest trade.
- **No debounce on the naming loop** — **because** the revision-gate + start-measured cadence floor +
  transcript cap already bound the work; debounce only avoids mid-utterance firing, which matters for a
  Vision *image* but not for whole-transcript name inference. (It was cargo-culted from the Vision
  skeleton and removed once called out.)

## 🚧 Problems & resolutions
- **Symptom:** after naming `Guest-2` → Don Rickles, a fresh **`Guest-2` pill reappeared** once he kept
  talking, and the loop "continuously failed to associate" the voice / reassigned other guests. →
  **Root cause:** rename was a one-time history rewrite; the live engine re-emits `Guest-2`, so
  `CommitFinal` re-spawned the bucket + pill, and the loop re-guessed the reborn label against a
  celebrity-heavy tail. → **Fix:** persistent acoustic-label alias applied at `CommitFinal` (+ resolved
  in `OverlaySink`); `Rename` re-points all matching raw labels.
- **Symptom:** the host was labeled **"Jane"** instead of Dean Martin. → **Root cause:** *not* a naming
  bug — the ASR mis-heard "Dean" as "Jane" ("thank you **Jane**") and the Oracle faithfully read it.
  Later "why not **Dean**?" doesn't self-correct because named labels are intentionally stable. → **Fix
  (by design):** one manual right-click → Rename, which the alias map then makes permanent. Don't chase
  prompt tweaks for upstream transcription error.
- **Symptom:** `KeyEventArgs` ambiguous (`System.Windows.Forms` vs `System.Windows.Input`). → **Fix:**
  fully-qualified `System.Windows.Input.KeyEventArgs`.
- **Transient:** the WPF `_wpftmp` markup-compile "could not find `*.g.cs`" glitch — cleared by a rebuild.

## ✅ Verification
- Builds green across `Hark.Core` + `Hark.App` (`daa5a77`); pre-existing warnings only (two CS4014 on
  the fire-and-forget refine calls, the app.manifest DPI note).
- **Manual rename proven live** on a Tony Clifton clip (Guest-1/2 → "TV Show Host" / "Tony Cliffton"),
  and the rename **propagated into the AI recap** (Speakers cards + Meeting Notes used the human names) —
  confirming the whole pipeline reads from the store.
- **Live merge proven** — renaming a late-appearing split back to "Dean Martin" merged it into the
  existing speaker.
- **Live Oracle naming proven** on a Don Rickles roast: `Guest-1/2` resolved to `Jane` (ASR's "Dean") +
  `Don Rickles` **continuously**, with **no reappearing `Guest-N`** after the alias fix (the earlier
  thrash was gone).

## 🔓 Open threads
- **Naming quality is ASR-bound** — the "Jane vs Dean" miss is upstream transcription, not inference;
  an optional **phrase-list of known names** on the live path (already a carried thread) would bias the
  recognizer and help both captions and naming.
- **Optional: allow a *confident* late correction** — a guarded exception to "named labels are stable"
  (e.g. only when a much stronger, explicit self-ID appears) — deliberately deferred to avoid flip-flop.
- **Stop-time result is still unreachable** (`Hide()` + `ResetConversation()`); the offline refine's
  global re-cluster + `NameAsync` land in a hidden bar. Making Stop *preserve & show* the refined result
  (or dropping the offline naming as redundant with the live loop) is a follow-up.
- Carried: the **engine boundary** (`RefinementEvent`/`GroundingEvent`) so live-history relabel is clean;
  diarization **Fork A** (sub-segment splits); the Vision **render dead-time** phase; and the standing
  threads in `STORYLINE.md`.
