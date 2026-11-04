# Day 0 throw-away test code

Not part of the product. The assembly `Downbeat.Day0` compiles only when the scripting define `DOWNBEAT_DAY0` is set
(Player Settings > Scripting Define Symbols), and `Plugins/Web/Day0Bridge.jslib` is disabled for all platforms so it does not
end up in the Web build. To rebuild the Day 0 test build: set the define, enable the jslib for WebGL in the Inspector,
run `Downbeat.EditorTools.Day0SceneBuilder.Create`, then `WebBuild.Run -dbScene Assets/_Day0/Scenes/Day0.unity`.
The scene is not kept (it was built on URP); `Create` makes a fresh one. Results of the Day 0 tests: docs/day0-results.md.
