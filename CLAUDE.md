# ChurchSlides

A Windows desktop app (.NET 10, WPF) for building and running church service slideshows. It must run well on old hardware with integrated graphics and limited RAM. No browser engine, no database, works fully offline.

## Read first

- `docs/STATUS.md`: current milestone, chunk checklist, known issues. Read this at the start of every session.
- `docs/DEVELOPMENT-PLAN.md`: the chunk list and the plan/execute/verify routine.
- `docs/plans/Cx.y.md`: the plan for the chunk you're working on. Stay inside it.
- `docs/ARCHITECTURE.md`: design. Read only the sections the plan names.
- `docs/FRD.md`: requirements. Look up only the requirement IDs the plan names (`docs/TRACEABILITY.md` maps IDs to chunks).

## Rules

1. **Never reverse FRD section 2 decisions** (DEC-xxx) or "improve" them. Ask first. Stay out of FRD section 22 (out of scope).
2. **Use glossary names** (FRD section 3) for classes, files and UI labels: Library, Catalog, Show, Slide, TextBox, Template, Service, ServiceItem, Variable, Timer, Action, Cue. No synonyms such as Project, Section, Group.
3. **Reference requirement IDs** (for example `AUD-017`) in tests, and in commit messages. Name tests so the ID is findable.
4. **Dependency rule** (enforced by `tests/ChurchSlides.Architecture.Tests`):
   `Domain` ← `Storage`, `Scripture` ← `Engine` ← `Audio`, `Rendering`, `Remote` ← `App`.
   `Domain`, `Storage`, `Scripture` and `Engine` must stay free of WPF, Windows-only APIs and ASP.NET.
5. **Headless first.** Logic lives in `Domain`/`Storage`/`Engine` and is tested test-first with `dotnet test`. WPF views stay thin. Behavior goes in view-models that are unit tested.
6. **Memory discipline.** No full-resolution images in memory during presentation. Bounded caches. No Electron/CEF/WebView2.
7. **Simplicity.** When two designs meet a requirement, pick the one with fewer settings and modes.
8. **Open questions:** if you reach an unresolved item (FRD section 23) or an ambiguity, stop and ask. Don't guess.
9. **Scope:** anything found but outside the current chunk goes in `docs/STATUS.md` under Follow-ups. Don't widen the diff.
10. All time access goes through `IClock`. No `DateTime.Now` in Engine or Domain logic.

## Commands

```powershell
dotnet build -warnaserror        # whole solution
dotnet test                      # all tests
./scripts/check.ps1              # build + test, as run before every commit
```

## Chunk routine (summary)

Plan (fresh session, write `docs/plans/Cx.y.md`) → Execute (fresh session, test-first) → Verify (`/verify`, `/simplify`, update `docs/TRACEABILITY.md` and `docs/STATUS.md`) → Commit with the chunk ID and requirement IDs in the message.

## Local data

Machine-local data lives in `%LOCALAPPDATA%\ChurchSlides\` (never in the Library). The Library is a user-chosen synced folder of plain JSON files. Never store absolute paths in Library files.
