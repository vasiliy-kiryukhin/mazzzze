# Maze Prototype 1 — Technical Specification

> **Target audience:** AI coding agents. This document describes the current implementation
> in sufficient detail to recreate an equivalent game from scratch.

---

## 1. Technology Stack

| Layer | Choice | Version |
|-------|--------|---------|
| Engine | Godot | 4.6 |
| Language | C# | .NET 8.0 |
| Physics | Jolt Physics | (built-in) |
| Renderer | Forward Plus | (built-in) |
| Platform | Windows (D3D12) | - |

**Project file:** `maze-prototype-1.csproj` — Godot SDK 4.6.3, nullable enabled.

## 2. Build and Run

```bash
dotnet build                          # Debug build
dotnet build -c ExportRelease         # Release build
godot                                 # Launch editor
godot --headless                      # Run headless
```

Main scene: `res://main.tscn` (configured in `project.godot`).

## 3. Project File Tree

```
maze-prototype-1/
├── project.godot              # Engine config, input map, physics, rendering
├── main.tscn                  # Main scene - root of the game
├── player.tscn                # Player character scene (instantiated into main)
├── chunk.tscn                 # Maze chunk scene (instantiated dynamically)
├── mob.tscn                   # Enemy scene (not yet instantiated)
├── MazeTiles.tres             # MeshLibrary: Floor + Wall tiles for GridMap
├── maze-prototype-1.csproj    # C# project file
├── maze-prototype-1.sln       # Solution file
├── icon.svg / icon.webp       # App icons
├── game_object.cs             # Placeholder (unused)
├── CLAUDE.md                  # Original Claude guidance (outdated)
├── AGENTS.md                  # Compact agent instruction file (authoritative for agents)
├── art/
│   ├── AnimationLibrary_Godot_Standard.glb  # Current player: rigged humanoid + 46 anim clips
│   ├── player.glb             # Old sphere-based player model (unused)
│   ├── player.blend           # Old player source (Blender)
│   ├── body.tres              # Player body material (orange)
│   ├── eye.tres               # Player eye material (white, emissive)
│   ├── pupil.tres             # Player pupil material (black, rim)
│   ├── ifrit.glb  # Ifrit monster model (fiery humanoid, full anim set) — US-20
│   ├── mob.glb                # Old enemy model (unused; Mob stub superseded)
│   ├── mob.blend              # Enemy source (Blender)
│   ├── mob_body.tres          # Enemy body material (blue)
│   ├── mob_eye.tres           # Enemy eye material (red, emissive)
│   └── House In a Forest Loop.ogg  # Background music
├── requirements/             # Requirements catalog (WHAT) + this TECH_SPEC (HOW)
│   ├── README.md             # Catalog index/registry (feature → US → F-ID → status → file)
│   ├── TECH_SPEC.md          # This file (HOW, authoritative implementation reference)
│   ├── REQ-0000-vision.md    # Product vision
│   ├── REQ-0001-user-journey.md  # Narrative user journey
│   ├── REQ-0002-non-functional.md # Non-functional requirements (US-09)
│   ├── REQ-0003-core/        # Core maze game (US-01..09, F-01..08) — IMPLEMENTED
│   ├── REQ-0010-minimap/     # Mini-map (US-10, F-09..11) — IMPLEMENTED
│   ├── REQ-0011-inventory/   # Inventory (US-11, F-12..14) — IMPLEMENTED
│   ├── REQ-0012-base-item/   # Base item entity (US-12, F-15..19a) — IMPLEMENTED (base)
│   │   ├── REQ-0014-base-item-item-in-world/  # InWorld state (F-24..25)
│   │   ├── REQ-0015-base-item-drop/           # Drop (F-26..28)
│   │   └── REQ-0016-base-item-pickup/         # Pickup (F-29..30)
│   ├── REQ-0013-vintage-camera/ # Vintage camera (US-13, F-20..23) — IMPLEMENTED
│   ├── REQ-0017-photo/       # Photo portal item (US-17, F-31..34) — IMPLEMENTED
│   ├── REQ-0018-localization/ # i18n (US-18, F-35..38) — planned
│   └── REQ-0019-base-monster/ # Monster template (US-19, F-39..43) — IMPLEMENTED
│       └── REQ-0020-base-monster-ifrit/ # Ifrit (US-20, F-44..46) — IMPLEMENTED
└── src/
    ├── Player.cs              # Player controller
    ├── MazeData.cs            # Maze world data & procedural generation
    ├── ChunkManager.cs        # Chunk loading/unloading orchestrator
    ├── Chunk.cs               # Single chunk - GridMap filler from cell data
    ├── Minimap.cs             # Mini-map HUD widget (Control, procedural _Draw)
    ├── MinimapState.cs        # Mini-map fog-of-war / exploration memory
    ├── Inventory.cs           # 12-slot backpack data model
    ├── Item.cs                # Item entity (data) + BuildModel factory; ItemUsage A/B
    ├── PhotoItem.cs           # Photo subclass: captured pos/yaw/pitch + procedural polaroid
    ├── InventoryHud.cs        # Item system hub (Control): states, transitions, input, draw
    ├── WorldItem.cs           # InWorld item representation + registry
    ├── DropProjectile.cs      # Drop star (inventory → world)
    ├── PickupProjectile.cs    # Pickup star (world → inventory)
    ├── ItemStar.cs            # Shared emissive star visual
    ├── ViewfinderHud.cs       # Vintage-camera viewfinder + timer + focus (Control)
    ├── PhotoEnterHud.cs       # Photo walk-into progress vignette + teleport flash (Control)
    ├── Monster.cs             # Base monster template: perception, FSM, pathfinding, contact (US-19)
    ├── Ifrit.cs              # First concrete monster: fiery humanoid, contact (US-20)
    ├── MonsterSpawner.cs      # Minimal debug spawner (places Ifrit near start)
    ├── DamageHud.cs           # Red hit-flash overlay (no health system yet)
    └── Mob.cs                 # Old enemy stub (superseded by Monster/Ifrit, unused)
```

## 4. Scene Hierarchy (Runtime)

