# Development Plan: Church Presentation App

Version: 0.1 (draft for review)
Date: 2026-11-23
Inputs: `Functional Requirements  Church Presentation App.md` (FRD), `Architecture.md` (ARCH)
Audience: the product owner (review and gates) and Claude Code (execution)

This plan breaks the build into **work chunks** sized to fit one focused Claude Code session, orders them so risk is retired early and every milestone is usable, and defines the **plan → execute → verify** routine each chunk follows. Chunk IDs (`C3.2`) are stable. Reference them in commits and in `docs/STATUS.md`.

---

## 1. How we work

### 1.1 Principles

1. **Contracts first, then implementations.** The interfaces in ARCH §3 (`ILibrary`, `IAudioPlayer`, `IClock`, `IOutputSurface`, `ICommandApi`, …) are written in the first chunks and treated as stable. Later chunks code against them, so a session never needs to understand the whole system.
2. **Headless before visual.** Domain, Storage, and Engine have no WPF dependency, so they are built test-first and verified entirely by `dotnet test`. WPF work comes after the logic it displays is proven.
3. **Retire risk early.** The spike (rich text, crossfade, audio accuracy, memory) runs before features, and rich-text editing and rendering are built before things that depend on them.
4. **Every milestone is usable on the church machine.** No long stretches of unrunnable code.
5. **The tests are the memory.** Between sessions, Claude has no recollection. What persists is code, tests, `CLAUDE.md`, `docs/STATUS.md`, and per-chunk plan files. Anything a future session must know goes into one of those.
6. **One chunk, one commit series, one clean state.** A chunk ends with a green build, passing tests, updated docs, and a commit. Never leave a chunk half-integrated.

### 1.2 Repository layout (created in C0.1)

```
/                       solution root
  CLAUDE.md             conventions, build/test commands, "read these first" (kept short)
  src/
    ChurchSlides.Domain/ .Storage/ .Scripture/ .Engine/ .Audio/ .Rendering/ .Remote/ .App/
  tests/
    ChurchSlides.Domain.Tests/ ... (one per assembly), ChurchSlides.Acceptance.Tests/
  spike/                throwaway spike app (deleted after M0)
  docs/
    FRD.md              the requirements (v0.2 after C0.1)
    ARCHITECTURE.md
    DEVELOPMENT-PLAN.md this file
    STATUS.md           chunk checklist, current milestone, known issues (updated every chunk)
    TRACEABILITY.md     FRD ID → code/tests, updated every chunk
    plans/Cx.y.md       one plan file per chunk (written in the planning step)
    decisions/          short ADRs when we deviate from ARCH
    frd-slices/         optional: FRD split by area so a session loads only what it needs
    checklists/         manual test checklists per chunk
```

### 1.3 `CLAUDE.md` (kept under ~100 lines)

Contains: what the app is in three sentences, the dependency rule between assemblies, build/test/run commands, naming rules (glossary names only, FRD §0.7), "never reverse FRD §2 decisions", the chunk routine (§1.4), and pointers to the docs above. It does **not** duplicate the FRD or ARCH.

### 1.4 The routine for every chunk

Each chunk is two or three short sessions rather than one long one. Starting each phase in a fresh context keeps quality high.

**Step A: Plan (fresh session, plan mode).**
- Read `CLAUDE.md`, `docs/STATUS.md`, the chunk's entry in this plan, the listed FRD slices, and the ARCH sections it names.
- Inspect the existing code it will touch.
- Write `docs/plans/Cx.y.md`: files to create or change, public interfaces and signatures, the test list (named cases, mapped to FRD IDs), edge cases, and what is explicitly **out** of this chunk.
- Product owner reviews the plan (a quick skim for most chunks, a real review for the ones marked **Review gate**). Questions go back to the owner, not guessed (FRD §0.6).

**Step B: Execute (fresh session).**
- Read `CLAUDE.md` and the plan file only, plus the code the plan names.
- Headless chunks: test-first. Write failing tests from the plan's list, then implement.
- WPF chunks: build the view-models headless-testable first, then the views.
- Stay inside the plan. Anything discovered but out of scope goes in `docs/STATUS.md` under "Follow-ups", not into the diff.

**Step C: Verify and close (same session, or a short fresh one).**
- `dotnet build -warnaserror` and `dotnet test` green.
- Run `/verify` and `/simplify` before committing, and `/review` on the larger chunks.
- Update `TRACEABILITY.md` and `STATUS.md`.
- Commit with the chunk ID and FRD IDs in the message.
- For UI chunks, produce a manual checklist in `docs/checklists/` and note which items the owner should try.

