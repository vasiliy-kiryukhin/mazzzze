# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

A Godot 4.6 3D maze game prototype using C# (.NET 8.0) with the Jolt Physics engine. The project uses the Forward Plus renderer and targets Windows (D3D12) primarily.

## Build & Run

The project targets **Godot 4.7** mono (`Godot.NET.Sdk/4.7.0`). The editor is a repo-local,
git-ignored download (there is a `godot` shell alias to it):
`.bin/Godot_v4.7-stable_mono_linux_x86_64/Godot_v4.7-stable_mono_linux.x86_64`

```bash
# 0. Build the C# project (MUST run after any .cs change before launching Godot)
dotnet build
dotnet build -c ExportRelease          # release configuration

# Convenience handle used in the examples below
GODOT="$PWD/.bin/Godot_v4.7-stable_mono_linux_x86_64/Godot_v4.7-stable_mono_linux.x86_64"   # run from the repo root

# 1. Import assets (only needed after adding/changing .tscn/.tres/.glb, or a fresh checkout)
"$GODOT" --headless --import

# 2. Launch the editor / run the game by hand
"$GODOT" --editor --path .             # open the editor (F5 runs the game)
"$GODOT" --path .                      # run the game windowed (needs DISPLAY)
```

The main scene is `main.tscn` (set in project.godot).

## How to verify changes (no GUI needed)

There are no unit tests, integration tests, or CI. All verification is CLI-based. Never add test files unless explicitly asked.

Always `dotnet build` first, then:

**A. Logic / physics check (headless, reads `GD.Print` logs):**

```bash
timeout 8 "$GODOT" --headless --path . 2>&1 | grep -iE "Player|Chunk|error|exception"
```

Expected output: `[Player] Start cell=…` and `[ChunkManager] LOAD` lines, zero error/exception lines. For physics questions add a temporary throttled `GD.Print` in `Player._PhysicsProcess`, run the command, then **remove the debug code**.

**B. Visual check (real render → screenshot).** A real GPU + `DISPLAY=:0` are available. Temporarily add to `Player._PhysicsProcess`:

```csharp
// TEMP — remove after verifying
if (++_dbgFrame == 90) {
    GetViewport().GetTexture().GetImage().SavePng("res://shot.png");
    GetTree().Quit();
}
```

```bash
dotnet build && DISPLAY=:0 timeout 20 "$GODOT" --path . --resolution 1280x720
# -> shot.png in the project root; view it, then delete it and the temp code
```

Always delete `shot.png` and revert debug code before finishing.

## Architecture

`requirements/TECH_SPEC.md` is the authoritative technical reference. Key facts below.

### Runtime scene hierarchy

```
Main (Node3D)
├── Ground (StaticBody3D, Y=-0.5)          — fallback floor, 256×1×256 box
├── DirectionalLight3D                      — "sun"; energy/color/angle set **per region** by LightingController — **off** for DarkCanyon (default)
├── MazeData (Node + MazeData.cs)          — singleton, procedural world data
├── Player (CharacterBody3D)               — player.tscn
│   ├── ModelPivot (Node3D)                — faces movement direction
│   │   └── Character (AnimationLibrary_Godot_Standard.glb)  — AnimationPlayer with 46 clips
│   ├── CollisionShape3D (Y=0.35)          — SphereShape3D, radius=0.3
│   ├── HeadLight (OmniLight3D, Y=4.0)     — local fill light
│   └── CameraYaw (Node3D, Y=2.0)
│       └── CameraPitch (Node3D, -60°)
│           └── Camera3D (Z=10)            — spring-arm shortened to avoid walls
├── ChunkManager (Node3D)                  — orchestrates chunk lifecycle
│   └── Chunk ×N (dynamic)                — chunk.tscn instances, each a 16×16 GridMap
├── MonsterSpawner (Node3D + MonsterSpawner.cs) — mini debug spawner (places Ifrit near start)
├── LightingController (Node + LightingController.cs) — applies the region kit's LightingProfile to sun/env/HeadLight at _Ready
├── Ifrit ×N (CharacterBody3D, dynamic)   — spawned under Main (persistent, not chunk-bound)
├── WorldEnvironment                        — procedural sky + bloom
└── HUD (CanvasLayer)
    ├── Minimap (Control)                  — top-left overlay, procedural _Draw
    ├── Inventory (Control + InventoryHud.cs) — bottom-right overlay, procedural _Draw
    ├── Viewfinder (Control + ViewfinderHud.cs) — camera viewfinder overlay (hidden until use)
    ├── PhotoEnter (Control + PhotoEnterHud.cs) — photo walk-into vignette/flash overlay
    └── DamageFlash (Control + DamageHud.cs)  — red hit-flash (created by MonsterSpawner)
```