```
Main (Node3D)                              - main.tscn, root
├── Ground (StaticBody3D)                  - collision floor, Y=-0.5
│   ├── CollisionShape3D                   - BoxShape3D(256, 1, 256)
│   └── MeshInstance3D                     - BoxMesh(256, 1, 256), green-brown
├── DirectionalLight3D                     - "sun"; energy/color/angle/visibility set per region by LightingController (§5.7) - OFF for DarkCanyon (default)
├── MazeData (Node + MazeData.cs)         - Singleton, procedural world data
├── Player (CharacterBody3D)              - instance of player.tscn
│   ├── ModelPivot (Node3D, Y=-0.2)       - faces movement direction
│   │   └── Character (AnimationLibrary_Godot_Standard.glb, scale=1.0) - rigged humanoid + AnimationPlayer
│   ├── CollisionShape3D (Y=0.35)         - SphereShape3D, radius=0.3
│   ├── HeadLight (OmniLight3D, Y=4.0)    - travels with player; energy/color/range/attenuation set per region by LightingController (§5.7)
│   └── CameraYaw (Node3D, Y=2.0)         - horizontal orbit, elevated rig
│       └── CameraPitch (Node3D, default -50 deg) - vertical tilt, angled down
│           └── Camera3D (Z=10, current)  - perspective, default FOV
├── ChunkManager (Node3D + ChunkManager.cs) - orchestrates chunk lifecycle
│   └── Chunk (xN, dynamic)              - instances of chunk.tscn
│       └── GridMap (cell_size=3.6,1,3.6, cell_center_y=false) - renders Floor/Wall tiles
├── MonsterSpawner (Node3D + MonsterSpawner.cs) - minimal debug spawner (§5.8)
├── Ifrit (xN, CharacterBody3D + Ifrit.cs) - monsters, spawned under Main (persistent) (§5.8)
├── LightingController (Node + LightingController.cs) - applies the region kit's LightingProfile to sun/env/HeadLight (§5.7)
├── WorldEnvironment                      - procedural sky; ambient/sky/fog set per region by LightingController (§5.7)
└── HUD (CanvasLayer)
    ├── Minimap (Control + Minimap.cs)    - top-left mini-map overlay (§5.10)
    ├── Inventory (Control + InventoryHud.cs) - bottom-right inventory + item-system hub (§5.11)
    ├── Viewfinder (Control + ViewfinderHud.cs) - camera viewfinder overlay (§5.11)
    ├── PhotoEnter (Control + PhotoEnterHud.cs) - photo walk-into vignette/flash (§5.11)
    └── DamageFlash (Control + DamageHud.cs) - red hit-flash (created by spawner) (§5.8)
```

## 5. Subsystem Specifications

### 5.1 MazeData - World Data and Procedural Generation

**File:** `src/MazeData.cs`
**Type:** `Node` (singleton via `Instance` static property)
**Initialization:** `_EnterTree()` sets `Instance = this`; `_Ready()` prints debug info.

**Constants:**

| Constant | Value | Meaning |
|----------|-------|---------|
| RegionFootprintSide | 15 | Region footprint per side, in world (Block) cells — the maze size |
| RandomizeEachLaunch | true | Fresh maze each run (seed from `Time.GetTicksUsec()`); false → use `FixedSeed` |
| FixedSeed | 12345 | Reproducible seed when `RandomizeEachLaunch = false` |
| CellWorldSize | 3.6f | World units per maze cell = corridor width = 6 x player diameter (0.6) |
| WallHeight | 30.0f | Wall height in world units (towering canyon walls) |

**Computed Properties (runtime, from the generated region):**

| Property | Formula | Value (15×15 footprint) |
|----------|---------|-------|
| RegionSize | region `Size` (Vector2I); `Zero` before `_Ready` | (15, 15) |
| WorldOffsetX | -RegionSize.X * CellWorldSize / 2 | ≈ -27 |
| WorldOffsetZ | -RegionSize.Y * CellWorldSize / 2 | ≈ -27 |
| PlayerStartCell | region **Entrance** POI cell (varies per seed) | e.g. (9, 3) |

**Environment kit selection (`REQ-0022`, US-22 / F-51):**

| Member | Type | Meaning |
|--------|------|---------|
| `RegionEnvironment` | `[Export] EnvironmentId` | Which environment kit the region uses — `SlotCanyon`, `Ravine`, or `DarkCanyon` (default). One value for the whole region; picks both the wall-rock look **and** the lighting mood (§5.7). Flip in the editor to A/B the kits. |
| `WorldSeed` | `int` (public get, private set) | The region's generation seed, captured in `_Ready`. Consumed by `Chunk.CellSeed` so wall-rock placement is deterministic per world cell (see 5.3). |

`ChunkManager.LoadChunk` reads `MazeData.Instance.RegionEnvironment` and resolves the corresponding
kit via `EnvironmentKitRegistry.Get(...)` once per chunk load, passing it into `Chunk.Setup`. See
[`requirements/REQ-0022-environment-kits/design.md`](./REQ-0022-environment-kits/design.md) for the
full `EnvironmentKit`/`SlotCanyonKit`/`RavineKit`/`EnvironmentKitRegistry` design.

`WorldOffset` is no longer a fixed `-18000` constant — it is derived from the region size and
returns 0 until `_Ready()` builds the region (nothing may read it earlier). The region is
centred at the world origin: a 15×15 footprint maps to world X/Z ≈ [-27, +27].

**Map source — maze-gen region façade (replaces the old hash):**

`MazeData` no longer computes cells from a murmur3 hash. In `_Ready()` it builds **one real
region** via the maze-gen library (`PlayersWorlds.Maps`):

```
new World(new NullRegionStore(), seed,
          new Vector(RegionFootprintSide, RegionFootprintSide),
          RegionRecipe.Maze
              .WithAlgorithm(RegionAlgorithm.AldousBroder)
              .WithRooms(2, new Vector(3, 3), new Vector(5, 5), RoomKind.Hall)
              .WithCells(1))                          // square 1×1 cells (client-owned shape)
    .GetOrCreate(new RegionAddress(new Vector(0, 0)))  // -> resident RegionView
```

- `NullRegionStore` = regenerate every launch, no persistence yet.
- Entrance/exit come from the region's **POIs** (`PoiKind.Entrance` / `PoiKind.Exit`, the
  longest-path ends); `PlayerStartCell = entrance`.
- **IsFloor(wx, wz):** O(1), answered by the region —
  `region.Contains(cell) && region.CellAt(cell).IsPassable`. Outside the region reads as wall.
  No global array is stored (the region is the resident data).

**Chunk Data API - GetChunkData(chunkX, chunkZ, chunkSize):**

Returns `int[chunkSize, chunkSize]` where 0=floor, 1=wall. Iterates over the chunk's cell range and calls IsFloor() for each. Cells outside the region return 1 (wall).

### 5.2 ChunkManager - Dynamic Chunk Streaming

**File:** `src/ChunkManager.cs`
**Type:** `Node3D`

**Constants:**

| Constant | Value | Meaning |
|----------|-------|---------|
| ChunkSize | 16 | Cells per chunk (16x16) |
| LoadDistance | 1 | Chunk load radius (Chebyshev): 3x3 = 9 active chunks |