### 1.5 Sizing rules (context-window friendly)

- A chunk touches **at most about 10-15 files and about 1,500 lines** of new code including tests. If the plan step says it's bigger, split it.
- A chunk depends on **at most one or two** other areas of the codebase. If it needs more, introduce a narrower interface.
- A chunk's plan fits in **under 2 pages**.
- Large source files (the FRD is about 770 lines) are not re-read every time. `frd-slices/` or the traceability table points to just the relevant requirement IDs.

### 1.6 How Claude verifies UI work

Claude can build and run tests but can't casually "look at" a WPF window. Three tools bridge this:

1. **Headless render harness (built in C2.1).** A test helper renders a `RenderModel` to a PNG through the real renderer in an STA test, so Claude can open the PNG and check layout, fonts, and colors. Selected renders are kept as golden images.
2. **View-model tests.** All behavior (commands, enable/disable rules, state transitions) lives in view-models and is unit tested.
3. **Owner checklists.** Per-chunk checklists for the things only a person can judge (feel, readability, workflow). These are the human gates below.

---

## 2. Milestones and human gates

| Milestone | Contents | Usable result | Owner gate |
|---|---|---|---|
| **M0 Foundations** | Repo, spike, FRD v0.2 | Spike numbers from the church machine | **G0:** run the spike, send figures. Go/no-go on crossfade, text approach, audio approach |
| **M1 Present** | Domain, Storage, Engine nav, Output, operator UI | Open a hand-built Service and present it with keyboard control | **G1:** UX review of the Service view (items vs slides) and Output on the church projector |
| **M2 Author** | Rich text editor, Shows, Catalog, search, Service editor | Build and run a real Sunday from scratch | **G2:** build a real Service at home, present at church via Drive |
| **M3 Reuse** | Templates, Variables, Service Templates, Placeholders | Weekly service from a template | **G3:** template workflow review |
| **M4 Audio** | Audio, cues, cue editor | Songs with audio-synced slides | **G4:** cue accuracy at church |
| **M5 Automation** | Timers, Actions | Pre-service countdown loop (AS-04) | **G5:** run AS-04 on the projector |
| **M6 Compliance** | Mode, CCLI, pre-service check | Logs and CSV export | **G6:** CSV format check against CCLI's template (Q-005) |
| **M7 Scripture** | Zefania import, generation | Scripture items | |
| **M8 Remote** | Phone remote | Control from a phone | **G8:** try it on church Wi-Fi |
| **M9 Release** | Hardening, recovery, performance, packaging | Release candidate | **G9:** acceptance scenarios AS-01..AS-09 |

Gates are the only points where progress waits for the owner. Between gates Claude can work through the chunks in order.

---

## 3. Work chunks

Size: **S** = small (under about 600 lines), **M** = medium, **L** = large (consider splitting at plan time).
**Review gate** marks chunks where the owner should read the plan before execution, because the choice is hard to undo.

### Phase 0: Foundations (M0)

#### C0.1: Repository and documentation setup. Size S
- **Goal:** a buildable, empty solution with conventions in place; FRD updated to v0.2 with the decisions since v0.1.
- **Do:** `git init`; solution and eight `src` projects plus test projects with the dependency rule enforced (an architecture test that fails if, for example, `Engine` references WPF); `Directory.Build.props` (nullable, warnings as errors, analyzers); `global.json`; `CLAUDE.md`; move docs into `docs/`; create `STATUS.md` and `TRACEABILITY.md`; add a build/test script.
- **FRD v0.2 amendments:** Actions on Service Items and "within this Show" targets (Q-A1, ACT-002/022/023); `NextItem`/`PreviousItem` commands; audio stops on operator-initiated item change (Q-A7, replaces Q-003); timer tokens standalone (Q-A3); §0 point 6 reference fix.
- **Done when:** `dotnet build` and `dotnet test` pass on an empty solution; dependency-rule test passes.

#### C0.2: Spike A, output and crossfade. Size M (throwaway, in `spike/`)
- Covers S1, S2, S6, S7, S8 from ARCH §12: borderless output on a chosen monitor at exact pixels; two-bitmap crossfade; memory and CPU readouts shown on screen; .NET 10 on the church Windows 10 build; ReadyToRun size and startup time.

