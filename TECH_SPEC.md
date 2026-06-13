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
├── TECH_SPEC.md               # This file
├── CLAUDE.md                  # Original Claude guidance (outdated)
├── art/
│   ├── player.glb             # Player 3D model (GLTF binary)
│   ├── player.blend           # Player source (Blender)
│   ├── body.tres              # Player body material (orange)
│   ├── eye.tres               # Player eye material (white, emissive)
│   ├── pupil.tres             # Player pupil material (black, rim)
│   ├── mob.glb                # Enemy 3D model
│   ├── mob.blend              # Enemy source (Blender)
│   ├── mob_body.tres          # Enemy body material (blue)
│   ├── mob_eye.tres           # Enemy eye material (red, emissive)
│   └── House In a Forest Loop.ogg  # Background music
└── src/
    ├── Player.cs              # Player controller
    ├── MazeData.cs            # Maze world data & procedural generation
    ├── ChunkManager.cs        # Chunk loading/unloading orchestrator
    ├── Chunk.cs               # Single chunk - GridMap filler from cell data
    └── Mob.cs                 # Enemy controller (placeholder)
```

## 4. Scene Hierarchy (Runtime)

```
Main (Node3D)                              - main.tscn, root
├── Ground (StaticBody3D)                  - collision floor, Y=-0.5
│   ├── CollisionShape3D                   - BoxShape3D(256, 1, 256)
│   └── MeshInstance3D                     - BoxMesh(256, 1, 256), green-brown
├── DirectionalLight3D                     - "sun", ~42 deg elevation, ahead (-Z), energy=2.4, big disk (angular_distance 5)
├── MazeData (Node + MazeData.cs)         - Singleton, procedural world data
├── Player (CharacterBody3D)              - instance of player.tscn
│   ├── ModelPivot (Node3D, Y=0.25)       - faces movement direction
│   │   └── Character (player.glb, scale=0.3) - 3D model
│   ├── CollisionShape3D (Y=0.35)         - SphereShape3D, radius=0.3
│   ├── HeadLight (OmniLight3D, Y=4.0)    - travels with player, lights nearby tiles/walls
│   └── CameraYaw (Node3D, Y=2.0)         - horizontal orbit, elevated rig
│       └── CameraPitch (Node3D, default -50 deg) - vertical tilt, angled down
│           └── Camera3D (Z=10, current)  - perspective, default FOV
├── ChunkManager (Node3D + ChunkManager.cs) - orchestrates chunk lifecycle
│   └── Chunk (xN, dynamic)              - instances of chunk.tscn
│       └── GridMap (cell_size=3.6,1,3.6, cell_center_y=false) - renders Floor/Wall tiles
└── WorldEnvironment                      - procedural sky, ambient light
```

## 5. Subsystem Specifications

### 5.1 MazeData - World Data and Procedural Generation

**File:** `src/MazeData.cs`
**Type:** `Node` (singleton via `Instance` static property)
**Initialization:** `_EnterTree()` sets `Instance = this`; `_Ready()` prints debug info.

**Constants:**

| Constant | Value | Meaning |
|----------|-------|---------|
| WorldWidth | 10000 | Maze cells in X dimension |
| WorldHeight | 10000 | Maze cells in Z dimension |
| CellWorldSize | 3.6f | World units per maze cell = corridor width = 6 x player diameter (0.6) |
| WallHeight | 30.0f | Wall height in world units (towering canyon walls) |

**Computed Properties:**

| Property | Formula | Value |
|----------|---------|-------|
| WorldOffsetX | -WorldWidth * CellWorldSize / 2 | -18000 |
| WorldOffsetZ | -WorldHeight * CellWorldSize / 2 | -18000 |
| PlayerStartCell | Vector2I(1, 1) | Entry cell (maze coordinates) |

The world is centred at origin: cells [0, 9999] map to world X/Z approx [-18000, +17996].

**Procedural Maze Algorithm - IsFloor(wx, wz):**

Deterministic, stateless, O(1) per cell. No global array stored.

Cell classification rules (evaluated in order):

1. Border cells (wx<=0 or wz<=0 or wx>=9999 or wz>=9999) -> wall
2. Entrance: cell (1, 0) -> floor (top entrance)
3. Exit: cell (9998, 9999) -> floor (bottom exit)
4. Odd-Odd cells ((wx&1)==1 and (wz&1)==1) -> floor (corridor hubs, ensures global connectivity)
5. Even-Odd cells (vertical walls between adjacent corridors): murmur3-finalizer hash -> floor if hash%100 < 70 (70% open)
6. Odd-Even cells (horizontal walls): same hash -> floor if hash%100 < 70
7. Even-Even cells (pillars): floor if hash%100 < 5 (5% open)

**Hash function** (murmur3 finalizer variant):
```
h = wx * 0x45d9f3b + wz * 0x119de1f3
h = (h ^ (h >> 16)) * 0x85ebca6b
h = (h ^ (h >> 13)) * 0xc2b2ae35
h = h ^ (h >> 16)
```

**Chunk Data API - GetChunkData(chunkX, chunkZ, chunkSize):**

Returns `int[chunkSize, chunkSize]` where 0=floor, 1=wall. Iterates over the chunk's cell range and calls IsFloor() for each. Out-of-bounds chunks return all-1 (wall).

### 5.2 ChunkManager - Dynamic Chunk Streaming

**File:** `src/ChunkManager.cs`
**Type:** `Node3D`

**Constants:**

| Constant | Value | Meaning |
|----------|-------|---------|
| ChunkSize | 16 | Cells per chunk (16x16) |
| LoadDistance | 1 | Chunk load radius (Manhattan): 3x3 = 9 active chunks |

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
4. Scan active chunks: if Manhattan distance > LoadDistance -> QueueFree() + remove from dict
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

**Setup(Vector2 coord, int[,] chunkData):**

1. Store chunkCoord
2. gridmap.Clear() - remove previous tiles
3. Iterate x in [0, ChunkSize), z in [0, ChunkSize):
   - cellType = chunkData[x, z]
   - tileId = 0 if floor, 1 if wall, -1 if unknown
   - gridmap.SetCellItem(new Vector3I(x, 0, z), tileId)

GridMap places each tile centred at the cell's world position. cell_size=(3.6,1,3.6) means adjacent cells are 3.6 world-units apart in XZ. With cell_center_y=false the floor sits on the Y=0 plane.

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

**Item 1 - Wall:** dark, vertically-fluted canyon rock (matches the reference look in `walls.png`).

| Property | Value |
|----------|-------|
| Mesh | BoxMesh(**3.66**, 30, **3.66**) - tall pillar, slightly larger than the 3.6 cell (see "Tile overlap" below) |
| Material | StandardMaterial3D, see below |
| Collision | BoxShape3D(3.6, 30, 3.6) - matches the **cell** size, not the mesh - with Transform3D Y=+15 |
| mesh_transform | Transform3D Y=+15 - wall SITS ON the floor (Y=0 to 30) |
| Shadow casting | On |

Wall material detail (`StandardMaterial3D_wall` in `MazeTiles.tres`):
- **Noise:** one `FastNoiseLite` (Cellular-ish, frequency 0.05, 6 fractal octaves) with **domain warp enabled** (type 1, amplitude 12, frequency 0.02) so the rock reads as organic channelled stone rather than even speckle. Baked into two 512² seamless `NoiseTexture2D`s — an albedo one through a 4-stop `Gradient` (near-black valleys `Color(0.035,0.03,0.026)` → dusty brown ridges `Color(0.34,0.3,0.25)`) and a normal map (`as_normal_map`, bump_strength 4.5).
- **Mapping:** world-space triplanar (`uv1_world_triplanar`) with an anisotropic `uv1_scale = Vector3(0.14, 0.06, 0.14)` — chunky horizontally, ~2.3× stretched vertically. This turns the isotropic noise into the tall **vertical fluting** seen in the reference. (Stretching further — uv1_scale.y below ~0.05 — fans the streaks into "fur"; 0.06 is the stable maximum. World-space mapping also makes adjacent tiles blend into one continuous wall with no per-tile seams.)
- roughness 0.92, metallic_specular 0.18, normal_scale 1.8.

The Y=+15 offset (= WallHeight/2) is critical: without it, the wall BoxMesh would be centred at Y=0 (half below floor). With the offset, wall occupies Y=0 to Y=30, on top of floor tile (Y=-0.1 to Y=0.1). Walls tower far above the camera, blocking any over-the-top view of the maze.

**Tile overlap (seam fix).** The floor and wall **meshes** are 3.66 wide while the GridMap `cell_size` is 3.6, so neighbouring tiles overlap by ~0.03 per side instead of abutting exactly. Reason: the maze renders at large world coordinates (`WorldOffset = -WorldWidth*CellWorldSize/2 = -18000`; the player starts near world (-17994, -17994)), where float32 precision is only ~0.002. Exactly-coincident tile edges there leave hairline cracks that show the dark background through the floor/walls. The small overlap covers the cracks. Because both materials use **world-space triplanar** mapping, the overlapping region samples the *same* texel on both tiles, so the resulting coplanar z-fight is invisible — no flicker, no double-lighting. The **collision** shapes stay at the true 3.6 cell size so gameplay/clearance is unchanged (the 0.03 visual lip has no collider behind it, which is negligible against the 0.3 player radius). A deeper fix for the precision loss would be a floating-origin / re-centred world, but that is a larger change than the seam bug required.

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
├── ModelPivot (Node3D, Y=0.25)          - faces movement direction
│   └── Character (player.glb instance, scale=0.3)
├── CollisionShape3D (Y=0.35)            - SphereShape3D, radius=0.3
├── HeadLight (OmniLight3D, Y=4.0)       - local fill light, follows the player
└── CameraYaw (Node3D, Y=2.0)            - mouse X: RotateY
    └── CameraPitch (Node3D, init -60 deg) - mouse Y: RotateX (clamped)
        └── Camera3D (Z=10, current=true) - perspective, distance auto-shortened to avoid walls
```