**State:**

| Field | Type | Purpose |
|-------|------|---------|
| activeChunks | Dictionary<string, Node3D> | Key = "{chunkX}_{chunkZ}", value = chunk instance |
| chunkScene | PackedScene | res://chunk.tscn |
| meshLibrary | MeshLibrary | res://MazeTiles.tres |

**UpdateChunks(Vector2 playerWorldPos):**

Called every frame from Player._PhysicsProcess(). Steps:

1. Convert world position -> maze cell coordinates -> chunk coordinates
2. Iterate chunk coords in range [center-LoadDistance, center+LoadDistance]
3. For each not-yet-loaded chunk -> LoadChunk()
4. Scan active chunks: if Chebyshev distance (max of per-axis abs delta) > LoadDistance -> QueueFree() + remove from dict
5. Print "[ChunkManager] UNLOAD ..." for each removed chunk

**LoadChunk(Vector2 chunkPos) (private):**

1. Call MazeData.Instance.GetChunkData(chunkX, chunkZ, 16) -> int[16,16]
2. Instantiate chunk.tscn
3. Set world position:
   - chunk.Position.X = chunkX * 16 * CellWorldSize + WorldOffsetX
   - chunk.Position.Z = chunkZ * 16 * CellWorldSize + WorldOffsetZ
4. Assign MeshLibrary to chunk
5. AddChild(chunk) - enters scene tree, _Ready() fires
6. chunk.Setup(chunkPos, chunkData) - fills GridMap with tiles
7. Store in activeChunks dict
8. Print "[ChunkManager] LOAD chunk (X,Z) size=32x32 world=(wx,wz) totalActive=N"

Each chunk covers 16x16 cells = 57.6x57.6 world units (CellWorldSize=3.6).

### 5.3 Chunk - GridMap Tile Filler

**File:** `src/Chunk.cs`
**Type:** `Node3D` with [Export] int ChunkSize=16 and [Export] MeshLibrary MeshLibrary.

**Scene (chunk.tscn):**
```
Chunk (Node3D + Chunk.cs)
└── GridMap
    mesh_library = MazeTiles.tres
    cell_size = Vector3(3.6, 1, 3.6)
    cell_center_y = false
```

**Cell centering (critical):** GridMap defaults `cell_center_x/y/z = true`, which offsets each
cell origin by `cell_size/2` on that axis. X and Z stay centered (true) so cell (n) maps to
`n*3.6 + cell_size/2` — matching the player spawn formula. **Y is set to false** so the cell
origin sits at world Y=0; the Floor tile then rests on the Y=0 plane and walls rise Y=[0..30].
`cell_size.y` is left at 1 (a neutral vertical-layer spacing) — wall height is driven by the
mesh (30 tall, offset +15), NOT by `cell_size.y`. Setting `cell_size.y` to the wall height while
`cell_center_y` is true would push the floor up by half the wall height and drop the player below it.

**Setup(Vector2 coord, int[,] chunkData, EnvironmentKit kit)** (`kit` param added by `REQ-0022`,
US-22/F-51..F-53 — see [`REQ-0022-environment-kits/design.md`](./REQ-0022-environment-kits/design.md)):

1. Store chunkCoord
2. gridmap.Clear() - remove previous tiles
3. Iterate x in [0, ChunkSize), z in [0, ChunkSize):
   - cellType = chunkData[x, z]
   - tileId = 0 if floor, 1 if wall, -1 if unknown
   - gridmap.SetCellItem(new Vector3I(x, 0, z), tileId)
   - if cellType == 1 (wall): compute `seed = CellSeed(MazeData.Instance.WorldSeed, wx, wz)`
     (world cell coords, FNV-1a) and the cell's local center; call `kit.PlaceRocks(center, seed)`
     and bucket each returned `RockPlacement` by prototype index
4. After the cell loop: for each non-empty prototype bucket, build one `MultiMeshInstance3D`
   (`Mesh = kit.Prototypes[i]`, `MaterialOverride = kit.RockMaterial`, one instance transform per
   bucketed placement) and add it as a child of the chunk

GridMap places each tile centred at the cell's world position. cell_size=(3.6,1,3.6) means adjacent cells are 3.6 world-units apart in XZ. With cell_center_y=false the floor sits on the Y=0 plane.

**Wall rock rendering (`REQ-0022`):** the GridMap wall item (id 1, below) supplies collision and
opacity only; the *visible* rock surface on top of it is built per-chunk from
`EnvironmentKitRegistry.Get(MazeData.Instance.RegionEnvironment)`, batched into at most
`kit.Prototypes.Length` `MultiMeshInstance3D`s per chunk (≤ 8 draw calls/chunk today). Placement is
deterministic per world cell (`CellSeed`), so rocks do not change or flicker as chunks stream in
and out. Full design: [`REQ-0022-environment-kits/design.md`](./REQ-0022-environment-kits/design.md).

### 5.4 MeshLibrary - Maze Tiles

**File:** `MazeTiles.tres`
**Type:** `MeshLibrary` with 2 items.

**Item 0 - Floor:**

| Property | Value |
|----------|-------|
| Mesh | BoxMesh(**3.66**, 0.2, **3.66**) - flat square, slightly larger than the 3.6 cell (see "Tile overlap" below) |
| Material | StandardMaterial3D, dark stone albedo=Color(0.2, 0.19, 0.18), roughness 0.4 + FastNoiseLite normal map (bump 1.5). World-space triplanar, uv1_scale 0.12. Fairly smooth so the distant sun reflects as a streak down the corridor. |
| Collision | BoxShape3D(3.6, 0.2, 3.6) - matches the **cell** size, not the mesh - centred at cell Y=0 |
| shapes array | `[shape, Transform3D identity]` - the Transform3D is REQUIRED: MeshLibrary `shapes` is a flat `[shape, transform, ...]` list. Without the transform the floor gets no collision and the player falls through into the void. |
| mesh_transform | Identity (Y=0, centred on floor) |
| Shadow casting | On |

**Item 1 - Wall:** dark occluder + collision box (`REQ-0022`, US-22/F-52 — see below). Prior to
`REQ-0022` this item's own `BoxMesh` carried a fluted-noise material and *was* the visible wall
surface; since `REQ-0022` the visible rock surface is a set of kit-driven `MultiMeshInstance3D`s
built by `Chunk.Setup` on top of this box (see 5.3), and this item's material was restyled to a
plain dark occluder so it no longer needs to look like rock itself — it only has to stay opaque
behind whatever gaps exist between the instanced rocks.