### Maze geometry

- **MazeData** is a singleton backed by the **maze-gen region façade** (`PlayersWorlds.Maps`). At startup it generates **one real region** (footprint `RegionFootprintSide` = 15 world cells; `RegionRecipe.Maze` + Aldous-Broder; square 1×1 cells) with a `NullRegionStore` (regenerated each run — `RandomizeEachLaunch = true` — no persistence yet). `IsFloor(wx, wz)` answers O(1) from the resident `RegionView` (`region.CellAt(cell).IsPassable`); cells outside the region read as wall. The maze size **is** the region footprint (currently 15×15 cells) — no longer a fixed 10 000×10 000 bound, and no longer a murmur3 hash.
- **CellWorldSize = 3.6** world units (corridor width = 6× player diameter). **WallHeight = 30**.
- **WorldOffset** centres the region on the world origin and is now **computed at runtime** from the region size (was a fixed `−18 000` constant): `WorldOffsetX/Z = −RegionSize × CellWorldSize / 2` ≈ **−27** for the 15×15 footprint. It returns 0 until `MazeData._Ready` builds the region, so nothing may read it before then. Player start = the region's **entrance POI** cell (varies per seed), landing near the origin — e.g. world ≈ (7.2, 0.3, −14.4).
- **Chunk streaming**: `ChunkManager` keeps a 3×3 (LoadDistance=1) grid of 16×16-cell chunks loaded. `UpdateChunks()` is called every `Player._PhysicsProcess`.
- **MazeTiles.tres**: `MeshLibrary` with exactly 2 items — id 0 = Floor, id 1 = Wall.
- **Tile overlap (seam fix):** floor/wall *meshes* are 3.66 wide; GridMap `cell_size` and collision shapes stay 3.6. The 0.03 overlap was added to hide float32 precision cracks back when the world rendered at ≈ −18 000; now the façade centres the maze near the origin (≈ ±27), where float32 has ample precision, so that problem is gone and the overlap is merely **defensive/harmless**. World-space triplanar mapping still keeps the coplanar z-fight invisible.
- **Wall rendering — environment kits (`REQ-0022`, US-22/F-51..F-53):** the GridMap Wall item (id 1) is now a **dark occluder + collision box only** (flat near-black `StandardMaterial3D`, no normal map) — collision and light-occlusion, unchanged geometry. The *visible* wall surface is kit-driven **instanced rock geometry**: `Chunk.Setup(coord, chunkData, EnvironmentKit kit)` places rocks per wall cell via `kit.PlaceRocks(cellCenterLocal, seed)` and batches the results into one `MultiMeshInstance3D` per rock prototype (≤ 8 draw calls/chunk). The kit comes from `MazeData.RegionEnvironment` ([Export] `EnvironmentId`, one value per region) resolved through `EnvironmentKitRegistry`; two kits exist — `SlotCanyonKit` (red-sand, tight/tall/near-vertical cliffs) and `RavineKit` (grey photoscan, tilted/wider-spread) — both built from `art/RockPack1/` meshes. Placement is **deterministic per world cell** (`Chunk.CellSeed(worldSeed, wx, wz)`, FNV-1a), so rocks don't change or flicker as chunks stream in/out. Rocks are visual-only (no collision) and may overhang the corridor. Details: [`requirements/REQ-0022-environment-kits/design.md`](./requirements/REQ-0022-environment-kits/design.md).
- **Per-region lighting (`REQ-0022`, US-22/F-54):** each `EnvironmentKit` also carries a **`LightingProfile`** (`src/LightingProfile.cs`, plain data) describing the region's lighting mood — sun (visible/energy/color/pitch), ambient fill, sky colors, depth fog, and the player `HeadLight` (energy/color/range/attenuation/height/shadow — height grazes close walls dimmer while keeping the floor pool lit; shadow enables the omni cubemap so rocks/walls block the torch, on for DarkCanyon). `LightingController` (`src/LightingController.cs`, `Main/LightingController`, wired to the sun / `WorldEnvironment` / `Player/HeadLight` via `[Export] NodePath`) resolves the resident region's kit in `_Ready` and applies its profile before the first frame — so **DarkCanyon** is a pitch-black dungeon (sun off, ambient off, near-black sky, subtle black fog, only the warm short-range torch), while `SlotCanyon` bakes under a scorching sun and `Ravine` sits under flat overcast. The stored `main.tscn`/`player.tscn` light values are just editor defaults (aligned to DarkCanyon); the controller overwrites them at runtime. **The "glow from below" on rock undersides was flat COLOR ambient** (uniform fill ignores occlusion) — disabling ambient fixed it.

