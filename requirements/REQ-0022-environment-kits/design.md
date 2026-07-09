# REQ-0022-environment-kits — Implementation (Design / HOW)

> **Feature:** REQ-0022-environment-kits · **Document type:** implementation (HOW) · **Status:** ✅ implemented (base)
> **Global context:** [TECH_SPEC.md](../TECH_SPEC.md)
> **Approved design spec (architecture, rationale, alternatives considered — not duplicated here):**
> [`docs/superpowers/specs/2026-07-07-environment-kits-design.md`](../../docs/superpowers/specs/2026-07-07-environment-kits-design.md)

---

## What this replaces

Previously every wall cell rendered as a single `BoxMesh` GridMap item (`MazeTiles.tres` item 1)
with a fluted-noise `StandardMaterial3D` — a normal map can re-angle lighting on a flat plane but
cannot change a silhouette, so every wall edge stayed a straight box line and every corridor
corner was a hard 90°. This feature replaces the *visible* wall surface with kit-driven instanced
rock geometry, while keeping the GridMap wall box as the collision + light-occluder backbone —
**Approach A** from the design spec: the volatile part (rock instancing/tuning) is isolated from
the stable part (collision, chunk streaming), so rock kits can be iterated on without touching the
code that keeps the player from falling through walls or seeing into the void.

## Code structure

- **`src/EnvironmentId.cs`** — `enum EnvironmentId { SlotCanyon, Ravine }`.
- **`src/RockPlacement.cs`** — `readonly struct RockPlacement { int PrototypeIndex; Transform3D Transform; }`, the unit a kit hands back per rock.
- **`src/EnvironmentKit.cs`** — abstract base for all kits:
  - `Mesh[] Prototypes`, `Material RockMaterial`, `float[] BaseScales`, tunable `FootprintFraction` (default 1.2).
  - `LoadCliffPrototypes()` — shared loader for the 8 `Cliff_models_cliff{1..8}_mesh.res` bare meshes from `art/RockPack1/Models/meshes/` (both kits use the same set).
  - `ComputeBaseScales()` — for each prototype, reads its local-space AABB and derives a **uniform** scale so the rock's **horizontal footprint** (`max(Size.X, Size.Z)`) spans `CellWorldSize * FootprintFraction` (≈ one corridor cell). Scaling is uniform (not stretched to `WallHeight`): the source cliffs are chunky ~1:1 blocks, so fitting width to a cell keeps natural proportions — stretching a single rock to 30 tall instead turned them into thin blades.
  - `BuildStack(cellCenterLocal, seed, jitter, tiltMax, scaleMin, scaleMax, overlap)` — shared placement: stacks cell-footprint rocks up a vertical column from `y = 0` to `MazeData.WallHeight`, stepping `max(rockHeight * overlap, 1.0)` per layer (rocks overlap so the wall reads continuous), each with a random prototype, yaw, optional tilt, per-rock scale variance, and XZ jitter. Kits differ only in the params they pass. A guard caps the loop at 48 layers.
  - `abstract List<RockPlacement> PlaceRocks(Vector3 cellCenterLocal, ulong seed)` — the *only* place a concrete kit's behaviour differs.
  - `protected static RandomNumberGenerator Rng(ulong seed)` and `MakeTransform(pos, eulerRad, scale)` helpers shared by subclasses.
