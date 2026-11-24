# Functional Requirements: Church Presentation App

Version: 0.3 (draft)
Date: 2026-11-24
Changes since 0.1: see Section 26. Product name is ChurchSlides.
Status: Functional requirements only. UI layout, visual design, and detailed technical design are deliberately not covered here.

---

## 0. Instructions for the AI coding assistant

Read this section first. It explains how to interpret the rest of the document.

1. **Requirement IDs.** Every requirement has a stable ID such as `AUD-004`. Reference these IDs in code comments, commit messages, tests, and questions.
2. **Priority keywords.** Each requirement is tagged:
   - `[MUST]`: needed for the first usable release.
   - `[SHOULD]`: expected soon after. Design for it now, even if you build it later.
   - `[COULD]`: backlog. Don't build it unless asked, but don't make design choices that rule it out.
3. **Normative language.** "MUST", "MUST NOT", "SHOULD", and "MAY" inside a requirement follow RFC 2119.
4. **Decisions are final.** Section 2 lists decisions the product owner has already made, with the reasons. Don't reverse or "improve" them without asking. Several of them reject features that similar products (FreeShow, OpenLP) have, and they do so on purpose.
5. **Assumptions are not final.** Items marked `ASSUMPTION` are reasonable defaults the product owner hasn't explicitly confirmed. Implement them as written, and keep them easy to change.
6. **Open questions.** Section 23 lists unresolved items. If you reach one, stop and ask. Don't guess.
7. **Glossary names are code names.** Use the terms in Section 3 for classes, files, UI labels, and documentation. Don't introduce synonyms (for example, don't use "Project", "Presentation item", "Song item", "Section", or "Group").
8. **Constraints override convenience.** If a library or approach conflicts with Section 1 (hardware and RAM limits), the constraint wins. Flag the conflict.
9. **Simplicity is a requirement.** A non-technical volunteer must be able to run a Service. When two designs both meet a requirement, choose the one with fewer settings and fewer modes.

---

## 1. Platform and performance constraints

| ID | Priority | Requirement |
|---|---|---|
| PLT-001 | MUST | The app runs on Windows 10 (64-bit) and Windows 11. |
| PLT-002 | MUST | The app runs well on older hardware with integrated Intel graphics and no discrete GPU. |
| PLT-003 | MUST | The app is built on .NET (current LTS). The product owner suggests WPF for the desktop UI and output rendering. Confirm that with a prototype on the target machine before committing (see PLT-008). |
| PLT-004 | MUST NOT | The app must not use Electron, CEF, WebView2, or any other embedded browser engine for the operator UI or the output window. The RAM cost of browser engines is the main reason this project exists. |
| PLT-005 | SHOULD | Working set stays at or under 300 MB with a typical Service loaded (about 3 Songs, 5 Presentations, 1 Scripture item, and background images). |
| PLT-006 | SHOULD | A slide change appears on the output in under 100 ms when transitions are set to Cut. |
| PLT-007 | MUST | Timer text updates must not flicker, reflow other text, or re-render the whole slide (see TMR-010). |
| PLT-008 | MUST | Before building features, produce a spike that runs on the target church machine and verifies: (a) full-screen output on a second display, (b) a crossfade between two image-backed slides, (c) a rich-text box with mixed fonts and colors, (d) MP3 playback with an accurate position readout, (e) a timer updating every second without flicker. Record RAM and CPU figures. |
| PLT-009 | MUST | The app works fully offline. Network access is needed only for the phone remote (on the LAN) and for the user's own folder sync (outside the app). |

Target environment for reference: the church projector is 1440×900 (16:10). The operator machine has modest RAM and integrated Intel graphics.

---

## 2. Decisions already made (do not reverse)

| ID | Decision | Rationale |
|---|---|---|
| DEC-001 | The object that holds one Sunday's run order is a **Service**. Don't call it "Project". | Product owner preference. |
| DEC-002 | A Show is a flat ordered list of Slides. There are **no Sections or groups** inside a Show. Slides carry a free-text label such as "Verse 1a". | Labels are enough. Sections add complexity for no gain. |
| DEC-003 | Repeated choruses are **duplicated slides**. There's no arrangement or reorder model, and no slide is shown more than once by reference. | Keeps playback a simple linear walk. |
| DEC-004 | The hierarchy depth is fixed: Service → Service Item → Slide. | Fixed depth is enough. |
| DEC-005 | A slide copied between Shows is a **copy**, never a shared reference. Duplicating a Show in the Catalog creates an independent copy. | Prevents edits from showing up in unexpected places. |
| DEC-006 | A Service Item **references** a catalog Show until the Show is edited in the context of that Service. Then it becomes a Service-local copy (see SVC-020 to SVC-024). | Most Shows are never edited per Service. Liturgy and announcement slides sometimes are. |
| DEC-007 | Changes to catalog Shows may change how old Services look. This is acceptable. Services aren't archived and have no long-term value. | The product owner explicitly doesn't care about historical fidelity. |
| DEC-008 | There's **no text view** for editing a Song as plain text. Plain-text import, if built, is a one-time conversion into slides. | FreeShow's text view caused more problems than it solved. |
| DEC-009 | Variables are **per Service**, not global. Slides contain variable tokens by name. | FreeShow's global variables were a pain point. |
| DEC-010 | Rehearse mode and Live mode look and behave **identically**. The only difference is CCLI logging (Section 14). | Rehearsal must test the real thing. |
| DEC-011 | CCLI logging rule: if a Service was marked Live, every Show in it with a CCLI number is logged when the Service is closed, whether or not it was displayed. Display activity isn't tracked. | Skipping a planned song almost never happens. Simplicity matters more. |
| DEC-012 | The pre-service check is **advisory only**. It never blocks opening, presenting, or going Live. | Product owner requirement. |
| DEC-013 | Manual navigation between slides of the same Show during audio playback never stops, pauses, or seeks the audio. (Changing to a different Service Item is covered by DEC-020.) | Manual navigation mid-song is a correction, not a change of plan. |
| DEC-014 | Folder sync is done by an external tool (Google Drive for Desktop in mirror mode). The app doesn't do its own network sync. It only has to behave safely in a synced folder. | The shared Drive folder works already. The app only needs to avoid conflicts. |
| DEC-015 | Editing happens on both machines (home and church). Neither machine is read-only. | Someone might build a Service at church. |
| DEC-016 | Only one output display. There's no stage display, NDI, or multi-output. | Not needed. |
| DEC-017 | Only one Timer is expected to be visible at a time, but Timers are named objects and several may exist in a Service. | Supports the looping countdown use case. |
| DEC-018 | Only one audio track plays at a time. | Simplicity. |
| DEC-019 | The phone remote can't change Rehearse or Live mode. | Product owner requirement. |
| DEC-020 | Audio stops when the **operator** changes to a different Service Item (Next/Previous crossing an item boundary, Next item, Previous item, or clicking another item or a slide in another item, from the operator UI, hotkeys, clicker, or phone remote). It does **not** stop when the change is caused by an Action or a Cue. | Jumping out of a song means the operator has abandoned it. Automation authors keep full control and can add `StopAudio` explicitly. Resolves Q-003. |
| DEC-021 | Presenting starts only with one of two buttons: **Go Live** (presents in Live mode) or **Rehearse** (presents in Rehearse mode). Clicking a slide while not presenting only selects it. | Starting to present is a deliberate act, and choosing the mode at that moment means it is never forgotten. Resolves Q-009. |

---

## 3. Glossary and taxonomy

Use these names exactly.

| Term | Code name | Definition |
|---|---|---|
| Library | `Library` | The root folder holding all synced data: Shows, Services, Templates, media, Bibles, and logs. |
| Catalog | `Catalog` | The browsable, searchable collection of all Shows in the Library. |
| Show | `Show` | A reusable catalog item. It is an ordered list of Slides plus metadata, an optional default Template, optional audio with cues, and actions. Every Show has a `Kind`. |
| Show Kind | `ShowKind` | `Song` or `Presentation`. It affects editing prompts (SVC-022), metadata expectations, and CCLI handling. It doesn't change the data structure. |
| Slide | `Slide` | The smallest displayable unit. It has a label, a background, one or more Text Boxes, an optional Template override, and actions. |
| Text Box | `TextBox` | A positioned rich-text region on a Slide. It is either bound to a Template Box or detached. |
| Template | `Template` | A reusable layout: a background, named Template Boxes, and default text formatting. |
| Template Box | `TemplateBox` | A named, positioned text region inside a Template, with default formatting. |
| Category | `Category` | A flat label. Each Show has exactly one. Categories drive default Templates and Placeholder filtering. One built-in Category, Scripture, always exists (CAT-010). |
| Tag | `Tag` | Optional, many per Show. `[COULD]` |
| Service | `Service` | One dated run order. It holds an ordered list of Service Items, Variables, Timers, service-level actions, and a mode (Rehearse or Live). |
| Service Item | `ServiceItem` | One entry in a Service. It has one of four forms: Show Reference, Show Copy, Placeholder, or Scripture. |
| Show Reference | `ServiceItemKind.ShowReference` | A Service Item pointing to a catalog Show. It may carry a list of skipped slides. |
| Show Copy | `ServiceItemKind.ShowCopy` | A Service Item holding its own Service-local copy of a Show, created by forking a reference. |
| Placeholder | `ServiceItemKind.Placeholder` | An unfilled slot in a Service, optionally restricted to a Category and/or Kind. It is filled by choosing a Show. The word means only this. |
| Scripture Item | `ServiceItemKind.Scripture` | A Service Item that generates Service-local slides from a Bible passage. |
| Service Template | `ServiceTemplate` | A blueprint for creating new Services: Service Items (including Placeholders), variable definitions, Timer definitions, and service-level actions. |
| Variable | `Variable` | A named value defined on a Service, such as `preacher`. Slides reference it with a token. |
| Timer | `Timer` | A named countdown object defined on a Service. It keeps running independently of which slide is displayed. |
| Action | `Action` | A trigger, an optional delay, and an ordered list of Commands. |
| Trigger | `Trigger` | The event that fires an Action. |
| Command | `Command` | One operation an Action performs. |
| Audio Track | `AudioTrack` | An audio file attached to a Show, with trim points and slide cues. |
| Cue | `Cue` | A time in the Audio Track at which a given slide must be displayed. |
| Mode | `ServiceMode` | `Rehearse` or `Live`. |
| Usage Log | `UsageLog` | The record of CCLI-reportable song uses, independent of Services. |
| Output | `Output` | The full-screen projector window. |
| Preview | `Preview` | The operator's view of the current and next slides. |
| Bible | `Bible` | An imported translation, from Zefania XML. |