### Player controller (`src/Player.cs`)

- Camera-relative WASD movement via `Input.GetVector()`. No jump; gravity only.
- **Dual-node orbit camera**: `CameraYaw` (mouse X) → `CameraPitch` (mouse Y, clamped [−85°, −25°]) → `Camera3D`. Spring-arm raycast shortens the camera distance each physics frame to avoid wall clips.
- **HeadLight** (OmniLight3D, Y=4) travels with the player for fill light at the canyon floor.
- Animation: `ModelPivot/Character/AnimationPlayer` cross-fades `Idle` ↔ `Jog_Fwd`; speed-scaled by `planarSpeed / WalkAnimRefSpeed` to match foot pace. `PlayPickupGesture()` plays a one-shot clip (`PickUpAnim` = `Interact`) that overrides locomotion until it finishes (used on item activation).
- **Character model forward is +Z** but `Basis.LookingAt` points −Z at the target, so `player.tscn` carries a **180° Y rotation on the Character node** — without it the player walks backward.
- Exported: `Speed` (5.0), `MouseSensitivity`, `Gravity` (15.0), zoom (`MinZoom`/`MaxZoom`/`ZoomStep`), pitch (`DefaultPitchDeg`/`MinPitchDeg`/`MaxPitchDeg`), anim clip names (`IdleAnim`/`WalkAnim`/`WalkAnimRefSpeed`/`AnimBlend`).

### Mini-map (`src/Minimap.cs`, `src/MinimapState.cs`)

Top-left HUD overlay, procedurally drawn — no textures. Implements `requirements/REQ-0010-minimap/` (US-10/F-09/F-10/F-11).
- **Fog of war** (`MinimapState`): FIFO of last 1000 entered cells; each reveals a 3×3 neighbourhood. Entrance/exit permanently revealed once entered.
- **Two draw zones**: near (Chebyshev ≤ 7 → 15×15) = per-cell floor/wall detail; far revealed = flat schematic silhouette.
- **Cell-visit detection is in `_PhysicsProcess`** — do not move it to `_Process` (cells can be skipped at variable render rate).
- Tab toggles orientation (camera-forward-up ↔ world-north-up). Ctrl+wheel zooms the map; player ignores wheel while Ctrl is held.

### Item system (inventory · drop · world item · pickup · activation · camera · photo)

