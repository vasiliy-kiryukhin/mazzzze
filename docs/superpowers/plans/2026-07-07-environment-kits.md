# Environment Kits (Rock-Wall Rendering) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace flat box maze walls with kit-driven instanced rock geometry so walls read as natural canyon rock, selectable per region via a game-side environment-kit abstraction.

**Architecture:** Approach A — GridMap stays as the collision + opaque-occluder backbone; each `Chunk` additionally asks the region's `EnvironmentKit` to emit rock instances, batched into one `MultiMeshInstance3D` per rock prototype. Environment is a game-side `EnvironmentId` chosen on `MazeData` and resolved through a registry. Two kits ship: `SlotCanyon` and `Ravine`.

**Tech Stack:** Godot 4.7 mono, C# (.NET 8.0), Jolt physics, GridMap + MultiMesh, Arnklit Godot Cliffs & Rocks Pack 1 (v1_02, non-HighRes).

**Spec:** `docs/superpowers/specs/2026-07-07-environment-kits-design.md`

## Global Constraints

- **Engine/runtime:** Godot 4.7 mono (`Godot.NET.Sdk/4.7.0`), .NET 8.0. Editor binary is repo-local and absolute:
  `GODOT="$PWD/.bin/Godot_v4.7-stable_mono_linux_x86_64/Godot_v4.7-stable_mono_linux.x86_64"`.
- **Build before running:** run `dotnet build` after any `.cs` change before launching Godot.
- **NO test files.** Verification is CLI-only: `dotnet build`, headless `GD.Print` log grep, and `DISPLAY=:0` screenshots. Never create test files. (CLAUDE.md override of the usual TDD flow.)
- **Debug logging:** use `GD.Print`, never `GD.PrintErr`. Temporary debug prints/screenshot code must be **removed** before the task's final commit.
- **All game-critical behaviour in `_PhysicsProcess`** (60 Hz), not `_Process`.
- **No code comments unless explicitly asked.**
- **Git:** work on a feature branch (e.g. `feat/environment-kits`), never `main`. Commit per task. Do not push (explicit-ask only). Never amend.
- **Docs are part of "done":** any behaviour/architecture/art-pipeline change updates `requirements/TECH_SPEC.md`, the feature `REQ` folder, `requirements/README.md`, and both `CLAUDE.md` + `AGENTS.md` (kept in sync). Requirements prose is Russian (WHAT); TECH_SPEC + design.md are English (HOW).
- **Coordinate/geometry facts:** `MazeData.CellWorldSize = 3.6f`, `MazeData.WallHeight = 30.0f`. Floor cells = `0`, wall cells = `1` in chunk data. MeshLibrary ids: `0` = Floor, `1` = Wall.
- **All new `.cs` files live flat in `src/`** (project convention — no subfolders).

---

## File Structure

**Create:**
- `src/EnvironmentId.cs` — the enum of environment kinds.
- `src/RockPlacement.cs` — value struct returned by kits (`PrototypeIndex` + `Transform3D`).
- `src/EnvironmentKit.cs` — abstract base: prototype meshes, shared material, AABB-derived base scales, seeded-RNG + transform helpers, abstract `PlaceRocks`.
- `src/SlotCanyonKit.cs` — tall, near-vertical, red-sand cliffs.
- `src/RavineKit.cs` — shorter, tilted, spread, grey cliffs.
- `src/EnvironmentKitRegistry.cs` — lazy `EnvironmentId → EnvironmentKit` singletons.
- `art/RockPack1/` — imported rock meshes, materials, shader (from the pack zip).
- `requirements/REQ-0021-environment-kits/` — feature docs (README + facets + design.md).

**Modify:**
- `src/MazeData.cs` — add `[Export] RegionEnvironment` and public `WorldSeed`.
- `src/ChunkManager.cs` — resolve the kit and pass it to `Chunk.Setup`.
- `src/Chunk.cs` — build rock MultiMeshes from the kit while filling the GridMap.
- `MazeTiles.tres` — restyle the wall item's material to a plain dark occluder.
- `requirements/TECH_SPEC.md`, `requirements/README.md`, `CLAUDE.md`, `AGENTS.md` — docs.