#### C0.3: Spike B, rich text. Size M
- S3: `RichTextBox` with mixed formatting and a `RichText ⇄ FlowDocument` round trip, and a `FormattedText` renderer drawing the same content. Side-by-side comparison screen. Includes pasting from Word, and a token "chip".
- **Outcome:** a written verdict on D-3/D-4 (own model plus own renderer), and a list of control quirks to design around.

#### C0.4: Spike C, audio and timer. Size M
- S4, S5: compare `Mp3FileReader` vs `MediaFoundationReader` and WaveOut vs WASAPI for trim, seek, and position accuracy, with a click-to-measure latency test. Fixed-cell timer digits running 10+ minutes next to the wall clock.

**G0 (owner):** run the three spike apps on the church machine, send the numbers and impressions. I write `docs/SPIKE-RESULTS.md` and update ARCH where reality disagrees. Do not start C1.x UI-dependent work until this is done. (C1.1 to C1.3 are headless and may start in parallel.)

---

### Phase 1: Headless core (toward M1)

These chunks need no UI and no hardware, so they run fully test-first and are the safest place to build momentum.

#### C1.1: Domain model and serialization. Size M
- **FRD:** §3, §4, SLD-001, SLD-005, SLD-009, LIB-004.
- **Do:** records and enums; `ServiceItem` sealed hierarchy with discriminator; `RichText` model (paragraphs, runs, token runs); relative geometry; `System.Text.Json` source-generated contexts; `SchemaVersion` envelope.
- **Tests:** round-trip every entity; unknown future fields tolerated; newer-schema file flagged read-only.

#### C1.2: Resolution logic. Size M. **Review gate**
- **FRD:** TPL-010..027, VAR-001..005, CCLI-003/004/005, SVC-020..025 rules.
- **Do:** template resolver; box binding/detaching/mismatch rule; copyright box injection; token parser and resolver; `RenderModel` (immutable output of resolution); `IdRemapper`; fork (Reference → Copy, preserving Slide IDs); duplicate (new IDs, remapped).
- **Tests:** a table-driven suite from FRD scenarios, including AS-05 (template change, detached boxes, inline red text).
- Review gate: this defines the core semantics of the product. Owner reads the test names and edge cases.

#### C1.3: Storage foundation. Size M
- **FRD:** LIB-001..005, LIB-008, SAFE-001/002 (draft store).
- **Do:** `ILibrary` and repositories for Shows, Services, Templates, Service Templates, Categories, Library settings; atomic `.saving` writer with retry; content-hash and last-write tracking; schema migration framework; local-data paths in `%LOCALAPPDATA%`; draft store.
- **Tests:** against a temp folder: atomic save survives a simulated crash mid-write, `.saving` files ignored, overwrite conflict detected, migration chain.

#### C1.4: Library watching and search. Size M
- **FRD:** LIB-006/007/009/010/011, CAT-003..006.
- **Do:** debounced `FileSystemWatcher` pipeline; self-write suppression; conflict-copy and duplicate-ID detection; in-memory search index with incremental updates; Library-unavailable handling.
- **Tests:** simulated bursts of file events, Drive-style temp files, duplicate IDs; search ranking, accent insensitivity, filter by Kind/Category. Includes AS-08 (edit conflict) at the storage level.

#### C1.5: Navigation engine. Size M. **Review gate**
- **FRD:** LIVE-005..009, LIVE-010..014, LIVE-040, SVC-021/026, ARCH §5.2 and §6.1-6.2.
- **Do:** `ServiceSequence` index grouped by item; `PresentationSession` with immutable `PresentationState`; `ICommandApi` including `NextItem`/`PreviousItem` and `Origin`; screen state (Black/Clear/Logo toggles); re-anchoring when the Service changes mid-presentation; item/slide position info; `ItemChanged` event.
- **Tests:** AS-02 (continuous flow with skips, placeholders, missing shows, end stop, no wraparound); item-level navigation; screen state persisting through navigation (LIVE-014).
- Review gate: this is the operator's mental model, so the owner confirms behaviors before UI is built on top.

---

### Phase 2: Rendering and operator UI (M1, then M2)

#### C2.1: Slide renderer and image pipeline. Size L (split at plan time into 2.1a renderer, 2.1b image cache)
- **FRD:** SLD-002/003/006/007/008, LIB-011, PLT-006, FUT-003, ARCH §7.
- **Do:** layered `SlideRenderer` (background, text, live text, screen state) from a `RenderModel`; `FormattedText` run layout; background fit modes; letterboxing; downscaled image cache on disk plus byte-bounded LRU; bitmap prerender window (current, next, previous); thumbnails; the **headless PNG render harness** (§1.6); missing-media fallback.
- **Tests:** golden-image renders for text styling, fit modes, letterbox; memory-bounded cache behavior; missing media doesn't throw.