**Player Model:**
- Source: art/player.glb (GLTF binary)
- Original bounds (3 primitives): X[-1.0..1.0], Y[-0.47..0.43], Z[-1.04..1.96]
- Applied scale: 0.3 -> effective size approx 0.61 wide x 0.27 tall x 0.90 deep
- ModelPivot Y offset: 0.25 - places model feet approx at floor level (Y=0)
- Collision sphere: radius 0.3, centred at Player.Y+0.35, bottom at Player.Y+0.05

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

**Start Position:**
```
Position = (PlayerStartCell.X * CellWorldSize + WorldOffsetX + CellWorldSize/2,
            0.3,   // just above the floor (top Y~0.1); settles onto it
            PlayerStartCell.Y * CellWorldSize + WorldOffsetZ + CellWorldSize/2)
         = (1*3.6 + (-18000) + 1.8, 0.3, 1*3.6 + (-18000) + 1.8)
         = (-17994.6, 0.3, -17994.6)
```
The +CellWorldSize/2 centres the player within the cell: with cell_center_x/z=true, GridMap places the cell (and its floor tile) centre at cell_index * cell_size + cell_size/2, so the spawn formula lands exactly on the floor-tile centre of cell (1,1).

**Input Map (project.godot):**

| Action | Keys (physical) | Keys (logical) | Gamepad |
|--------|-----------------|----------------|---------|
| move_left | A(65), LeftArrow(4194319) | - | Button 2 |
| move_right | D(68), RightArrow(4194321) | - | Button 1 |
| move_forward | W(87), UpArrow(4194377) | - | Button 3 |
| move_back | S(83), DownArrow(4194376) | - | Button 4 |
| jump | Space(32) | - | Button 0 |