---

## Task 1: Import the rock pack into the project

**Files:**
- Create: `art/RockPack1/` (copied from the pack zip: `Models/`, `Materials/`, `Shaders/`)

**Interfaces:**
- Produces: mesh resources at `res://art/RockPack1/Models/meshes/Cliff_models_cliff{1..8}_mesh.res`; materials at `res://art/RockPack1/Materials/Cliff_Material_Red_Sand.tres` and `res://art/RockPack1/Materials/Cliff_Material_Photoscan.tres`; shader at `res://art/RockPack1/Shaders/advanced_rock.gdshader`.

- [ ] **Step 1: Extract the runtime folders from the pack**

```bash
cd /home/data/repos/github.com/krmrn42/mazzzze
rm -rf /tmp/rockpack && mkdir -p /tmp/rockpack art/RockPack1
unzip -o /home/shav/Downloads/GodotCliffsRockPack1_v1_02.zip \
  'GodotCliffsRockPack1_v1_02/RockPack1/Models/*' \
  'GodotCliffsRockPack1_v1_02/RockPack1/Materials/*' \
  'GodotCliffsRockPack1_v1_02/RockPack1/Shaders/*' \
  -d /tmp/rockpack
cp -r /tmp/rockpack/GodotCliffsRockPack1_v1_02/RockPack1/. art/RockPack1/
```

- [ ] **Step 2: Import assets so Godot generates `.godot` import metadata**

Run:
```bash
GODOT="$PWD/.bin/Godot_v4.7-stable_mono_linux_x86_64/Godot_v4.7-stable_mono_linux.x86_64"
"$GODOT" --headless --import 2>&1 | grep -iE "error|RockPack|import" | head -40
```
Expected: import lines for the RockPack1 assets; **zero** lines containing "error". If a material logs a missing-texture error, confirm the `Materials/<Rock>/*.png` files were copied (Step 1 includes `Materials/*`).

- [ ] **Step 3: Confirm the mesh resources are present**

Run:
```bash
ls art/RockPack1/Models/meshes/Cliff_models_cliff1_mesh.res \
   art/RockPack1/Materials/Cliff_Material_Red_Sand.tres \
   art/RockPack1/Materials/Cliff_Material_Photoscan.tres \
   art/RockPack1/Shaders/advanced_rock.gdshader
```
Expected: all four paths listed, no "No such file".

- [ ] **Step 4: Commit**

```bash
git add art/RockPack1 .godot 2>/dev/null; git add art/RockPack1
git commit -m "feat: import Arnklit Cliffs & Rocks pack (v1_02) runtime assets"
```
(If `.godot/` is git-ignored, only `art/RockPack1` is staged — that is correct.)

---

## Task 2: `EnvironmentId` enum and `RockPlacement` struct

**Files:**
- Create: `src/EnvironmentId.cs`, `src/RockPlacement.cs`

**Interfaces:**
- Produces: `enum EnvironmentId { SlotCanyon, Ravine }`; `readonly struct RockPlacement { int PrototypeIndex; Transform3D Transform; ctor(int, Transform3D) }`.

- [ ] **Step 1: Create `src/EnvironmentId.cs`**

```csharp
public enum EnvironmentId
{
    SlotCanyon,
    Ravine,
}
```

- [ ] **Step 2: Create `src/RockPlacement.cs`**