`InventoryHud` (`Control`, HUD bottom-right) is the hub; procedurally drawn like the mini-map. It owns the item **state machine** (InWorld / InInventory / Activated) — see `requirements/REQ-0012-base-item/design.md`.
- **`Inventory` / `Item`** — 12-slot model (3×4, pure data) and an item entity (`TypeId`, `DisplayName`, `Category` = Consumable/Key, `Usage` = ImmediateA/ActivatedB, `ModelPath`, `Icon`, `Use()`, `virtual BuildModel()`). A camera (Usage=ActivatedB) is seeded into slot 0 in `_Ready`. `PhotoItem : Item` adds captured pos/yaw/pitch and a procedural polaroid `BuildModel()`.
- **Inventory HUD** (REQ-0011, US-11): compact (bag + "N/12") vs expanded (3×4 grid). **single I** toggles; when open, digit **1–3** picks a row then **1–4** a column. On a cell: pattern-A → `Item.Use`; pattern-B → **activate into hand** (toggle to deactivate; plays `Player.PlayPickupGesture()` = the `Interact` clip). Game **not paused**, mouse stays captured. Slot-icon camera is tightly framed (FOV 30) so the model fills the cell.
- **Item icons / world models:** `Item.BuildModel()` (glb by `ModelPath`, or procedural for `PhotoItem`) is rendered in a `SubViewport` (`OwnWorld3D`, camera auto-framed from AABB) → `ViewportTexture` for the slot; `WorldItem.Setup` uses the same factory. In-world scale = `WorldItemSizeFraction` (**0.25**) × player height.
- **Drop** (`REQ-0015-base-item-drop`, US-15): **Shift+column** or **G** → `DropProjectile` ("star") lands as a **`WorldItem`** on the floor.
- **Pickup** (`REQ-0016-base-item-pickup`, US-16): **automatic** — `InventoryHud._PhysicsProcess` scans `WorldItem.All` for the nearest *armed* item within `PickupRange`, clear line-of-sight (raycast, mask 1), inventory not full. `PickupProjectile` flies it back. Items **arm** only after the player has once left their radius.
- **Activation / reservation** (F-18/B, F-19a): a pattern-B item stays in its slot but is flagged `_activatedItem`/`_reservedSlot` → the slot is blocked. `ActivateSlot`/`Deactivate`/`DropActivated`/`ConsumeActivated(replacement)` are the transitions; exclusivity = one activated item.
- **Vintage camera** (`REQ-0013-vintage-camera`, US-13): **LMB** (`use_activated`) on the activated camera opens `ViewfinderHud` — a framed **window above the player's head** (third-person view kept, **no darken**) showing a `SubViewport` first-person **level/yaw** lens view (shared `World3D`), with sepia + vignette + countdown 3→2→1 (`TickSeconds` 0.6667 — 3× faster). Focus check = forward horizontal ray, min 1.8 (3×0.6); blocked-before or lost-during resets without consuming. Mouse pitch stays free (third-person). On fire → `PhotoItem` created into the reserved slot, camera destroyed.
- **Photo** (`REQ-0017-photo`, US-17): activated photo shows a **live, monochrome-sepia window centred on screen with a simple drawn frame** (`PhotoEnterHud` — one `SubViewport` `_vp` renders the captured point via a `Camera3D` at `CapturedWorldPos`/yaw with `Environment` saturation 0, so a monster passing there shows live; a warm `Sepia` overlay + procedural wood/brass border are drawn in `_Draw`, matching the viewfinder). A 3D polaroid-frame variant (from a `polaroid_photo.glb` model) was tried and dropped, and the model removed. Walking forward teleports: `UpdatePhotoEnter` accrues progress while `move_forward` held and actually advancing (`Velocity·camForward > Speed*0.4`), the window **grows** from centre; at `EnterDuration` 1.3333 s (1.5× shorter) → `Player.TeleportTo(pos, yaw, pitch)` (main top-down pitch preserved), sepia flash, photo consumed. Camera fire also plays `PlayPickupGesture()` + slot flash so the new photo is noticed. Live view is limited to currently-streamed chunks.
- **Shared:** `ItemStar` builds the emissive star for both projectiles. `WorldItem` keeps a static registry (`All`) and holds its `Item` so pickup restores it.
- **Not implemented:** `Item` is a plain class (no `Resource`/`.tres` type registry); no serialization; no large edge-screen activated indicator (compact badge only); photo icon is a shared placeholder (no captured-view thumbnail).

### Monster system (base template · Ifrit)