Dead zone: 0.2. Mouse captured (Input.MouseMode = Captured).

### 5.6 Ground - Floor Collision

Scene node in main.tscn:
- Ground (StaticBody3D, Y=-0.5)
  - CollisionShape3D: BoxShape3D(256, 1, 256), identity transform
  - MeshInstance3D: BoxMesh(256, 1, 256), green-brown material

Ground collision spans Y=[-1.0, 0.0]. Top surface at Y=0. Provides flat floor across 256x256 playable area. Maze is 20000x20000. For outer areas, GridMap floor tile collision provides walking surface.

### 5.7 Lighting and Environment

**DirectionalLight3D (the "sun"):**
- Light travels (0, -0.669, 0.743): ~42 deg below horizontal, heading +Z. The sun therefore sits AHEAD of the player (toward -Z, where the corridor opens) at a ~42 deg elevation — high enough to read as "up there" yet low enough that, looking down a long straight corridor, the **sun disk appears high up at the far end** (the goal in `walls.png`) instead of out of frame overhead. It front-lights the player and the corridor floor rather than only grazing the wall tops. The azimuth is grid-aligned (purely a rotation about X), so the disk lines up with the maze's straight runs.
- Energy 2.4, light_color warm `Color(1, 0.88, 0.72)`, light_specular 1.0.
- **light_angular_distance 5.0** — deliberately large: it grows the rendered sun disk and softens the shadows into the long, soft cast seen in the reference.
- Shadows enabled (mode 2, max distance 120).