| Property | Value |
|----------|-------|
| Mesh | BoxMesh(**3.66**, 30, **3.66**) - tall pillar, slightly larger than the 3.6 cell (see "Tile overlap" below) |
| Material | StandardMaterial3D, flat dark occluder: `albedo_color = Color(0.05, 0.045, 0.04, 1)`, `roughness = 1.0`, `metallic_specular = 0.0`, no normal map |
| Collision | BoxShape3D(3.6, 30, 3.6) - matches the **cell** size, not the mesh - with Transform3D Y=+15 |
| mesh_transform | Transform3D Y=+15 - wall SITS ON the floor (Y=0 to 30) |
| Shadow casting | On |

**Pre-`REQ-0022` wall material (superseded, sub-resources still present but unreferenced in
`MazeTiles.tres`):** one `FastNoiseLite` (Cellular-ish, frequency 0.05, 6 fractal octaves) with
domain warp enabled (type 1, amplitude 12, frequency 0.02), baked into an albedo `NoiseTexture2D`
through a 4-stop `Gradient` and a normal-map `NoiseTexture2D`, mapped world-space triplanar with
anisotropic `uv1_scale = Vector3(0.14, 0.06, 0.14)` for vertical fluting. Kept only as orphaned
sub-resources in the `.tres` file (harmless); no longer assigned to item 1.

**Visible wall rock surface (`REQ-0022`, US-22/F-51..F-53):** built by `Chunk.Setup` per wall cell
via the region's `EnvironmentKit` (`EnvironmentKitRegistry.Get(MazeData.Instance.RegionEnvironment)`),
batched into per-prototype `MultiMeshInstance3D`s (see 5.3). Two kits exist —
`SlotCanyonKit` (8 `Cliff*` meshes, `Cliff_Material_Red_Sand.tres`, tight/tall/near-vertical) and
`RavineKit` (same 8 meshes, `Cliff_Material_Photoscan.tres`, tilted/wider-spread) — sourced from
`art/RockPack1/` (Arnklit Cliffs & Rocks v1_02). Full design, parameters, and scope limits:
[`requirements/REQ-0022-environment-kits/design.md`](./REQ-0022-environment-kits/design.md).

The Y=+15 offset (= WallHeight/2) is critical: without it, the wall BoxMesh would be centred at Y=0 (half below floor). With the offset, wall occupies Y=0 to Y=30, on top of floor tile (Y=-0.1 to Y=0.1). Walls tower far above the camera, blocking any over-the-top view of the maze.

**Tile overlap (seam fix).** The floor and wall **meshes** are 3.66 wide while the GridMap `cell_size` is 3.6, so neighbouring tiles overlap by ~0.03 per side instead of abutting exactly. **Historical reason:** the maze used to render at large world coordinates (`WorldOffset = -18000`; the player started near world (-17994, -17994)), where float32 precision is only ~0.002 — exactly-coincident tile edges there left hairline cracks showing the dark background through the floor/walls, and the small overlap covered them. **Since the region-façade refactor** the maze is centred near the origin (`WorldOffset ≈ -27`; footprint 15×15), where float32 precision is ample, so those cracks no longer occur and the overlap is now **defensive/harmless** rather than load-bearing. Because both materials use **world-space triplanar** mapping, the overlapping region samples the *same* texel on both tiles, so the coplanar z-fight is invisible — no flicker, no double-lighting. The **collision** shapes stay at the true 3.6 cell size so gameplay/clearance is unchanged (the 0.03 visual lip has no collider behind it, negligible against the 0.3 player radius). The move near the origin is effectively the "re-centred world" that a deeper precision fix would have required.

### 5.5 Player - Character Controller

**File:** `src/Player.cs`
**Type:** `CharacterBody3D` (extends from player.tscn scene)

**Exported Properties:**

| Property | Default | Purpose |
|----------|---------|---------|
| Speed | 5.0f | Movement speed (units/sec) |
| MouseSensitivity | 0.002f | Mouse look sensitivity |
| Gravity | 15.0f | Downward acceleration |
| ZoomStep | 1.0f | Mouse wheel zoom increment |
| MinZoom | 6.0f | Closest/lowest camera distance |
| MaxZoom | 14.0f | Furthest/highest camera distance (stays below 30-tall walls) |
| DefaultPitchDeg | -60.0f | Default downward camera tilt (steep, near-overhead) |
| MinPitchDeg | -85.0f | Steepest downward tilt (almost straight down) |
| MaxPitchDeg | -25.0f | Least downward tilt (reveals wall height + sky/sun) |
| CameraMargin | 0.4f | Gap kept between camera and a wall it would otherwise clip |

**Scene Hierarchy (player.tscn):**
```
Player (CharacterBody3D)
├── ModelPivot (Node3D, Y=-0.2)          - faces movement direction
│   └── Character (AnimationLibrary_Godot_Standard.glb instance, scale=1.0)
│       ├── Rig/GeneralSkeleton/Mannequin - rigged humanoid mesh
│       └── AnimationPlayer               - 46 clips; Idle/Walk driven by Player.cs
├── CollisionShape3D (Y=0.35)            - SphereShape3D, radius=0.3
├── HeadLight (OmniLight3D, Y=4.0)       - local fill light, follows the player
└── CameraYaw (Node3D, Y=2.0)            - mouse X: RotateY
    └── CameraPitch (Node3D, init -60 deg) - mouse Y: RotateX (clamped)
        └── Camera3D (Z=10, current=true) - perspective, distance auto-shortened to avoid walls
```

**Player Model & Animation:**
- Source: art/AnimationLibrary_Godot_Standard.glb (rigged humanoid "Mannequin" + AnimationPlayer with 46 clips). Replaced the old sphere-based art/player.glb.
- Native mesh bounds (rest pose): ~1.94 wide x 1.83 tall x 0.37 deep. Applied scale: 1.0 (≈1.83 units tall — roughly human, fits the 3.6-wide corridor).
- ModelPivot Y offset: -0.2 - drops the model so the animated feet rest on the floor (tuned visually; the Idle foot plane sits above the model origin).
- Animation: Player.cs caches `ModelPivot/Character/AnimationPlayer` and cross-fades `Idle` <-> `Walk` (clip names + blend exported as IdleAnim/WalkAnim/AnimBlend). `PlayAnim(name)` no-ops if the clip is already current. Swap WalkAnim to `Jog_Fwd`/`Sprint` if foot-sliding appears at higher Speed.
- Collision sphere: radius 0.3, centred at Player.Y+0.35, bottom at Player.Y+0.05 (unchanged — covers the lower body only; the tall mesh has no upper-body collider, which is fine for the narrow top-down corridors)

