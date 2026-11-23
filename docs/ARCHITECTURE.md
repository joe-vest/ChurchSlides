# Architecture: Church Presentation App

Version: 0.2
Date: 2026-11-23
Input: `docs/FRD.md` (FRD v0.3)
Status: Decisions in §14 and §15 confirmed by the product owner on 2026-11-23. v0.2 adds the decisions from the document review of the same day (D-10 to D-13, and changes in §4.1, §5, §6, §7, §12 and §16).

Requirement IDs (for example `AUD-017`) refer to the FRD. `ChurchSlides` is the product name (Q-010, resolved).

---

## 1. Goals and design drivers

Ranked by how much they shape the design:

1. **Low memory and CPU on integrated graphics** (PLT-002, 004, 005). This rules out browser engines, large UI frameworks, and any design that keeps full-resolution images or many rendered slides in RAM.
2. **The Output must never fail in front of the congregation** (SAFE-006, LIB-011, DEC-012). Nothing in the editor, the remote, the sync watcher, or Actions may blank or crash the projector.
3. **One rendering path.** The editor, previews, thumbnails, and Output all use the same renderer (SLD-026), so what you edit is what is shown.
4. **Plain files in a synced folder** (LIB, DEC-014). The app behaves like a good citizen next to Google Drive: atomic writes, conflict detection, tolerance for files appearing and changing under it.
5. **Simplicity** (FRD §0.9). Few moving parts, few dependencies, one UI thread, one command API.

---

## 2. Technology choices

| Concern | Choice | Why / notes |
|---|---|---|
| Runtime | **.NET 10 (LTS)**, x64 | .NET 8 reaches end of support in Nov 2026, so 10 is the current LTS. Confirm .NET 10 runs on the church's Windows 10 build during the spike. |
| UI + Output | **WPF** | Retained-mode, GPU-composited (falls back to software on weak hardware), mature `RichTextBox`/`FlowDocument` for editing (SLD-022), native multi-monitor control. No browser engine (PLT-004). |
| MVVM | `CommunityToolkit.Mvvm` | Small source-generated library. No Prism, no Material/UI-kit packages (they cost RAM and startup time). |
| DI / logging | `Microsoft.Extensions.DependencyInjection`, a small file logger | Keep it to the BCL where possible. |
| Persistence | JSON files via `System.Text.Json` with **source generators** | Fast, low allocation, no reflection at startup. Plain files per LIB-002. |
| Audio | **NAudio** | Suggested by the FRD. Behind an interface (AUD-020). |
| Phone remote | ASP.NET Core **Kestrel** minimal API + Server-Sent Events | Only started if enabled (REM-002). Isolated in its own assembly so it costs nothing when off. |
| QR code | `QRCoder` | Small, pure managed. |
| Tests | xUnit | Core/Engine are UI-free so they test without WPF. |
| Packaging | Self-contained, x64, **ReadyToRun** (not trimmed, since WPF doesn't support trimming) | Avoids installing a runtime on the church PC. Faster startup on slow CPUs. Larger download is acceptable. Framework-dependent is a fallback if the runtime is already installed. |

Runtime settings to apply and measure in the spike: workstation (non-server) GC, `System.GC.ConserveMemory`, `TieredPGO` off, and `TieredCompilation` on (quick startup). Per-monitor DPI awareness (v2) in the app manifest.

**What we deliberately don't use:** Electron/CEF/WebView2 (PLT-004), Entity Framework or any database (LIB-002), Lucene or other search engines (a Library of a few thousand Shows fits a simple in-memory index), WinForms (we'll P/Invoke `EnumDisplayMonitors` instead of pulling in `Screen`), Newtonsoft.Json.

---

## 3. Solution structure

A modular monolith. One process, a handful of assemblies with strict dependency direction. Only the last two reference WPF.

```
                ┌──────────────────────────┐
                │      ChurchSlides.App         │  WPF shell: windows, views, view-models,
                │  (composition root)      │  hotkeys, settings UI, DI wiring
                └──┬────────┬─────────┬────┘
                   │        │         │
        ┌──────────▼──┐  ┌──▼──────┐  ▼───────────────┐
        │ .Rendering  │  │ .Remote │  │ .Audio        │
        │ (WPF)       │  │(Kestrel)│  │ (NAudio)      │
        └──────┬──────┘  └────┬────┘  └──────┬────────┘
               │              │              │  implement interfaces
        ┌──────▼──────────────▼──────────────▼───────┐
        │              ChurchSlides.Engine                │  presentation session, command API,
        │  (no UI, no file I/O, no audio library)    │  actions, timers, cues, CCLI, checks
        └──────────────────────┬─────────────────────┘
        ┌──────────────────────▼─────────────────────┐
        │  ChurchSlides.Storage        ChurchSlides.Scripture  │  files, migration, watcher, index  /
        │  (Library I/O)          (Zefania + parser) │  Bible import and reference parsing
        └──────────────────────┬─────────────────────┘
        ┌──────────────────────▼─────────────────────┐
        │              ChurchSlides.Domain                │  records, enums, tokens, template
        │        (pure, no dependencies)             │  resolution, flatten/navigation rules
        └────────────────────────────────────────────┘
```

| Assembly | Contains | FRD areas |
|---|---|---|
| `Domain` | Entities (Show, Slide, TextBox, Template, Service, ServiceItem, …), rich-text model, token parser, **template resolver**, **Service flattening** (item/slide sequence with skips), validation rules | §3, §4, TPL, VAR, SVC |
| `Storage` | `ILibrary` and repositories, atomic writer, content-hash conflict detection, schema migration, `FileSystemWatcher` pipeline, search index, draft store, usage-log files | LIB, CAT, SAFE, CCLI-013 |
| `Scripture` | Zefania importer, per-Bible index, reference parser, slide generator | SCR |
| `Engine` | `PresentationSession`, `CommandApi`, `ActionProcessor`, `TimerService`, `CueController`, `CcliService`, `CheckService`, `ServiceEditor` operations (fork, fill placeholder, skip) | LIVE, ACT, TMR, AUD (logic), MODE/CCLI, CHK, SVC |
| `Audio` | `NAudioPlayer : IAudioPlayer` and its `IPlaybackClock` | AUD |
| `Rendering` | `SlideRenderer`, image cache, text layout, transitions, thumbnails, `OutputWindow` host control | SLD, PLT-006/007, LIVE-030..033 |
| `Remote` | Kestrel host, embedded web page, SSE broadcaster, token and IP filter | REM |
| `App` | Views and view-models, editors, cue editor, settings, `OutputWindow`, hotkeys, crash handling | UI, SAFE-006 |