**Code-name exceptions.** Four glossary terms collide with built-in .NET/WPF types, so their C# names differ. UI labels and all FRD wording stay as in the table above.

| Glossary term | C# name | Collides with |
|---|---|---|
| Action | `TriggeredAction` | `System.Action` |
| Timer | `CountdownTimer` | `System.Threading.Timer` |
| Trigger | `ActionTrigger` | `System.Windows.Trigger` |
| Text Box | `SlideTextBox` | `System.Windows.Controls.TextBox` |

### 3.1 Hierarchy

```
Library
├── Catalog
│   └── Show (Kind: Song | Presentation; one Category)
│       ├── Slide[] (ordered)
│       │   └── TextBox[]
│       ├── AudioTrack? (with Cue[])
│       └── Action[]
├── Template[]
├── ServiceTemplate[]
├── Service[]
│   ├── Variable[]
│   ├── Timer[]
│   ├── Action[] (service-level)
│   └── ServiceItem[] (ordered; each may carry Action[])
│       ├── ShowReference → Show (+ skipped slide IDs)
│       ├── ShowCopy (owns a Show copy)
│       ├── Placeholder (Category/Kind filter)
│       └── Scripture (generates Slide[])
├── Bible[]
├── Media (images, audio)
└── UsageLog
```

---

## 4. Data model (non-normative sketch)

This sketch shows the fields the requirements need. Field names are suggestions. Use whatever serialization works, but keep the concepts. All entities have a stable `Id` (GUID) and a `SchemaVersion` at the file level.

```csharp
enum ShowKind { Song, Presentation }
enum BackgroundFit { Fit, Fill, Stretch }
enum CopyrightPlacement { None, FirstSlide, LastSlide }
enum ServiceMode { Rehearse, Live }
enum TimerType { CountdownToClockTime, CountdownDuration }
enum TimerEndBehavior { HoldAtZero, ContinueNegative }
enum TimerState { Stopped, Running, Expired }

record Show(
  Guid Id, string Title, ShowKind Kind, Guid CategoryId, List<string> Tags,
  SongMetadata? Metadata, Guid? DefaultTemplateId,
  List<Slide> Slides, AudioTrack? Audio, List<Action> Actions,
  CopyrightPlacement CopyrightPlacement);

record SongMetadata(
  string? Authors, string? Copyright, string? CcliSongNumber, bool IsPublicDomain);

record Slide(
  Guid Id, string Label, bool SkippedByDefault, Guid? TemplateOverrideId, Background? BackgroundOverride,
  List<TextBox> TextBoxes, List<Action> Actions);

record Background(MediaKind Kind, string? MediaPath, string? ColorHex, BackgroundFit Fit);
enum MediaKind { Image, SolidColor /* Video reserved for future */ }

record TextBox(
  Guid Id, string? TemplateBoxName,        // the Template Box it takes defaults from; kept when detached
  RectRelative? Geometry,                  // non-null = detached (own position)
  RichTextContent Content);                // inline formatting runs; may contain tokens

record RectRelative(double X, double Y, double Width, double Height); // 0..1 of canvas

record Template(
  Guid Id, string Name, Background Background, List<TemplateBox> Boxes);

record TemplateBox(
  string Name, RectRelative Geometry, TextDefaults Defaults, bool IsCopyrightBox);

record TextDefaults(
  string FontFamily, double FontSizeRelative, string ColorHex, bool Bold, bool Italic,
  HorizontalAlign HAlign, VerticalAlign VAlign, Outline? Outline, Shadow? Shadow);

record AudioTrack(
  string MediaPath, TimeSpan? TrimStart, TimeSpan? TrimEnd, List<Cue> Cues);

record Cue(Guid SlideId, TimeSpan Position); // Position = absolute file time

record Service(
  Guid Id, string Name, DateOnly Date, ServiceMode Mode,   // Mode is saved in the Service state file (LIB-013)
  List<ServiceItem> Items, List<Variable> Variables, List<Timer> Timers,
  List<Action> Actions, Guid? CreatedFromServiceTemplateId);

record ServiceItem(
  Guid Id, ServiceItemKind Kind,
  Guid? ShowId,                     // ShowReference
  List<Guid> SkippedSlideIds,       // ShowReference only
  Show? LocalShow,                  // ShowCopy, Scripture (generated slides)
  PlaceholderSpec? Placeholder,     // Placeholder
  ScriptureSpec? Scripture,         // Scripture
  Guid? TemplateOverrideId,         // [COULD]
  List<Action> Actions);            // item-level Actions (ACT-002), owned by the Service

record PlaceholderSpec(string Label, Guid? CategoryFilter, ShowKind? KindFilter);

record ScriptureSpec(
  string BibleId, string Reference, int VersesPerSlide,
  bool ShowVerseNumbers, bool ShowReferenceLine, Guid? TemplateId);

record Variable(string Name, string Value);

record Timer(
  string Name, TimerType Type, TimeOnly? TargetClockTime, TimeSpan? Duration,
  TimerEndBehavior EndBehavior, int WarningThresholdSeconds, string? WarningColorHex,
  bool AutoStartOnDisplay);

record Action(Trigger Trigger, TimeSpan Delay, List<Command> Commands);
```

---

## 5. Library storage and sync (LIB)