- **`src/SlotCanyonKit.cs`** — `Cliff_Material_Red_Sand.tres` as the shared material. `PlaceRocks` → `BuildStack(jitter 0.35, tiltMax 0, scale 0.9–1.2, overlap 0.6)` — tight, upright, low-jitter → fluted slot-canyon look.
- **`src/RavineKit.cs`** — same 8 prototypes, `Cliff_Material_Photoscan.tres` (grey) as the material. `PlaceRocks` → `BuildStack(jitter 0.5, tiltMax 0.10 rad, scale 0.8–1.05, overlap 0.65)` — tilted, wider spread → open-ravine look.
- **`src/EnvironmentKitRegistry.cs`** — `static EnvironmentKit Get(EnvironmentId id)`; lazily constructs and caches one kit instance per id in a `Dictionary<EnvironmentId, EnvironmentKit>` (prototypes/materials loaded once, not per chunk).
- **`src/MazeData.cs`** — `[Export] public EnvironmentId RegionEnvironment = EnvironmentId.SlotCanyon;` (flip in the editor to A/B the two kits — the seam for a future `regionAddress -> EnvironmentId` map) and `public int WorldSeed { get; private set; }` (set from the region's generation seed in `_Ready`, logged alongside it).
- **`src/ChunkManager.cs`** `LoadChunk` — resolves the kit once per chunk load: `var kit = EnvironmentKitRegistry.Get(maze.RegionEnvironment); chunk.Setup(chunkPos, chunkData, kit);`.
- **`src/Chunk.cs`** `Setup(Vector2 coord, int[,] chunkData, EnvironmentKit kit)` — unchanged floor/wall `GridMap.SetCellItem` fill, plus: for every wall cell (`cellType == 1`), computes `seed = CellSeed(worldSeed, wx, wz)` and the cell's local center (`gridmap.MapToLocal`, Y pinned to 0), calls `kit.PlaceRocks(center, seed)`, and buckets each returned `RockPlacement` by `PrototypeIndex` into a `List<Transform3D>[]` sized to `kit.Prototypes.Length`. After the cell loop, for each non-empty bucket it builds **one `MultiMeshInstance3D`** (`MultiMesh.TransformFormat = Transform3D`, `Mesh = kit.Prototypes[i]`, `InstanceCount` + `SetInstanceTransform` per placement, `MaterialOverride = kit.RockMaterial`) and adds it as a child of the chunk.
- **`MazeTiles.tres`** item 1 (Wall) — the `StandardMaterial3D` is restyled to a flat dark occluder: `albedo_color = Color(0.05, 0.045, 0.04, 1)`, `roughness = 1.0`, `metallic_specular = 0.0`, no normal map. The old fluted-noise `Gradient`/`NoiseTexture2D` sub-resources are left in the file as orphaned, unreferenced resources (harmless — Godot doesn't error on unused sub-resources). Mesh/collision box sizes (3.66 visual / 3.6 collision, Y=+15 offset) are unchanged from before this feature.

## Determinism — `Chunk.CellSeed`

```csharp
private static ulong CellSeed(int worldSeed, int wx, int wz)
{
    unchecked
    {
        ulong h = 1469598103934665603UL;               // FNV-1a offset basis
        h = (h ^ (uint)worldSeed) * 1099511628211UL;
        h = (h ^ (uint)wx) * 1099511628211UL;
        h = (h ^ (uint)wz) * 1099511628211UL;
        return h;
    }
}
```

FNV-1a folded over `(WorldSeed, wx, wz)` — **world** cell coordinates, not chunk-local. A wall
cell's rock placement is therefore identical no matter which chunk load streamed it in, and stable
across unload/reload — no flicker or re-rolled placement as `ChunkManager` streams chunks in and
out. Each kit's `PlaceRocks` seeds its own `RandomNumberGenerator` from this value, so the exact
rock sequence for a cell is fully reproducible from `(WorldSeed, wx, wz)` alone.

## Batching

While iterating a chunk's 16×16 cells, `Chunk` accumulates `Transform3D`s per prototype index
rather than emitting one node per rock. After the loop it emits **one `MultiMeshInstance3D` per
non-empty prototype bucket** — at most `kit.Prototypes.Length` (8, for both current kits) draw
calls per chunk, regardless of how many individual rock instances that bucket holds (a vertical
stack of ~8–12 rocks × wall-cell-count per chunk). With the 3×3 = 9 active-chunk streaming window (`ChunkManager`,
`LoadDistance = 1`), this keeps total wall-rock draw calls in the tens, not the hundreds — the
batching strategy the design spec's §7/§10 called for.

## Occluder + collision (Approach A)

The GridMap wall item (id 1) keeps doing both of its original jobs unchanged:

- **Collision** — the same `BoxShape3D` (3.6×30×3.6, Y=+15) as before this feature; player/monster
  clearance and pathfinding (`MazeData.IsFloor`) are untouched.
- **Opacity** — its visible material is now a plain near-black `StandardMaterial3D` instead of the
  fluted-noise stone, since the rock `MultiMeshInstance3D`s now cover the surface the player
  actually sees. This guarantees there is **no see-through or light-leak** through gaps between
  individually-placed rocks, and means rock placement/density can be tuned freely without ever
  risking a visible hole in the wall.

Rocks are **visual-only** — a `MultiMeshInstance3D` carries no collision — and are allowed to
overhang into the corridor freely; the corridor is 6× player width (`CellWorldSize` = 3.6, player
diameter 0.6), so overhang is not a gameplay problem.

## Extensibility seam

Matches design spec §8. Adding a future biome is three local edits: (1) add an `EnvironmentId`
value, (2) add an `EnvironmentKit` subclass with its own prototypes/material/`PlaceRocks`, (3)
register it in `EnvironmentKitRegistry`. No change to `Chunk`, `ChunkManager`, or `MazeData` logic
is required. `MazeData.RegionEnvironment` is a single `[Export]` value today (one region); when
multi-region streaming lands, it can be swapped for a `regionAddress -> EnvironmentId` map without
touching kit or chunk code.

## Verification performed (per CLAUDE.md CLI-only verification)

- `dotnet build` after each change.
- Headless logic check: `GD.Print` of the active kit name, wall-cell count, and total placed-rock
  count, grepped from a headless run — confirms placement and determinism with zero
  error/exception lines.
- Visual check: `DISPLAY=:0` screenshots taken for both `SlotCanyon` and `Ravine` (flipping the
  `RegionEnvironment` export) to eyeball the silhouette difference; temporary debug code and
  screenshots removed afterward.

## Границы (out of scope / not implemented)

- **Walls only.** Floor, sky, fog, and lighting are not per-environment — only the wall surface
  differs between kits.
- **Single region.** `RegionEnvironment` is one `[Export]` value for the whole (only) region — no
  per-chunk or per-cell environment, and no multi-region streaming or `regionAddress ->
  EnvironmentId` map (seam left for later, per the extensibility section above, not built now).
- **No maze-gen biome field.** Environment selection is entirely game-side, on `MazeData`; the
  maze-gen region facade (`PlayersWorlds.Maps`, `RegionRecipe`/`RegionCell`) is untouched and has
  no visual/biome concept of its own.
- **Boulders / standalone rocks / rock piles are unused.** Both kits currently draw only from the
  same 8 `Cliff*` prototypes; the Arnklit pack's `Boulder1-6`, `Rock1-4`, and `Pile1-2` meshes are
  not wired into either kit (a straightforward future addition to `RavineKit` in particular).
- **Per-kit textures are shared, not bespoke.** The two kits differ by prototype-subset-and-none
  (both use all 8 `Cliff*` meshes today), placement parameters, and a **material swap**
  (`Cliff_Material_Red_Sand.tres` vs `Cliff_Material_Photoscan.tres`, both pack-supplied, both
  using the pack's shared `advanced_rock.gdshader`) — no custom per-biome art was authored.
- **POM / vertex displacement / voxel-SDF geometry not used.** Evaluated in the design spec's
  Appendix and deliberately rejected: parallax-occlusion mapping cannot change a silhouette and
  fails at grazing angles; Godot 4 has no GPU tessellation for cheap vertex displacement; a
  voxel/marching-cubes/SDF renderer swap was judged overkill for a grid maze. Instanced discrete
  rock meshes (this feature) were chosen instead.
- **No rock LOD/decimation.** The design spec (§10 Risks) flags the pack's `Cliff*` meshes as
  high-poly (photoscan-grade, ~130-210 KB each); no decimated LOD variants were produced. A wall
  cell now stacks ~8–12 rocks up its full height (each source cliff is only ~1 cell tall once
  footprint-fitted, so a column is needed to cover the 30-unit wall), traded against the batching
  (one MultiMesh per prototype) and a future filler-cell cull to manage the triangle budget.

## Per-region lighting (F-54)

Lighting is a property of the kit, so the region's mood ships alongside its rock look.

- **`src/LightingProfile.cs`** — a plain data class the kit hands over: sun (`SunVisible`,
  `SunEnergy`, `SunColor`, `SunPitchDeg`, `SunYawDeg`), ambient (`AmbientColor`, `AmbientEnergy`
  — `≤0` disables ambient entirely), sky (`SkyTopColor`, `SkyHorizonColor`, `SkyEnergy`), depth fog
  (`FogEnabled`, `FogColor`, `FogDensity`), and the player torch (`HeadLightEnergy`,
  `HeadLightColor`, `HeadLightRange`, `HeadLightAttenuation`, `HeadLightHeight` — local Y above the
  player; raising it grazes close vertical walls dimmer while keeping the horizontal floor pool lit —
  and `HeadLightShadow` — enables the omni cubemap shadow so rocks + wall occluder boxes block the
  torch instead of it shining through them; off by default, on for DarkCanyon, costs a per-frame
  shadow render).
- **`EnvironmentKit.Lighting`** — `{ get; protected set; }`, defaulted to `new LightingProfile()`;
  each kit's ctor assigns its own profile (DarkCanyon: sun+ambient off, near-black sky, black fog,
  warm short-range torch · SlotCanyon: scorching warm sun, warm ambient, bright sky, dim torch ·
  Ravine: soft cool sun, grey ambient/sky, faint haze, mid torch).
- **`src/LightingController.cs`** (`Main/LightingController`, `Node`) — `[Export] NodePath`s to the
  sun, `WorldEnvironment`, and `Player/HeadLight`. In `_Ready` it reads
  `EnvironmentKitRegistry.Get(MazeData.Instance.RegionEnvironment).Lighting` and applies it: sets the
  DirectionalLight3D visibility/energy/color/rotation; sets `Environment.AmbientLightSource` to
  `Disabled` when `AmbientEnergy ≤ 0` else `Color`; casts `Environment.Sky.SkyMaterial` to
  `ProceduralSkyMaterial` for the sky colors/energy; toggles `FogEnabled`/`FogLightColor`/`FogDensity`;
  and sets the OmniLight3D energy/color/`OmniRange`/`OmniAttenuation`. Runs before the first frame,
  so the stored `main.tscn`/`player.tscn` light values are only editor defaults (kept aligned to
  DarkCanyon).

**Why disabling ambient kills the "glow from below":** flat COLOR ambient adds a uniform term to
every fragment regardless of surface orientation or occlusion (only the baked crevice-AO texture
nudges it). Downward-facing rock undersides therefore received the same fill as the tops and read as
lit from beneath. Disabling ambient (DarkCanyon) leaves only the directional torch, so undersides
fall to black naturally.

**Границы (scope/limits):** one profile per kit, applied once at `_Ready` (no runtime transitions
between regions yet — there is only one resident region). Volumetric fog / god-rays around the torch
not used (depth fog only). Glow/bloom, tonemap, and reflection source stay at the scene defaults —
the controller does not touch them. Numbers are first-pass, tuned by playtest.

## Links

Design spec:
[`docs/superpowers/specs/2026-07-07-environment-kits-design.md`](../../docs/superpowers/specs/2026-07-07-environment-kits-design.md).
Related: [REQ-0003-core](../REQ-0003-core/README.md) — maze/chunk rendering
(`MazeData`/`ChunkManager`/`Chunk`, the GridMap tile system this feature builds on); this feature
changes only the visual representation of wall cells, not the maze geometry, collision, or chunk
streaming itself.
