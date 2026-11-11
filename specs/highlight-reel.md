# Highlight reel: mark moments in online videos and get a pre-cut timeline

A command line tool for collecting highlight-worthy moments ("milestones") in one or more YouTube videos. It turns them into a timeline of short clips, ready for quick editing into a highlight reel.

## Concepts

- **Video**: a YouTube video, identified by its 11-character video ID.
- **Milestone**: a moment in a video worth including. It has a video, a time (whole seconds from the start), a short label, and the order it was added.
- **Clip**: a stretch of one video, from a start time to an end time.
- **Reel**: a project file holding videos (with their known durations) and milestones.

## Reading video links and times

- H-1 These URL forms must be accepted, with or without `https://` and `www.`:
  - `youtube.com/watch?v=<id>` (other query parameters in any order)
  - `youtu.be/<id>`
  - `youtube.com/shorts/<id>`
  - `youtube.com/embed/<id>`
  - `m.youtube.com/watch?v=<id>`
- H-2 A link's `t` or `start` parameter, if present, gives a time.
- H-3 Times may be written as plain seconds (`95`), `m:ss` (`1:35`), `h:mm:ss` (`1:01:35`), or with units (`1h1m35s`, `1m35s`, `95s`).
- H-4 Anything else is an error that names the bad input.

## Commands

The reel is a text file given with `--reel <path>` (default `reel.txt`). It is created if missing.

- H-5 `highlights add <url> [<time>] <label>`: adds a milestone. The time comes from the argument, or else from the URL's `t` parameter. If neither gives one, it's an error.
- H-6 `highlights duration <url-or-id> <time>`: records a video's length. It's optional, and used to stop clips running past the end.
- H-7 `highlights list`: lists milestones grouped by video, in time order within each video, numbered from 1 in that order, as `<n>. <id>  <h:mm:ss>  <label>`.
- H-8 `highlights links`: prints, for each milestone, a link that opens the video at that moment: `https://youtu.be/<id>?t=<seconds>`.
- H-9 `highlights timeline [--before <s>] [--after <s>] [--format text|csv|json|edl]`: prints the pre-cut timeline (below). The defaults are 5 seconds before and 5 after.
- H-10 `highlights remove <n>`: removes the n-th milestone as numbered by `list`.

## Building the timeline

- H-11 Each milestone at time `t` becomes a clip from `t − before` to `t + after`.
- H-12 Clips are clamped to start no earlier than 0 and, if the duration is known, to end no later than the duration.
- H-13 Clips from the same video that overlap or touch are merged into one clip that keeps all their labels, joined by ` / ` in time order.
- H-14 Clips are ordered by the order their video was first used, then by start time.
- H-15 Each clip's position in the reel (`reel start`) is the sum of the lengths of the clips before it.

## Output formats

- `text`: one line per clip: `<n>. <reel start>  <id>  <start>–<end>  <labels>`, times as `h:mm:ss`, with total reel length at the end.
- `csv`: header `index,video_id,start_seconds,end_seconds,reel_start_seconds,labels,url`. The url is an embed link that plays just that clip: `https://www.youtube.com/embed/<id>?start=<s>&end=<e>`. Fields are quoted per RFC 4180.
- `json`: `{"clips": [{"index", "videoId", "start", "end", "reelStart", "labels": [...], "url"}], "totalSeconds"}`.
- `edl`: a CMX 3600 edit decision list at 30 fps non-drop. There is one event per clip, with the video ID as the reel name (truncated to 8 characters) and a `* FROM CLIP NAME:` comment with the labels.

## Examples

With these commands:

```
highlights add "https://www.youtube.com/watch?v=dQw4w9WgXcQ&t=43s" "Chorus starts"
highlights add https://youtu.be/dQw4w9WgXcQ 0:50 "Dance move"
highlights add https://youtu.be/9bZkp7q19f0 1:08 "Horse dance"
highlights add "youtube.com/shorts/9bZkp7q19f0" 3 "Intro"
highlights duration 9bZkp7q19f0 4:12
```

```
$ highlights timeline
1. 0:00:00  dQw4w9WgXcQ  0:00:38–0:00:55  Chorus starts / Dance move
2. 0:00:17  9bZkp7q19f0  0:00:00–0:00:08  Intro
3. 0:00:25  9bZkp7q19f0  0:01:03–0:01:13  Horse dance
Total: 0:00:35
```

- The first two milestones (43s and 50s) give clips 38–48 and 45–55, which overlap, so they merge into 38–55.
- "Intro" at 3s gives −2–8, clamped to 0–8.
- `highlights add youtu.be/abc 1:00 "x"` fails: `'abc' is not a valid video ID (expected 11 characters)`.
- `highlights add https://youtu.be/dQw4w9WgXcQ "No time"` fails: `no time given and the link has no t= parameter`.

## Out of scope

Downloading or cutting video, checking that videos exist, playlists, non-YouTube sites, frame-accurate times.