`ILibrary` lives in `Storage` (the dependency rule has Engine reference Storage), so Engine tests fake it from there. `Engine` talks to the rest of the outside world only through interfaces it owns: `IAudioPlayer`, `IClock` (defined in `Domain`, shared with `Storage`), `IOutputSurface`, `IRemoteHost`. That makes it testable with fakes (fake clock for timers, fake audio for cues) and keeps the FRD's FUT-004 "single command API" honest.

---

## 4. Runtime architecture

### 4.1 Threads

- **One WPF UI thread** runs all view-models, the Engine, and the Output. Reasons: simplicity, no cross-thread marshalling bugs, and the Engine's state is single-writer.
- **Smooth crossfades don't depend on the UI thread being idle.** A crossfade animates the opacity of two pre-rendered bitmap layers. WPF runs that on its independent composition thread, so an autosave or index update on the UI thread won't stutter it.
- **Background work** runs on the thread pool and posts results back: image decoding and downscaling, search index rebuild, Bible index build, file hashing, autosave.
- **Rendering to bitmaps is not thread-pool work.** WPF can only build visuals and run `RenderTargetBitmap` on a thread with a dispatcher (STA). Pre-rendering a 1440×900 slide can take tens of milliseconds, and on the UI thread that delays hotkeys and Cue posts (AUD-018 allows 50 ms). Plan: a single **render thread** (STA, own dispatcher) that builds frozen bitmaps from `RenderModel`s and hands them to the UI thread. Spike S4/S6 measures UI-thread stalls first. If they stay under about 10 ms, the render thread can wait.
- **High-priority audio poller:** one dedicated thread polls audio position every ~10 ms for cue accuracy (AUD-018), then posts the cue to the UI thread.
- **Kestrel** uses its own threads. It never touches Engine state directly. It enqueues commands (§6) and reads an immutable state snapshot.

### 4.2 Fault isolation (SAFE-006)

- `DispatcherUnhandledException` and `TaskScheduler.UnobservedTaskException` log and mark the exception handled. They never shut down the app. Each editor view-model and the Action processor wrap their entry points in a guard that logs and shows a non-blocking notice.
- The Output window renders only from immutable, pre-resolved `RenderModel`s (§7). It holds no references to editable objects, so a half-edited Show can't break it.
- If the Output's own render throws, it falls back to its last good frame or a black frame and logs it.
- The Output window is **not** a child of any editor window. Closing or crashing an editor doesn't close it.
- **Decision D-1 (§14):** I recommend the Output stay on the main UI thread. A separate UI thread (or process) for Output gives more isolation but adds real complexity. The `IOutputSurface` interface keeps that option open.

### 4.3 Startup and shutdown

