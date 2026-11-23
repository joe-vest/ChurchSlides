# ChurchSlides spike program

A throwaway test program for the church PC (FRD PLT-008, ARCHITECTURE section 12). It measures whether
the real app's design works on that machine. It is not part of the product and is deleted after G0.

C0.2a has the System (S8), Output (S1) and Startup (S7) tabs. C0.2b adds slides and crossfade (S2) and
memory (S6). C0.3 and C0.4 add rich text, audio and timer tabs.

## Build (development PC)

```powershell
./spike/publish.ps1
```

This makes two self-contained folders, so the church PC needs no .NET install:

- `spike/out/r2r`: precompiled (ReadyToRun).
- `spike/out/plain`: compiled at startup (JIT only).

and a zip of each in `spike/out`.

## Copy and run (church PC)

1. Copy both zips to the church PC, for example to `Documents\Spike`.
2. Right-click each zip, Properties, tick **Unblock**, OK. Then Extract All. Keep the two folders apart
   (`Spike\r2r`, `Spike\plain`). The program writes its results next to itself, so don't put it in
   Program Files.
3. Run `ChurchSlides.Spike.exe` in the `r2r` folder.
4. For the Kestrel figures, make a shortcut to the same exe and add ` --kestrel` after the quotes in
   the shortcut's Target box.

`--auto` runs the automatic tests, saves the results and closes. It works with `--kestrel` too.

## What to do

### Remotely (Chrome Remote Desktop is fine)

1. Press **Run automatic tests**. It records the System facts, opens a test pattern on the second
   monitor for a moment and checks its position, and saves the results.
2. Startup figures (S7): every launch is recorded. Restart Windows, then launch in this order and close
   the program each time:
   - `r2r`, `r2r --kestrel`, `plain` (cold runs, straight after the restart),
   - the same three again (warm runs).

### In person (needs eyes)

On the **Output** tab press **Open test pattern** and look at the second monitor and the projector.
Answer the checks with Pass or Fail, and add a note where useful. The test pattern has a
1-pixel line on each edge: red top, green right, blue bottom, yellow left. Press **Close it** when
done. On the **Startup** tab, say whether the startup times are acceptable.

## What to send back

The `spike-results-<date>.md` file from the `r2r` folder (and from `plain`, if you ran it there).
Press **Save results** first; the program also saves when it closes.

## What the figures mean

| Tab | Pass |
|---|---|
| System (S8) | The program runs, and WPF's render tier is 2 (hardware). The instruction sets show which CPU level .NET sees. |
| Output (S1) | The window rectangle equals the monitor's pixel rectangle. All four edge lines show, no taskbar, no flicker. |
| Startup (S7) | Recorded for the owner to judge. As a guide: warm 3 s or less, cold 10 s or less. The development PC's `r2r` build JIT-compiles about 840 methods (1,270 with Kestrel). Many more on the church PC means the precompiled code was rejected on its CPU. |

The status bar shows the program's memory and CPU, sampled every 250 ms. **Count frames** shows
frames per second, but it makes WPF draw every frame, so leave it off unless needed.