`Monster` (`src/Monster.cs`, abstract `CharacterBody3D`) is the template (`REQ-0019`, US-19/F-39..F-43); `Ifrit` (`src/Ifrit.cs`) is the first concrete type (`REQ-0020`, US-20/F-44..F-46). All behaviour lives in the base; a type just sets params in its ctor.
- **Registry** (F-43): static `Monster.All`, add/remove in `_EnterTree`/`_ExitTree` (mirrors `WorldItem.All`). Monsters live under `Main` → **persistent**, not chunk-bound.
- **Perception** (F-40): `CanSee(target)` = in vision cone (`VisionRange` + `VisionFovDeg` around `_facing`) **and** clear LoS (`IntersectRay` from eyes, wall mask 1, excludes self+player). Same check finds the player and lure items (`WorldItem.All`).
- **FSM** (F-41), all in `_PhysicsProcess`: `Cycle` (patrol) · `Threat` (chase) · `Stun` · `Distract`. Priority: Stun > player-visibility > distraction. No memory after disruption → back to `Cycle`.
- **Movement**: BFS pathfinding over `MazeData.IsFloor` cells (`FindPath`), following cell centres with a direct final-approach; patrol restricted to a segment. Robust around corners. Gravity + `MoveAndSlide`; `ModelPivot` faces movement via `LookAt` + `ModelYawOffsetDeg` (180° for the ifrit — forward is +Z like the player rig).
- **Animation** (`UpdateAnim`/`PlayAttack`): base plays the model's `AnimationPlayer` clips by name — `IdleAnim` (still) / `MoveAnim` (moving, looping + speed-scaled by velocity) / `AttackAnim` (one-shot on contact) / `StunAnim` (one-shot on `Stun()`). Idle/Move loops are forced (`SetLoop`, since glb clips import as one-shot). The ifrit glb ships a full set — `Idle`, `Run`, `Attack`, `BeHit` (`Monster_YiFuLiTe_*`).
- **Contact damage** (F-42/F-44): planar touch distance, throttled by `ContactInterval`; emits `PlayerHit(damage)` signal + `DamageHud` red flash + log (no health system yet).
- **Model scaling**: local-space AABB (computed via local transform chains, not global coords — was needed against float32 loss at world −18000, still sound now that the world sits near the origin). Humanoid → scaled by **height** (`TargetHeight`); a low/long model would set `ScaleByLength` (fits by horizontal span). `ModelUprightPitchDeg` corrects a mis-authored up-axis (0 for the ifrit).
- **Ifrit defaults**: vision 18 wu / 100°, patrol 2.0 / chase 4.0, damage 10, chase-drop 57.6 wu (1 chunk), contact 0.7 s, stun 2.5 s, segment 16 cells, `art/ifrit.glb`.
- **Spawn**: `MonsterSpawner` (`src/MonsterSpawner.cs`, `Main/MonsterSpawner`) is a **minimal debug spawner** — places a few Ifrit near the player start and creates `DamageHud`. A real spawner is a future feature.
- **Not implemented / hooks**: `Stun()` is public but has no trigger (future tennis ball, IDEA-0025); distraction reacts to any `WorldItem` (no dedicated lure type); Ranged delivery + Small size are future; player health is a future feature (monster only reports hits); no `Death` clip hooked (no death state yet). The old `Mob.cs`/`mob.tscn` charge stub is **superseded** (still present, unused).

### Input map

WASD / arrow keys, **Tab** (minimap orientation), **I** (inventory, single press), **1–4** (inventory row/cell → apply/activate), **Shift+1–4** (drop, incl. drop-activated on the reserved cell), **G** (drop cursor slot), **LMB** (`use_activated` — use activated camera), **hold W/↑** (walk into activated photo), **Ctrl+Q** quits, mouse look, wheel zoom, gamepad left stick. Pickup is automatic (no key). See `[input]` in `project.godot`.

### Art pipeline

Source `.blend` files live in `art/`, imported as `.glb`. Materials are separate `.tres` resources.