```csharp
using Godot;

public readonly struct RockPlacement
{
    public readonly int PrototypeIndex;
    public readonly Transform3D Transform;

    public RockPlacement(int prototypeIndex, Transform3D transform)
    {
        PrototypeIndex = prototypeIndex;
        Transform = transform;
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build`
Expected: `Build succeeded`, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add src/EnvironmentId.cs src/RockPlacement.cs
git commit -m "feat: add EnvironmentId enum and RockPlacement struct"
```

---

## Task 3: `EnvironmentKit` abstract base

**Files:**
- Create: `src/EnvironmentKit.cs`

**Interfaces:**
- Consumes: `RockPlacement` (Task 2); `MazeData.WallHeight` (existing const).
- Produces:
  - `abstract class EnvironmentKit`
  - `Mesh[] Prototypes { get; protected set; }`
  - `Material RockMaterial { get; protected set; }`
  - `float[] BaseScales { get; private set; }` — filled by `ComputeBaseScales()`
  - `protected void ComputeBaseScales()` — call after `Prototypes` is set; `BaseScales[i] = WallHeight / prototype[i].GetAabb().Size.Y`
  - `protected static RandomNumberGenerator Rng(ulong seed)`
  - `protected static Transform3D MakeTransform(Vector3 pos, Vector3 eulerRad, Vector3 scale)`
  - `abstract List<RockPlacement> PlaceRocks(Vector3 cellCenterLocal, ulong seed)`

- [ ] **Step 1: Create `src/EnvironmentKit.cs`**

```csharp
using Godot;
using System.Collections.Generic;

public abstract class EnvironmentKit
{
    public Mesh[] Prototypes { get; protected set; }
    public Material RockMaterial { get; protected set; }
    public float[] BaseScales { get; private set; }

    protected void ComputeBaseScales()
    {
        BaseScales = new float[Prototypes.Length];
        for (int i = 0; i < Prototypes.Length; i++)
        {
            float h = Prototypes[i].GetAabb().Size.Y;
            BaseScales[i] = h > 0.001f ? MazeData.WallHeight / h : 1.0f;
        }
    }

    public abstract List<RockPlacement> PlaceRocks(Vector3 cellCenterLocal, ulong seed);

    protected static RandomNumberGenerator Rng(ulong seed)
    {
        var rng = new RandomNumberGenerator();
        rng.Seed = seed;
        return rng;
    }

