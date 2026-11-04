# archive/day2-stage: the parked character-stage screens

The Day 2 screen was a 2D stage with three painted musicians and an audience strip. On 2026-10-29 the project moved to a 3D meadow where nature is the
performer (`docs/superpowers/specs/2026-10-29-meadow-orchestra-design.md`). These files were moved here with `git mv` (history kept) when Task 5 removed the
`theme` block from `content/content.json`. They sit outside `Assets/`, so Unity does not compile them.

| Path | What it was |
|---|---|
| `Scripts/Views/{StageView,PerformerView,AudienceView,HandCursorView,SpriteFactory}.cs` | the 2D stage, the musicians, the audience dots, the hand cursors |
| `Scripts/Views/{ViewAssets,ViewLayout,ViewMode}.cs` | the scene reference holder and the stage / audience split of one canvas |
| `Editor/DownbeatSceneBuilder.cs`, `Scenes/Main.unity`, `Materials/GlowAdditive.mat` | the one-object bootstrap scene and its additive material |

The nine "forest-night" pictures they used (and `sprites.json`, their measured anchors) were in `forest-img/` (left out of the public copy); they were moved out of `content/` so that builds stop shipping them (they were not referenced by `content.json` any more).