**HeadLight (OmniLight3D, child of Player):**
- Local point light at Y=4 above the player (just above head height). Travels with the player so the player, the floor tiles underfoot and the nearby walls are always clearly lit, even at the bottom of the deep canyons where the directional sun barely reaches.
- light_color warm white (1, 0.96, 0.86), energy 4.0, omni_range 20, omni_attenuation 0.7 (gentle falloff so a few cells around the player stay bright). Shadows off — it is a fill light.

**WorldEnvironment:**
- Background: Sky (mode=2)
- Sky: `ProceduralSkyMaterial` — dark blue zenith `Color(0.04,0.05,0.08)` fading to a **warm horizon glow** `Color(0.5,0.42,0.32)` (sky_curve 0.09). The sun disk follows the DirectionalLight3D direction; `sun_angle_max 42` enlarges its glow halo and `sun_curve 0.12` softens the falloff, so the disk reads as a bright, bloomed sun at the corridor's end. sky_energy_multiplier 0.85.
- Ambient light: source=Sky (mode=2), energy 0.6, cool tint `Color(0.4,0.45,0.6)` — a low, uniform non-occluded fill so shadowed canyon surfaces stay just visible without washing out the dramatic contrast.
- Reflected light: source=Sky (mode=1)
- **Glow/bloom:** enabled, intensity 0.7, strength 1.15, bloom 0.2, hdr_threshold 0.95 — the low threshold lets the bright sun disk bloom into the soft halo that fills the end of the corridor.
- Tonemap: Filmic (mode 3), white 6.0.
- SDFGI: disabled

### 5.8 Mob - Enemy (Placeholder)

**File:** `src/Mob.cs`
**Type:** `CharacterBody3D`
**Scene:** `mob.tscn` (not instantiated in main scene)

- Collision: BoxShape3D(2, 1, 2)
- Model: art/mob.glb with separate body (blue) and eye (red emissive) materials
- Initialize(startPos, playerPos): faces player, sets forward velocity with random speed [10, 15]
- OnVisibilityNotifierScreenExited(): self-destructs via QueueFree()
- Not currently spawned - code exists but no spawner implemented.

### 5.9 Art Assets

