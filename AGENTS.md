# AGENTS.md

Compact instruction file for OpenCode sessions. Every line answers: "Would an agent likely
miss this without help?" If not, it's left out. See also `CLAUDE.md` for the original AI
guidance (most technical detail lives there).

## Quick start (command order matters)

```bash
# 1. Build C# (MUST run after any .cs change before Godot)
dotnet build

# Editor: Godot 4.7 mono (Godot.NET.Sdk/4.7.0), a repo-local git-ignored download.
GODOT="$PWD/.bin/Godot_v4.7-stable_mono_linux_x86_64/Godot_v4.7-stable_mono_linux.x86_64"

# 2. Import assets (REQUIRED after new/changed .tscn/.tres/.glb or a fresh clone —
#    .godot/imported/ is git-ignored, so glb models are invisible until re-imported)
"$GODOT" --headless --import

# 3. Run (DISPLAY needed)
DISPLAY=:0 "$GODOT" --path .
```

## Verification (no GUI)

After any behaviour change, verify with:

```bash
dotnet build && timeout 8 "$GODOT" --headless --path . 2>&1 | grep -iE "Player|Chunk|error|exception"
```

Expected output includes `[Player] Start cell=…` and `[ChunkManager] LOAD` lines, with no
error/exception lines. For physics questions add a temporary throttled `GD.Print` inside
`Player._PhysicsProcess`, run the command, then remove the debug code.

For visual changes, temporarily save a screenshot from `_PhysicsProcess` (see `CLAUDE.md` for
the snippet), run windowed with `DISPLAY=:0`, then delete the temp code and `shot.png`.

## No tests — all verification is CLI-based

There are no unit tests, integration tests, or CI. Change verification is the headless
`grep` above for logic, or a temporary screenshot for visuals. Never add test files unless
the user asks for them.

## Critical gotchas

**GridMap cell centering.** `chunk.tscn` sets `cell_center_y = false` while X/Z stay true.
This keeps the floor on Y=0. Do NOT change `cell_size.y` to wall height — wall mesh is 30
tall with an explicit `mesh_transform` Y=+15 offset. Changing `cell_size.y` or
`cell_center_y` without understanding this will push the floor up and drop the player.

**Tile overlap (seam fix).** Floor and wall *meshes* are 3.66 wide in `MazeTiles.tres` but
GridMap `cell_size` is 3.6 and collision shapes are 3.6. This 0.03 overlap per side was added
to hide float32 precision cracks back when the maze rendered at huge world coords (~-18000);
the region façade now centres it near the origin (~±27), so the cracks no longer occur and the
overlap is merely defensive. Never resize collision shapes to match the meshes.

**Floor collision requires Transform3D in MeshLibrary shapes.** The `shapes` array in
`MazeTiles.tres` is a flat `[shape, transform, ...]` list. The floor item must include
`Transform3D identity` after its `BoxShape3D` — without it the floor has no collision and
the player falls through.

**Chunk load order: AddChild before Setup.** `ChunkManager.LoadChunk` calls
`chunk.AddChild(gridmap)` then `chunk.Setup()`. `_Ready` only fires after entering the
scene tree, so `Setup` must come after `AddChild` or `gridmap` will be null.

**Input.UseAccumulatedInput = false.** Set in `Player._Ready`. Held-key auto-repeat on
Linux/X11 starves queued `InputEventMouseMotion` events; accumulation off fixes mouse look
while movement keys are held.

**Character faces the right way via 180° Y rotation.** `Basis.LookingAt` points −Z at the
target direction. The character model's own forward is +Z, so `player.tscn` rotates the
`Character` node 180° around Y. Without this the player walks backward.

**Mini-map cell detection MUST stay in `_PhysicsProcess`** (fixed 60 Hz), not `_Process`.
At the variable render rate a cell can be skipped in a single frame.

**Wall `uv1_scale.y` floor.** Don't push `uv1_scale.y` below ~0.05 in `MazeTiles.tres` —
the vertical noise streaks fan out into "fur".

## Architecture (what filenames don't tell you)

