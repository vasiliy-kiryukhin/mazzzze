# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

A Godot 4.6 3D maze game prototype using C# (.NET 8.0) with the Jolt Physics engine. The project uses the Forward Plus renderer and targets Windows (D3D12) primarily.

## Build & Run

The Godot 4.6.3 mono editor lives at:
`/home/user13/Apps/Godot_v4.6.3-stable_mono_linux_x86_64/Godot_v4.6.3-stable_mono_linux.x86_64`

```bash
# 0. Build the C# project (MUST run after any .cs change before launching Godot)
dotnet build
dotnet build -c ExportRelease          # release configuration

# Convenience handle used in the examples below
GODOT="/home/user13/Apps/Godot_v4.6.3-stable_mono_linux_x86_64/Godot_v4.6.3-stable_mono_linux.x86_64"

# 1. Import assets (only needed after adding/changing .tscn/.tres/.glb, or a fresh checkout)
"$GODOT" --headless --import

# 2. Launch the editor / run the game by hand
"$GODOT" --editor --path .             # open the editor (F5 runs the game)
"$GODOT" --path .                      # run the game windowed (needs DISPLAY)
```

The main scene is `main.tscn` (set in project.godot).

## How to verify changes (no GUI needed)

This is the loop used to validate gameplay/lighting changes from the CLI. Always `dotnet build` first.

**A. Logic / physics check (headless, reads `GD.Print` logs):**

```bash
timeout 8 "$GODOT" --headless --path . 2>&1 | grep -iE "Player|Chunk|error|exception"
```