| File | Type | Purpose |
|------|------|---------|
| art/player.glb | GLTF binary | Player model (sphere-based character) |
| art/player.blend | Blender source | Player model source |
| art/body.tres | SpatialMaterial | Player body - orange (#E85A00), roughness 0.5 |
| art/eye.tres | SpatialMaterial | Player eye - white (#DBDBDB), metallic, emissive |
| art/pupil.tres | SpatialMaterial | Player pupil - black, roughness 0.3, rim effect |
| art/mob.glb | GLTF binary | Enemy model |
| art/mob.blend | Blender source | Enemy model source |
| art/mob_body.tres | SpatialMaterial | Enemy body - blue (#0F447D), roughness 0.43 |
| art/mob_eye.tres | SpatialMaterial | Enemy eye - red (#C21D30), metallic, emissive |
| art/House In a Forest Loop.ogg | OGG audio | Background music (not yet integrated) |

All .blend import disabled (filesystem/import/blender/enabled=false).

## 6. Physics Configuration

- Engine: Jolt Physics
- Player collision: sphere radius 0.3, layer 1, mask 1
- Ground collision: static box 256x1x256, layer 1 (default)
- Wall collision: GridMap-generated BoxShape3D(3.6, 30, 3.6) per wall tile, offset Y=+15
- Floor collision: GridMap-generated BoxShape3D(3.6, 0.2, 3.6) per floor tile
- Movement: CharacterBody3D.MoveAndSlide() with built-in sliding collision

## 7. Coordinate Systems

**Maze Cell Coordinates:**
- Origin: (0, 0) at top-left corner of the maze
- X-axis: east (increasing column index)
- Z-axis: south (increasing row index)
- Range: [0, 9999] in both axes
- Cell (1, 0) = entrance; Cell (9998, 9999) = exit

**World Coordinates:**
- Origin: centre of the maze
- X-axis: east; Y-axis: up; Z-axis: south (Godot convention: -Z = forward)
- Maze cells map to world via:
  - worldX = cellX * CellWorldSize + WorldOffsetX + CellWorldSize/2
  - worldZ = cellZ * CellWorldSize + WorldOffsetZ + CellWorldSize/2
  - where WorldOffsetX = WorldOffsetZ = -18000, CellWorldSize = 3.6
- Maze extends from world X/Z approx [-18000, +17996]
- Floor surface Y = 0; walls occupy Y = [0, 30]

**Chunk Coordinates:**
- Chunk (0, 0) covers cells [0, 15]; Chunk (624, 624) covers cells [9984, 9999]
- Total: 625x625 = 390,625 possible chunks
- At any moment, 9 chunks are active (LoadDistance=1 -> 3x3 grid around player)

## 8. Key Design Decisions

1. **Procedural, stateless maze** - 10000x10000 = 100M cells cannot fit in memory (400MB for ints). Hash-based IsFloor() generates any cell in O(1) without storage.

2. **Odd-odd cells = guaranteed corridors** - ensures the entire maze is connected. Without this, the hash could create isolated rooms.

3. **70% wall removal** between adjacent corridors - balances openness (maze is navigable) with structure (dead ends and turns exist).

4. **CellWorldSize = 3.6** - corridors are 3.6 world-units wide = 6x the player diameter (0.6), giving a wide, comfortable canyon-like passage with ~42% clearance on each side. Wall thickness equals corridor width (one cell).

4a. **WallHeight = 30** - walls tower far above the (max ~15.5-high) camera, so the maze layout can never be seen from above. The tall walls plus a grid-aligned ~42 deg sun produce dramatic canyon shafts of light, a narrow strip of sky overhead, and the sun disk glowing high at the end of long straight corridors.

5. **Dual-node orbit camera** - avoids gimbal lock. Yaw-pitch decomposition means camera orbits cleanly regardless of orientation.

6. **Camera elevated + steep pitch [-85 deg, -25 deg] + spring arm** - the camera sits high above and slightly behind the player, always angled steeply downward so it stays in the open column above the player's cell rather than ramming into the side walls of the 3.6-wide corridor. A per-frame raycast spring arm shortens the camera distance whenever a wall would otherwise be between the camera and the player, so the player is always framed and the camera never renders from inside a wall. Even at max zoom the camera stays below the 30-unit walls, so the maze layout is never visible from above.

7. **AddChild before Setup** - _Ready() only fires after entering scene tree. AddChild(chunk) must precede chunk.Setup() so gridmap is not null.

8. **GridMap for maze rendering** - Godot's built-in GridMap efficiently batches same-mesh tiles, reducing draw calls. 16x16 cells per chunk = 256 tiles per GridMap.
