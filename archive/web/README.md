# archive/web: the parked Day 0-2 web prototype

On 2026-10-29 the project moved from a browser app (Unity WebGL + Web Audio + in-page MediaPipe) to a native Unity app with a separate
hand-tracking process. The web-only code was moved here with `git mv`, so its history is kept. It sits outside `Assets/`, so Unity does not compile it.

| Path | What it was |
|---|---|
| `WebGLTemplates/Downbeat/` | the page: start screen, `js/` page scripts (Web Audio beat clock and note player, `tracker.js` MediaPipe hand tracker, hand buffer, id continuity, camera start, recorder) |
| `Scripts/Audio/WebAudioEngine.cs` | `IAudioEngine` that calls the page's audio scripts |
| `Scripts/App/{JsHandSource,PagePort}.cs` | pulls the hand buffer from the page; the page <-> Unity port |
| `Plugins/Web/Downbeat.jslib` | the jslib behind `PagePort` and `WebAudioEngine` |
| `Editor/WebBuild.cs` | batchmode WebGL build entry point |
| `test/` | Node tests of the page scripts; `npm test` still runs them (143 tests together with `web/test` and `tools/`) |

To bring the web build back: copy these folders to their old places under `unity/Downbeat/Assets/` (`WebGLTemplates/` at the top of `Assets/`, the others under
`_Project/`), re-add the `UNITY_WEBGL` branches in `DownbeatLifetimeScope` and `ContentLocator`, and install the Web module.
The Node tools in `tools/` (`assemble_site.mjs`, `run_day*.mjs`, ...) are unchanged and still describe how the web measurements were made.
