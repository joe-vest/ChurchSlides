# ChurchSlides spike program

A throwaway test program for the church PC (FRD PLT-008, ARCHITECTURE section 12). It measures whether
the real app's design works on that machine. It is not part of the product and is deleted after G0.

C0.2 has the System (S8), Output (S1), Slides (S2), Memory (S6) and Startup (S7) tabs. C0.3 adds the
Rich text (S3) tab. C0.4 adds the Audio (S4) and Timer (S5) tabs.

The Slides and Memory tabs need the church's backgrounds. Press **Choose media folder…** (top right)
and pick the folder with the background images (PNG, JPG). Put the church logo in the same folder,
named `logo.jpeg` (or `logo.png`): it's used for the Logo overlay, not as a background.

The Audio tab needs a few of the church's songs as MP3s (3 to 6 is plenty; the VBR ones matter most)
in the same media folder. No audio is copied into the program.

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
`--auto-s4` runs only the audio test and the Cue test (needs the MP3s and the backgrounds; the Cue
test uses the second monitor). `--auto-s5` runs only the timer test (12 minutes on the second monitor).

## What to do

### Remotely (Chrome Remote Desktop is fine)

1. Choose the media folder (once; it's remembered). Press **Run automatic tests** and leave the
   computer alone for about 25 minutes. It records the System facts, opens a test pattern on the
   second monitor and checks its position, runs the memory test (S6: decode times, then a typical
   Service presented on the second monitor), then the slide test (S2: 60 Cuts and 120 crossfades on
   the second monitor), then the rich-text test (S3, on the Rich text tab), then the audio test (S4,
   about 5 minutes), then the Cue test (S4, about 4 minutes: a song changes slides on the second
   monitor every 2 seconds), then the timer test (S5, 12 minutes on the second monitor), and saves
   the results. Remote access doesn't spoil these figures: frame
   timing is measured inside the program, and the audio is checked by recording what the program
   sends to the speakers (Windows "loopback"), not with a microphone.
   **Volume:** keep the speakers low but not muted. Muted, the recording may be silent and the
   audio figures say "loopback heard nothing". Don't play anything else on the computer meanwhile.
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

On the **Rich text** tab (S3), the left canvas is the editor (type in the dashed box) and the right
one is the renderer that draws the Output. Pick a sample, try both **Token chips** designs, and:

- Copy a few verses from Word and paste them into the box once in each **Paste from other programs**
  mode. The note beside it says what was kept and dropped.
- Use **Insert token**, then step over the token with the arrow keys, Backspace or Delete it, make it
  bold or a color, copy and paste it inside the box.
- Press **Flick** to swap the editor for the renderer's picture in the same place: the text shouldn't
  move. Drag the window to the Output monitor to see both on the projector.
- Ctrl+Z and Ctrl+Y undo and redo.

On the **Audio** tab (S4), pick a song, a reader and an output, and press Play. The big readout is
the position to 0.1 s. Click anywhere on the bar under it to jump there and play (S4-5). Trim Start
and Trim End (seconds) set where Play and Restart start and where playback stops.

Ear checks (Audio tab, second monitor, speakers on):

- **Sync** (S4-2): press **Sync: play clicks**. A click sounds every second and the Output flashes
  white through a Cue on each one. They should be together.
- **Tap** (S4-3): pick a song, press **Tap: record**, press Space at the start of each sung line,
  then **Stop**. Press **Tap: play back**: the song plays again and the Output changes ("Line 1",
  "Line 2", …) where you tapped.
- **Clean audio** (S4-1): play a song with the output the automatic run found best, then step
  through slides on the Slides tab while it plays. Listen for crackle.

Device change (S4-4): the projector's VGA splitter carries no sound, so test it like this. Play a
song, then in Windows **Settings > System > Sound** choose another output device (or plug in
headphones). The log under the buttons shows every notification and whether the output stopped with
an error. Try it three ways: as it is; with **Resume on the new default device** ticked (the program
restarts playback at the same place on the new device and logs how long that took); and with
**WasapiPlayer follows the default device** ticked and a WasapiPlayer output (Windows moves the
sound itself). The log is saved with the results.

On the **Timer** tab (S5), press **Run timer test** and watch the second monitor and the projector.
It counts down from 11:00, turns red at 1:00, carries on below zero with a minus sign, and for the
second half crossfades between two Timer slides every 15 seconds. Font, size, start value, warning
and run length can be changed first (a shorter run is fine for the eye checks; the automatic run
uses 12 minutes). Esc on the Output stops it.

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
| Audio (S4) | Each figure names the reader (Mp3FileReader or MediaFoundationReader) and the output (WaveOut, or WasapiPlayer with a 50 or 100 ms buffer). **Seek**: how far from the asked position a jump really lands, found by matching the sound against a decode from the start; pass is within 20 ms on the VBR files for at least one reader (otherwise Trim Start is dropped, see STATUS). **Position − heard**: what the program would believe the position is, minus what was actually being sent to the speakers. The "offset" is a fixed delay the product subtracts; what's left must be within 20 ms (95% of samples) and 40 ms at most for at least one pairing (needed for Cues, AUD-018). **Trim End** must stop within 20 ms. **Seek and play**: from the click to the new sound, for the cue editor and Restart. **Memory**: playing must add no more than about 20 MB. **Decoder offset**: how far apart the two readers place the same sound; Cues saved with one reader would move by this if the product changed reader. |
| Cues (S4) | Four runs of 40 s, a Cue every 2 s, each showing its own slide. A slide is pre-rendered every 500 ms meanwhile (decoding its background: the costly case), on the UI thread or the render thread, at Windows' default timer resolution or with the 1 ms setting. **Cue heard to slide shown** is from the moment the Cue's spot in the song left the program (loopback) to the frame after the one with the slide (an upper bound). Pass: within 50 ms at 95% (AUD-018) where the product will pre-render. **Parts** shows where the time goes; "poller loop" shows what a 10 ms sleep really takes. |
| Timer (S5) | **Whole second to the frame showing it**: within 100 ms every time, and no second skipped or shown twice. **Slot redraw** under 2 ms. Crossfades with the timer drop no more frames than S2. The **plain DispatcherTimer** line shows how far an interval timer would have drifted (the product doesn't use one). **Digit advance widths** shows how far digits would move in each font without the fixed cells. |
| Startup (S7) | Recorded for the owner to judge. As a guide: warm 3 s or less, cold 10 s or less. The development PC's `r2r` build JIT-compiles about 840 methods (1,270 with Kestrel). Many more on the church PC means the precompiled code was rejected on its CPU. |

The status bar shows the program's memory and CPU, sampled every 250 ms. **Count frames** shows
frames per second, but it makes WPF draw every frame, so leave it off unless needed.