    protected static Transform3D MakeTransform(Vector3 pos, Vector3 eulerRad, Vector3 scale)
    {
        var basis = Basis.FromEuler(eulerRad).Scaled(scale);
        return new Transform3D(basis, pos);
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build`
Expected: `Build succeeded`, 0 errors. (The class is abstract and not yet referenced — this only checks it compiles.)

- [ ] **Step 3: Commit**

```bash
git add src/EnvironmentKit.cs
git commit -m "feat: add EnvironmentKit abstract base (AABB scaling, seeded rng)"
```

---

## Task 4: Concrete kits + registry

**Files:**
- Create: `src/SlotCanyonKit.cs`, `src/RavineKit.cs`, `src/EnvironmentKitRegistry.cs`

**Interfaces:**
- Consumes: `EnvironmentKit` (Task 3), `EnvironmentId` (Task 2), `RockPlacement` (Task 2), pack assets (Task 1).
- Produces: `EnvironmentKitRegistry.Get(EnvironmentId) → EnvironmentKit` (lazy singletons).

**Placement knobs (real starting values; tuned later via screenshots):** both kits use the 8 cliff prototypes. Per wall cell they emit `2` cliffs. `SlotCanyon`: near-vertical (no tilt), full-height (`BaseScale × 0.95..1.15`), small horizontal jitter (`±0.5`), red-sand material. `Ravine`: tilted (`±0.20 rad` pitch/roll), shorter (`BaseScale × 0.65..0.9`), wider jitter (`±1.0`), grey photoscan material.

- [ ] **Step 1: Create `src/SlotCanyonKit.cs`**

```csharp
using Godot;
using System.Collections.Generic;

public sealed class SlotCanyonKit : EnvironmentKit
{
    private const int RocksPerCell = 2;

    public SlotCanyonKit()
    {
        Prototypes = new Mesh[8];
        for (int i = 0; i < 8; i++)
        {
            Prototypes[i] = GD.Load<Mesh>(
                $"res://art/RockPack1/Models/meshes/Cliff_models_cliff{i + 1}_mesh.res");
        }
        RockMaterial = GD.Load<Material>(
            "res://art/RockPack1/Materials/Cliff_Material_Red_Sand.tres");
        ComputeBaseScales();
    }

    public override List<RockPlacement> PlaceRocks(Vector3 cellCenterLocal, ulong seed)
    {
        var rng = Rng(seed);
        var list = new List<RockPlacement>(RocksPerCell);
        for (int i = 0; i < RocksPerCell; i++)
        {
            int proto = (int)(rng.Randi() % (uint)Prototypes.Length);
            float yaw = rng.RandfRange(0.0f, Mathf.Tau);
            float s = BaseScales[proto] * rng.RandfRange(0.95f, 1.15f);
            var scale = new Vector3(s, s, s);
            var pos = cellCenterLocal + new Vector3(
                rng.RandfRange(-0.5f, 0.5f), 0.0f, rng.RandfRange(-0.5f, 0.5f));
            list.Add(new RockPlacement(proto, MakeTransform(pos, new Vector3(0, yaw, 0), scale)));
        }
        return list;
    }
}
```

- [ ] **Step 2: Create `src/RavineKit.cs`**

```csharp
using Godot;
using System.Collections.Generic;

public sealed class RavineKit : EnvironmentKit
{
    private const int RocksPerCell = 2;

    public RavineKit()
    {
        Prototypes = new Mesh[8];
        for (int i = 0; i < 8; i++)
        {
            Prototypes[i] = GD.Load<Mesh>(
                $"res://art/RockPack1/Models/meshes/Cliff_models_cliff{i + 1}_mesh.res");
        }
        RockMaterial = GD.Load<Material>(
            "res://art/RockPack1/Materials/Cliff_Material_Photoscan.tres");
        ComputeBaseScales();
    }

    public override List<RockPlacement> PlaceRocks(Vector3 cellCenterLocal, ulong seed)
    {
        var rng = Rng(seed);
        var list = new List<RockPlacement>(RocksPerCell);
        for (int i = 0; i < RocksPerCell; i++)
        {
            int proto = (int)(rng.Randi() % (uint)Prototypes.Length);
            float yaw = rng.RandfRange(0.0f, Mathf.Tau);
            float pitch = rng.RandfRange(-0.20f, 0.20f);
            float roll = rng.RandfRange(-0.20f, 0.20f);
            float s = BaseScales[proto] * rng.RandfRange(0.65f, 0.9f);
            var scale = new Vector3(s, s, s);
            var pos = cellCenterLocal + new Vector3(
                rng.RandfRange(-1.0f, 1.0f), 0.0f, rng.RandfRange(-1.0f, 1.0f));
            list.Add(new RockPlacement(proto, MakeTransform(pos, new Vector3(pitch, yaw, roll), scale)));
        }
        return list;
    }
}
```

- [ ] **Step 3: Create `src/EnvironmentKitRegistry.cs`**

```csharp
using System.Collections.Generic;

public static class EnvironmentKitRegistry
{
    private static readonly Dictionary<EnvironmentId, EnvironmentKit> _kits = new();

    public static EnvironmentKit Get(EnvironmentId id)
    {
        if (!_kits.TryGetValue(id, out var kit))
        {
            kit = id switch
            {
                EnvironmentId.SlotCanyon => new SlotCanyonKit(),
                EnvironmentId.Ravine => new RavineKit(),
                _ => new SlotCanyonKit(),
            };
            _kits[id] = kit;
        }
        return kit;
    }
}
```

- [ ] **Step 4: Build**

Run: `dotnet build`
Expected: `Build succeeded`, 0 errors.

- [ ] **Step 5: Verify meshes load and AABB scaling is sane (temporary headless check)**

Temporarily add to `src/MazeData.cs` at the **end of `_Ready()`**:

```csharp
// TEMP — remove after verifying
var _k = EnvironmentKitRegistry.Get(EnvironmentId.SlotCanyon);
GD.Print($"[EnvKitCheck] protos={_k.Prototypes.Length} " +
    $"aabbY0={_k.Prototypes[0].GetAabb().Size.Y:F2} baseScale0={_k.BaseScales[0]:F2} " +
    $"place={_k.PlaceRocks(Godot.Vector3.Zero, 12345UL).Count}");
```

Run:
```bash
dotnet build && GODOT="$PWD/.bin/Godot_v4.7-stable_mono_linux_x86_64/Godot_v4.7-stable_mono_linux.x86_64"
timeout 8 "$GODOT" --headless --path . 2>&1 | grep -iE "EnvKitCheck|error|exception"
```
Expected: one `[EnvKitCheck] protos=8 aabbY0=<positive> baseScale0=<positive> place=2` line, no error/exception. `baseScale0` should be a positive number that scales the cliff to ~30 tall (e.g. if `aabbY0≈2.0`, `baseScale0≈15`).

- [ ] **Step 6: Remove the temporary check and commit**

Delete the `// TEMP` block from `src/MazeData.cs`, then:
```bash
dotnet build
git add src/SlotCanyonKit.cs src/RavineKit.cs src/EnvironmentKitRegistry.cs
git commit -m "feat: add SlotCanyon/Ravine kits and EnvironmentKitRegistry"
```

---

## Task 5: `MazeData` exposes `RegionEnvironment` + `WorldSeed`

**Files:**
- Modify: `src/MazeData.cs`

**Interfaces:**
- Consumes: `EnvironmentId` (Task 2).
- Produces: `MazeData.Instance.WorldSeed` (int), `MazeData.Instance.RegionEnvironment` (EnvironmentId, editor-exported, defaults `SlotCanyon`).

- [ ] **Step 1: Add the exported field + WorldSeed property**

In `src/MazeData.cs`, after the existing `public Vector2I PlayerStartCell { get; private set; }` line, add:

```csharp
[Export]
public EnvironmentId RegionEnvironment = EnvironmentId.SlotCanyon;

public int WorldSeed { get; private set; }
```

- [ ] **Step 2: Record the seed and log the environment**

In `_Ready()`, the seed is currently a local `var seed = …`. Immediately after that line, add:

```csharp
WorldSeed = seed;
```

Then change the existing startup `GD.Print($"[MazeData] region …")` call to also report the environment by appending `environment={RegionEnvironment}, ` inside the string (place it right after `seed={seed}, `).

- [ ] **Step 3: Build**

Run: `dotnet build`
Expected: `Build succeeded`, 0 errors.

- [ ] **Step 4: Verify the log reports environment + seed**

Run:
```bash
GODOT="$PWD/.bin/Godot_v4.7-stable_mono_linux_x86_64/Godot_v4.7-stable_mono_linux.x86_64"
timeout 8 "$GODOT" --headless --path . 2>&1 | grep -iE "MazeData|error|exception"
```
Expected: the `[MazeData] region …` line now contains `environment=SlotCanyon` and `seed=<n>`; no error/exception.

- [ ] **Step 5: Commit**

```bash
git add src/MazeData.cs
git commit -m "feat: expose RegionEnvironment export and WorldSeed on MazeData"
```

---

## Task 6: `Chunk` builds rock MultiMeshes via the kit

**Files:**
- Modify: `src/Chunk.cs`, `src/ChunkManager.cs`

**Interfaces:**
- Consumes: `EnvironmentKit` (Task 3), `RockPlacement` (Task 2), `EnvironmentKitRegistry` (Task 4), `MazeData.Instance.WorldSeed` + `RegionEnvironment` (Task 5).
- Produces: `Chunk.Setup(Vector2 coord, int[,] chunkData, EnvironmentKit kit)` (new 3-arg signature); rock `MultiMeshInstance3D` children on each chunk.

- [ ] **Step 1: Replace `Chunk.Setup` and add the cell-seed helper in `src/Chunk.cs`**

Replace the entire existing `Setup(Vector2 coord, int[,] chunkData)` method with:

```csharp
public void Setup(Vector2 coord, int[,] chunkData, EnvironmentKit kit)
{
    chunkCoord = coord;
    gridmap ??= GetNode<GridMap>("GridMap");
    gridmap.Clear();

    var buckets = new List<Transform3D>[kit.Prototypes.Length];
    for (int i = 0; i < buckets.Length; i++)
        buckets[i] = new List<Transform3D>();

    int worldSeed = MazeData.Instance.WorldSeed;

    for (int x = 0; x < ChunkSize; x++)
    {
        for (int z = 0; z < ChunkSize; z++)
        {
            var cellType = chunkData[x, z];
            var tileId = cellType switch
            {
                0 => 0,
                1 => 1,
                _ => -1,
            };
            gridmap.SetCellItem(new Vector3I(x, 0, z), tileId);

            if (cellType != 1) continue;

            int wx = (int)coord.X * ChunkSize + x;
            int wz = (int)coord.Y * ChunkSize + z;
            ulong seed = CellSeed(worldSeed, wx, wz);
            Vector3 center = gridmap.MapToLocal(new Vector3I(x, 0, z));
            center.Y = 0.0f;

            foreach (var p in kit.PlaceRocks(center, seed))
                buckets[p.PrototypeIndex].Add(p.Transform);
        }
    }

    for (int i = 0; i < buckets.Length; i++)
    {
        if (buckets[i].Count == 0) continue;
        var mm = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
            Mesh = kit.Prototypes[i],
        };
        mm.InstanceCount = buckets[i].Count;
        for (int j = 0; j < buckets[i].Count; j++)
            mm.SetInstanceTransform(j, buckets[i][j]);

        var mmi = new MultiMeshInstance3D
        {
            Multimesh = mm,
            MaterialOverride = kit.RockMaterial,
        };
        AddChild(mmi);
    }
}

private static ulong CellSeed(int worldSeed, int wx, int wz)
{
    unchecked
    {
        ulong h = 1469598103934665603UL;
        h = (h ^ (uint)worldSeed) * 1099511628211UL;
        h = (h ^ (uint)wx) * 1099511628211UL;
        h = (h ^ (uint)wz) * 1099511628211UL;
        return h;
    }
}
```

- [ ] **Step 2: Add the required `using` for `List<>` in `src/Chunk.cs`**

The file currently has `using System.Collections;`. Add below it:

```csharp
using System.Collections.Generic;
```

- [ ] **Step 3: Resolve and pass the kit in `src/ChunkManager.cs`**

In `LoadChunk`, immediately before `var chunk = chunkScene.Instantiate<Chunk>();`, add:

```csharp
var kit = EnvironmentKitRegistry.Get(maze.RegionEnvironment);
```

Then change the call `chunk.Setup(chunkPos, chunkData);` to:

```csharp
chunk.Setup(chunkPos, chunkData, kit);
```

- [ ] **Step 4: Build**

Run: `dotnet build`
Expected: `Build succeeded`, 0 errors.

- [ ] **Step 5: Verify rocks are emitted (temporary headless count)**

Temporarily add, at the very end of `Chunk.Setup` (before its closing brace):

```csharp
// TEMP — remove after verifying
int _rocks = 0;
foreach (var b in buckets) _rocks += b.Count;
GD.Print($"[ChunkRocks] chunk=({coord.X},{coord.Y}) mmInstances={_rocks}");
```

Run:
```bash
dotnet build && GODOT="$PWD/.bin/Godot_v4.7-stable_mono_linux_x86_64/Godot_v4.7-stable_mono_linux.x86_64"
timeout 8 "$GODOT" --headless --path . 2>&1 | grep -iE "ChunkRocks|error|exception"
```
Expected: several `[ChunkRocks] … mmInstances=<n>` lines with `n > 0`; no error/exception. Remove the `// TEMP` block afterward.

- [ ] **Step 6: Visual check — screenshot the rock walls**

Temporarily add to `src/Player.cs` inside `_PhysicsProcess` (top of method body):

```csharp
// TEMP — remove after verifying
if (++_dbgFrame == 90) {
    GetViewport().GetTexture().GetImage().SavePng("res://shot.png");
    GetTree().Quit();
}
```
And add a field `private int _dbgFrame = 0;` to the class.

Run:
```bash
dotnet build && DISPLAY=:0 timeout 20 "$GODOT" --path . --resolution 1280x720
```
Open `shot.png`. Expected: the maze walls are now built from instanced cliff rocks (irregular silhouette), not flat boxes. Then **delete `shot.png` and remove the temp code + field**.

- [ ] **Step 7: Build clean and commit**

```bash
dotnet build
rm -f shot.png
git add src/Chunk.cs src/ChunkManager.cs
git commit -m "feat: build wall rocks as per-prototype MultiMeshes via environment kit"
```

---

## Task 7: Restyle the wall item to a dark occluder

**Files:**
- Modify: `MazeTiles.tres`

**Interfaces:**
- Consumes: nothing new. Produces: the GridMap wall box now renders as a plain dark surface (visible only in gaps between rocks, reads as shadow); collision unchanged.

- [ ] **Step 1: Point the wall mesh at a plain dark material**

In `MazeTiles.tres`, find `[sub_resource type="StandardMaterial3D" id="StandardMaterial3D_wall"]` and replace its whole body (all `albedo_texture`/`normal_*`/`uv1_*`/`metallic`/`roughness` lines under that sub_resource header) with:

```
albedo_color = Color(0.05, 0.045, 0.04, 1)
roughness = 1.0
metallic_specular = 0.0
```

Leave the `BoxMesh_wall` (size `3.66, 30, 3.66`), `BoxShape3D_wall` (`3.6, 30, 3.6`), and the `[resource]` item bindings untouched. The now-unreferenced wall `NoiseTexture2D_*`/`Gradient_wall`/`FastNoiseLite_wall` sub-resources may remain (harmless) or be deleted for tidiness — do not touch the **floor** sub-resources.

- [ ] **Step 2: Re-import the changed resource**

Run:
```bash
GODOT="$PWD/.bin/Godot_v4.7-stable_mono_linux_x86_64/Godot_v4.7-stable_mono_linux.x86_64"
"$GODOT" --headless --import 2>&1 | grep -iE "error|MazeTiles" | head
```
Expected: no error lines.

- [ ] **Step 3: Visual check — gaps read as shadow, not bright box**

Reuse the temporary screenshot code from Task 6 Step 6 in `src/Player.cs`. Run:
```bash
dotnet build && DISPLAY=:0 timeout 20 "$GODOT" --path . --resolution 1280x720
```
Open `shot.png`. Expected: between/around the rocks, the backing reads as dark recess (no bright flat wall showing through). Delete `shot.png` and remove the temp code again.

- [ ] **Step 4: Commit**

```bash
git add MazeTiles.tres
git commit -m "feat: restyle GridMap wall to dark occluder behind rocks"
```

---

## Task 8: Documentation

**Files:**
- Create: `requirements/REQ-0021-environment-kits/README.md`, `requirements/REQ-0021-environment-kits/01-visual.md`, `requirements/REQ-0021-environment-kits/02-data.md`, `requirements/REQ-0021-environment-kits/design.md`
- Modify: `requirements/README.md`, `requirements/TECH_SPEC.md`, `CLAUDE.md`, `AGENTS.md`

**Interfaces:** none (docs only). Use the next free feature id **REQ-0021 / US-21 / F-47..F-49** (verify these are unused in `requirements/README.md` before writing; if taken, use the next free ones and keep them consistent across all files).

- [ ] **Step 1: Confirm the free IDs**

Run: `grep -oE "REQ-00[0-9][0-9]|US-[0-9]+|F-[0-9]+" requirements/README.md | sort -u | tail -30`
Expected: shows the highest used ids. Use the next free `REQ`, `US`, and `F` numbers (this plan assumes REQ-0021 / US-21 / F-47..F-49).

- [ ] **Step 2: Write the feature folder (Russian WHAT facets + English design.md HOW)**

Create `requirements/REQ-0021-environment-kits/README.md` (Russian): User Story US-21 ("Как игрок, я вижу стены как естественные скалы, различающиеся по типу окружения региона"), acceptance criteria (стены выглядят как скалы, не как коробки; тип окружения выбирается на регион; два типа — «каньон-щель» и «овраг»), an `ID → файл` map, status `✅ реализовано базово`.

Create `01-visual.md` (Russian, WHAT): стены состоят из пересекающихся скальных мешей; силуэт неровный; углы не прямые. Create `02-data.md` (Russian, WHAT): у региона есть тип окружения; «Параметры» таблица (плотность камней на клетку, диапазоны наклона/масштаба — имя + значение + смысл, без имён кода).

Create `design.md` (English, HOW): reference the spec; name `EnvironmentKit`/`SlotCanyonKit`/`RavineKit`/`EnvironmentKitRegistry`/`RockPlacement`, the `Chunk` MultiMesh build, Approach A, determinism (`CellSeed(worldSeed,wx,wz)`), the dark occluder box, and an explicit **границы** section (walls-only; single region; no maze-gen biome field; boulders/rocks/piles and per-kit textures deferred; POM/displacement/voxel not used).

- [ ] **Step 3: Update the registry `requirements/README.md`**

Add a row for REQ-0021 (id · name · US-21 · F-47..F-49 · `✅` · path `REQ-0021-environment-kits/`) and a "Связи между фичами" note linking it to REQ-0003-core (maze/chunk rendering).

- [ ] **Step 4: Update `requirements/TECH_SPEC.md`**

In the maze-geometry/rendering section, add that the GridMap wall item is now a **dark occluder + collision box**, and the visible wall surface is **kit-driven rock `MultiMeshInstance3D`** chosen per region via `MazeData.RegionEnvironment` → `EnvironmentKitRegistry`, deterministic per world cell.

- [ ] **Step 5: Update `CLAUDE.md` and `AGENTS.md` (keep in sync)**

In both files: (a) Architecture — note walls render as environment-kit rock MultiMeshes over a dark occluder GridMap box; (b) Art pipeline table — add `art/RockPack1/` (Arnklit Cliffs & Rocks v1_02) as **Active**, used for wall rocks.

- [ ] **Step 6: Verify no broken relative links**

Run: `grep -rn "REQ-0021" requirements/ | head` and visually confirm the paths resolve.
Expected: registry row + folder files reference each other consistently.

- [ ] **Step 7: Commit**

```bash
git add requirements/ CLAUDE.md AGENTS.md
git commit -m "docs: REQ-0021 environment kits — feature docs, tech spec, art pipeline"
```

---

## Self-Review (completed by plan author)

**Spec coverage:**
- Rock-wall replacement via instanced rocks → Tasks 4, 6. ✅
- Game-side `EnvironmentId` chosen at region creation, `[Export]` for A/B → Task 5. ✅
- Environment-kit abstraction + two kits + registry → Tasks 3, 4. ✅
- Approach A: GridMap collision+occluder kept, rocks additive → Tasks 6, 7. ✅
- Determinism `hash(worldSeed, wx, wz)` → Task 6 (`CellSeed`). ✅
- Per-prototype MultiMesh batching → Task 6. ✅
- Dark occluder material → Task 7. ✅
- Reuse pack material + bare `.res` meshes → Tasks 1, 4. ✅
- Extensibility seam (enum + kit + registry) → structure of Tasks 2–4. ✅
- Verification (build + headless log + screenshot) → each task. ✅
- Docs update (REQ/TECH_SPEC/CLAUDE/AGENTS/registry) → Task 8. ✅

**Placeholder scan:** All placement constants are concrete real values (counts, jitter, tilt, scale multipliers); heights are derived from mesh AABB, not guessed. No "TBD/handle edge cases/similar to Task N". ✅

**Type consistency:** `Setup(coord, chunkData, kit)` defined in Task 6 matches the call updated in the same task; `PlaceRocks(Vector3, ulong) → List<RockPlacement>` consistent across Tasks 3/4/6; `Prototypes`/`BaseScales`/`RockMaterial` names consistent across Tasks 3/4/6; `RegionEnvironment`/`WorldSeed` defined in Task 5 and consumed in Task 6. ✅

**Known iteration point (not a placeholder):** the placement knobs in Task 4 and the dark-material color in Task 7 are expected to be tuned via the Task 6/7 screenshot loop — that is the project's visual-verification method, not deferred work.