**Camera System - Dual-Node Orbit (elevated, top-down-angled) + spring arm:**

- CameraYaw (Node3D, Y=2.0): rotates around world Y axis via RotateY(-mouse_dx * sensitivity). Horizontal orbit.
- CameraPitch (Node3D, child of CameraYaw): rotates around local X axis via RotateX(-mouse_dy * sensitivity). Initialised to DefaultPitchDeg (-60 deg) in _Ready(); clamped to [MinPitchDeg, MaxPitchDeg] = [-85 deg, -25 deg]. Always tilted steeply downward.
- Camera3D (child of CameraPitch, local Z=zoom): desired distance set by mouse wheel (6..14), actual distance shortened each physics frame by the spring arm (see below).

Why this works: CameraYaw rotates the entire pitch+camera assembly horizontally. CameraPitch tilts down. With the camera at local +Z and a negative pitch theta, the camera is lifted UP by zoom*sin(-theta) and pushed BEHIND by zoom*cos(-theta), looking down at the player — a high, slightly-behind, angled-down view.

**Why steep (default -60 deg):** the corridor is only 3.6 wide (half-width 1.8). The camera's horizontal offset behind the player is zoom*cos(pitch). A shallow pitch pushes the camera sideways out of the player's open cell column and INTO the neighbouring wall (which renders as a black/single-face view). A steep pitch keeps the camera in the open air directly above the player's cell. The spring arm handles the residual cases.

**Spring arm (UpdateCameraCollision, code-based):** every physics frame a ray is cast from the pivot (CameraPitch.GlobalPosition, ~2 units above the player) toward the desired camera position (pivot + pitch-basis +Z * zoom), collision mask 1, excluding the player's own body. If it hits a wall the camera is moved to the hit point minus CameraMargin (0.4); otherwise it eases back out to the full zoom (snap-in to never reveal a wall, MoveToward-out at 12 u/s to avoid popping). This guarantees the player stays framed and the camera never sits inside a wall.

Camera height: walls are 30 tall. At default (zoom 10, pitch -60 deg) the un-clipped camera is at world Y = 2.0 + 10*sin(60) = 10.66, behind by 10*cos(60) = 5.0 — well below the wall tops, so the maze layout is never visible from above; the tall walls fill the frame and recede toward a narrow sky strip with the sun. In a narrow corridor the spring arm pulls it closer, keeping the player large and centred.

**Movement Logic (_PhysicsProcess):**

1. Gravity: if !IsOnFloor(), Velocity.Y -= Gravity * dt
2. Input: Input.GetVector("move_left", "move_right", "move_forward", "move_back") returns Vector2(-1..1, -1..1)
3. Camera-relative direction:
   - camForward = -CameraYaw.GlobalBasis.Z (world forward)
   - camRight = CameraYaw.GlobalBasis.X (world right)
   - moveDir = (camForward * -input.Y + camRight * input.X).Normalized()
   - Note: input.Y is negated because GetVector returns -1 for the "negative Y" action (move_forward, i.e. W key). So -input.Y is +1 when W is pressed.
4. Velocity: vel.X = moveDir.X * Speed; vel.Z = moveDir.Z * Speed
5. Model facing: ModelPivot.Basis = Basis.LookingAt(moveDir.WithY(0), Vector3.Up)
6. Physics: MoveAndSlide() handles collision with floor and walls
7. Chunk update: ChunkManager.UpdateChunks(new Vector2(GlobalPosition.X, GlobalPosition.Z))
8. Camera spring: UpdateCameraCollision(dt) raycasts pivot->camera and shortens the camera distance if a wall is in the way (see Camera System above)