| File | Status | Purpose |
|------|--------|---------|
| `art/AnimationLibrary_Godot_Standard.glb` | **Active** | Player: rigged humanoid + AnimationPlayer (46 clips) |
| `art/old_kodak_camera.glb` | **Active** | 3D model for the camera item (US-13), seeded in inventory + dropped in world |
| `art/ifrit.glb` | **Active** | Ifrit monster model (US-20); fiery humanoid demon with full anim set (Idle/Run/Attack/BeHit), spawned near start |
| `art/RockPack1/` | **Active** | Arnklit "Godot Cliffs & Rocks Pack 1" (`v1_02`, non-HighRes) — bare `Cliff*` meshes + `Cliff_Material_Red_Sand.tres`/`Cliff_Material_Photoscan.tres` + `advanced_rock.gdshader`, instanced by `SlotCanyonKit`/`RavineKit` (REQ-0022) as wall rocks. `Boulder*`/`Rock*`/`Pile*` meshes in the pack are imported but not yet wired into a kit. |
| `art/vintage_camera.glb` | Asset (unused in scene) | Alternate camera model candidate |
| `art/mob.glb` | Asset (unused in scene) | Old enemy model; `Mob.cs`/`mob.tscn` stub superseded by `Monster`/`Ifrit` |
| `art/player.glb` | **Deprecated** | Old sphere-based player; no longer in any scene |
| `art/House In a Forest Loop.ogg` | Asset (unused) | Background music, not yet integrated |

### Requirements

`requirements/` docs are **in Russian**. They describe WHAT the game does, not HOW. `requirements/TECH_SPEC.md` is the authoritative technical reference in English. Catalog index: `requirements/README.md` (feature → US → F-ID → status → file).

Reserved low IDs (0000–0009) are core/meta docs; feature IDs match their US number (US-10 → REQ-0010):
- `requirements/REQ-0000-vision.md`, `REQ-0001-user-journey.md`, `REQ-0002-non-functional.md` — context/vision (US-09).
- `requirements/REQ-0003-core/` — US-01..08/F-01..08 (**the core game**: movement, camera, maze, chunking, input, visual — what all of `src/*.cs` implements). Start here to map a C# file to its requirement.

Feature folders (`REQ-NNNN-<slug>/`, each with README + facet files + `design.md`):
- `requirements/REQ-0010-minimap/` — US-10/F-09..F-11 (mini-map, implemented)
- `requirements/REQ-0011-inventory/` — US-11/F-12..F-14 (inventory, 3×4 grid, implemented)
- `requirements/REQ-0012-base-item/` — US-12/F-15..F-19a (base item entity; implemented base — 3 states, patterns A/B, slot reservation; see `design.md` for scope). Sub-features: REQ-0014 (in-world), REQ-0015 (drop), REQ-0016 (pickup)
- `requirements/REQ-0013-vintage-camera/` — US-13/F-20..F-23 (vintage camera: activation, viewfinder, timer, focus — implemented)
- `requirements/REQ-0017-photo/` — US-17/F-31..F-34 (photo portal item: capture, activation, walk-into teleport — implemented)

### Placeholder / unused code

- `game_object.cs` — empty `Node` subclass, unused placeholder.
- `src/Mob.cs` / `mob.tscn` — old enemy charge stub, **superseded** by `Monster`/`Ifrit` (never spawned).
- `3d_squash_the_creeps_starter/` — tutorial reference assets, not part of this project.

## Documentation & requirements rules (MANDATORY)

Binding rules. Any change that touches behaviour, input, or architecture MUST update the docs in the **same change** — docs are part of "done", not a follow-up.

**Where docs live**
- `requirements/` — the requirements catalog. Docs are in **Russian** and describe **WHAT** the game does, never HOW.
- `requirements/TECH_SPEC.md` — the single authoritative technical reference (**HOW**), in **English**.
- `requirements/README.md` — the registry/index: one row per feature (ID · name · US · F-ID · status · path) plus a "Связи между фичами" section. Update it whenever a feature is added, moved, or changes status.
- `requirements/REQ-0004-keybindings.md` — a live snapshot of every working key. Update on ANY input-map or hardwired-key change.

