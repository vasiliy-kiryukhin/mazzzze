using Godot;
using PlayersWorlds.Maps.World;
using MgVector = PlayersWorlds.Maps.Vector;

// Map engine (mazzzze v1). Sources the map from the maze-gen region façade
// instead of a coordinate hash: it generates ONE real region at startup with a
// NullRegionStore (regenerate each run, no persistence yet) and answers every
// map query — passability, chunk data, spawn/goal — from the resident
// RegionView. The public surface is unchanged, so ChunkManager, Minimap, and
// Player keep working; only the source of truth moved.
public partial class MazeData : Node
{
	public static MazeData Instance { get; private set; }
	public override void _EnterTree() { Instance = this; }

	// Region footprint per side, in world (Block) cells. This is exactly the
	// size the façade returns (see RegionSize); the corridor count within it is
	// derived from the recipe. 15 = a 7-cell maze rendered at square 1×1.
	private const int RegionFootprintSide = 15;
	// The world seed decides the maze. Set RandomizeEachLaunch = false and use
	// FixedSeed for a reproducible maze; true gives a fresh maze every start.
	private const bool RandomizeEachLaunch = true;
	private const int FixedSeed = 12345;

	// Ширина коридора = 6 × диаметр игрока (коллизия: сфера r=0.3 → Ø0.6) = 3.6
	public const float CellWorldSize = 3.6f;  // размер клетки в мировых единицах
	// Высота стен: уходят высоко в небо (canyon-style), полностью блокируют обзор
	public const float WallHeight = 30.0f;

	private RegionView _region;
	private Vector2I _entrance;
	private Vector2I _exit;

	public Vector2I PlayerStartCell { get; private set; }

	[Export]
	public EnvironmentId RegionEnvironment = EnvironmentId.DarkCanyon;

	public int WorldSeed { get; private set; }

	// The rendered region size in Block cells (replaces the old fixed
	// 10000×10000 world bounds).
	public Vector2I RegionSize =>
		_region == null ? Vector2I.Zero
			: new Vector2I(_region.Size.X, _region.Size.Y);

	// Centre the region on the world origin.
	public float WorldOffsetX => -RegionSize.X * CellWorldSize / 2.0f;
	public float WorldOffsetZ => -RegionSize.Y * CellWorldSize / 2.0f;

	// Entrance/exit cells come from the region's POIs (the longest-path ends).
	public static Vector2I EntranceCell =>
		Instance?._entrance ?? Vector2I.Zero;
	public static Vector2I ExitCell =>
		Instance?._exit ?? Vector2I.Zero;

	public override void _Ready()
	{
		// The world is created once with our footprint and default recipe.
		// Recipe is OUR (client) setting: a Hunt-and-Kill maze with square 1×1
		// cells, so tiles are square in world space.
		var seed = RandomizeEachLaunch ? (int)Time.GetTicksUsec() : FixedSeed;
		WorldSeed = seed;
		var world = new World(
			new NullRegionStore(), seed,
			new MgVector(RegionFootprintSide, RegionFootprintSide),
			RegionRecipe.Maze
				.WithAlgorithm(RegionAlgorithm.AldousBroder)
				.WithRooms(2, new MgVector(3, 3), new MgVector(5, 5), RoomKind.Hall)
				.WithCells(1));
		_region = world.GetOrCreate(new RegionAddress(new MgVector(0, 0)));

		var entrance = FindPoi(PoiKind.Entrance);
		var exit = FindPoi(PoiKind.Exit);
		_entrance = new Vector2I(entrance.X, entrance.Y);
		_exit = new Vector2I(exit.X, exit.Y);
		PlayerStartCell = _entrance;

		GD.Print($"[MazeData] region {RegionSize.X}x{RegionSize.Y} block cells, " +
			$"seed={seed}, environment={RegionEnvironment}, entrance={_entrance}, exit={_exit}, " +
			$"offset=({WorldOffsetX:F0}, {WorldOffsetZ:F0})");
	}

	private MgVector FindPoi(PoiKind kind)
	{
		foreach (var poi in _region.Pois)
		{
			if (poi.Kind == kind) return poi.Local;
		}
		return new MgVector(0, 0);
	}

	// Детерминированная проверка: является ли клетка коридором (false = стена).
	// Now answered by the region: outside the region reads as wall.
	public static bool IsFloor(int wx, int wz)
	{
		var region = Instance?._region;
		if (region == null) return false;
		var cell = new MgVector(wx, wz);
		return region.Contains(cell) && region.CellAt(cell).IsPassable;
	}

	private const string TagWallAxisX = "MAZE2D_WALL_AXIS_X";
	private const string TagWallAxisY = "MAZE2D_WALL_AXIS_Y";

	public static WallAxis WallAxisAt(int wx, int wz)
	{
		var region = Instance?._region;
		if (region == null) return WallAxis.None;
		var cell = new MgVector(wx, wz);
		if (!region.Contains(cell)) return WallAxis.None;
		var tags = region.CellAt(cell).Tags;
		if (tags == null) return WallAxis.None;
		for (int i = 0; i < tags.Count; i++)
		{
			if (tags[i] == TagWallAxisX) return WallAxis.X;
			if (tags[i] == TagWallAxisY) return WallAxis.Z;
		}
		return WallAxis.None;
	}

	// Генерация данных чанка: 0 = пол (коридор), 1 = стена.
	public int[,] GetChunkData(int chunkX, int chunkZ, int chunkSize)
	{
		int[,] data = new int[chunkSize, chunkSize];
		for (int x = 0; x < chunkSize; x++)
		{
			for (int z = 0; z < chunkSize; z++)
			{
				int wx = chunkX * chunkSize + x;
				int wz = chunkZ * chunkSize + z;
				data[x, z] = IsFloor(wx, wz) ? 0 : 1;
			}
		}
		return data;
	}
}