Confirms the maze initializes, the player spawns inside the maze (`[Player] Start cell=…`),
chunks stream in, and no errors/exceptions appear. For physics questions (e.g. "does the
player land on the floor or fall through?") add a temporary throttled print in
`Player._PhysicsProcess`, e.g. `if (frame % 30 == 0) GD.Print($"Y={GlobalPosition.Y} onFloor={IsOnFloor()}");`,
then run the command above and **remove the debug code afterwards**.

**B. Visual check (real render → screenshot).** A real GPU + `DISPLAY=:0` are available, so the
Forward+ renderer works. To capture what the camera actually sees, temporarily add to
`Player._PhysicsProcess` a block that, after ~90 frames, saves the viewport and quits:

```csharp
// TEMP — remove after verifying
if (++_dbgFrame == 90) {
    GetViewport().GetTexture().GetImage().SavePng("res://shot.png");
    GetTree().Quit();
}
```

Then build and run windowed on the display, and open the PNG:

```bash
dotnet build && DISPLAY=:0 timeout 20 "$GODOT" --path . --resolution 1280x720
# -> shot.png in the project root; view it, then delete it and the temp code
```

Remember to delete `shot.png` and revert the temporary debug code before finishing.

## Architecture

### Scene Hierarchy

```
Main (Node3D) — main.tscn
├── Ground (StaticBody3D) — 60×2×60 box with collision
├── DirectionalLight3D — overhead light with shadows enabled
├── Player (CharacterBody3D) — instance of player.tscn
│   ├── Pivot (Node3D)
│   │   └── Character (GLB model from art/player.glb)
│   └── CollisionShape3D — sphere radius 0.8
└── CameraPivot (Marker3D) — positioned above/behind
    └── Camera3D — orthographic projection, size 19
```

> **Note:** The scene hierarchy above is from the original tutorial starter and is partly outdated. The current game is a streaming procedural maze — see `TECH_SPEC.md` for the authoritative, up-to-date architecture (MazeData, ChunkManager, Chunk, elevated camera). Key facts below reflect the current implementation.

### Maze Geometry (current)

- **Corridor width** = `MazeData.CellWorldSize` = **3.6** world units = 6 × player diameter (collision sphere Ø0.6). Wall thickness equals corridor width (one cell).
- **Wall height** = `MazeData.WallHeight` = **30** — towering canyon walls that block any over-the-top view of the maze and form a narrow sky strip overhead.
- **Wall look** (`MazeTiles.tres`): dark, vertically-fluted canyon rock matching the reference `walls.png`. A single domain-warped `FastNoiseLite` feeds an albedo gradient (near-black valleys → dusty brown ridges) and a normal map; world-space triplanar with an anisotropic `uv1_scale = (0.14, 0.06, 0.14)` stretches the noise vertically into tall flutes. Don't push `uv1_scale.y` below ~0.05 — the streaks fan out into "fur".
- Tile dimensions live in `MazeTiles.tres`; the GridMap `cell_size` in `chunk.tscn` must match `CellWorldSize` (X/Z).
- **Tile overlap (seam fix):** the floor/wall *meshes* are **3.66** wide (slightly larger than the 3.6 cell) so neighbours overlap ~0.03/side. The maze renders at huge world coords (~-18000, since `WorldOffset = -WorldWidth*CellWorldSize/2`), where float32 precision (~0.002) leaves hairline cracks between exactly-abutting tiles that show the dark background through the floor. The overlap hides them; world-triplanar mapping makes the overlap sample the same texel so the coplanar z-fight is invisible. **Collision shapes stay 3.6** — don't enlarge them. See TECH_SPEC §5.4 "Tile overlap".

### Player Controller (`src/Player.cs`)

`CharacterBody3D`-based controller with camera-relative WASD movement (no jump — gravity only). Key details:
- Movement uses `Input.GetVector()` mapped to `move_left`, `move_right`, `move_forward`, `move_back`, resolved relative to the camera yaw
- The `ModelPivot` child rotates to face the movement direction via `Basis.LookingAt(direction)`
- **Camera**: dual-node yaw/pitch orbit rig sitting high above and slightly behind the player, angled steeply downward (default pitch −60°, clamped to [−85°, −25°]). A per-frame raycast **spring arm** shortens the camera distance when a wall would block the view of the player, so in the narrow 3.6-wide corridors the camera never clips into a wall and the player stays framed. It stays below the 30-unit wall tops, so the maze layout is never visible from above. Mouse looks; wheel zooms (desired distance 6–14).
- **HeadLight**: an `OmniLight3D` child of the Player (Y=4, just above head) travels with the player and keeps the player, nearby floor tiles and walls clearly lit at the bottom of the deep canyons where the directional sun barely reaches.
- **Sun & sky** (`main.tscn`): the `DirectionalLight3D` "sun" is grid-aligned at ~42° elevation ahead of the player (−Z), warm and bright (energy 2.4) with a large `light_angular_distance` (5) so its disk renders big and casts long soft shadows. The `ProceduralSkyMaterial` has a dark zenith, warm horizon glow and an enlarged sun halo, and the environment uses strong bloom (low `glow_hdr_threshold`) so that **looking down a long straight corridor you see the glowing sun disk high up at the far end** (the look of `walls.png`). A low cool sky-driven ambient fill keeps shadows readable without killing the contrast.
- Exported properties: `Speed` (5.0), `MouseSensitivity`, `Gravity` (15.0), zoom (`MinZoom`/`MaxZoom`/`ZoomStep`), and pitch (`DefaultPitchDeg`/`MinPitchDeg`/`MaxPitchDeg`)

### Input Map

WASD + arrow keys for movement, spacebar for jump. Gamepad left stick and D-pad also mapped. See `[input]` section in `project.godot`.

### Art Pipeline

Source files are `.blend` files in `art/`. They are imported as `.glb` scenes. Materials (`.tres`) are defined separately:
- `player.glb` — player character model with body, eye, and pupil materials
- `mob.glb` — enemy model (not yet placed in any scene)
- `House In a Forest Loop.ogg` — background music

Fonts: Montserrat-Medium.ttf (SIL Open Font License).

### Placeholder Code

- `game_object.cs` is an empty `Node`-derived class — not used in any scene, likely for future use.
- The `3d_squash_the_creeps_starter/` directory and its `.zip` are reference/tutorial assets, not part of this project.
