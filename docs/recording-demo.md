# Recording the demo GIF

Notes for producing the README demo (Definition of done item 9). Written for
this machine: Wayland session under the niri compositor. `wf-recorder`,
`slurp`, and `ffmpeg` are already installed; `grim` working confirms the
compositor exposes the screencopy protocol that `wf-recorder` depends on.

## 1. Record

Drag to select a region, `Ctrl+C` to stop:

```bash
wf-recorder -g "$(slurp)" -f ~/demo.mp4
```

Select the browser window only, not the whole output. A full ultrawide capture
scaled down to README width makes the status tags unreadable, which are the
thing the demo exists to show.

## 2. Trim

Cut dead time before converting — every second costs GIF bytes.

```bash
ffmpeg -i ~/demo.mp4 -ss 00:00:02 -to 00:00:22 -c copy ~/demo-trim.mp4
```

## 3. Convert

Generate a palette rather than relying on ffmpeg's default quantiser, which
makes dark UIs look muddy:

```bash
ffmpeg -i ~/demo-trim.mp4 -vf \
  "fps=12,scale=900:-1:flags=lanczos,split[s0][s1];[s0]palettegen=max_colors=128[p];[s1][p]paletteuse=dither=bayer:bayer_scale=3" \
  -loop 0 ~/demo.gif
```

900px matches GitHub's README content width, so the GIF renders 1:1 with no
browser resampling. 12fps is plenty — the motion here is status tags changing,
not video.

Check the result with `du -h ~/demo.gif` and aim for under ~5MB. If it is over,
drop to `fps=10` or `scale=800` before reducing colours; resolution and frame
rate cost less visually than palette depth on a flat-coloured UI.

For better quality, `gifski` beats ffmpeg's quantiser noticeably:

```bash
paru -S gifski
gifski --fps 12 --width 900 -o ~/demo.gif ~/demo-trim.mp4
```

## 4. Commit and embed

```bash
mkdir -p docs && mv ~/demo.gif docs/demo.gif
```

```markdown
![PrintSpooler dashboard](docs/demo.gif)
```

## What to actually record

Two things specific to this project:

**Use a small file, not a multi-megabyte one.** A 4.9MB file takes roughly ten
seconds to write to Azure SQL — measured, not estimated. In a GIF that is ten
seconds of a row sitting on "Staged" with a spinner, which is a third of the
runtime spent on the least interesting thing, and a reviewer reads it as the
app being slow. A small PDF makes staged → Queued → Submitting → Processing
land in a couple of seconds.

**Show a transition the backend actually earns.** The completion path is the
best one: submit, watch the row reach Processing, then Completed via the
SignalR push with no page refresh. Cancel is the second-best shot, because
`Cancelling` → `Cancelled` demonstrates the printer confirming rather than the
UI guessing.

## Longer clips

A GIF cannot carry much beyond ~25 seconds at a reasonable size. For anything
longer, drag the `.mp4` into a GitHub issue comment, copy the
`github.com/user-attachments/assets/...` URL that GitHub generates, and use that
URL in the README — GitHub renders it as a real video player.

The README uses a GIF because it autoplays without a click, which is the right
default for the top of the page. Video is a supplement, not a replacement.