| ID | Priority | Requirement |
|---|---|---|
| LIB-001 | MUST | All user data lives in one Library folder. Its path is set per machine in local settings (not in the Library). |
| LIB-002 | MUST | Data is stored as plain files, one file per object: each Show, Service, Template, Service Template, and Category list. JSON is recommended. Don't use a database. |
| LIB-003 | MUST | Media files (images, audio) are **copied into** the Library's media folder when added, and are referenced by Library-relative paths. Absolute paths MUST NOT be stored. |
| LIB-004 | MUST | Every file has a `SchemaVersion`. Loading an older schema migrates it in memory. Loading a newer schema than the app understands opens read-only, with a warning. |
| LIB-005 | MUST | Saves are atomic. Write to a temp file with a distinct extension (for example `.saving`), then rename over the target. Files with the temp extension are ignored when loading. |
| LIB-006 | MUST | Overwrite protection: when loading an object, record its file's last-write time and content hash. At save time, if the file on disk has changed since loading, don't overwrite it. Tell the user it was changed elsewhere and offer **Reload** (discarding local changes) or **Save as copy**. |
| LIB-007 | MUST | The app watches the Library folder. When a file changes on disk and the matching object isn't being edited, the app reloads it and updates the Catalog and search index. |
| LIB-008 | MUST | Machine-local data (search index, image caches, drafts, window positions, output display choice, Library path, remote token) is stored outside the Library, in `%LOCALAPPDATA%\ChurchSlides\`. It is rebuildable or machine-specific, and MUST NOT be synced. |
| LIB-009 | SHOULD | Detect files that look like sync-tool conflict copies (for example names containing `(1)`, `conflict`, or a duplicate `Id` in two files). List them in a "Library problems" panel. Don't load them silently as separate objects. |
| LIB-010 | MUST | If the Library folder is unavailable at startup, show a clear message and let the user choose another folder. Don't crash. |
| LIB-011 | SHOULD | A media file that is referenced but missing renders as a neutral fallback (for example a solid dark background) and is flagged by the pre-service check. It must never crash or block the Output. |
| LIB-012 | COULD | A "clean up unused media" tool lists media files no object references. |
| LIB-013 | MUST | Facts about a Service that change outside its editor are saved in a separate **Service state file** (`service-state/{id}.json`, its own folder so it is never mistaken for a Service), not in the Service file: the Mode (MODE-001), pre-service check dismissals (CHK-002), and the "don't ask again" answer of CCLI-016. Writing them never conflicts with an open Service draft (SAFE-001). The state file is small and the last write wins (no LIB-006 prompt). A missing state file means Rehearse mode, no dismissals, and not yet asked. |

Suggested folder layout (non-normative):

```
Library/
  library.json            (library settings: canvas aspect, CCLI license #, logo, transition)
  categories.json
  shows/{id}.json
  services/{id}.json
  service-state/{id}.json  (mode, check dismissals, CCLI-016 answer; LIB-013)
  templates/{id}.json
  service-templates/{id}.json
  bibles/{id}.xml          (original Zefania files)
  media/images/...
  media/audio/...
  logs/ccli/{serviceId}.json
```

---

## 6. Catalog, categories, and search (CAT)

| ID | Priority | Requirement |
|---|---|---|
| CAT-001 | MUST | The Catalog lists all Shows, with filters by Kind (Song or Presentation) and by Category. |
| CAT-002 | MUST | Categories are a flat list. Each Show has exactly one Category. Categories can be created, renamed, and deleted. Deleting a Category that is in use requires reassigning its Shows. |
| CAT-003 | MUST | Search-as-you-type by Show title, case- and accent-insensitive, with results updating as each character is typed. |
| CAT-004 | SHOULD | Search also matches the slide text (lyrics) and CCLI song number. Title matches rank above text matches. |
| CAT-005 | MUST | The search index is built locally (LIB-008) and updates incrementally when Shows change. |
| CAT-006 | MUST | Search is available wherever a Show is chosen: the Catalog, adding an item to a Service, and filling a Placeholder (SVC-031). |
| CAT-007 | MUST | Shows can be created, duplicated (independent copy, DEC-005), renamed, recategorized, and deleted. Deleting a Show referenced by a Service warns and lists those Services. |
| CAT-008 | COULD | Tags: many per Show. They are filtered by picking from a list of existing tags, not by typing. |
| CAT-009 | COULD | Usage statistics per Song, such as last-used date and count over the last N months, derived from the Usage Log. |
| CAT-010 | MUST | A built-in **Scripture** Category always exists. It has a fixed, well-known ID (the same on every machine, so two machines never create two of them). It can be renamed but not deleted. Its default Template (TPL-012) is the default Scripture Template (SCR-013). Slides generated by Scripture Items belong to it, and catalog Shows may use it too. |

---

## 7. Shows and slides (SHOW, SLD)

### 7.1 Shows

| ID | Priority | Requirement |
|---|---|---|
| SHOW-001 | MUST | A Show is an ordered list of Slides. Slides can be added, duplicated, deleted, and moved up or down within the list. |
| SHOW-002 | MUST | Each Slide has a free-text label (for example "Verse 1a", "Chorus", "Bridge"). The label shows in the operator UI and on the phone remote. It is never projected. |
| SHOW-003 | MUST | Song metadata fields: Title (required), Authors, Copyright, CCLI Song Number, Is Public Domain. All except Title are optional. These fields are available on Presentations too, but are normally empty. |
| SHOW-004 | MUST | Each Show has a Copyright Placement setting: None, First Slide, or Last Slide. The default for Songs is First Slide (ASSUMPTION), and the default for Presentations is None. |
| SHOW-005 | MUST | A Show may have a default Template (see TPL-010). |
| SHOW-006 | MUST | A Show may have at most one Audio Track (Section 11). |
| SHOW-007 | MUST | A Show may have Actions (Section 13). |
| SHOW-008 | MUST | A Slide may be marked **Skipped** in the catalog. This is only a default for Services that add the Show (SVC-027). The slide still exists and is still shown in the editor. Not allowed for a Show with an Audio Track that has Cues (SVC-025). |

### 7.2 Slides and canvas

| ID | Priority | Requirement |
|---|---|---|
| SLD-001 | MUST | There is a global canvas aspect ratio setting in the Library: 16:10 (default), 16:9, or 4:3. All slide geometry is stored as fractions (0–1) of the canvas. |
| SLD-002 | MUST | The Output renders the canvas at the output display's resolution. If the display's aspect ratio differs from the canvas aspect ratio, the canvas is letterboxed or pillarboxed in black. |
| SLD-003 | MUST | Supported output resolutions include at least 1280×720, 1280×800, 1440×900, 1600×900, 1680×1050, 1920×1080, and 1920×1200. Any resolution the OS reports should work through scaling. |
| SLD-004 | SHOULD | Changing the canvas aspect ratio rescales existing geometry and warns that slides may need touch-ups. |
| SLD-005 | MUST | Font sizes are stored relative to canvas height, so text scales with output resolution. |
| SLD-006 | MUST | A Slide has a background: an Image (fit modes Fit, Fill, Stretch) or a Solid Color. The background comes from the resolved Template unless the Slide overrides it. |
| SLD-007 | MUST | Images are decoded and downscaled to the output resolution once, and cached locally (LIB-008). Full-resolution originals MUST NOT be held in memory during presentation. |
| SLD-008 | SHOULD | Supported image formats: JPEG and PNG (MUST), plus BMP, GIF (static), and WebP if the chosen framework supports them without extra dependencies. |
| SLD-009 | MUST | Backgrounds are modeled as a media reference with a kind (Image or SolidColor), so Video can be added later without changing the slide model (FUT-001). |

### 7.3 Text boxes and rich text

| ID | Priority | Requirement |
|---|---|---|
| SLD-020 | MUST | A Slide can have any number of Text Boxes. |
| SLD-021 | MUST | Text Box content is rich text with formatting on **ranges** of characters, not only the whole box. Range formatting includes font family, size, color, bold, and italic. |
| SLD-022 | MUST | Rich text editing happens inside the app, using an existing rich-text editing component (for example WPF `RichTextBox`/`FlowDocument` or a mature third-party control). Don't build a text editor from scratch. |
| SLD-023 | SHOULD | Text Boxes support horizontal and vertical alignment and line spacing. |
| SLD-024 | COULD | Bullets and numbered lists, if the chosen component supports them. |
| SLD-025 | COULD | Text outline and drop shadow, per Text Box or per range. Only build these if they render well on integrated graphics (PLT-002). |
| SLD-026 | MUST | The editor shows the slide as it will appear on the Output (WYSIWYG at reduced scale), using the same renderer as the Output. |
| SLD-027 | MUST | Text Box content may contain tokens: Variables (VAR-002), Timers (TMR-009), and Show metadata (CCLI-003). In the editor, tokens appear as recognizable markers. On the Output they show their resolved values. |
| SLD-028 | SHOULD | The editor flags text that overflows its Text Box (a visual indicator). The pre-service check reports it too (CHK-001). |
| SLD-029 | COULD | Auto-fit: shrink text to fit its box. It is off by default and controlled per Text Box. This is backlog. Don't build it in the first release. |

---

## 8. Templates (TPL)

| ID | Priority | Requirement |
|---|---|---|
| TPL-001 | MUST | A Template defines a background, one or more named Template Boxes (geometry plus default text formatting), and optionally one Template Box marked as the copyright box. |
| TPL-002 | MUST | Templates can be created, edited, duplicated, renamed, and deleted. Deleting a Template that is in use warns and lists where it's used. Affected items fall back through the resolution order (TPL-010). |
| TPL-003 | MUST | Changing a Template immediately changes every slide that resolves to it, without modifying the slides' files. |
| TPL-010 | MUST | Template resolution order, first match wins: Slide override, then Show default Template, then Category default Template, then the Library default Template. |
| TPL-011 | COULD | A Service Item Template override, inserted between Slide override and Show default in the order. |
| TPL-012 | MUST | Each Category may have a default Template. |
| TPL-013 | MUST | Template resolution happens at render time, so changing a Category's default Template changes all its Shows the next time they're displayed. |
| TPL-014 | MUST | Resolution never fails. A Template ID whose Template no longer exists is skipped and reported, and the next level applies. If no Template resolves at any level, a built-in plain Template (black background, one full-size box, one copyright box) is used and the problem is reported to the pre-service check (CHK-001). Template Box names match ignoring case. |
| TPL-020 | MUST | A Text Box is either **bound** to a Template Box by name, or **detached**. |
| TPL-021 | MUST | A bound Text Box takes its geometry and default formatting from the resolved Template's matching box. |
| TPL-022 | MUST | A detached Text Box keeps its own geometry. It still uses the Template's defaults for any formatting it doesn't set itself. |
| TPL-023 | MUST | Detaching is an explicit user action ("Detach from template"), and a detached box shows a visible badge in the editor. A "Reattach" action restores binding and discards the box's own geometry. A detached Text Box remembers the name of the Template Box it came from. That is how it gets its defaults (TPL-022) and how Reattach knows where to go back to. Detaching copies the box's current position, so the box doesn't move when detached. |
| TPL-024 | MUST | Inline range formatting (SLD-021) always belongs to the text and survives Template changes. Template defaults apply only to text without explicit formatting. |
| TPL-025 | SHOULD | A "Clear formatting" action on a Text Box or selection removes inline formatting so Template defaults apply again. |
| TPL-026 | MUST | New slides created in a Show get one bound Text Box for each Template Box of the resolved Template, except the copyright box. |
| TPL-027 | MUST (ASSUMPTION) | Mismatch rule: if a bound Text Box's name doesn't exist in the resolved Template, render it in the Template's first non-copyright box. Flag the slide in the editor and in the pre-service check. Don't lose any content. |

---

## 9. Variables (VAR)

| ID | Priority | Requirement |
|---|---|---|
| VAR-001 | MUST | Variables are defined on a Service, each with a name and a text value. Names are case-insensitive, unique within a Service, and limited to letters, digits, and underscores. |
| VAR-002 | MUST | A slide refers to a Variable with a token such as `{{preacher}}`. The same token works in any Show, and it resolves against the Service currently being presented. |
| VAR-003 | MUST | When a Show is viewed outside a Service (for example in the Catalog editor), tokens display as their names (for example "{{preacher}}") in a marker style. |
| VAR-004 | MUST | A Service Template defines Variable names with optional default values. New Services copy them. |
| VAR-005 | MUST | A token whose Variable is undefined or empty renders as empty text on the Output, and the pre-service check flags it (CHK-001). |
| VAR-006 | MUST | Formatting applied to a token range applies to its resolved value. |
| VAR-007 | SHOULD | The Service editor shows one form listing every Variable used by any slide in the Service, so all values can be filled in one place. |
| VAR-008 | MUST NOT | There are no global (Library-wide) Variables. CCLI license number and Show metadata tokens are separate (CCLI-003) and aren't Variables. |

---

## 10. Services, Service Templates, and Placeholders (SVC)

### 10.1 Services

| ID | Priority | Requirement |
|---|---|---|
| SVC-001 | MUST | A Service has a name, a date, an ordered list of Service Items, Variables, Timers, service-level Actions, and a Mode (Rehearse by default). |
| SVC-002 | MUST | Service Items can be added (from the Catalog via search, as a Placeholder, or as a Scripture Item), removed, and reordered. |
| SVC-003 | MUST | Services can be created blank, created from a Service Template, duplicated, renamed, and deleted. |
| SVC-004 | MUST | The Service list sorts by date, newest first. |
| SVC-005 | SHOULD | A "Save as Service Template" action turns a Service into a Service Template. Show Copies become Show References to their catalog Show. Filled Placeholders that remember their label and filter (SVC-033) become Placeholders again. Scripture Items become Scripture Item stubs (SVC-030). |

### 10.2 References, copies, and skipped slides

| ID | Priority | Requirement |
|---|---|---|
| SVC-020 | MUST | Adding a catalog Show to a Service creates a Show Reference. The Service displays the catalog Show's current content. |
| SVC-021 | MUST | A Show Reference may skip slides: the user toggles individual slides off for this Service only. Skipped slides aren't shown in presentation. The catalog Show is unchanged. This covers singing 4 of 8 verses. The user can also un-skip any slide, including one the catalog marks as Skipped, and skip others instead. Changes apply only to this Service. |
| SVC-022 | MUST (ASSUMPTION for prompt wording) | When the user edits slide content of a Show Reference from inside a Service: for **Presentations**, the item is automatically forked into a Show Copy and the edit applies only to this Service. For **Songs**, prompt "Edit the catalog song (changes every Service)" or "Make a copy for this Service only", with catalog edit as the default. |
| SVC-023 | MUST | A forked item is clearly marked as "Modified for this Service" in the Service editor. |
| SVC-024 | SHOULD | A Show Copy offers "Revert to catalog version", which discards the copy and restores the reference. |
| SVC-025 | MUST | Skipping slides is disabled for a Show that has an Audio Track with Cues. The UI explains why (audio cues require the full sequence). The same rule applies to catalog Skipped marks (SHOW-008). If Cues are added after a Service already skips slides of that Show, the Service's skip list is ignored during presentation (all slides are displayable). |
| SVC-026 | MUST | If a referenced Show is deleted from the Catalog, the Service Item shows as "Missing show" and the pre-service check flags it. The Service still opens. |
| SVC-027 | MUST | When a catalog Show is added to a Service (directly, or by filling a Placeholder), the Show's catalog Skipped marks (SHOW-008) are **copied** into the Service Item's skip list. From then on the Service Item is independent: later changes to the catalog marks don't affect it. Example: verses 5 and 6 are Skipped in the catalog. A new Service starts with 5 and 6 skipped. In that Service the user un-skips them and skips 3 and 4. The catalog is untouched. |
| SVC-028 | SHOULD | The Service editor offers "Reset to catalog defaults" on a Show Reference, which replaces its skip list with the current catalog marks. |

### 10.3 Service Templates and Placeholders

| ID | Priority | Requirement |
|---|---|---|
| SVC-030 | MUST | A Service Template contains an ordered list of items (Show References, Placeholders, Scripture Item stubs), Variable definitions with defaults, Timer definitions, and service-level Actions. A Scripture Item stub keeps the Bible and the settings of SCR-007 with an empty reference, to be filled in each week. |
| SVC-031 | MUST | A Placeholder has a label (for example "Opening song") and an optional Category filter and/or Kind filter. Filling it opens a search list restricted to matching Shows. The chosen Show replaces the Placeholder as a Show Reference in the same position. |
| SVC-032 | MUST | A Placeholder keyed to a Category supports the rotating-liturgy case: a Category such as "Communion liturgy" holds several Shows, and each week the user picks one. |
| SVC-033 | SHOULD | A filled Placeholder remembers its original label and filter, so the item can be cleared back to a Placeholder and re-filled. |
| SVC-034 | MUST | Unfilled Placeholders are skipped during presentation (LIVE-006) and flagged by the pre-service check. |
| SVC-035 | MUST | Changes to a Service Template don't change Services already created from it. |

---

## 11. Audio and slide sync (AUD)

| ID | Priority | Requirement |
|---|---|---|
| AUD-001 | MUST | A Show may have one Audio Track. MP3 MUST be supported. Other formats (WAV, others) are supported only if the chosen audio library handles them with no extra work. NAudio is the suggested library. |
| AUD-002 | MUST | Audio plays to the Windows default output device. There's no in-app volume control, device selection, fade, or crossfade. |
| AUD-003 | MUST | Trim: an optional Trim Start and Trim End per Audio Track. Playback starts at Trim Start and stops at Trim End (or at the end of the file). |
| AUD-004 | MUST | Cues: each Cue pairs a Slide with a position in the audio. When playback reaches a Cue's position, that Slide is displayed. |
| AUD-005 | MUST | Cue positions are stored as **absolute file time**, not relative to Trim Start, so changing the trim doesn't shift the cues. (This changes an earlier conversational suggestion. Absolute time is more robust.) |
| AUD-006 | MUST | Cues go to a **specific slide**, not "next slide". If the operator advances manually by mistake, the next Cue still lands on the correct slide (it self-corrects). |
| AUD-007 | MUST | The first slide of a Show is displayed when the Show starts. Typically it has no Cue. Each later slide may have a Cue. |
| AUD-008 | MUST | Cue editor, tap mode: play the audio and press a key (for example Space) to record the next slide's Cue at the current position, in slide order. |
| AUD-009 | MUST | Cue editor, timeline: a horizontal timeline of the trimmed audio with one draggable marker per Cue, labeled with the slide label. Trim Start and Trim End are also draggable markers. |
| AUD-010 | MUST | Cue editor, seek and test: click anywhere on the timeline to play from that point. The slide preview follows the Cues. Typical use is jumping a few seconds before a Cue to check it. |
| AUD-011 | MUST | Cue editor: the current playback position shows with at least 0.1 s precision. Cue positions can be nudged by fixed increments (for example ±0.1 s, ±0.5 s) and typed in directly. |
| AUD-012 | COULD | Waveform display on the timeline. |
| AUD-013 | MUST | Live audio controls in the operator UI: Restart (play from Trim Start and display the first slide of the item the audio belongs to, going back to that item if the operator has moved on), and Stop. No live seeking is required. The operator UI always shows which Show's audio is playing, with a Stop control, even when the current item is a different one. |
| AUD-014 | MUST | Manual navigation between slides of the same Show never affects audio playback (DEC-013). An operator-initiated change to a different Service Item stops audio (DEC-020). Each command carries an origin (Operator or Automation) so this can be decided in one place. |
| AUD-015 | MUST | Only one Audio Track plays at a time. Starting audio stops any other playing audio. |
| AUD-016 | MUST | A "Start audio" command for audio that is already playing does nothing (doesn't restart). "Already playing" means the same Show's Audio Track is playing. Another Show that uses the same file (AUD-019) starts it again from its own Trim Start. This handles navigating back onto a slide whose display action starts audio. |
| AUD-017 | MUST | Cue-driven slide changes happen only while the Show owning the playing audio is the current Service Item. If an Action or Cue moves to another item, the audio keeps playing but its Cues don't change the slide (an operator-initiated move stops audio instead, DEC-020). |
| AUD-018 | MUST | Audio position accuracy: a Cue fires within 50 ms of its position during normal playback. |
| AUD-019 | SHOULD | Audio Tracks are referenced by path so the same file can be used by more than one Show. |
| AUD-020 | MUST | The audio engine is behind an interface, so a future video player can share the same clock and cue model (FUT-002). |
| AUD-021 | MUST | `StartAudio` starts audio only while the current slide is the **first displayable slide** of the Show that owns the Audio Track. Otherwise it does nothing. So entering a song at its last slide (Previous crossing back, LIVE-005) or by clicking a slide in the middle (LIVE-004) doesn't start the audio. Restart (AUD-013) displays the first slide, so it always plays. |

---

## 12. Timers (TMR)

| ID | Priority | Requirement |
|---|---|---|
| TMR-001 | MUST | A Timer is a named object defined on a Service (or a Service Template, from which it is copied). Timer names follow the Variable rules (VAR-001): case-insensitive, unique within a Service, letters, digits, and underscores only. |
| TMR-002 | MUST | Timer types: **Countdown to clock time** (target time of day, such as 10:00) and **Countdown duration** (for example 5:00). |
| TMR-003 | MUST | Timer states: Stopped, Running, Expired. |
| TMR-004 | MUST | Once started, a Timer keeps running regardless of which slide is displayed or whether it's visible. Several slides showing the same Timer show the same value. |
| TMR-005 | MUST | End behavior per Timer: **Hold at zero**, or **Continue negative** (displayed with a leading minus, for example `-1:23`). |
| TMR-006 | MUST | Warning: one threshold in seconds and one warning color per Timer. When remaining time is at or below the threshold, the Timer's text shows in the warning color. Above the threshold it uses the text's own formatting. Only one threshold is supported. A Timer with no warning color doesn't change color. New Timers created in the UI default to red. |
| TMR-007 | MUST | When a Running Timer reaches zero, it changes to Expired and fires its `TimerExpired` trigger **exactly once** per start, whatever the end behavior. |
| TMR-008 | MUST | If a Countdown-to-clock-time Timer is started after its target time has already passed today, it goes straight to Expired, shows zero (or negative, under Continue negative), and **doesn't** fire its trigger. |
| TMR-009 | MUST | A slide shows a Timer with a token such as `{{timer:ServiceStart}}` inside a Text Box. The default display format drops leading zero parts: `h:mm:ss` while there are hours (`1:05:09`), then `mm:ss` (`12:00`), `m:ss` below ten minutes (`9:59`), and `:ss` below one minute (`:59`). A negative value keeps the same form after its minus sign (TMR-005). As the leading parts drop away, the remaining digits keep their places on the slide (TMR-010). The token stands alone in its Text Box. It isn't embedded in running text. The editor only inserts Timer tokens on their own. A Timer token found next to other text (for example from a paste or an import) is reported as a problem and flagged by the pre-service check. |
| TMR-010 | MUST | Timer display is smooth: digits use fixed-width (tabular) figures, or the timer run is laid out at a fixed width, so the text around it doesn't shift. Only the timer's text is updated each tick. The slide isn't re-rendered, and transitions don't apply to timer ticks. |
| TMR-011 | MUST | Timers tick on whole seconds, tied to the system clock (not an accumulating interval timer), so they don't drift. |
| TMR-012 | MUST | Commands: Start Timer (name) and Stop Timer (name). Stop returns the Timer to Stopped and resets it. Starting a Running Timer does nothing. Starting an Expired Timer restarts it, the same as Stop followed by Start (TMR-008 still applies to clock-time Timers). |
| TMR-013 | MUST | `AutoStartOnDisplay` per Timer (default on): displaying a slide that shows a Stopped Timer starts it. |
| TMR-014 | MUST | A Stopped Timer shows its full duration (duration type) or the live remaining time to target (clock type). |
| TMR-015 | COULD | Display format options other than the TMR-009 default (for example always `h:mm:ss`, or always `mm:ss`). |
| TMR-016 | MUST | Timers behave the same in Rehearse and Live modes. |
| TMR-017 | MUST | Timer state is not saved. Stopping presentation resets every Timer to Stopped. Reopening a Service, or restoring after a crash (SAFE-005), starts with all Timers Stopped. Clock-time Timers show the correct remaining time anyway (TMR-014). |

Reference scenario (must work end to end): a Service-start countdown to 10:00. Announcement slides loop with auto-advance, all showing `{{timer:ServiceStart}}`. Text turns red in the last 60 seconds. At expiry, the presentation jumps to the first Service Item after the announcements.

---

## 13. Actions (ACT)

### 13.1 Model

| ID | Priority | Requirement |
|---|---|---|
| ACT-001 | MUST | An Action is a Trigger, an optional Delay (default 0), and an ordered list of one or more Commands, run in order. |
| ACT-002 | MUST | Actions can be attached to: a Slide, a Show, a Service Item (owned by the Service, not by the catalog Show), a Service, and a Service Template (copied to new Services). When a Service is created from a Service Template or duplicated, every Action that names a Service Item (at Service, Service Item, or any other level) is remapped to the new item IDs. |
| ACT-003 | MUST | Actions run identically in Rehearse and Live modes. |
| ACT-004 | MUST | A delayed Action is cancelled if, before the delay ends, the slide changes (for slide-scoped triggers: `SlideDisplayed`, `SlideEnded`, and `TimerExpired` on a Slide) or the item changes (for show-scoped triggers: `ShowStarted`, `ShowEnded`). Delayed Service-level Actions are not cancelled by navigation. Stopping presentation cancels every pending Action. |
| ACT-005 | MUST | Order of events when the current slide changes. When the item changes too, the steps run in this order: (1) if the operator caused an item change, stop audio (DEC-020); (2) `SlideEnded` for the old slide; (3) `ShowEnded` for the old item; (4) the new slide is displayed; (5) `ShowStarted` for the new item; (6) `SlideDisplayed` for the new slide. Steps 3 and 5 are left out when the item doesn't change. Each step finishes its zero-delay Actions before the next step starts. |
| ACT-006 | MUST | When one event fires Actions at several levels, they run in the order Service, Service Item, Show, Slide. Within one level, they run in the order they are listed. |

### 13.2 Triggers

| ID | Priority | Trigger | Fires when | Valid on |
|---|---|---|---|---|
| ACT-010 | MUST | `ServiceStarted` | Presentation of a Service begins (the user starts presenting the Service). | Service |
| ACT-011 | MUST | `ShowStarted` | The current item changes to this Show (entered from any direction). | Show, Service Item |
| ACT-012 | MUST | `ShowEnded` | The current item changes away from this Show. | Show, Service Item |
| ACT-013 | MUST | `SlideDisplayed` | This slide becomes the current slide (by any means: manual, cue, or action). Clicking or going to the slide that is already current doesn't fire it again. On a Service Item it fires for every slide of that item. | Slide, Service Item |
| ACT-014 | MUST | `SlideEnded` | This slide stops being the current slide. On a Service Item it fires for every slide of that item. | Slide, Service Item |
| ACT-015 | MUST | `TimerExpired(name)` | The named Timer expires (TMR-007). On a Slide, it fires only if that slide is current. On a Service, it fires regardless of the current slide. | Slide, Service |
| ACT-016 | SHOULD | `AudioEnded` | The Show's Audio Track reaches Trim End or the end of the file. It doesn't fire when audio is stopped for any other reason (Stop, `StopAudio`, another track starting, or DEC-020). | Show |

### 13.3 Commands

| ID | Priority | Command | Behavior |
|---|---|---|---|
| ACT-020 | MUST | `NextSlide` | Same as the operator pressing Next (crosses item boundaries, LIVE-005). |
| ACT-021 | MUST | `PreviousSlide` | Same as the operator pressing Previous. |
| ACT-022 | MUST | `GoToSlide(itemId, slideId)` | Display a specific slide in the current Service. The target may also be given as a slide within the current Show (slideId only), so Actions on catalog Shows stay valid in any Service. |
| ACT-023 | MUST | `GoToItem(itemId)` | Display the first slide of a specific Service Item. Used for loops and timer-expiry jumps. |
| ACT-024 | MUST | `StartAudio` | Start the current Show's Audio Track from Trim Start (AUD-015, AUD-016). |
| ACT-025 | MUST | `StopAudio` | Stop any playing audio. |
| ACT-026 | MUST | `Clear` | Sets the Output to Clear (LIVE-011). Unlike the operator control it isn't a toggle: if Clear is already on, nothing changes. |
| ACT-027 | MUST | `Black` | Sets the Output to Black (LIVE-010). Not a toggle. |
| ACT-028 | MUST | `Logo` | Sets the Output to Logo (LIVE-012). Not a toggle. |
| ACT-029 | MUST | `StartTimer(name)` | See TMR-012. |
| ACT-030 | MUST | `StopTimer(name)` | See TMR-012. |
| ACT-031 | MUST | `NextItem` | Display the first displayable slide of the next Service Item. |
| ACT-032 | MUST | `PreviousItem` | Display the first displayable slide of the previous Service Item. |
| ACT-033 | MUST | `Normal` | Turns off Black, Clear, or Logo, so the current slide shows. Does nothing if none is on. |

Auto-advance is expressed as `SlideDisplayed` + Delay N seconds + `NextSlide`. The slide editor SHOULD offer a one-click "Auto-advance after N seconds" shortcut that creates this Action.

### 13.4 Safety and defaults

| ID | Priority | Requirement |
|---|---|---|
| ACT-040 | MUST | Loop guard: if Actions cause more than 20 slide changes in under 1 second with no user input, pause Actions for the current Service Item, keep the current slide, and show a non-blocking warning ("Automation paused for this item: Actions changed slides too quickly"). Actions work again as soon as the current item changes to a different item. Normal delayed loops (for example auto-advance every 8 seconds) are unaffected. |
| ACT-041 | MUST | References to a missing slide, item, or Timer do nothing and are flagged by the pre-service check. |
| ACT-042 | MUST (ASSUMPTION) | When an Audio Track is attached to a Show, the editor offers to add `ShowStarted → StartAudio` by default. The user can remove it. |
| ACT-043 | SHOULD | The operator UI shows a small indicator when a delayed Action is pending (for example "Auto-advance in 5 s"). |
| ACT-044 | MUST NOT | No global "disable automation" switch for now. |

---

## 14. Rehearse/Live mode and CCLI (MODE, CCLI)

### 14.1 Mode

| ID | Priority | Requirement |
|---|---|---|
| MODE-001 | MUST | Each Service has a Mode: Rehearse (default) or Live. It is saved in the Service state file (LIB-013). |
| MODE-002 | MUST | Mode can be switched at any time, including mid-presentation. Switching doesn't change the Output, the current slide, Timers, audio, or any state. |
| MODE-003 | MUST | The only effect of Mode is CCLI logging when the Service is closed (CCLI-010). |
| MODE-004 | MUST | The current Mode is shown prominently in the operator UI, and read-only on the phone remote. |
| MODE-005 | MUST | Presenting starts only with **Go Live** or **Rehearse** (DEC-021). Each sets the Service's Mode to match and starts presenting. While presenting, the operator can still switch Mode (MODE-002). |

### 14.2 Metadata and display

| ID | Priority | Requirement |
|---|---|---|
| CCLI-001 | MUST | Library setting: church CCLI license number. |
| CCLI-002 | MUST | Show metadata as in SHOW-003. A Show is "reportable" if Is Public Domain is false and CCLI Song Number is not empty. |
| CCLI-003 | MUST | Metadata tokens available in any Text Box: `{{show.title}}`, `{{show.authors}}`, `{{show.copyright}}`, `{{show.ccli}}`, `{{church.ccliLicense}}`. |
| CCLI-004 | MUST | If the resolved Template has a copyright box, the copyright text is rendered in it on the Show's first or last slide according to Copyright Placement (SHOW-004). Its content comes from a Library-level copyright format string built from metadata tokens (default: `{{show.title}} — {{show.authors}} · © {{show.copyright}} · Used by permission · CCLI License #{{church.ccliLicense}}`). "First" and "last" mean the first and last slide actually **displayed** in the Service: slides skipped in the Service are ignored. Metadata that is empty renders as empty text, without clean-up of the surrounding punctuation. |
| CCLI-005 | MUST | The copyright box isn't rendered for public-domain Shows, or when Copyright Placement is None. |

### 14.3 Usage logging

| ID | Priority | Requirement |
|---|---|---|
| CCLI-010 | MUST | When a Service whose Mode is Live is closed (by closing the Service, opening another Service, or exiting the app), show a short summary listing the reportable Shows in it and the Service date, with one **OK** button (and Cancel, which skips logging this time). On OK, write the log. Cancel doesn't stop the close or the exit. |
| CCLI-011 | MUST | Every reportable Show in the Service is logged, whether displayed or not (DEC-011). Unfilled Placeholders are ignored. Show Copies are logged using their own metadata. |
| CCLI-012 | MUST | The log date is the Service's date, not the date of closing. |
| CCLI-013 | MUST | Each Service writes its own log file (`logs/ccli/{serviceId}.json`). Logging a Service again **replaces** its file, so reopening and closing doesn't double-count, and song swaps update the log. |
| CCLI-014 | MUST | Log entries store a snapshot: service ID, service name, service date, show ID, title, authors, copyright, CCLI song number, and logged-at timestamp. Later Show edits or deletions don't alter past entries. |
| CCLI-015 | MUST | Deleting a Service doesn't delete its log file. |
| CCLI-016 | MUST | Safety net: when a Service in Rehearse mode is closed, its date is today or earlier, it has reportable Shows, and it has no log file, ask once: "Log this as a live service?" If the user answers Yes, set the Mode to Live and run CCLI-010. If No, don't ask again for this Service (the answer is kept in the Service state file, LIB-013). |
| CCLI-017 | SHOULD | A Usage Log viewer lists entries by date. Entries can be deleted, and a Service's log can be regenerated from the Service if it still exists. |
| CCLI-018 | MUST | Crash safety net: if the app ended without closing a Live Service (a crash or power loss, known from the machine's last-session record, SAFE-005), the next startup runs CCLI-010 for that Service, if it still exists and has no log file. |

### 14.4 Reporting and reminders

| ID | Priority | Requirement |
|---|---|---|
| CCLI-020 | MUST | Export a CSV for a chosen date range, with one row per CCLI song number, columns `CCLI Song Number`, `Title`, `Authors`, `Uses`. Uses counts distinct Services, not slides. |
| CCLI-021 | SHOULD | Make the CSV columns match CCLI's current manual-reporting spreadsheet template. The template hasn't been reviewed yet. See Q-005. |
| CCLI-022 | SHOULD | Library setting: the current CCLI reporting period (start date, end date), entered from CCLI's notice. |
| CCLI-023 | SHOULD | During the reporting period, show a non-blocking banner at the start of the period and again 14 days before it ends, with the count of logged uses in the period and a shortcut to export. |
| CCLI-024 | MUST NOT | No write-in song support (every song either has a CCLI number or is public domain). No automatic submission to CCLI. |

---

## 15. Presentation and operator controls (LIVE)

### 15.1 Output and preview

| ID | Priority | Requirement |
|---|---|---|
| LIVE-001 | MUST | The Output is a borderless full-screen window on a display chosen in local settings. The choice is remembered per machine. |
| LIVE-002 | MUST | If the chosen display isn't connected, fall back to a resizable window on the primary display and show a notice. |
| LIVE-003 | MUST | The operator UI shows a Preview of the **current** slide (exactly as on the Output, including Black/Logo/Clear state) and the **next** slide. |
| LIVE-004 | MUST | The operator UI shows the entire Service as one scrollable list of items and their slides (labels and thumbnails or text), with the current slide highlighted. While presenting, clicking any slide displays it immediately. While not presenting, clicking a slide only selects it (DEC-021). |

### 15.2 Navigation flow

| ID | Priority | Requirement |
|---|---|---|
| LIVE-005 | MUST | The Service plays as one continuous sequence. **Next on the last slide of an item goes to the first slide of the next item. Previous on the first slide of an item goes to the last slide of the previous item.** No extra clicks. This is a key requirement (FreeShow pain point). |
| LIVE-006 | MUST | Skipped slides (SVC-021), unfilled Placeholders, and missing Shows are passed over during navigation. So is an item with no displayable slide (all its slides are skipped, or its Show has none). Clicking the header of such an item does nothing. |
| LIVE-007 | MUST | At the last slide of the Service, Next does nothing (no wrap-around). Loops happen only through `GoToItem` / `GoToSlide` Actions. |
| LIVE-008 | MUST | The "next slide" shown in the Preview and on the remote follows the same rules (crosses items, honors skips). It does **not** predict Action-driven jumps. |
| LIVE-009 | MUST | Presentation starts only with Go Live or Rehearse (MODE-005). Starting fires `ServiceStarted`, then displays the selected slide, or the first displayable slide of the Service if none is selected. |
| LIVE-018 | MUST | While not presenting (before Go Live or Rehearse, and after Stop presenting), the Output window stays open and shows black. |
| LIVE-016 | MUST | Item-level navigation: **Next item** and **Previous item** go to the first displayable slide of the adjacent item (skipping unfilled Placeholders and missing Shows). Clicking an item header does the same for that item. Items and slides remain two visible levels in the operator UI (LIVE-004). The continuous Next/Previous of LIVE-005 is a convenience for moving through slides, not a flattening of the item level. The current position is always an item plus a slide. |
| LIVE-017 | MUST | Skipped slides appear greyed with a **SKIPPED** badge in the operator UI. Clicking a Skipped slide still displays it (LIVE-004). Next and Previous from it move to the nearest non-skipped slide (LIVE-006). |

### 15.3 Screen controls

| ID | Priority | Requirement |
|---|---|---|
| LIVE-010 | MUST | **Black**: the Output shows solid black. |
| LIVE-011 | MUST | **Clear**: the Output hides all text, including Timers and the copyright box, and keeps the current background. |
| LIVE-012 | MUST | **Logo**: the Output shows the Library's logo image (a Library setting) on black. If no logo is set, it shows black. |
| LIVE-013 | MUST | Black, Clear, and Logo are toggles and are mutually exclusive (turning one on turns the others off). The operator UI shows which is active. |
| LIVE-014 | MUST | Navigating while Black, Clear, or Logo is active updates the current slide but keeps the screen state, until the operator toggles it off (or an Action runs `Normal`, ACT-033). Resolves Q-004. |
| LIVE-015 | MUST NOT | No Freeze function. |

### 15.4 Keyboard and clickers

| ID | Priority | Requirement |
|---|---|---|
| LIVE-020 | MUST | Default hotkeys while presenting: Next = Right Arrow, Down Arrow, Page Down, Space. Previous = Left Arrow, Up Arrow, Page Up. Black = B and `.` (period). Logo = L. Clear = Esc or C. |
| LIVE-021 | MUST | Hotkeys work whether the operator window or the Output window has focus. They're inactive while typing in a text field. |
| LIVE-022 | MUST | Presentation clickers that send Page Up, Page Down, `.`, or B work with no setup. |
| LIVE-023 | COULD | Configurable hotkeys. |

### 15.5 Transitions

| ID | Priority | Requirement |
|---|---|---|
| LIVE-030 | MUST | One global transition setting: **Cut** or **Crossfade**, with a crossfade duration (default 300 ms, range 100–1000 ms). |
| LIVE-031 | MUST | The crossfade must run smoothly on integrated graphics (verified in PLT-008). If it doesn't, ship Cut only and leave the setting hidden. |
| LIVE-032 | MUST | Transitions apply to slide changes and to Black, Clear, and Logo toggles. They don't apply to Timer ticks or Variable updates. |
| LIVE-033 | MUST NOT | No per-slide or per-Show transitions, and no other transition types. |

### 15.6 Live editing

| ID | Priority | Requirement |
|---|---|---|
| LIVE-040 | SHOULD | Edits saved during presentation (for example fixing a typo) appear on the Output the next time that slide is displayed. If the edited slide is current, it refreshes after the save. The refresh doesn't fire `SlideDisplayed`. |
| LIVE-041 | MUST | If the current slide or item is removed while presenting, the Output keeps showing what it shows. Next goes to the slide that followed the removed one, and Previous to the one that preceded it. |

---

## 16. Phone remote (REM)

| ID | Priority | Requirement |
|---|---|---|
| REM-001 | SHOULD | The app hosts a small embedded web server (for example ASP.NET Core Kestrel) serving one mobile-friendly web page. No app store install is required. |
| REM-002 | SHOULD | The remote is off by default and enabled in local settings. The port can be configured. |
| REM-003 | SHOULD | Access requires a random token. The operator UI shows the remote URL with the token, and a QR code to scan. Settings include "Regenerate token", which invalidates old links. |
| REM-004 | SHOULD | The server only accepts connections from the local network (private IP ranges). It doesn't need internet exposure. |
| REM-005 | SHOULD | Controls: Next, Previous, Black, Logo, Clear. These behave exactly like the operator controls. |
| REM-006 | SHOULD | Display: current item title, current slide label and text, next slide label and text (text only, no images), Mode indicator (read-only, DEC-019), and the active screen state (Black, Clear, Logo). |
| REM-007 | SHOULD | State updates are pushed to the phone (WebSocket or server-sent events) within 300 ms of a change. |
| REM-008 | SHOULD | Large touch targets usable one-handed. |
| REM-009 | MUST NOT | The remote can't edit content, change Mode, or open Services. |
| REM-010 | SHOULD | The first time the remote is enabled, the app explains that Windows may ask for firewall permission. If possible, it detects and reports when the port is blocked. |

---

## 17. Scripture (SCR)

| ID | Priority | Requirement |
|---|---|---|
| SCR-001 | MUST | Import Bibles from **Zefania XML** files (root `XMLBIBLE` with `biblename`; `BIBLEBOOK` with `bnumber`, `bname`, optional `bsname`; `CHAPTER` with `cnumber`; `VERS` with `vnumber` and text). The original XML is copied to `Library/bibles/`. |
| SCR-002 | MUST | Parse with `System.Xml.Linq` (no third-party Bible library). Trim leading and trailing whitespace from verse text. Ignore unknown elements and attributes (some Zefania files carry notes or formatting markup), keeping only plain verse text. |
| SCR-003 | MUST | Books are keyed by `bnumber` (1–66). Display names come from `bname`. |
| SCR-004 | MUST | Each imported Bible has a display name (default from `biblename`) and a short abbreviation (for example "ESV", "NKJV"), set by the user. |
| SCR-005 | MUST | A parsed index per Bible is cached locally (LIB-008) for fast lookup, and rebuilt if missing or stale. |
| SCR-006 | MUST | Reference parser: accepts common forms such as `John 3:16`, `Jn 3:16-18`, `John 3:16-4:2`, `Ps 23`, `1 Cor 13:4-7`, and comma-separated parts (`Rom 8:28,31-39`). The app supplies its own list of book abbreviations, since `bsname` is optional. Invalid references give a clear error. |
| SCR-007 | MUST | A Scripture Item stores: Bible, reference, verses per slide, show verse numbers (on/off), show reference line (on/off), and an optional Template. |
| SCR-008 | MUST | During Service prep, a Scripture Item **generates ordinary slides**, local to the Service, using the chosen or default Scripture Template. The generated Show is in the Scripture Category (CAT-010) and uses the Item's chosen Template as its default Template, so normal Template resolution (TPL-010) applies. There is no live Scripture lookup. |
| SCR-009 | MUST | Verse numbers, when enabled, render inline (for example as superscript or a smaller run) from `vnumber`. |
| SCR-010 | MUST | The reference line, when enabled, renders in a Template Box named for it (for example `reference`). It shows the verses on that slide plus the translation abbreviation, such as `John 3:16–17 (ESV)`. ASSUMPTION: per-slide range rather than the whole passage. |
| SCR-011 | MUST | Generated slides can be edited like any slide (for example to rebalance a long verse). |
| SCR-012 | MUST | "Regenerate" rebuilds the slides from the stored reference and settings, with a warning that manual edits will be lost. |
| SCR-013 | MUST | The default Scripture Template is the default Template of the Scripture Category (CAT-010). There is no separate Library setting for it. |
| SCR-014 | COULD | Split by maximum characters per slide, in addition to verses per slide. |
| SCR-015 | COULD | Show the same passage in two translations. |
| SCR-016 | MUST NOT | No copyright notice is required on Scripture slides. |

---

## 18. Pre-service check (CHK)

| ID | Priority | Requirement |
|---|---|---|
| CHK-001 | MUST | A "Check Service" function lists warnings for the open Service: unfilled Placeholders; missing Shows; Variables referenced but empty or undefined; missing image or audio files; reportable Songs missing a copyright notice or authors; Songs with no CCLI number that aren't marked public domain; text overflow (SLD-028); Template box mismatches (TPL-027); Actions referring to missing slides, items, or Timers (ACT-041); Audio Tracks with Cues pointing to deleted slides; Timer tokens naming an undefined Timer, or not standing alone in their Text Box (TMR-009); Scripture Items whose Bible is missing or whose reference is empty; Actions with no delay that change slides in a cycle (which would trip the loop guard, ACT-040); Library problems (LIB-009); the chosen output display not being connected (LIVE-002). |
| CHK-002 | MUST | The check is advisory only (DEC-012). It never blocks anything. Each warning can be dismissed for this Service. |
| CHK-003 | SHOULD | Clicking a warning opens the item or slide involved. |
| CHK-004 | SHOULD | The check runs automatically when the user starts presenting, and shows a dismissible summary if there are warnings. |

---

## 19. Editing safety, autosave, undo, and recovery (SAFE)

| ID | Priority | Requirement |
|---|---|---|
| SAFE-001 | MUST | Editing an existing object (Show, Service, Template, Service Template) works on a **draft**. The Library file isn't changed until the user commits (Save). Closing with unsaved changes prompts Save, Discard, or Cancel. |
| SAFE-002 | MUST | Drafts autosave to local storage (LIB-008), not into the Library, at least every 30 seconds and on focus loss. Synced files are never left half-edited. |
| SAFE-003 | MUST | Undo and redo within an editing session, at least 50 steps. |
| SAFE-004 | SHOULD | On startup after a crash, offer to restore any unsaved drafts. |
| SAFE-005 | SHOULD | On startup after a crash during presentation, offer to reopen the last Service at the last displayed slide. |
| SAFE-006 | MUST | Unhandled exceptions in editors, the remote server, or Action processing must not close the Output window or blank the projector. Log them to a local log file. |
| SAFE-007 | MUST NOT | No multi-version history or version browser. |

---

## 20. Import (IMP)

| ID | Priority | Requirement |
|---|---|---|
| IMP-001 | COULD | One-time batch import of FreeShow shows into the Catalog: slide text, slide labels, and metadata (title, authors, CCLI number) where present. FreeShow's file format must be inspected first. |
| IMP-002 | COULD | During FreeShow import, strip inline formatting by default so imported Songs follow Templates (TPL-024). Offer an option to keep formatting. |
| IMP-003 | COULD | One-time plain-text lyric import: paste text, split into slides at blank lines, use bracketed or first-line labels where present ("[Verse 1]"). The result is ordinary slides. There's no round-trip back to text (DEC-008). |
| IMP-004 | COULD | Import of audio, Cues, and Templates from FreeShow. Expected to be done manually instead. |

---

## 21. Future extensibility (FUT)

These aren't features to build now. They're constraints on the architecture.

| ID | Priority | Requirement |
|---|---|---|
| FUT-001 | MUST | Backgrounds and slide media are modeled as a media reference with a kind, so `Video` can be added later (SLD-009). |
| FUT-002 | MUST | The playback clock used for audio Cues is an abstraction that a video player could implement later (AUD-020). |
| FUT-003 | MUST | The renderer composes the output in layers (background, text, overlay or screen state), so a video layer can be added without restructuring. |
| FUT-004 | SHOULD | The command layer (Next, Previous, Black, and so on) is a single internal API used by the operator UI, hotkeys, Actions, and the remote alike. |

---

## 22. Out of scope

Don't build any of the following unless asked:

- Video playback, video backgrounds, or live camera input.
- Animations, per-element animations, and transition types other than Cut or Crossfade.
- Advanced slide editing (shapes, arbitrary images on slides, layers, grouping, alignment guides).
- Song Sections, groups, arrangements, or reorderable chorus repeats.
- A text view or round-trip plain-text editing of Songs.
- Multiple outputs, stage or confidence displays, NDI, MIDI, or OSC.
- Live Scripture lookup during presentation.
- Global Variables.
- Multi-stage Timer warnings.
- Audio volume control, fades, crossfades, device selection, or multiple simultaneous tracks.
- Service archiving or snapshot of past Services.
- Version history.
- Built-in cloud sync, user accounts, or multi-user permissions.
- CCLI write-in songs or automatic CCLI submission.
- Remote editing or remote mode switching.
- A Freeze function.
- Copyright notices on Scripture slides.

---

## 23. Open questions

Ask the product owner before implementing these.

| ID | Question | Current default |
|---|---|---|
| Q-001 | Text Box mismatch rule (TPL-027): is "render in the first box and flag it" acceptable? | Implement as written. |
| Q-002 | Song edit from a Service (SVC-022): is "prompt, defaulting to catalog edit" right, or should Song edits always be global? | Prompt. |
| Q-003 | RESOLVED by DEC-020: operator-initiated item changes stop audio. Actions and Cues don't. No default `ShowEnded → StopAudio`. | n/a |
| Q-004 | RESOLVED: Black, Clear, and Logo stay on through navigation until toggled off (LIVE-014). | n/a |
| Q-005 | Obtain CCLI's current manual-reporting spreadsheet template and confirm the CSV columns (CCLI-021). Confirm whether the church's license has a separate "true-up" obligation. | Columns as in CCLI-020. |
| Q-006 | Scripture reference line: per-slide verse range (default) or the whole passage on every slide? | Per-slide range. |
| Q-007 | Default Copyright Placement for Songs: first or last slide? | First slide. |
| Q-008 | RESOLVED by CCLI-002: any Show with a CCLI number that isn't public domain is reportable. Kind doesn't matter. | n/a |
| Q-009 | RESOLVED by DEC-021 and MODE-005: Go Live and Rehearse buttons start presenting and set the Mode. The rest of the mode indicator's look is UI design. | n/a |
| Q-010 | RESOLVED: the product name is ChurchSlides. | n/a |
| Q-011 | Phone remote slide text (REM-006): show Variables and metadata tokens resolved? And Timer tokens as the live value, or as the Timer's name? | Resolved values; Timers as their name. Ask before C8.1. |
| Q-012 | Changing the canvas aspect ratio (SLD-004): geometry is stored as fractions, so boxes already stretch with the canvas. What should "rescale" do: nothing beyond the warning, or keep boxes' shapes by adjusting their fractions? | Warning only. Ask before the settings chunk. |

---

## 24. Suggested build order (non-normative)

Each phase should produce something usable on the church machine.

1. **Spike (PLT-008):** output window, crossfade, rich text, MP3 position, timer smoothness, RAM figures.
2. **Core render and present:** Library loading and saving (LIB), Shows and Slides with rich text (SHOW, SLD), Output and Preview, continuous navigation (LIVE-001 to LIVE-022).
3. **Catalog and Services:** Categories, search, Services, Show References, skipped slides, forking (CAT, SVC-001 to SVC-026).
4. **Templates and Variables:** resolution order, binding and detaching (TPL, VAR).
5. **Service Templates and Placeholders** (SVC-030 to SVC-035).
6. **Audio and Cues** (AUD).
7. **Timers and Actions** (TMR, ACT).
8. **CCLI** (MODE, CCLI) and **pre-service check** (CHK).
9. **Scripture** (SCR).
10. **Phone remote** (REM).
11. **Safety polish** (SAFE-004, SAFE-005), transitions tuning, and optional import (IMP).

---

## 25. Acceptance scenarios

End-to-end scenarios the finished app must pass. Use them as integration tests.

**AS-01: Sunday build at home, present at church.** Create a Service from the "Sunday Morning" Service Template. Fill three song Placeholders through search, and fill the "Communion liturgy" Placeholder by choosing from its Category. Set the `preacher` and `sermon_title` Variables, and add a Scripture Item (ESV, `Rom 8:28-39`, 2 verses per slide). Save. On the church machine, after Drive syncs, open the Service. All content, media, and Variables appear correctly.

**AS-02: Continuous flow.** Pressing Next (keyboard, clicker, or phone) from the first slide to the last of the Service never needs any other control. It crosses every item boundary, passes over skipped slides and unfilled Placeholders, and stops at the end.

**AS-03: Audio-synced song.** A Song with an MP3, Trim Start at 2.4 s, and Cues for every slide plays and advances on its own. Pressing Next once by mistake mid-song doesn't affect the audio, and the next Cue displays the correct slide.

**AS-04: Pre-service loop with countdown.** Three announcement slides auto-advance every 8 seconds and loop with `GoToItem`. Each shows `{{timer:ServiceStart}}` (countdown to 10:00, warning red at 60 s). The countdown stays consistent across slide changes, updates without flicker, turns red at 9:59:00, and at 10:00:00 jumps to the welcome item.

**AS-05: Template change.** Changing the background and box position of the Category default Template for "Hymns" changes every hymn the next time it's displayed. Slides with detached Text Boxes keep their positions. Inline red text in a hymn stays red.

**AS-06: Per-Service edit.** Editing an announcement slide in this week's Service forks the Presentation for this Service only. The catalog version and next week's Service are unchanged.

**AS-07: CCLI.** Rehearse the full Service at home (no log written). At church, switch to Live mid-service. On closing, the summary lists the two CCLI songs (not the public-domain hymn), and OK writes one log file. Reopen and close again: still one log file and no duplicates. Export the CSV for the year: each song's Uses count is correct.

**AS-08: Edit conflict.** Open a Show on both machines, save on machine A, let Drive sync, then save on machine B. Machine B refuses to overwrite and offers Reload or Save as copy.

**AS-09: Resource limits.** On the church machine with AS-01's Service loaded and presenting, the working set stays within PLT-005 and crossfades stay smooth.

---

## 26. Change log

**0.2 (2026-11-23)**
- Product name set to ChurchSlides (Q-010).
- Added DEC-020 and amended DEC-013, AUD-014, AUD-017: audio stops on an operator-initiated change to a different Service Item, not on Actions or Cues (resolves Q-003). AUD-013: always-visible audio indicator with Stop.
- Added LIVE-016 and ACT-031/032: item-level navigation (Next item, Previous item, click an item header). Items and slides stay two visible levels.
- ACT-002/011/012/022: Actions may be attached to Service Items, and `GoToSlide` accepts a target within the current Show, so loops and jumps work from catalog Shows.
- TMR-009: a timer token stands alone in its Text Box.
- Section 0 point 6 now points to Section 23.

**0.3 (2026-11-23)**
- Skipping: catalog Skipped marks as a per-Service default (SHOW-008, SVC-027, SVC-028), clarified SVC-021 and SVC-025, and LIVE-017 (skipped slides display when clicked).
- C1.2a: Text Box keeps its Template Box name when detached (TPL-023, section 4 sketch). TPL-014 (resolution never fails, built-in fallback Template, case-insensitive box names). CCLI-004 clarified: first/last displayed slide, no punctuation clean-up. Slide gets `SkippedByDefault` in the section 4 sketch.
- C1.2b: a Show Copy keeps the item's skip list and remembered Placeholder. Forking keeps the Service Item ID and every Slide and Text Box ID, and gives the local Show a new Show ID. Duplicating a Service starts it in Rehearse mode. A catalog Skipped mark is ignored for Shows with audio cues.
- Document review (owner decisions of 2026-11-23):
  - Starting to present: DEC-021, MODE-005, LIVE-009 (Go Live / Rehearse buttons set the Mode; clicking a slide when not presenting only selects it). LIVE-018: Output is black when not presenting. Resolves Q-009.
  - Navigation: Q-004 resolved (LIVE-014). LIVE-006: items with no displayable slide are passed over. LIVE-041: current slide or item removed mid-presentation. LIVE-011: Clear hides all text. LIVE-012: no logo shows black.
  - Actions: ACT-005 order of events on a slide or item change, ACT-006 order across levels. ACT-013/014: `SlideDisplayed` and `SlideEnded` allowed on a Service Item, and re-clicking the current slide doesn't fire `SlideDisplayed`. ACT-026..028 set the screen state (not toggles), new ACT-033 `Normal`. ACT-004: cancellation of Service-level delays and on stop. ACT-016 clarified. ACT-040: the loop guard pauses Actions for the current item only. ACT-002: all item references remapped.
  - Audio: AUD-021 (`StartAudio` only at the Show's first displayable slide), AUD-013 Restart from another item, AUD-016 "already playing" means the same Show. SVC-025: a Service's skip list is ignored once the Show has Cues.
  - Timers: TMR-001 naming rules, TMR-012 starting an Expired Timer restarts it, TMR-017 Timer state isn't saved.
  - Storage: LIB-013 Service state file (Mode, check dismissals, CCLI-016 answer).
  - Scripture: CAT-010 built-in Scripture Category, which replaces the Library setting in SCR-013 (SCR-008 updated).
  - Services: SVC-005 and SVC-030 define Save as Service Template and Scripture Item stubs.
  - CCLI: CCLI-010 Cancel doesn't stop the close, CCLI-018 crash safety net. Q-008 resolved by CCLI-002.
  - CHK-001: more checks. "Placeholder" now means only the Service Item kind (VAR-003, LIB-011 reworded). New open questions Q-011 and Q-012.
- Plan review (2026-11-23): the Service state file moves to its own folder `service-state/` (LIB-013). TMR-009: Timer tokens not standing alone are reported. TMR-006: no warning color means no change, new Timers default to red.
- Owner, 2026-11-24 (during C0.4b): TMR-009's default Timer format drops leading zero parts (`1:05:09`, `12:00`, `9:59`, `:59`), with digits keeping their places. TMR-005 example and TMR-015 options updated to match.