#### C2.2: Output window and operator shell. Size L. **Review gate (G1)**
- **FRD:** LIVE-001..004, LIVE-010..013, LIVE-020..022, LIVE-030..033, PLT-006.
- **Do:** composition root and DI; monitor enumeration and `OutputWindow` (fallback to primary); Cut and crossfade; main operator window: **Service view with items as headers and slides beneath**, current-slide highlight, click to jump; current and next Preview; Black/Clear/Logo controls with active state; always-visible audio indicator slot; hotkeys on both windows; exception guards (SAFE-006). Service loaded from a hand-edited JSON file (no editors yet).
- **Done when:** a hand-built Service can be presented end to end with keyboard and clicker keys.
- **G1 (owner):** run on the church projector. Review the Service view layout against the item/slide mental model before the editors are built.

#### C2.3: Rich-text slide editor. Size L. **Review gate**
- **FRD:** SLD-020..028, TPL-020..026 (binding UI), SAFE-001..003.
- **Do:** WYSIWYG slide editor canvas; `RichTextBox` overlay with converters; formatting toolbar (font, size, color, bold, italic, alignment); token chips; overflow indicator; detach/reattach/clear formatting; draft handling; app-level snapshot undo/redo with `RichTextBox` undo disabled (D-2).
- **Tests:** converter round trips and view-model behavior headless. Golden images for editor/renderer agreement.
- Highest UI risk. Plan this using the spike's findings, and split off the converters as their own sub-chunk if needed.

#### C2.4: Shows, Catalog, categories, search UI. Size M
- **FRD:** CAT-001..007, SHOW-001..007, SLD-004.
- **Do:** Catalog window (list, filters, instant search); create/duplicate/rename/recategorize/delete (with "used in these Services" warning); Show editor shell (slide list: add, duplicate, delete, reorder, label); metadata form; categories management; audio and actions slots left as empty tabs.

#### C2.5: Service editor. Size L (split at plan time)
- **FRD:** SVC-001..004, SVC-020..026, VAR-007 (shell only).
- **Do:** Service list (newest first); create blank/duplicate/rename/delete; add items from Catalog via search; reorder/remove; per-slide skip toggles; "edit slide" flow with the Presentation auto-fork and the Song prompt (SVC-022); "Modified for this Service" marker and Revert to catalog; "Missing show" display.
- **Tests:** AS-06 (per-Service edit) at view-model level.

**M2 / G2 (owner):** build a real Sunday service at home, sync via Drive, present at church. Collect UX fixes into a short fix-up chunk **C2.6** before moving on.

---

### Phase 3: Reuse (M3)

#### C3.1: Template editor and Library defaults. Size M
- **FRD:** TPL-001..013, SLD-006, CCLI-004 (copyright format setting).
- **Do:** Template list/editor (background, named boxes, defaults, copyright box); Category default Templates; Library default; delete-with-usage warning; **live propagation** check (TPL-003/013) including AS-05 end to end.

#### C3.2: Variables UI. Size S
- **FRD:** VAR-001..008.
- **Do:** Service Variables form; token insertion in the editor; in-catalog display of token names (VAR-003); "all used variables in one form" (VAR-007).

#### C3.3: Service Templates and Placeholders. Size M
- **FRD:** SVC-005, SVC-030..035.
- **Do:** Save as Service Template; create from template (using `IdRemapper`); Placeholder add/fill/clear with Category/Kind-restricted search; unfilled Placeholders skipped in presentation.
- **Tests:** AS-01 up to the Service build step.

---

### Phase 4: Audio (M4)

#### C4.1: Audio engine and cue controller. Size M. **Review gate**
- **FRD:** AUD-001..007, 013..020, DEC-013, ARCH §6.5.
- **Do:** `IAudioPlayer`/`IPlaybackClock`; NAudio implementation using the spike's chosen reader and output; trim; `CueController` against a fake clock; item-change audio policy (Q-A7, option 3) via command `Origin`; audio indicator with Stop and Restart.
- **Tests (headless, fake clock/player):** AS-03; stop on operator item change, not on Cue/Action; start while playing is a no-op; cues driving slides only while the owning item is current.

