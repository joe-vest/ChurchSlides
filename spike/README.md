# ChurchSlides spike program

A throwaway test program for the church PC (FRD PLT-008, ARCHITECTURE section 12). It measures whether
the real app's design works on that machine. It is not part of the product and is deleted after G0.

C0.2 has the System (S8), Output (S1), Slides (S2), Memory (S6) and Startup (S7) tabs. C0.3 adds the
Rich text (S3) tab. C0.4 adds audio and timer tabs.

The Slides and Memory tabs need the church's backgrounds. Press **Choose media folder…** (top right)
and pick the folder with the background images (PNG, JPG). Put the church logo in the same folder,
named `logo.jpeg` (or `logo.png`): it's used for the Logo overlay, not as a background.

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
`--auto-s3` runs only the rich-text test (no second monitor or media needed).

## What to do

### Remotely (Chrome Remote Desktop is fine)

1. Choose the media folder (once; it's remembered). Press **Run automatic tests** and leave the
   computer alone for about 5 minutes. It records the System facts, opens a test pattern on the
   second monitor and checks its position, runs the memory test (S6: decode times, then a typical
   Service presented on the second monitor), then the slide test (S2: 60 Cuts and 120 crossfades on
   the second monitor), then the rich-text test (S3, on the Rich text tab), and saves the results. Remote access doesn't spoil these figures: frame
   timing is measured inside the program.
2. Startup figures (S7): every launch is recorded. Restart Windows, then launch in this order and close
   the program each time:
   - `r2r`, `r2r --kestrel`, `plain` (cold runs, straight after the restart),
   - the same three again (warm runs).

### In person (needs eyes)

On the **Output** tab press **Open test pattern** and look at the second monitor and the projector.
Answer the checks with Pass or Fail, and add a note where useful. The test pattern has a
1-pixel line on each edge: red top, green right, blue bottom, yellow left. Press **Close it** when
done.

On the **Slides** tab press **Open Output** and step through with Right and Left (or the buttons),
watching the second monitor and the projector:

- With Crossfade at 300 ms, try **Render thread** and then **UI thread**. The line under the
  buttons shows each transition's frames and dropped frames.
- Select only the 4:3 background, choose Fit, press Open Output; then Fill, then Stretch.
- Press B, C and L (Black, Clear, Logo) to show and hide each overlay.

On the **Startup** tab, say whether the startup times are acceptable.

## What to send back

The `spike-results-<date>.md` file from the `r2r` folder (and from `plain`, if you ran it there).
Press **Save results** first; the program also saves when it closes.

## What the figures mean

| Tab | Pass |
|---|---|
| System (S8) | The program runs, and WPF's render tier is 2 (hardware). The instruction sets show which CPU level .NET sees. |
| Output (S1) | The window rectangle equals the monitor's pixel rectangle. All four edge lines show, no taskbar, no flicker. |
| Slides (S2) | A 300 ms crossfade has on average under 1 dropped frame with pre-render where the product will run it, and looks smooth. A dropped frame is one more than 1.5 refresh intervals late. "Input to frame" is from the key press to the frame after the one showing the change (an upper bound), and must be under 100 ms (PLT-006). If UI-thread pre-render takes over about 10 ms, the product pre-renders on the render thread. |
| Memory (S6) | Working set at most 300 MB while presenting (PLT-005). Decode times show what a downscaled cache copy saves over the original. |
| Rich text (S3) | Round trips: every sample and random document unchanged. Editor / renderer: the same line breaks (equal "lines" and "breaks same"). The ink-pixel figure is the share of text pixels that differ: about 0-7% is edge smoothing only (the overlay looks identical); 20% or more means visibly different text. The "+N px" figures show how much wider the editor must be to break lines like the renderer. Keystroke to next frame under 50 ms and renderer under about 30 ms per slide (outline is a cost figure for SLD-025). |
| Startup (S7) | Recorded for the owner to judge. As a guide: warm 3 s or less, cold 10 s or less. The development PC's `r2r` build JIT-compiles about 840 methods (1,270 with Kestrel). Many more on the church PC means the precompiled code was rejected on its CPU. |

The status bar shows the program's memory and CPU, sampled every 250 ms. **Count frames** shows
frames per second, but it makes WPF draw every frame, so leave it off unless needed.
