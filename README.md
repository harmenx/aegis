# Aegis

A real-time lobby you can drive with your own points. Each point can light a city, draw an arc, raise the activity bars, and land in the stream.

![Aegis lobby](docs/preview.gif)

Unity 6.6 (`6000.6.6f1`). Open the project and press Play on `Assets/Scenes/Lobby.unity`.

Until your own points arrive, a built-in demo keeps the picture moving. The first custom point pauses that demo.

## Send points

Three ways in, one shape of data.

**A JSON file.** Copy `points.sample.json` to `points.json` in the project folder. Aegis reloads it when the file changes. Points with the same id and values are not drawn twice. Append one JSON object per line to `points.jsonl` when a stream is easier than rewriting the whole file.

**HTTP.** While the lobby is playing, post to `http://127.0.0.1:8787/points`:

```bash
curl -X POST http://127.0.0.1:8787/points \
  -H "Content-Type: application/json" \
  -d '{"label":"gate-3","series":"arrivals","category":"alert","place":"Singapore","latitude":1.35,"longitude":103.82,"value":64}'
```

A single object, a raw array, or `{"points":[...]}` all work.

**C#.** From any script in the project:

```csharp
AegisFeed.Submit(new DataPoint
{
    Label = "gate-3",
    Series = "arrivals",
    Category = "alert",
    Place = "Singapore",
    Latitude = 1.35f,
    Longitude = 103.82f,
    HasTarget = true,
    TargetPlace = "Sydney",
    TargetLatitude = -33.87f,
    TargetLongitude = 151.21f,
    Value = 64f
});
```

`AegisFeed.SubmitJson(text)` accepts the same JSON as the file and the port. `AegisFeed.UseDemo(true)` runs your points and the built-in demo together.

## Fields

| Field | What it does |
| --- | --- |
| `label` | Name on the callout, the stream, and the trending list |
| `series` | Group shown beside the label |
| `category` | Color and which counter moves. `alert` is red, `resolve` thickens the stability mesh, and any other word gets its own color |
| `latitude`, `longitude` | Where the point sits on the globe. A known `place` such as `Tokyo` is enough if you omit them |
| `targetLatitude`, `targetLongitude`, `targetPlace` | The other end of an arc. Set `hasTarget` when the target is exactly `0,0` |
| `value` | Bar height and lines added |
| `secondary` | Lines removed |
| `id` | Lets a rewritten `points.json` update a point instead of repeating it |

## Views

The buttons under the stage hold a view. Keys `1`–`4` do the same. Space pauses the cycle. Drag to orbit, and scroll to move closer.

- **Globe**: points raise tufts of fibres over their city, and arcs draw along great circles to their target, then retract. Recent places get a labelled callout.
- **Activity**: barcode columns where each point lights the top of its column, keyed by category or repo. After a few seconds the stage tips back into receding lanes.
- **Scan**: a sweep of streaks as points arrive.
- **Stability**: a mesh that thickens as resolve, review, and merge points come in.

## Screenshots

| Globe | Activity |
| --- | --- |
| ![Globe](docs/screenshots/globe.png) | ![Activity](docs/screenshots/activity.png) |
| **Lanes** | **Scan** |
| ![Lanes](docs/screenshots/lanes.png) | ![Scan](docs/screenshots/scan.png) |
| **Stability** | |
| ![Stability](docs/screenshots/stability.png) | |

## Panels

- **Trending Developers** ranks actors by point count, with their latest repo.
- **Data Stream** lists each point with its time, type, actor, and repo.
- **Code Lines Added / Deleted** cycles Min, Hour, Day, and Week. Min is a rolling 60 s sum of `value` and `secondary`. Click a tab to hold it.
- **Contribution Graph** has one row per event type and a column per 5 s.
- **Users Reviewing & Commenting** drifts a bubble per review across a 40 s timeline. Bubble size follows `value`.

## Check it headless

```bash
Unity -batchmode -projectPath . -executeMethod Gitmos.Editor.ProjectSetup.BuildAndVerify -logFile Logs/verify.log
```

This rebuilds the scene and plays each view. It writes `playback-globe.png`, `playback-activity.png`, `playback-lanes.png`, `playback-scan.png`, and `playback-shot.png`, plus `playback-report.txt` with PASS or FAIL. Unity can hang on exit after the report is written, so stop it once the report appears. Touch `gif-record.flag` first to also dump 36 frames to `gif-frames/` for the preview.

`github.token` at the project root is only used by the demo's public GitHub feed. It stays out of git, and so do `points.json` and `points.jsonl`.

The dashboard is set in Inter, with IBM Plex Mono for the data stream. Both are included under the SIL Open Font License.