#### C4.2: Audio and cue editor UI. Size L
- **FRD:** AUD-008..012, SVC-025.
- **Do:** attach audio to a Show; timeline with draggable cue and trim markers; tap-to-cue mode; click-to-seek with slide follow; nudge and typed positions; `ShowStarted → StartAudio` default offer (ACT-042); skipping disabled for cued Shows with an explanation.

**G4 (owner):** check cue accuracy on the church machine and speakers.

---

### Phase 5: Timers and Actions (M5)

#### C5.1: Timers. Size M
- **FRD:** TMR-001..016.
- **Do:** `TimerService` on `IClock` with whole-second alignment; states and expiry-once logic; clock-time and duration types; live text slots in the renderer with fixed-cell digits; warning color; auto-start on display; Timer definitions UI on Service and Service Template.
- **Tests:** deterministic fake-clock tests for every TMR rule, including late start. Visual check for flicker at G5.

#### C5.2: Action processor. Size L (split: engine, then UI)
- **FRD:** ACT-001..044, Q-A1 amendment.
- **Do:** triggers and commands; delays and cancellation; loop guard; pending-action info; Actions on Slide, Show, **Service Item**, Service, Service Template; "within this Show" targets; `IdRemapper` coverage for Action targets; Action editor UI with the one-click auto-advance shortcut.
- **Tests:** AS-04 end to end on fake clock (loop, countdown, red at 60 s, jump at expiry); loop guard trips only on rapid cycles; cancellation on slide change.

**G5 (owner):** run AS-04 on the projector.

---

### Phase 6: Compliance (M6)

#### C6.1: Mode and CCLI logging. Size M
- **FRD:** MODE-001..004, CCLI-001..016, CCLI-020, CCLI-024.
- **Do:** Mode toggle and indicator; CCLI metadata UI; close flow (summary, OK/Cancel, safety-net prompt); per-Service log files with replace semantics; CSV export by date range; Usage Log viewer shell.
- **Tests:** AS-07 end to end, including idempotent logging and distinct-Service counting.

#### C6.2: Pre-service check. Size M
- **FRD:** CHK-001..004, ACT-041, LIB-011, TPL-027, SLD-028.
- **Do:** `ICheckRule` per CHK-001 item; panel with click-through; per-Service dismissals; auto-run when presenting starts.

#### C6.3: CCLI reporting polish. Size S (after Q-005 answered)
- **FRD:** CCLI-021..023.
- **Do:** CSV columns matched to CCLI's template; reporting-period setting and reminder banners.

**G6 (owner):** supply CCLI's current spreadsheet template (Q-005).

---

### Phase 7: Scripture (M7)

#### C7.1: Bible import and reference parser. Size M (headless)
- **FRD:** SCR-001..006.
- **Do:** Zefania importer (`System.Xml.Linq`, tolerant of extra markup); per-Bible index cache; book alias table; reference parser for all FRD forms with clear errors.
- **Tests:** table-driven reference cases; a fixture Bible file; malformed XML.

#### C7.2: Scripture items. Size M
- **FRD:** SCR-007..013, SCR-016, SVC-002.
- **Do:** slide generation (verses per slide, inline verse numbers, per-slide reference line); Service-local result; Regenerate with warning; Library default Scripture Template; import UI (display name, abbreviation).
- **Tests:** generation shape and boundaries; AS-01's Rom 8:28-39 case.

---

### Phase 8: Phone remote (M8)

#### C8.1: Remote host. Size M
- **FRD:** REM-001..010, FUT-004.
- **Do:** lazy-loaded Kestrel host; embedded single page; token and cookie; private-IP filter; command endpoints into `ICommandApi` (with `Origin = Operator`); SSE state stream; QR code and URL in operator UI; firewall note.
- **Tests:** endpoint tests with an in-process test server (auth, IP filter, commands, SSE shape). Measure the RAM impact with the remote on and off (PLT-005).

**G8 (owner):** try on church Wi-Fi with a real phone.

---

### Phase 9: Hardening and release (M9)

#### C9.1: Recovery and safety. Size M
- **FRD:** SAFE-004..006, LIB-009, LIB-010.
- **Do:** crash-restore of drafts; reopen last Service at last slide; Library problems panel; exception audit across editors, remote, and Actions with fault-injection tests proving the Output stays up.

#### C9.2: Performance pass. Size M
- **FRD:** PLT-002/005/006/007, LIVE-031.
- **Do:** profile on the church machine against AS-09; tune cache sizes and prerender window; decide crossfade vs Cut-only from data; startup time; fix leaks.

