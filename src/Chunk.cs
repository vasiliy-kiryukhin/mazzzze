using Godot;
using System;
using System.Collections;
using System.Collections.Generic;

public partial class Chunk : Node3D
{
	[Export]
	public int ChunkSize = 16;

	[Export]
	public MeshLibrary MeshLibrary;

	private GridMap gridmap;
	private Vector2 chunkCoord;

	public override void _Ready()
	{
		gridmap = GetNode<GridMap>("GridMap");
		gridmap.MeshLibrary = MeshLibrary;
	}
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
				WallAxis axis = MazeData.WallAxisAt(wx, wz);

				foreach (var p in kit.PlaceRocks(center, seed, axis))
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
}
