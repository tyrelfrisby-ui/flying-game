# Compact split HUD review

These Unity IMGUI fixture captures call the same `LocalPlayerHud.Draw` method as live play. They are layout reviews,
not in-flight screenshots or evidence of device performance. Solid blue is a review backdrop.
The PNGs use the editor’s 2x Retina backing pixels; dimensions below describe logical points.
All three final captures were opened and checked; the final iOS-target Unity run had zero C# errors.

- `iphone-landscape.png`: 860 x 390 points, two ~430-point halves, representative 44-point side safe insets / 21 bottom.
- `ipad-landscape.png`: 1133 x 744 points, 20-point bottom safe inset.
- `iphone-portrait.png`: 390 x 860 points, 47-point top / 34-point bottom inset, stacked views.

P1 cyan: 112 kt, 2450 ft MSL, 274 degrees true, 1.2 G, ballistic ring fixture. P2 amber: 98 kt, 2380 ft MSL,
269 degrees true, 0.9 G, unarmed cross. These intentionally different fixture readings expose swapped player ownership.
Live pippers project each aircraft's `Gunnery.PredictRound` solution at 300 m through that player's camera.
The engine lane currently gives P2 no guns; the cross does not claim to be a firing solution.

Reproduce with Unity (with no other Unity / xcodebuild running):
`Unity -batchmode -projectPath unity -buildTarget iOS -executeMethod FlyingGame.EditorTools.LocalHudShots.Run -logFile build/local-hud-shots.log`
Do not pass `-nographics` or `-quit`: the tool renders its IMGUI review window and exits itself.

Integration hook outstanding: see AGENT-LOG NEEDS YOU. The shared touch tray and toolbar require coordinated input
suppression by the engine lane; fixture captures intentionally review only the per-player HUD.