#### C9.3: Packaging. Size S
- **Do:** self-contained x64 ReadyToRun publish; installer or zip with a first-run guide; app icon; version stamp; update procedure.

#### C9.4: Acceptance run. Size M
- **Do:** automate AS-01..AS-09 as far as possible (`Acceptance.Tests`); owner checklist for the rest.

#### C9.5: Optional imports. Size M each, only if wanted (IMP-001/003). **Deferred until requested.**

**G9 (owner):** full acceptance run at church.

---

## 4. Order and dependencies

```
C0.1 ──► C1.1 ──► C1.2 ──► C1.3 ──► C1.4
  │        │                          │
  │        └──────────► C1.5 ◄────────┘          (headless, can start before G0)
  │
  ├──► C0.2 ─┐
  ├──► C0.3 ─┼──► G0 ──► C2.1 ──► C2.2 ──► G1 ──► C2.3 ──► C2.4 ──► C2.5 ──► G2 (M2)
  └──► C0.4 ─┘                                                                  │
                                              ┌─────────────────────────────────┘
                                              ▼
                          C3.1 ► C3.2 ► C3.3 ► C4.1 ► C4.2 ► C5.1 ► C5.2 ► C6.1 ► C6.2 ► C6.3
                                                                                          │
                                                       C7.1 ► C7.2 ► C8.1 ► C9.1..C9.4 ◄──┘
```

Notes on ordering:
- **C1.x before C2.x:** the UI is built on proven logic, so UI sessions don't also debug semantics.
- **C2.3 (editor) after C2.2 (presenting):** a hand-edited Service still lets us validate the Output and the operator mental model early.
- **Audio (C4) before Timers/Actions (C5):** Actions like `StartAudio` and `AudioEnded` need audio to exist, and the cue controller's use of `GoToSlide` establishes the automation path that C5.2 reuses.
- **CCLI after Actions:** Service Templates and Show copies must be final before logging semantics are locked in.
- **Scripture and Remote are independent** of most of the above and can swap order, or be pulled earlier if the church needs them sooner. Scripture needs C3.1 (Templates). Remote needs only C1.5/C2.2.

---

## 5. Controlling risk and scope

| Risk | Handling in the plan |
|---|---|
| Rich-text editor trouble | Spike B (C0.3) first. C2.3 planned from its results, converters possibly split into their own sub-chunk. |
| Context overload on big chunks | Sizing rules (§1.5). Chunks marked L are explicitly split at plan time. |
| Drift from the FRD/ARCH between sessions | Per-chunk plan files, traceability table, `CLAUDE.md` rules, ADRs for any deviation. |
| Forgotten requirements | `TRACEABILITY.md` lists every FRD ID. C9.4 checks none are unmapped. |
| Scope creep (FreeShow-style features) | `CLAUDE.md` restates FRD §2 and §22. Plans must list "explicitly out of scope". |
| Regressions as features land | Full `dotnet test` on every chunk. Acceptance tests grow over time rather than waiting until the end. |
| Visual bugs invisible to Claude | PNG render harness and golden images; owner gates for feel and projector reality. |
| Church-machine-only issues | Spike on the real machine, a gate after every milestone, performance pass using AS-09. |

---

## 6. What I need from you, and when

| When | What |
|---|---|
| Now | Approve this plan, or change chunk order or scope. Note whether Scripture or the phone remote should come earlier. |
| Before C0.2 | Access to the church machine (or its specs, Windows build, and RAM) to run the spike. |
| G0 | Spike figures and impressions. |
| Before C2.3 | A few sample slides you like (screenshots from FreeShow are fine) to use as visual targets for Templates and text. |
| G1, G2 | Hands-on review at the projector. |
| Before C6.3 | CCLI's current manual-reporting spreadsheet (Q-005). |
| Anytime | Answers to remaining FRD open questions: Q-004 (Black/Clear/Logo and navigation), Q-006 (reference line scope), Q-007 (copyright placement), Q-008 (Presentations and CCLI), Q-009 (mode switch UI), Q-010 (product name). Q-004 is needed by C1.5, and Q-006 by C7.2. |

---

## 7. Proposed first moves

1. Run **C0.1** now (plan, then execute). It's small and unblocks everything.
2. In parallel with your spike scheduling, run **C1.1** and **C1.2**, which are headless and don't wait on hardware.
3. Build **C0.2-C0.4** and hand them to you for **G0**.