1. Single-instance mutex.
2. Load local settings from `%LOCALAPPDATA%\ChurchSlides\settings.json` (LIB-008): Library path, output display, remote settings.
3. Open the Library (LIB-010: if missing, prompt for a folder, don't crash).
4. Load `library.json`, categories, Templates. Build or load the search index lazily in the background, so the Catalog window is usable immediately.
5. Check for unsaved drafts and a crashed presentation (SAFE-004/005).
6. On exit, run the CCLI close flow (CCLI-010) before shutdown.

---

## 5. Domain model

The FRD's §4 sketch is the starting point, with these structural choices:

- **Immutable `record`s** for persisted entities. Editing happens on a draft copy (§9). A `ServiceItem` is a sealed class hierarchy (`ShowReferenceItem`, `ShowCopyItem`, `PlaceholderItem`, `ScriptureItem`) with a JSON type discriminator, rather than one record with many nullable fields. Compile-time exhaustive `switch`es then catch missing cases.
- **`RichText`** is our own small model: `Paragraph[]` of `Run`s, where a run is text plus optional `font / size / color / bold / italic`, or a `TokenRun` (Variable, Timer, or Show metadata). It is serialised to JSON directly. We **do not** store WPF XAML. The WPF `FlowDocument` is only the editing surface, with converters to and from `RichText` (§9.3). This protects files from framework changes and lets the Output renderer skip `FlowDocument` entirely.
- **Sizes are fractions** of canvas height, geometry is fractions of the canvas (SLD-001, SLD-005).
- **Stable IDs everywhere** (GUIDs). Two rules fall out of this:
  - *Forking a Show Reference into a Show Copy preserves all Slide IDs*, so skips, Actions, and Cues keep working.
  - *Duplicating a Show in the Catalog (DEC-005) and instantiating a Service Template both mint new IDs and remap every internal reference* (Cues, Actions, GoTo targets). This is a single `IdRemapper` utility used in both places.

### 5.1 Resolution pipeline

Everything displayed goes through one pure function in `Domain`:

```
Slide + Show + Template chain + Service context  ──►  RenderModel (immutable)
```

1. **Resolve Template**: Slide override → Show default → Category default → Library default (TPL-010).
2. **Bind boxes**: bound boxes take geometry and defaults from the Template Box, detached keep their own, mismatches go to the first non-copyright box and are flagged (TPL-021..027).
3. **Inject copyright box** on the first or last slide per Copyright Placement, unless public domain (CCLI-004/005).
4. **Resolve tokens**: Variables from the current Service, Show metadata, church license. Timer tokens become `LiveTextRun`s (§8.4), not static text.
5. Output a `RenderModel`: background spec, positioned text boxes with fully resolved run formatting (inline formatting wins over Template defaults, TPL-024), and live-text slots.

Resolution never fails (TPL-014). Anything wrong is worked around and listed in `RenderModel.Problems` (missing Template, unknown Template Box, empty or undefined Variable, undefined Timer, ...), which the pre-service check reads. If no Template resolves, a built-in plain Template is used. Timer tokens resolve to `RenderTimerRun`s that carry the Timer's warning settings, and the renderer fills in the live value. The resolver lives in `ChurchSlides.Domain.Resolution`: `TemplateResolver`, `SlideResolver`, `TokenMarkup`, `TextBoxBinding`.

Resolved models are cached. The key must hold everything the result depends on, or stale output reaches the projector:

| Key part | Why |
|---|---|
| `serviceId`, `itemId`, `slideId` | Variables belong to a Service. The same slide in two items can differ, because the copyright box depends on the item's skip list (CCLI-004 "first/last displayed"). |
| Show version | Slide content and metadata tokens (`{{show.title}}`). |
| Template version (all Templates in the chain) | TPL-003. |
| Category version | A Category's default Template can change (TPL-013). |
| Library settings version | Copyright format, CCLI licence number, Library default Template. |
| Service version | Variables and skip lists. |
| Canvas size | Output vs Preview vs thumbnail. |

Invalidation is by version counters. This satisfies TPL-003/013 (changing a Template affects the next display) without touching slide files. A simpler alternative is acceptable: clear the whole cache on any Library change, since resolution is cheap. Bitmaps are the expensive part.

### 5.2 Service navigation (items and slides are two levels, always)

**The hierarchy is never flattened in the model, the state, or the UI.** The operator thinks in two levels, so the app does too: the Service view shows items as headers (title, kind, audio badge, "modified for this Service") with their slides beneath, and the current position is always `(ItemId, SlideId)`. The sequence below is only an *index* used to compute "what comes after this slide", so Next can cross item boundaries (LIVE-005).

Navigation exists at both levels:

| Level | Operations |
|---|---|
| Slide | Next / Previous (cross item boundaries), click a slide |
| Item | **Next item / Previous item**, click an item header (goes to its first displayable slide) |

Item-level moves are first-class commands (§6.1), not a side effect of slide moves. They are what the operator uses to abandon a song, and they are the natural hook for item-level behaviour such as audio (§6.5, Q-A7).

`ServiceSequence` (Domain, `Navigation/`) is built by a pure function, `ServiceSequence.Build(service, findShow)`, and is immutable. It keeps the two levels: `Items[i]` is a `SequenceItem` (item ID, status `Show` / `MissingShow` / `UnfilledPlaceholder` / `NotGenerated`, the Show, `HasDisplayable`) and `Items[i].Slides[j]` a `SequenceSlide` (slide ID, `IsSkipped`, `IsDisplayable`). Positions are `SlideRef(ItemId, SlideId)`. The flat order exists only inside its searches (`NextDisplayable`, `PreviousDisplayable`, `FirstDisplayable`, `FirstDisplayableOf`, `Next/PreviousItemWithDisplayable`) and as `All` for LIVE-041. It holds **every** slide, with a flag. Skipped slides, slides of unfilled Placeholders and missing Shows, and items with no displayable slide are listed but not displayable (LIVE-006). The skip list is ignored for a Show whose Audio Track has Cues (SVC-025). One domain function returns the skip list in force for an item, and both this sequence and the copyright position (CCLI-004, `SlidePosition`) use it, so the projector and the copyright line always agree. Every slide is in the list because the operator can click a skipped slide (LIVE-017). Next and Previous then move to the nearest displayable entry in either direction. This gives LIVE-005 (crosses item boundaries) and LIVE-007 (stops at the ends) for free.

The list is rebuilt whenever the Service or a referenced Show changes, and the cursor is re-anchored by `(ItemId, SlideId)` so edits during a presentation don't lose position (LIVE-040). If the current slide or item was removed (LIVE-041), the cursor remembers its old neighbours: the Output keeps the last frame, Next goes to the slide that followed it, and Previous to the one before it.

**Not presenting.** Before Go Live or Rehearse, the session has a *selected* slide but no current slide. The Output shows black (LIVE-018). Starting presentation displays the selected slide, or the first displayable one (LIVE-009).

---

## 6. Engine: presentation, commands, and actions

### 6.1 Single command API (FUT-004)

```
ICommandApi                                    // every call also takes an Origin: Operator or Automation
  Next() Previous()                            // slide level, crosses items (LIVE-005)
  NextItem() PreviousItem()                    // item level (LIVE-016)
  GoToSlide(item?, slide) GoToItem(item)       // item omitted = within the current Show (ACT-022)
  ToggleScreen(Black|Clear|Logo)               // operator controls: toggles, mutually exclusive (LIVE-013)
  SetScreen(Normal|Black|Clear|Logo)           // Action commands: set, not toggle (ACT-026..028, ACT-033)
  StartAudio() StopAudio() RestartAudio()
  StartTimer(name) StopTimer(name)
  Select(item, slide)                          // not presenting: selection only (DEC-021)
  StartPresenting(service, mode) StopPresenting()   // Go Live / Rehearse (MODE-005)
```

Operator buttons, hotkeys, the phone remote, and the Action processor all call this one interface. There is one implementation (`PresentationSession`) and one place for logic like "navigating while Black is active keeps the screen state" (LIVE-014). Going to the slide that is already current changes nothing and fires no events (ACT-013).

### 6.2 `PresentationSession` state

An immutable **`PresentationState`** snapshot (current item *and* slide, with the item's position in the Service, e.g. "item 4 of 9", and slide position within it, e.g. "slide 2 of 6"; next slide and next item; `ScreenState` of Normal/Black/Clear/Logo, mode, audio status, pending-action info). Every change produces a new snapshot and raises `StateChanged`. The Output, Preview, operator list highlight, and remote all render from snapshots. Because they are immutable, handing them to the Kestrel threads is safe.

### 6.3 Action processor (ACT)

- Subscribes to Engine events: `ServiceStarted`, `ShowStarted/Ended`, `SlideDisplayed/Ended`, `TimerExpired`, `AudioEnded`.
- **Event order is fixed and lives in `PresentationSession`** (ACT-005). A move runs: operator audio stop (DEC-020) → `SlideEnded` → `ShowEnded` → display the new slide → `ShowStarted` → `SlideDisplayed`. Each step runs its zero-delay Actions to completion before the next. This order is what makes song A → song B work: A's audio stops before B's `StartAudio`, and an author's `ShowEnded → StopAudio` on A can't stop B. Commands issued by Actions during a step are queued and run after the current move completes, so a move never starts inside another.
- Finds Actions attached to the Service, Service Item, Show, and Slide, and runs them in that order (ACT-006). Delayed ones are scheduled with a `CancellationTokenSource` per pending Action and cancelled on slide or item change (ACT-004). `StopPresenting` cancels all of them.
- Executes Commands by calling `ICommandApi` with `Origin = Automation`, so Actions and operator input follow identical rules (ACT-020..033).
- **Loop guard** (ACT-040): a sliding window counts Action-originated slide changes. More than 20 in 1 s with no user input pauses Actions **for the current item** and raises a warning. The pause ends when the current item changes to a different one.
- Exposes "pending delayed action" info for the operator indicator (ACT-043).
- Actions pointing at missing targets are no-ops (ACT-041).

### 6.4 Timers (TMR)

- `TimerService` owns one `TimerRuntime` per Timer in the current Service.
- **Time source:** a single tick loop aligned to wall-clock second boundaries (compute the delay to the next whole second, then re-align each tick), not an accumulating `DispatcherTimer` interval (TMR-011). Countdown-to-clock timers compute `target − IClock.Now` (local time). Duration timers store `endsAtUtc` at start, so they never drift. Stopped timers display the full duration or live remaining time (TMR-014).
- **Expiry:** state machine Stopped → Running → Expired, with `TimerExpired` fired exactly once per start. Late-start for clock timers goes straight to Expired with no trigger (TMR-007/008). Starting an Expired Timer restarts it (TMR-012).
- **Not saved** (TMR-017): runtimes live only in the presentation session. `StopPresenting` resets all to Stopped.
- **Ticks produce an event of `(timerName, displayText, isWarning)`.** They don't produce a new `PresentationState` and they don't re-render slides (TMR-010).
- All time access goes through `IClock`, so tests can run a whole Service morning in milliseconds.

### 6.5 Audio and cues (AUD)

- `IAudioPlayer` (play from trim start, stop, position, ended event) and `IPlaybackClock` (position as `TimeSpan`) are Engine-owned interfaces. NAudio implements them in `Audio`. A future video player implements the same clock (FUT-002, AUD-020).
- `CueController` listens to the clock at ~10 ms. When position crosses the next Cue, it calls `GoToSlide` for the cue's specific slide (self-correcting, AUD-006). Cues are stored as absolute file time (AUD-005). Cues only drive slides while the audio's Show is the current item (AUD-017).
- Starting audio stops any other playing audio, and a start while the same Show's track is already playing is a no-op (AUD-015/016). `StartAudio` does nothing unless the current slide is the first displayable slide of the owning Show (AUD-021). Because ACT-005 displays the new slide before `ShowStarted` runs, a song entered at its first slide starts, and one entered at its last slide (Previous) or a middle slide (click) doesn't. `RestartAudio` from another item first goes back to the first slide of the song's item, then plays (AUD-013).
- **Default device changes.** Plugging in the projector over HDMI often makes it the Windows default audio device. The player must survive a device change mid-song (NAudio raises a playback-stopped error), log it, and show "Audio stopped: output device changed". Whether to resume automatically on the new default device is measured in spike S4 and decided at G4.
- **Leaving an item with audio playing** is the case where the item level matters. Slide navigation inside the song never touches audio (DEC-013). Moving to a different *item* is a distinct gesture, and the engine emits an `ItemChanged(from, to, initiatedBy)` event so the policy lives in one place. The policy is decided (Q-A7, option 3): **an operator-initiated change to a different item stops the audio** (hard stop, no fade, per AUD-002), and the UI shows a brief "Audio stopped" notice. "Operator-initiated" means anything entering via the command API from the operator UI, hotkeys, clicker, or phone remote: Next/Previous crossing an item boundary, Next/Previous item, clicking another item or a slide in another item. Changes caused by **Actions or Cues** (including `GoToItem` and timer-expiry jumps) do **not** stop audio, so authors keep full control (`ShowEnded → StopAudio` remains available as an explicit Action). Slide moves within the same item never touch audio (DEC-013). Each command carries an `Origin` (Operator or Automation) for this purpose. The FRD records this as DEC-020. AUD-017 still applies when automation moves away from a playing song: audio continues, cues stop driving slides. The state also exposes "audio playing from item X" so the operator UI can show it on the originating item's header even after the operator has moved on.
- **Accuracy note:** WaveOut and WASAPI buffers add latency, and `GetPosition` reports what's been written, not what's audible. For 50 ms accuracy (AUD-018) we use a modest buffer (about 50-100 ms), read the position from the output device, and tune a latency offset in the spike (§12).
- **MP3 caveat:** `Mp3FileReader` seeks approximately on VBR files. For accurate trim and seek we expect to use `MediaFoundationReader` (Windows' own decoder, no extra dependency). The spike validates this. Audio is streamed, never decoded wholesale into RAM.

### 6.6 CCLI and mode

`ServiceMode` has no effect except on close (MODE-003). It lives in the **Service state file** `service-state/{id}.json` (LIB-013, D-10; its own folder, because the Service repository loads every `.json` file in `services/`), together with check dismissals and the CCLI-016 answer. The presentation session and the close flow write that file directly, so they never collide with an open Service draft or trigger the LIB-006 overwrite prompt. The last write wins. `StartPresenting(service, mode)` writes the Mode (MODE-005).

`CcliService.OnServiceClosing` implements CCLI-010..016: build the reportable list, show the summary through an `IUserPrompt` interface, and write `logs/ccli/{serviceId}.json` atomically, replacing any prior file for that Service (CCLI-013). Cancel skips logging but never blocks the close or exit. On startup, `last-session.json` (§9.1) tells whether a Live Service was left open by a crash. If so, and it has no log file, the close flow runs then (CCLI-018). The CSV export reads all log files in a date range and groups by CCLI number, counting distinct Services (CCLI-020).

### 6.7 Pre-service check (CHK)

`CheckService` runs a list of `ICheckRule`s (one per CHK-001 bullet) over the Service plus resolved models and returns `Warning(severity, message, itemId?, slideId?)`. Rules are independent, so adding one is trivial. Dismissals are stored in the Service state file (LIB-013), keyed by rule and target. It never blocks (DEC-012). The zero-delay loop rule builds a small graph of "slide X, when displayed with no delay, goes to slide Y" and reports any cycle.

---

## 7. Rendering

### 7.1 Layered renderer (FUT-003)

```
OutputWindow
 └─ CanvasHost (letterboxed to canvas aspect, SLD-002)
     ├─ Layer 0: Background    (image or solid color, + future: video)
     ├─ Layer 1: Text          (static text, pre-rendered)
     ├─ Layer 2: Live text     (timer digits; small, updated per tick)
     └─ Layer 3: Screen state  (Black / Logo overlay)
```

Clear (LIVE-011) hides layers 1 and 2 and keeps layer 0, so the background must be available on its own (see §7.2). Logo with no logo set is the Black overlay (LIVE-012). When not presenting, the Output shows the Black overlay (LIVE-018).

The same `SlideRenderer` builds this visual for the Output, the editor canvas, the operator Preview, and thumbnails, each at its own scale. This is the "one renderer" rule (SLD-026).

### 7.2 Why it's fast on weak hardware

- **Pre-render, don't lay out on the hot path.** For the current, next, and previous slides, the renderer produces frozen bitmaps at output resolution: layer 0 (background) and layer 1 (text) as **two separate bitmaps**, so Clear can show the background alone. Backgrounds are shared by many slides and de-duplicate in the image cache. Showing a slide is then a bitmap swap, which is the <100 ms path (PLT-006). Memory: 1440×900×4 B ≈ 5 MB per bitmap. The window is 3 slides × 2 layers, plus the outgoing frames during a crossfade, so about 40 MB worst case, less when backgrounds are shared. Targets of Action jumps (`GoToItem`) aren't predicted (LIVE-008) and render on demand.
- **Crossfade = opacity animation between two slide groups.** Each group holds that slide's background, text, and live text (layer 2), so a Timer fades with its slide. This is composited by WPF's render thread and is cheap on integrated graphics. No pixel-shader effects. If it isn't smooth in the spike, the setting is hidden and Cut is the only option (LIVE-031).
- **Thumbnails** for the operator list (LIVE-004) are small (about 160×100, 64 KB each) and kept in a byte-capped cache. A 200-slide Service is about 13 MB.
- **No `DropShadowEffect` or `BlurEffect`.** They can fall back to software. Text outline and shadow (SLD-025, COULD) are drawn as geometry, and only if the spike shows they're affordable.
- **Text via `FormattedText` or `GlyphRun`** into a `DrawingVisual`, not via a `FlowDocument` tree. Lighter, and deterministic.
- Set `RenderOptions.BitmapScalingMode` and `TextOptions` appropriately for projector output (grayscale antialiasing reads better on projectors than ClearType).

### 7.3 Image pipeline (SLD-007)

- On import, the original is **copied into the Library** (LIB-003).
- A background step produces a **downscaled copy at output resolution** cached in `%LOCALAPPDATA%\ChurchSlides\cache\images\`, keyed by `(content hash, width, height)`. It is regenerated if the output resolution changes.
- At present time, only cached downscaled images are loaded (`BitmapCacheOption.OnLoad`, `DecodePixelHeight`, frozen). A small LRU, capped by bytes, keeps recently used backgrounds. Many slides share one Template background, so the cache de-duplicates naturally.
- Full-resolution originals are never held in memory during presentation.
- Missing files resolve to a solid dark fallback and a flag for the check (LIB-011).

### 7.4 Timer smoothness (TMR-010, PLT-007)

Timer tokens are laid out as **live text slots** in Layer 2:

- Layout reserves the width of the widest value (for example `0:00:00` or `-0:00:00`) and draws each character centered in a **fixed-width cell**, so the digits can't shift neighbouring text, whatever the font. (If the Template font has tabular figures this is a belt-and-braces measure.)
- On each tick only the affected live slot's `DrawingVisual` is redrawn (a few glyphs). Layers 0 and 1 are untouched, and transitions ignore ticks (LIVE-032).
- Warning color applies to the slot when remaining ≤ threshold (TMR-006).
- Limitation to confirm in the spike: a timer token in the middle of a sentence with mixed formatting means the static text layer needs a hole for it. The fixed-width approach makes this predictable.

### 7.5 Output display handling

- Enumerate monitors via `EnumDisplayMonitors` (P/Invoke), identify the chosen one (stable device name), and position a borderless, topmost window to the monitor's exact **pixel** bounds. Per-monitor DPI v2 avoids scaling surprises.
- If it's unavailable: resizable window on the primary display plus a notice (LIVE-002). Display hot-plug (`SystemEvents.DisplaySettingsChanged`) re-evaluates.
- `SetThreadExecutionState` prevents sleep and screen blank while presenting.

---

## 8. Editing

### 8.1 Draft-and-commit (SAFE-001/002)

- Opening an object for editing clones it into a **draft**. Edits only touch the draft. **Save** writes it to the Library through the concurrency check. Closing with changes prompts Save / Discard / Cancel.
- Drafts autosave to `%LOCALAPPDATA%\ChurchSlides\drafts\` every 30 s and on focus loss. This never writes into the Library, so Drive never sees half-edited files.
- After a crash, startup offers to restore any drafts (SAFE-004).

### 8.2 Undo/redo (SAFE-003)

- Structural edits (slide add/move/delete, box move, metadata) use a **snapshot (memento) stack**. A Show serialises to a few KB, so 50+ snapshots are trivial, and it's far simpler than per-operation inverse commands.
- Typing is coalesced into one undo step per pause.
- **Decision D-2:** WPF `RichTextBox` has its own undo stack, which would conflict with the app-level stack. Recommendation: disable the control's undo, intercept Ctrl+Z / Ctrl+Y, and use the app stack with snapshots taken on text-change pauses.

### 8.3 Conflict protection (LIB-005..007, DEC-014)

- **Atomic save:** write `{id}.json.saving` then replace via `File.Replace`/move, retrying on `IOException` (Drive may briefly hold a file open). `.saving` files are ignored when loading.
- **Overwrite check:** each loaded object remembers `(lastWriteTime, SHA-256)`. On save, re-check the disk: only a different hash counts as a change (a sync tool re-stamping the file does not), identical content is not rewritten, and a file from a newer schema is never touched. If changed, offer **Reload** or **Save as copy** (LIB-006, AS-08).
- **Watcher:** `FileSystemWatcher` events are debounced and coalesced per path, since Drive produces bursts and temp files. A file is reloaded only if it's not being edited, our own saves are ignored by hash, and the Catalog and search index are updated incrementally (LIB-007, CAT-005).
- **Conflict copies:** files named like sync conflicts or with duplicate `Id`s are listed in a "Library problems" panel, not loaded (LIB-009).
- **Schema:** each file has `SchemaVersion`. Older files migrate in memory via a chain of small migrators over `JsonNode`. Newer files open read-only with a warning (LIB-004).

### 8.4 Rich-text editing (SLD-021/022, TPL-024)

- The slide editor shows a WPF `RichTextBox` per Text Box (inside the same WYSIWYG canvas the renderer draws). When a box is selected for typing, the control is overlaid on the box. Otherwise the renderer's output is shown, so what you see always matches the Output.
- Converters `RichText ⇄ FlowDocument` are written once and unit-tested round-trip. Tokens appear as non-editable "chips" (inline UI containers or specially formatted runs).
- Range formatting is stored as run properties. Template defaults apply only where a run leaves a property unset (TPL-024). "Clear formatting" removes run properties (TPL-025).
- Overflow detection uses `FormattedText` measurement against the box geometry (SLD-028).

**This is the highest-risk UI area** (WPF `RichTextBox` has quirks around fonts, paste, and caret behavior). It's the first thing the spike validates (§12).

---

## 9. Library, search, and local data

### 9.1 Storage layout

As in the FRD §5. One additional rule: IDs are in the file contents and the filename (`{id}.json`), and the contents win if they disagree (a copied file then reads as a duplicate-ID conflict, LIB-009).

Machine-local, never synced (LIB-008), under `%LOCALAPPDATA%\ChurchSlides\`:

```
settings.json        library path, output display, remote on/off, port, token
cache\images\        downscaled backgrounds and thumbnails
cache\bibles\        parsed Bible indexes
cache\search.idx     search index snapshot
drafts\              autosaved drafts
logs\app.log         diagnostics (SAFE-006)
last-session.json    last service + slide, for crash recovery (SAFE-005)
```

### 9.2 Search (CAT)

- In-memory index built from all Shows at startup on a background thread, then incrementally corrected by the watcher. No snapshot file until startup is measured to be slow (C1.4 decision 10). Each entry holds a normalised (lowercase, accent-stripped) title, optional CCLI number, and slide text.
- Search-as-you-type is a linear scan over a few thousand short strings. That runs in a millisecond or two, with no need for an inverted index. Title hits rank above lyric hits (CAT-004).
- One `ICatalogSearch` is used by the Catalog, "add to Service", and "fill Placeholder" (CAT-006), the last with Category/Kind filters pre-applied.
- Memory: lyrics for 2,000 Songs is only a few MB.

### 9.3 Scripture (SCR)

- `System.Xml.Linq` parse on import, then a compact per-Bible index (book → chapter → verse offsets) cached locally. Bible text is loaded on demand when a passage is generated, not held resident.
- Our own book alias table feeds the reference parser (`Jn`, `1 Cor`, `Ps 23`, `Rom 8:28,31-39`).
- A Scripture Item generates ordinary Service-local slides (SCR-008) through the normal Template pipeline, with `reference` as a named Template Box. "Regenerate" rebuilds them after a warning.
- The generated Show is in the built-in **Scripture Category** (CAT-010, D-11), whose ID is a fixed constant in `Domain` so both machines agree. Its default Template is the default Scripture Template, so Template resolution has no special case. `categories.json` gains the Scripture Category on first load if it's missing. Deleting it is refused.

---

## 10. Phone remote (REM)

- `Remote` assembly, started only when enabled in local settings. Kestrel on a configurable port serves one embedded static page (plain HTML/CSS/JS, no framework) and a few endpoints.
- **Transport:** `POST /api/cmd/{next|prev|black|logo|clear}` map to `ICommandApi`. `GET /api/events` is a **Server-Sent Events** stream pushing `PresentationState` summaries (item title, slide label and text, next label and text, mode, screen state) within the 300 ms goal (REM-007). SSE is simpler than WebSockets for one-way push and reconnects automatically.
- **Security:** random token in the URL, exchanged for a cookie. Middleware rejects non-private source IPs (REM-004). There are no endpoints for content, mode, or Services (REM-009, DEC-019).
- Commands are posted to the UI thread via the dispatcher, and state is read from the immutable snapshot. Kestrel never touches editable objects.
- The operator UI shows the URL and a QR code. A first-run note covers the Windows firewall prompt (REM-010).
- Cost note: loading the ASP.NET Core shared framework adds tens of MB once started. Since REM is off by default and SHOULD priority, that cost is only paid when used, and PLT-005 is measured both with and without the remote on.

---

## 11. Cross-cutting concerns

- **Mode and Rehearse/Live** affect only CCLI close logic (DEC-010). There are no mode checks anywhere else in the code, which makes this easy to enforce by review.
- **Hotkeys** (LIVE-020..022) are handled with a `PreviewKeyDown` class handler on both the operator window and the Output window, suppressed when a text input has focus. Both route to `ICommandApi`. A clicker sending PageUp/PageDown/`.`/`B` needs no setup. (Clickers only work while one of our windows has focus. See Q-A5.)
- **Logging:** a small rolling file logger in `%LOCALAPPDATA%`. No telemetry, since the app works fully offline (PLT-009).
- **Configuration:** three tiers. *Library settings* (synced: canvas aspect, CCLI license, logo, transition, copyright format, default Templates), *machine settings* (local), *per-Service state* (in the Service file).
- **Accessibility/simplicity:** few dialogs, no "modes" beyond the FRD's, and sensible defaults for every setting (FRD §0.9).

---

## 12. Spike plan (PLT-008)

The spike is a throwaway WPF app, run on the **actual church machine** *before* feature work. Each item has a pass/fail criterion and we record working set and CPU.

| # | Question | Pass criterion |
|---|---|---|
| S1 | Borderless full-screen Output on the second display at 1440×900, correct pixel size under DPI scaling | Exact bounds, no taskbar, no flicker on show |
| S2 | Crossfade between two image-backed slides rendered to bitmaps | Smooth at 300 ms, CPU stays low, no dropped frames visible. Else Cut-only |
| S3 | Rich-text editing: mixed fonts, sizes, colors, bold/italic, paste, and `RichText ⇄ FlowDocument` round trip. Draw the same content in the `FormattedText` renderer | Editor and renderer agree visually. No corruption on round trip |
| S4 | MP3 trim and seek accuracy, position readout, cue accuracy: compare `Mp3FileReader` vs `MediaFoundationReader`, WaveOut vs WASAPI. Also: Cue latency while the UI thread pre-renders slides, and what happens when the default audio device changes mid-song (plug the projector's HDMI in and out) | Cue fires within 50 ms of audible position, with pre-rendering running. Seek to trim start is accurate on a VBR file. Device change doesn't crash |
| S5 | One-second timer with fixed-cell digits over a background, running for 10+ minutes | No flicker, no neighbour shift, no drift vs wall clock |
| S6 | Memory: 3 Songs, 5 Presentations, backgrounds, the §7.2 prerender window. Time to pre-render one slide on the UI thread | Working set ≤ 300 MB (PLT-005). Record idle and presenting. Record pre-render time (decides the render thread, §4.1) |
| S7 | Startup time and self-contained ReadyToRun size, with and without Kestrel loaded | Acceptable startup. Record numbers |
| S8 | .NET 10 on the church's Windows 10 build. Record the exact Windows version and whether it still gets updates | Runs, and WPF uses sane rendering tier. The build is on Microsoft's .NET 10 supported-OS list |

The spikes are built as **one program** with a tab per area and an on-screen checklist that records the figures to a file, so G0 needs one visit to the church. Results go into `docs/SPIKE-RESULTS.md`, and the architecture is revised where reality disagrees (especially S2, S3, S4).

---

## 13. Build order and traceability

Follows FRD §24, with the Engine written test-first in each phase because it has no UI dependency.

| Phase | Deliverable | Main modules | FRD |
|---|---|---|---|
| 0 | Spike (§12) | throwaway | PLT-008 |
| 1 | Library I/O, Shows/Slides with rich text, Output, continuous navigation, hotkeys | Domain, Storage, Rendering, Engine, App | LIB, SHOW, SLD, LIVE |
| 2 | Catalog, search, Services, references, skips, fork | Storage, Engine, App | CAT, SVC-001..026 |
| 3 | Templates, binding, Variables | Domain, Rendering | TPL, VAR |
| 4 | Service Templates, Placeholders | Engine, App | SVC-030..035 |
| 5 | Audio and cue editor | Audio, Engine, App | AUD |
| 6 | Timers and Actions | Engine, Rendering | TMR, ACT |
| 7 | Mode, CCLI, pre-service check | Engine | MODE, CCLI, CHK |
| 8 | Scripture | Scripture | SCR |
| 9 | Phone remote | Remote | REM |
| 10 | Safety polish, transition tuning, optional imports | all | SAFE, IMP |

Acceptance scenarios AS-01..AS-09 become automated integration tests wherever the Engine can run headless (AS-02, 03, 04, 06, 07 are largely Engine-only with a fake clock and fake audio). AS-01, 05, 08, 09 need the real file system or hardware.

---

## 14. Decisions (confirmed 2026-11-23)

All recommendations in the table below were accepted.

| ID | Decision | Recommendation | Alternative |
|---|---|---|---|
| D-1 | Output on the main UI thread | Yes. Add a global exception handler and fault guards. | Separate UI thread or process for Output (more isolation, more complexity) |
| D-2 | Undo strategy | App-level snapshot stack, disable `RichTextBox` undo | Per-operation inverse commands (more code) |
| D-3 | Store rich text as our own JSON model, not XAML | Yes | Store `FlowDocument` XAML (less code, locks files to WPF, bigger files) |
| D-4 | Text rendering | `FormattedText` into `DrawingVisual`, with `FlowDocument` for the editor only | `FlowDocument` for Output too (simpler, heavier, less predictable) |
| D-5 | Search | Plain in-memory scan | Embedded search engine (overkill at this size) |
| D-6 | Packaging | Self-contained x64 + ReadyToRun | Framework-dependent (small download, needs runtime installed) |
| D-7 | Kestrel for the remote | Yes, lazy-loaded | `HttpListener` (lighter, but URL-ACL and firewall issues without admin rights) |
| D-8 | `ServiceItem` as a sealed class hierarchy with a JSON discriminator | Yes | Single record with nullable fields (as in the FRD sketch) |
| D-9 | .NET 10 LTS rather than 8 | Yes (see §2) | .NET 8 (end of support Nov 2026) |
| D-10 | Mode, check dismissals, and the CCLI-016 answer in a separate Service state file (LIB-013) | Yes | Keep them in the Service file (conflicts with open drafts and with the other machine's edits) |
| D-11 | Built-in Scripture Category with a fixed ID replaces the "default Scripture Template" Library setting (CAT-010) | Yes | Scripture Shows without a Category, plus a special case in Template resolution |
| D-12 | Fixed event order on slide and item changes (ACT-005/006), owned by `PresentationSession` | Yes | Unordered events (audio and Action ordering bugs) |
| D-13 | Operator screen controls toggle, Action screen commands set, plus `Normal` (ACT-026..028, ACT-033) | Yes | Actions toggle like the operator controls |

---

## 15. Gaps and questions found in the FRD

**Resolutions (2026-11-23).** The questions are kept below for the record.

| ID | Resolution |
|---|---|
| Q-A1 | Actions may be attached to **Service Items** (owned by the Service / Service Template), and `GoToSlide` also supports a "within this Show" target. Catalog Shows stay valid in any Service. Needs an FRD amendment to ACT-002. |
| Q-A2 | Fork preserves Slide IDs. Duplicate and Service Template instantiation mint new IDs and remap references via `IdRemapper`. |
| Q-A3 | A timer token is a **standalone** element in its Text Box (not mid-sentence). Simplest and flicker-proof. |
| Q-A4 | No fit estimation in Scripture generation. Rely on the pre-service check (CHK-001). |
| Q-A5 | No global keyboard hook. Clickers work while one of our windows has focus. |
| Q-A6 | FRD §0 point 6 should say Section 23. Cosmetic. |
| Q-A7 | **Option 3.** Operator-initiated change to a different item stops audio. Actions and Cues don't. See §6.5. A persistent audio indicator with a Stop button is always visible in the operator UI. |

**Q-A1: Where do loop and jump Actions live? (ACT-002, ACT-022/023, AS-04).**
`GoToItem(itemId)` and `GoToSlide(itemId, slideId)` name a **Service Item ID**, which only exists inside one Service. But AS-04's loop (announcement slides auto-advance, then `GoToItem` back to the first announcement) puts Actions on Slides of a catalog Show. A catalog Show can't know a Service Item's ID. Options:
1. **Item-level Actions.** Allow Actions on a Service Item (a ServiceItem-scoped overlay), so the Service or Service Template owns the "loop back" Action.
2. **Relative targets.** Add `GoToSlide` within the current Show, and `GoToItem` by *relative* or *named* target (for example "first slide of this item", "item labelled Welcome").
3. **Show Copy only.** Loop Actions are only valid on Service-local copies.

I recommend (1) plus the simple "within this Show" form from (2). Actions on catalog Shows then stay valid in any Service. Service Template instantiation also needs the `IdRemapper` for Item IDs (§5).

**Q-A7: Leaving a song with audio playing (refines FRD Q-003, DEC-013).**
DEC-013 says manual navigation never stops audio, which is clearly right for slide moves within the song. When the operator jumps to a *different item* (Next item, clicking another item, an Action's `GoToItem`), options are:
1. Audio keeps playing, cues stop driving slides (FRD default, AUD-017). The operator stops it manually.
2. Item jumps stop audio, slide moves don't.
3. Item jumps stop audio only when initiated by the operator, not by Actions or Cues.
4. Ask the operator (a prompt mid-service is probably bad).

I lean towards (2) or (3) with the stop being explicit in the UI ("Audio stopped"), but this is your call. Related: should the operator UI keep a persistent audio indicator showing which song is playing plus Stop, since the current item may no longer be that song?

**Q-A2: Cross-show Action references on copy.** If the Q-A1 answer is (2) or (3), forking and duplicating must remap slide and item IDs. I've assumed preserving Slide IDs on fork and minting plus remapping on duplicate (§5).

**Q-A3: Timer tokens inside running text (TMR-009/010).** Confirm that a timer token is a **standalone** element in a Text Box, or that mid-sentence use with a fixed-width digit cell is acceptable. The first is simpler and bulletproof.

**Q-A4: Text overflow and Scripture.** With no auto-fit in release 1 (SLD-029), do we want Scripture generation to *estimate* fit and warn, or just rely on the check (CHK-001)?

**Q-A5: Clickers when another application has focus.** LIVE-021 covers our own windows. Do you want an optional global keyboard hook so clickers work while the app isn't focused? It's a small addition, but with some security-software and reliability tradeoffs, so I've left it out.

**Q-A6: FRD housekeeping.** §0 point 6 says open questions are in "Section 18", but they are in Section 23. Not blocking.

Still open in the FRD (v0.3): Q-005 (CCLI spreadsheet, needed by C6.3), Q-011 (remote slide text, C8.1) and Q-012 (canvas aspect change, settings chunk). Q-001, Q-002, Q-006 and Q-007 run on their defaults. Q-003, Q-004, Q-008, Q-009 and Q-010 are resolved.

---

## 16. Top risks

| Risk | Impact | Mitigation |
|---|---|---|
| WPF `RichTextBox` quirks, or editor and renderer visual mismatch | Editing feels wrong, WYSIWYG breaks (SLD-026) | Spike S3 first. Keep our own text model so the control is replaceable. |
| Crossfade stutters on integrated graphics | LIVE-030 unmet | Bitmap-opacity design. Fall back to Cut-only, which the FRD already allows. |
| Cue accuracy (WaveOut latency, VBR MP3 seeking) | AS-03 fails | Spike S4. Use `MediaFoundationReader`, tune the latency offset. |
| Memory over 300 MB | PLT-005 fails | Bounded bitmap and image caches, on-disk downscaled images, measure in S6 and AS-09. |
| Drive sync bursts, partial files, or locks | Spurious reloads, failed saves | Debounced watcher, hash comparison, retrying atomic writes, ignore `.saving`. |
| Slide pre-rendering stalls the UI thread | Late Cues (AUD-018), sluggish hotkeys | Measure in S4/S6. Dedicated STA render thread if needed (§4.1). |
| Default audio device switches to the projector's HDMI | Song stops or plays through the projector | Handle device-change errors (§6.5), test in S4 and at G4. |
| Church PC runs Windows 10, which no longer gets security updates, and .NET 10 may not support its build | App won't run, or an unpatched PC opens a LAN port for the remote | S8 checks the build against the .NET 10 supported-OS list. Raise a Windows 11 upgrade with the owner if it fails. |
| Golden-image render tests differ across machines | Flaky tests | Render with software rendering and a fixed font set, compare with a small tolerance. |
| Scope creep from comparable apps | Complexity | FRD §2 and §22 are treated as fences. |

---

## 17. Suggested next steps

1. ~~You review this document and answer §14 and §15.~~ Done.
2. ~~I create the solution skeleton.~~ Done (C0.1). Headless Phase 1 is under way (C1.1 to C1.3 done).
3. Build the combined spike program (C0.2 to C0.4). You run it on the church machine and send me the numbers (G0).
4. We revise this document with the results, then start Phase 2.