**Feature folder structure** — one folder per feature: `requirements/REQ-NNNN-<slug>/`
- `README.md` — overview only: User Story (US-NN), acceptance criteria, an `ID → файл` map, status, related links.
- Numbered **facet files** `NN-<facet>.md`, one concern each. Facets: `logic`, `ui`, `visual`, `input`, `data`, `animation`. Split a concern into its own file when it is distinct (e.g. keep `animation` separate from `visual`) — never cram two facets into one file.
- `design.md` — **required**. The HOW for this feature: names the `src/*.cs` files, key decisions, and an explicit scope/limits ("границы") note. References TECH_SPEC.
- Single-page context/meta docs may be a flat `REQ-NNNN-<slug>.md` instead of a folder (reserved low IDs 0000–0009 = core/meta).

**WHAT vs HOW — do not mix**
- Facet files and README = WHAT. No file/class/method names, no implementation mechanics. Tunable values go in a "Параметры" table (name + default + meaning), not as code.
- `design.md` = HOW. Names the code, explains mechanics/trade-offs, and lists what is deliberately NOT implemented.

**Semantic IDs are permanent anchors**
- `US-NN` (user story) and `F-NN` (functional requirement) are referenced from code comments and TECH_SPEC. **Never renumber or reuse them.**
- New feature → next free `REQ-NNNN` folder + `US-NN`. New functional requirement → next free `F-NN`. IDs are globally flat, not per-parent.

**Sub-features nest inside their parent**
- A requirement that refines/extends an existing feature lives in a **subfolder of that feature**, named `REQ-NNNN-<parent-slug>-<slug>` (e.g. `REQ-0012-base-item/REQ-0014-base-item-item-in-world/`). Global `NNNN` numbering stays flat. The parent README lists its sub-features. An independent feature stays top-level.
- When you move/rename a folder, fix EVERY relative link into and out of it (paths are depth-sensitive) plus the registry, and verify no broken links remain.

**Status markers** (README and registry must agree): `ℹ️` context · `🟡` planned · `✅` implemented (add "базово"/"частично" when partial). On implementing something, flip the status in the feature README AND the registry row, and record any scope cut in `design.md`.

**Update checklist for any behaviour / input / architecture change**
1. Feature docs: relevant facet(s) + README + `design.md` (incl. scope/limits) + status.
2. `requirements/README.md` registry (row, path, "Связи").
3. `requirements/REQ-0004-keybindings.md` — if input changed.
4. `CLAUDE.md` **and** `AGENTS.md` — if scene tree, art pipeline, architecture, or conventions changed. Keep the two files' shared rule/architecture content in sync.

## Critical gotchas

**GridMap cell centering.** `chunk.tscn` sets `cell_center_y = false` while X/Z stay true. This keeps the floor on Y=0. Do NOT change `cell_size.y` to wall height — wall mesh is 30 tall with an explicit `mesh_transform` Y=+15 offset. Changing `cell_size.y` or `cell_center_y` without understanding this will push the floor up and drop the player.

**Floor collision requires Transform3D in MeshLibrary shapes.** The `shapes` array in `MazeTiles.tres` is a flat `[shape, transform, ...]` list. The floor item must include `Transform3D identity` after its `BoxShape3D` — without it the floor has no collision and the player falls through.

**Chunk load order: AddChild before Setup.** `ChunkManager.LoadChunk` must call `AddChild(chunk)` before `chunk.Setup()`. `_Ready` only fires after entering the scene tree, so `gridmap` is null if Setup runs first.

**Input.UseAccumulatedInput = false.** Set in `Player._Ready`. Held-key auto-repeat on Linux/X11 starves queued `InputEventMouseMotion` events; accumulation off fixes mouse look while movement keys are held.

**Wall `uv1_scale.y` floor.** Don't push `uv1_scale.y` below ~0.05 in `MazeTiles.tres` — the vertical noise streaks fan out into "fur". Current value 0.06 is the stable maximum for vertical fluting.

## Conventions

- **No comments** unless asked. Use `GD.Print` for debug logging (not `GD.PrintErr`).
- **All game-critical behaviour goes in `_PhysicsProcess`** (fixed 60 Hz), not `_Process`. The only exception is `Minimap._Draw` (drawing) — but its cell-visit detection stays in `_PhysicsProcess`.
- Editor path is absolute; never assume it is in `$PATH`.