**Start Position:** (`PlayerStartCell` = the region's entrance POI, so the exact cell varies per seed)
```
Position = (PlayerStartCell.X * CellWorldSize + WorldOffsetX + CellWorldSize/2,
            0.3,   // just above the floor (top Y~0.1); settles onto it
            PlayerStartCell.Y * CellWorldSize + WorldOffsetZ + CellWorldSize/2)
   e.g. entrance (9, 3), WorldOffset ≈ -27:
         = (9*3.6 + (-27) + 1.8, 0.3, 3*3.6 + (-27) + 1.8)
         = (7.2, 0.3, -14.4)
```
The +CellWorldSize/2 centres the player within the cell: with cell_center_x/z=true, GridMap places the cell (and its floor tile) centre at cell_index * cell_size + cell_size/2, so the spawn formula lands exactly on the floor-tile centre of the entrance cell.

**Input Map (project.godot):**

| Action | Keys (physical) | Keys (logical) | Gamepad |
|--------|-----------------|----------------|---------|
| move_left | A(65), LeftArrow(4194319) | - | Button 2 |
| move_right | D(68), RightArrow(4194321) | - | Button 1 |
| move_forward | W(87), UpArrow(4194377) | - | Button 3 |
| move_back | S(83), DownArrow(4194376) | - | Button 4 |
| jump | Space(32) | - | Button 0 |
| minimap_toggle | Tab(4194306) | - | - |

Dead zone: 0.2 (0.5 for minimap_toggle). Mouse captured (Input.MouseMode = Captured).
Mouse wheel zooms the camera (plain) or the mini-map (with Ctrl held) — see §5.9.

### 5.6 Ground - Floor Collision

Scene node in main.tscn:
- Ground (StaticBody3D, Y=-0.5)
  - CollisionShape3D: BoxShape3D(256, 1, 256), identity transform
  - MeshInstance3D: BoxMesh(256, 1, 256), green-brown material

Ground collision spans Y=[-1.0, 0.0]. Top surface at Y=0. Provides flat floor across 256x256 playable area. Maze is 20000x20000. For outer areas, GridMap floor tile collision provides walking surface.

### 5.7 Lighting and Environment

**Per-region lighting (`REQ-0022`, US-22 / F-54).** Lighting is a property of the region's
`EnvironmentKit`, not a fixed scene setup — so one region can be a pitch-black dungeon while another
bakes under a scorching sun. Each kit exposes a **`LightingProfile`** (`src/LightingProfile.cs`,
plain data); **`LightingController`** (`src/LightingController.cs`, `Main/LightingController`,
wired to the sun / `WorldEnvironment` / `Player/HeadLight` via three `[Export] NodePath`s) resolves
the resident region's kit in `_Ready` — `EnvironmentKitRegistry.Get(MazeData.Instance.RegionEnvironment).Lighting`
— and applies it before the first frame (no flash of editor defaults). The values stored in
`main.tscn` / `player.tscn` are editor defaults only (aligned to DarkCanyon); the controller
overwrites them at runtime.

`LightingProfile` fields (all tunable per kit):

| Field | Governs | Notes |
|-------|---------|-------|
| `SunVisible` / `SunEnergy` / `SunColor` / `SunPitchDeg` / `SunYawDeg` | DirectionalLight3D | `SunVisible=false` makes the region dark; pitch is elevation (more negative = higher sun). |
| `AmbientColor` / `AmbientEnergy` | Environment ambient | `AmbientEnergy <= 0` → ambient **Disabled**. Flat COLOR ambient is uniform and ignores occlusion, so it lights downward-facing rock undersides ("glow from below") — disabling it is what makes undersides go dark. |
| `SkyTopColor` / `SkyHorizonColor` / `SkyEnergy` | `ProceduralSkyMaterial` background | There is no ceiling geometry, so the sky is visible looking up; a dungeon needs it near-black. |
| `FogEnabled` / `FogColor` / `FogDensity` | Environment depth fog | Fades distance to black so the torch doesn't end in a hard `omni_range` ring. |
| `HeadLightEnergy` / `HeadLightColor` / `HeadLightRange` / `HeadLightAttenuation` / `HeadLightHeight` / `HeadLightShadow` | Player OmniLight3D | `Range` is the hard cutoff; `Attenuation` shapes falloff (>1 pulls brightness in toward the player, <1 keeps it bright far out). `Height` = local Y above the player: higher grazes close vertical walls (dimmer walls) while the horizontal floor pool stays lit. `Shadow` enables the omni cubemap shadow so rocks + wall occluder boxes actually block the torch (rocks stop lighting whatever is behind them) — costs a per-frame shadow render, so it is off by default and on only for DarkCanyon. |

Region profiles today:
- **DarkCanyon** (default): sun **off**, ambient **off**, near-black sky (energy 0.12), subtle black fog (density 0.035). HeadLight warm `(1,0.945,0.83)`, energy 2.5, range 22.5, attenuation 1.25, height 4.0, **shadow on** — a soft pool reaching ~4–5 cells that fades to black, with rocks/walls casting shadows so the torch doesn't shine through them. The pitch-black dungeon.
- **SlotCanyon**: scorching sun (energy 3.0, warm `(1,0.9,0.72)`, pitch -62), warm ambient fill (0.45), bright sky, no fog; HeadLight dimmed to 0.5 (the sun already lights everything).
- **Ravine**: overcast — soft cool sun (energy 1.2, pitch -55), grey ambient (0.5), grey sky, faint grey haze (density 0.012); mid HeadLight (1.0).

**Unchanged scene defaults** (the controller does not touch these): tonemap Filmic/ACES (mode 3, white 6.0), glow/bloom (intensity 0.7, strength 1.15, bloom 0.2, hdr_threshold 0.95), reflected light source Disabled, SDFGI disabled.

### 5.8 Monsters - Base Template and Ifrit (US-19 / US-20)

**Files:** `src/Monster.cs` (abstract template), `src/Ifrit.cs` (concrete), `src/MonsterSpawner.cs`, `src/DamageHud.cs`.
**Requirements:** `REQ-0019-base-monster/` (F-39..43), `REQ-0020-base-monster-ifrit/` (F-44..46). See those `design.md` for full detail.

**`Monster` (abstract `CharacterBody3D`)** — the template; a concrete type sets params in its ctor.
- **Registry (F-43):** static `Monster.All`, add/remove in `_EnterTree`/`_ExitTree` (mirrors `WorldItem.All`). Monsters live under `Main` → **persistent**, not chunk-bound.
- **Perception (F-40):** `CanSee(target)` = in vision cone (`VisionRange` + `VisionFovDeg` around current facing) **and** clear LoS — `DirectSpaceState.IntersectRay` from eyes, wall mask 1, excluding self + player. Same check finds the player and lure items.
- **FSM (F-41)** in `_PhysicsProcess`: `Cycle` (patrol) / `Threat` (chase) / `Stun` / `Distract`. Priority Stun > player-visibility > distraction; no memory after disruption (→ `Cycle`).
- **Movement:** BFS pathfinding over `MazeData.IsFloor` cells (`FindPath`), following cell centres + direct final-approach; patrol restricted to a segment. Gravity + `MoveAndSlide`.
- **Contact damage (F-42):** planar touch distance, throttled by `ContactInterval`; emits `PlayerHit(damage)` signal + `DamageHud` red flash + log. No health system yet (monster only reports the hit).
- **Model:** `BuildBody` instantiates the type's glb (via `GD.Load<PackedScene>(ModelPath)`; **if the load fails — e.g. `.godot/imported/*.scn` missing because assets weren't re-imported — it falls back to an *empty* `Node3D`**, giving a monster with collision + contact damage but no visible mesh; run `--import` to fix), computes a **local-space** AABB (local transform chains, not global coords — historically avoided float32 loss at world −18000, still sound near the origin), scales by **height** (`TargetHeight`; `ScaleByLength` → by horizontal span for low/long models), grounds it, adds a capsule collider on layer 1 (blocks the player). `ModelUprightPitchDeg` corrects a mis-authored up-axis; `ModelPivot` faces movement via `LookAt` + `ModelYawOffsetDeg` (180° for the ifrit — forward +Z like the player rig).
- **Animation:** `UpdateAnim`/`PlayAttack` drive the model's `AnimationPlayer` — `IdleAnim` (still) / `MoveAnim` (moving, looped + speed-scaled) / `AttackAnim` (one-shot on contact) / `StunAnim` (one-shot on `Stun()`). Idle/Move loops forced via `SetLoop` (glb clips import one-shot). The ifrit ships a full set: `Idle`, `Run`, `Attack`, `BeHit` (`Monster_YiFuLiTe_*`).

**`Ifrit`** — fiery humanoid demon, contact delivery. Defaults: vision 18 wu / 100°, patrol 2.0 / chase 4.0 wu/s, damage 10, chase-drop 57.6 wu (1 chunk), contact 0.7 s, stun 2.5 s, segment 16 cells, height 2.4, `art/ifrit.glb`.

**`MonsterSpawner`** (`Main/MonsterSpawner`) — minimal **debug** spawner: places a few Ifrit near the player start and creates the `DamageHud`. A real spawner is a future feature.

**Not implemented / hooks:** `Stun()` is public but has no trigger yet (future tennis ball, IDEA-0025); distraction reacts to any `WorldItem` (no dedicated lure type); Ranged delivery, Small size, player health system, and a death state (the `Death` clip is unused) are future. The old `Mob.cs`/`mob.tscn` charge stub is **superseded** (present, unused).

### 5.9 Art Assets