- **MazeData.cs** is the central authority for world layout, backed by the **maze-gen region
  façade** (`PlayersWorlds.Maps`). At startup it generates one real region (footprint
  `RegionFootprintSide` = 15 cells; `RegionRecipe.Maze` + Aldous-Broder; square 1×1 cells) with
  a `NullRegionStore` (regenerated each run, no persistence). `IsFloor(wx, wz)` answers O(1)
  from the resident `RegionView` (`region.CellAt(cell).IsPassable`); outside the region = wall.
  Maze size is the region footprint (15×15), not a fixed 10000×10000 bound, and no longer a
  murmur3 hash. `WorldOffset` is computed at runtime from the region size
  (`−RegionSize × CellWorldSize / 2` ≈ −27), so it is 0 until `_Ready` builds the region.
- **Chunk streaming:** only 9 chunks (3×3, `LoadDistance=1`) are loaded. Load iterates a
  full `[-1,1]×[-1,1]` square and unload uses per-axis `Abs > LoadDistance` (Chebyshev, not
  Manhattan). Each chunk is 16×16 cells = 57.6×57.6 world units. `ChunkManager.UpdateChunks()`
  is called every `_PhysicsProcess` from `Player.cs`.
- **MazeTiles.tres** is a `MeshLibrary` with exactly 2 items: id 0 = Floor, id 1 = Wall.
- **Wall rendering — environment kits (`REQ-0022`)**: the GridMap Wall item is now a **dark
  occluder + collision box only** (flat near-black material, geometry unchanged); the *visible*
  wall surface is kit-driven **instanced rock geometry** — `Chunk.Setup(coord, chunkData,
  EnvironmentKit kit)` places rocks per wall cell (deterministic per world cell, `Chunk.CellSeed`)
  and batches them into one `MultiMeshInstance3D` per rock prototype. The kit is resolved from
  `MazeData.RegionEnvironment` (`[Export] EnvironmentId`, one value per region) via
  `EnvironmentKitRegistry`; two kits exist — `SlotCanyonKit` (red-sand, tall/tight cliffs) and
  `RavineKit` (grey photoscan, tilted/spread) — both built from `art/RockPack1/` meshes (Arnklit
  Cliffs & Rocks Pack 1, `v1_02`). Rocks are visual-only, no collision. Details:
  `requirements/REQ-0022-environment-kits/design.md`.
- **Per-region lighting (`REQ-0022`, US-22/F-54)**: each `EnvironmentKit` also carries a
  **`LightingProfile`** (`src/LightingProfile.cs`, plain data) — sun (visible/energy/color/pitch),
  ambient fill, sky colors, depth fog, and the player `HeadLight` (energy/color/range/attenuation/height/shadow — shadow enables the omni cubemap so rocks/walls block the torch, on for DarkCanyon).
  `LightingController` (`src/LightingController.cs`, `Main/LightingController`, wired to the sun /
  `WorldEnvironment` / `Player/HeadLight` by `[Export] NodePath`) resolves the resident region's kit
  in `_Ready` and applies its profile before the first frame. **DarkCanyon** = pitch-black dungeon
  (sun off, ambient off, near-black sky, subtle black fog, only the warm short-range torch);
  `SlotCanyon` = scorching sun; `Ravine` = flat overcast. Stored `main.tscn`/`player.tscn` light
  values are editor defaults only (aligned to DarkCanyon) — the controller overwrites at runtime.
  The old "glow from below" on rock undersides was flat COLOR ambient (uniform, ignores occlusion);
  disabling ambient fixed it.