| File | Type | Purpose |
|------|------|---------|
| art/AnimationLibrary_Godot_Standard.glb | GLTF binary | **Current** player: rigged humanoid + AnimationPlayer (Idle/Walk/Jog/Sprint/…) |
| art/player.glb | GLTF binary | Old sphere-based player model (unused) |
| art/player.blend | Blender source | Old player model source |
| art/body.tres | SpatialMaterial | Player body - orange (#E85A00), roughness 0.5 |
| art/eye.tres | SpatialMaterial | Player eye - white (#DBDBDB), metallic, emissive |
| art/pupil.tres | SpatialMaterial | Player pupil - black, roughness 0.3, rim effect |
| art/mob.glb | GLTF binary | Enemy model |
| art/mob.blend | Blender source | Enemy model source |
| art/mob_body.tres | SpatialMaterial | Enemy body - blue (#0F447D), roughness 0.43 |
| art/mob_eye.tres | SpatialMaterial | Enemy eye - red (#C21D30), metallic, emissive |
| art/House In a Forest Loop.ogg | OGG audio | Background music (not yet integrated) |

All .blend import disabled (filesystem/import/blender/enabled=false).

### 5.10 Mini-map (HUD)

**Files:** `src/Minimap.cs` (`Control`), `src/MinimapState.cs` (plain class).
**Scene:** `main.tscn` → `HUD` (`CanvasLayer`) → `Minimap` (`Control`, top-left, `mouse_filter=2/Ignore`).
**Requirements:** `requirements/REQ-0010-minimap/` (US-10, F-09, F-10, F-11).

A procedurally `_Draw`-rendered overlay over the streaming maze. No textures/scenes —
everything is drawn with `DrawCircle`/`DrawRect`/`DrawArc`/`DrawColoredPolygon`.

**Exploration memory — `MinimapState`:**
- FIFO `Queue<Vector2I>` of the last **1000** entered cells (`BufferCapacity`).
- Each visit reveals a `(2*RevealRadius+1)²` = **3×3** neighbourhood (`RevealRadius=1`),
  tracked by a `Dictionary<Vector2I,int>` reference count, so corridor + adjacent walls
  become visible. When the oldest visit is evicted its 3×3 contribution is decremented and
  the fog only re-closes over cells no longer covered by any remaining visit (trail fades
  from the tail).
- Entrance/exit cells, once entered, are added to a separate `_permanent` set — revealed
  forever (kept outside the FIFO) and used to gate the entrance/exit markers
  (`IsPermanentlyRevealed`, distinct from the neighbourhood-based `IsRevealed`).
- Purely in-memory; recreated each launch (no persistence — save/restore is a future doc).

**Widget / rendering — `Minimap`:**
- Square, side = `ScreenWidthFraction` (0.18) × viewport width, at top-left (`Margin` 16),
  resized every `_Process`. Drawn as a circle (parchment-toned fog disc + ink border ring).
- Per visible cell within the circle: fog (skip) if not revealed; else **near zone**
  (Chebyshev distance ≤ `NearRadius` = 7 ⇒ 15×15) renders per-cell floor (light) / wall
  (dark) via `MazeData.IsFloor`; **far** revealed cells render as a flat schematic
  silhouette colour (no per-cell detail). Cells overlap ~0.6px to hide rotation seams.
- **Rotation:** the whole map is drawn under `DrawSetTransform(center, φ, …)`.
  `φ = -π/2 - atan2(fwd.y, fwd.x)` maps `fwd` to screen-up. `fwd` = the player's planar
  camera-forward (default, "forward = up") or world-north `(0,-1)` when toggled. The player
  arrow points along `Player.PlanarFacing`; the entrance/exit arches sit at their cells.
- **Cell-visit detection runs in `_PhysicsProcess`** (fixed 60 Hz): between ticks the player
  moves ≤ Speed·dt ≈ 0.08 u ≪ 3.6-unit cell, so no entered cell is ever skipped. (Doing it
  in `_Process` at render rate can skip a cell the player crosses in a couple of physics
  ticks.) `_Process` only handles sizing + `QueueRedraw`.

**Input — `_Input` (F-11):**
- `minimap_toggle` (Tab) flips the orientation mode (camera-up ↔ north-up).
- **Ctrl + wheel** zooms (`_cellsRadius`, the cells-from-centre-to-edge), clamped to
  `[MinCellsRadius 5, MaxCellsRadius 28]` so you can neither bottom out on a single cell nor
  see the whole maze. Consumes the event (`SetInputAsHandled`); `Player` additionally ignores
  wheel events while Ctrl is held, so plain wheel still zooms the camera.

> **TODO (F-09 visual style, next version):** this first pass is functional styling
> (flat parchment palette, solid fog, simple arches). The full F-09 look — procedural
> parchment **texture**, soft "burnt" fog edges, **hatched** walls, decorative arch icons —
> is deferred. The behaviour above is complete and agreed as the first pass.

### 5.11 Item System (Inventory · Item · Camera · Photo)

**Files:** `src/Inventory.cs` (12-slot data), `src/Item.cs` (+`PhotoItem.cs` subclass),
`src/InventoryHud.cs` (hub `Control`), `src/WorldItem.cs`, `src/DropProjectile.cs`,
`src/PickupProjectile.cs`, `src/ItemStar.cs`, `src/ViewfinderHud.cs`, `src/PhotoEnterHud.cs`.
**Scene:** `HUD` (`CanvasLayer`) → `Inventory`, `Viewfinder`, `PhotoEnter` (`Control`s).
**Requirements:** `requirements/REQ-0011-inventory/`, `requirements/REQ-0012-base-item/`
(+ sub-features REQ-0014/0015/0016), `REQ-0013-vintage-camera/`, `REQ-0017-photo/`.

**States (no state field — location implies state):** *InWorld* = a `WorldItem` in the
`WorldItem.All` registry; *InInventory* = an `Item` in an `Inventory` slot; *Activated* =
still in its slot **and** referenced by `InventoryHud._activatedItem`/`_reservedSlot`, which
blocks the slot (reservation, F-19a). Transitions live in `InventoryHud`
(`ActivateSlot`/`Deactivate`/`DropActivated`/`ConsumeActivated`/`ApplySlot`/`StartPickup`/
`DropSlot`). See [REQ-0012 design.md](./REQ-0012-base-item/design.md).

**Usage patterns (F-18):** `Item.Usage` = `ImmediateA` (`Item.Use()` from the grid) or
`ActivatedB` (into hand, then a second gesture). Selecting a cell activates B-items and
toggles deactivation on the reserved cell; `use_activated` (LMB) drives the camera.

**Item model / icons:** `Item.BuildModel()` is a virtual factory (glb by `ModelPath`, or a
procedural node — `PhotoItem` overrides it with a placeholder polaroid). Both slot icons
(`InventoryHud.BuildIcon`, rendered via a per-item `SubViewport`→`ViewportTexture`) and the
in-world model (`WorldItem.Setup`) call it. In-world size = `WorldItemSizeFraction` (0.25) ×
`PlayerHeight`.

Activation plays a one-shot pickup gesture via `Player.PlayPickupGesture()` (the `Interact`
clip, `PickUpAnim`), which overrides locomotion until it finishes.

**Vintage camera (REQ-0013):** `ViewfinderHud` runs a `Phase{Inactive,Blocked,Counting}` FSM
in `_PhysicsProcess`. It draws a framed **window above the player's head** (screen-projected
`Player.HeadAnchor`; **third-person view kept, no darken**) containing a `SubViewport`
(`World3D = player.GetWorld3D()`) + eye-level `Camera3D` rendering a **horizontal (level, yaw)**
lens view. Focus (F-23) = a forward horizontal ray, min `FocusMinDistance` 1.8 (3×0.6); blocked
before start or lost mid-count resets without consuming. Timer (F-22) 3→2→1 at `TickSeconds` 2.
Mouse pitch stays free (third-person). On fire → `InventoryHud.OnCameraFired` builds a
`PhotoItem` and `ConsumeActivated`s the camera into its reserved slot.

**Photo (REQ-0017):** `PhotoItem` stores immutable `CapturedWorldPos` (XZ), `CapturedYawDeg`,
`CapturedPitchDeg` (the main top-down camera's pitch, so the normal view is preserved after
teleport). Activating it opens `PhotoEnterHud.BeginPreview`: a `SubViewport` `Camera3D` parked at
`CapturedWorldPos`/yaw renders that location **live** (a passing monster shows) in a window above
the head. `InventoryHud.UpdatePhotoEnter` accumulates progress while the photo is activated,
`move_forward` is held, and `Velocity·PlanarCamForward > Speed*0.4` (real advance, not wall-
blocked); the window **grows** toward centre. At `EnterDuration` 2 s → `Player.TeleportTo(pos,
yaw, pitch)` (stands on floor at Y=0.3, re-streams chunks), `PhotoEnterHud.Flash()` (sepia),
`ConsumeActivated(null)`. Live preview is limited to currently-streamed chunks.

## 6. Physics Configuration

- Engine: Jolt Physics
- Player collision: sphere radius 0.3, layer 1, mask 1
- Ground collision: static box 256x1x256, layer 1 (default)
- Wall collision: GridMap-generated BoxShape3D(3.6, 30, 3.6) per wall tile, offset Y=+15
- Floor collision: GridMap-generated BoxShape3D(3.6, 0.2, 3.6) per floor tile
- Movement: CharacterBody3D.MoveAndSlide() with built-in sliding collision

## 7. Coordinate Systems

**Maze Cell Coordinates:**
- Origin: (0, 0) at top-left corner of the region
- X-axis: east (increasing column index)
- Z-axis: south (increasing row index)
- Range: [0, RegionSize-1] in both axes (currently [0, 14] for the 15×15 footprint)
- Entrance / exit = the region's `Entrance` / `Exit` POIs (vary per seed)

**World Coordinates:**
- Origin: centre of the region
- X-axis: east; Y-axis: up; Z-axis: south (Godot convention: -Z = forward)
- Maze cells map to world via:
  - worldX = cellX * CellWorldSize + WorldOffsetX + CellWorldSize/2
  - worldZ = cellZ * CellWorldSize + WorldOffsetZ + CellWorldSize/2
  - where WorldOffsetX = WorldOffsetZ ≈ -27 (= -RegionSize * CellWorldSize / 2), CellWorldSize = 3.6
- Maze extends from world X/Z ≈ [-27, +27] (15×15 footprint)
- Floor surface Y = 0; walls occupy Y = [0, 30]

**Chunk Coordinates:**
- Chunk (0, 0) covers cells [0, 15]; a 15×15 region fits within a single chunk's span
- The chunk grid is unbounded in principle; only cells inside the region are floor
- At any moment, up to 9 chunks are active (LoadDistance=1 -> 3x3 grid around player)

## 8. Key Design Decisions

1. **Maze-gen region façade** - the map is one generated region (`PlayersWorlds.Maps`, Aldous-Broder) resident in memory, not a coordinate hash. It replaced the old stateless murmur3 `IsFloor()` (which needed a huge 10000×10000 finite bound to feel infinite); a real, small region gives a proper connected maze with genuine entrance/exit POIs and rooms. Centring it near the origin also eliminated the float32-precision seam problem of the old -18000 coordinates.

2. **Client-owned cell shape** - the recipe pins **square 1×1 cells** (`.WithCells(1)`), so the generator's abstract cells render as square tiles; the game (not the library) decides world scale via `CellWorldSize`.

3. **Regenerate each launch** - `NullRegionStore` + `RandomizeEachLaunch` give a fresh maze every run (no persistence yet); set `RandomizeEachLaunch = false` with `FixedSeed` for a reproducible layout.

4. **CellWorldSize = 3.6** - corridors are 3.6 world-units wide = 6x the player diameter (0.6), giving a wide, comfortable canyon-like passage with ~42% clearance on each side. Wall thickness equals corridor width (one cell).

4a. **WallHeight = 30** - walls tower far above the (max ~15.5-high) camera, so the maze layout can never be seen from above. The tall walls plus a grid-aligned ~42 deg sun produce dramatic canyon shafts of light, a narrow strip of sky overhead, and the sun disk glowing high at the end of long straight corridors.

5. **Dual-node orbit camera** - avoids gimbal lock. Yaw-pitch decomposition means camera orbits cleanly regardless of orientation.

6. **Camera elevated + steep pitch [-85 deg, -25 deg] + spring arm** - the camera sits high above and slightly behind the player, always angled steeply downward so it stays in the open column above the player's cell rather than ramming into the side walls of the 3.6-wide corridor. A per-frame raycast spring arm shortens the camera distance whenever a wall would otherwise be between the camera and the player, so the player is always framed and the camera never renders from inside a wall. Even at max zoom the camera stays below the 30-unit walls, so the maze layout is never visible from above.

7. **AddChild before Setup** - _Ready() only fires after entering scene tree. AddChild(chunk) must precede chunk.Setup() so gridmap is not null.

8. **GridMap for maze rendering** - Godot's built-in GridMap efficiently batches same-mesh tiles, reducing draw calls. 16x16 cells per chunk = 256 tiles per GridMap.