- **Item system** hubs in `InventoryHud.cs` (`HUD/Inventory`) — it owns the item state machine (InWorld / InInventory / Activated). `Inventory`/`Item` = 12-slot model; `Item.Usage` is ImmediateA (`Use()`) or ActivatedB (into hand). Slot icons render `Item.BuildModel()` (glb, or a procedural polaroid for `PhotoItem`) into a `SubViewport`; same factory builds the in-world model (scale = `WorldItemSizeFraction` 0.25 × player height). Drop (`DropProjectile`) flings a "star" that lands as a `WorldItem` (static `WorldItem.All` registry). Pickup is **automatic** (no key): scan registry for nearest armed item in range with line-of-sight; `PickupProjectile` flies it back. `ItemStar` is the shared star visual.
  - **Activation / reservation** (F-18/B, F-19a): a pattern-B item stays in its slot but is flagged `_activatedItem`/`_reservedSlot`, blocking the slot; `ActivateSlot`/`Deactivate`/`DropActivated`/`ConsumeActivated` transition it. Activation plays `Player.PlayPickupGesture()` (the `Interact` clip). **Vintage camera** (REQ-0013): **LMB** (`use_activated`) opens `ViewfinderHud` — a framed **window above the player's head** (third-person view kept, no darken) showing a `SubViewport` first-person level/yaw lens view, sepia + vignette + 3→2→1 timer (`TickSeconds` 0.6667, 3× faster), forward focus ray min 1.8; on fire a `PhotoItem` is created into the reserved slot and the camera destroyed. On fire it also plays `PlayPickupGesture()` + a slot flash so the new photo is noticed. **Photo** (REQ-0017): activating it opens a **live, monochrome-sepia window centred on screen with a simple drawn frame** (`PhotoEnterHud` — one `SubViewport` `_vp` renders the captured pos/yaw with `Environment` saturation 0; a warm sepia overlay + procedural wood/brass border are drawn in `_Draw`; passing monsters show live; a 3D `polaroid_photo.glb` frame variant was tried, dropped, and the model removed); **holding W** while advancing grows the window from centre and teleports at `EnterDuration` 1.3333 s (1.5× shorter) (`UpdatePhotoEnter` → `Player.TeleportTo`, main pitch preserved) + sepia flash.
  - Requirements: `REQ-0011-inventory/`, `REQ-0012-base-item/` (+ sub-features `REQ-001{4,5,6}-...`), `REQ-0013-vintage-camera/`, `REQ-0017-photo/`. Not implemented: `Item` as `.tres` type registry, serialization, edge-screen activated indicator, photo thumbnail icon.
- **Monster system** (`REQ-0019`/`REQ-0020`): `Monster` (`src/Monster.cs`, abstract `CharacterBody3D`) is the template; `Ifrit` (`src/Ifrit.cs`) the first concrete type (fiery humanoid ifrit, contact). Static `Monster.All` registry (persistent under `Main`, not chunk-bound). Perception = vision cone + LoS ray (`CanSee`); FSM in `_PhysicsProcess` — `Cycle` (patrol) / `Threat` (chase) / `Stun` / `Distract`, priority Stun > player-visibility > distraction, no memory after disruption. Movement = **BFS over `MazeData.IsFloor` cells** (patrol restricted to a segment ≈ 1 chunk); model faces movement via `LookAt` + `ModelYawOffsetDeg` (180° — ifrit forward is +Z like the player rig). Animation (`UpdateAnim`/`PlayAttack`) plays the glb's `AnimationPlayer` clips: `IdleAnim`/`MoveAnim` (looped via `SetLoop`, speed-scaled) + `AttackAnim`/`StunAnim` (one-shot); the ifrit ships a full set — `Idle`/`Run`/`Attack`/`BeHit` (`Monster_YiFuLiTe_*`). Contact damage emits `PlayerHit` signal + `DamageHud` red flash (no health system yet). Model scaled by **height** (`TargetHeight`; `ScaleByLength` for low/long models), local-space AABB (local transform chains, not global coords — was needed against float32 loss at world −18000, still sound near the origin). **NB:** if `BuildBody` can't load `ModelPath` (e.g. `.godot/imported/*.scn` missing because assets weren't re-imported) it falls back to an *empty* `Node3D` — the monster then has collision + contact damage but no visible mesh; run `--import` to fix. `MonsterSpawner` (`Main/MonsterSpawner`) is a **minimal debug spawner** near start (real spawner is future). Hooks/TODO: `Stun()` has no trigger (future ball IDEA-0025), distraction reacts to any `WorldItem`, Ranged/Small/health/death-state are future.
- **Mob.cs** / **mob.tscn** — old charge stub, superseded by `Monster`/`Ifrit`, never spawned.
- **game_object.cs** is an empty unused placeholder. Ignore it.

## Conventions

- No comments unless asked. The codebase has none; don't add them.
- Use `GD.Print` for debug logging, not `GD.PrintErr` (the headless verification greps
  both stdout and stderr anyway).
- All game-critical behaviour is in `_PhysicsProcess`, not `_Process`. The mini-map is the
  exception — drawing lives in `_Process` but cell-visit detection stays in
  `_PhysicsProcess`.
- Editor path is absolute, not in PATH. Always use the full path to the Godot binary.
- `.claude/` is in `.gitignore` — don't put OpenCode config there.
- Requirements catalog in `requirements/` (index: `requirements/README.md`): WHAT in Russian,
  one `REQ-NNNN-<slug>/` folder per feature (README + facet files `NN-logic/ui/visual/input.md`
  + `design.md`). `requirements/TECH_SPEC.md` is the authoritative technical reference (HOW) in English.
  Full rules below.

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
