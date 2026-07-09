using Godot;
using System;
using System.Collections.Generic;

public partial class ChunkManager : Node3D
{
	private const int ChunkSize = 16;
	private const int LoadDistance = 1;

	private Dictionary<string, Node3D> activeChunks = [];
	private PackedScene chunkScene;
	private MeshLibrary meshLibrary;

	public override void _Ready()
	{
		chunkScene = ResourceLoader.Load<PackedScene>("res://chunk.tscn");
		meshLibrary = ResourceLoader.Load<MeshLibrary>("res://MazeTiles.tres");
		GD.Print($"[ChunkManager] Ready — chunkSize={ChunkSize}, loadDistance={LoadDistance}");
	}

	public void UpdateChunks(Vector2 playerWorldPos)
	{
		var maze = MazeData.Instance;
		if (maze == null) return;

		float cellSize = MazeData.CellWorldSize;
		float cellX = (playerWorldPos.X - maze.WorldOffsetX) / cellSize;
		float cellZ = (playerWorldPos.Y - maze.WorldOffsetZ) / cellSize;

		Vector2 centerChunk = new(
			Mathf.Floor(cellX / ChunkSize),
			Mathf.Floor(cellZ / ChunkSize)
		);

		// Загрузка
		for (int dx = -LoadDistance; dx <= LoadDistance; dx++)
		{
			for (int dz = -LoadDistance; dz <= LoadDistance; dz++)
			{
				Vector2 chunkPos = centerChunk + new Vector2(dx, dz);
				string key = $"{chunkPos.X}_{chunkPos.Y}";
				if (!activeChunks.ContainsKey(key))
				{
					LoadChunk(chunkPos);
				}
			}
		}

		// Выгрузка
		var toRemove = new List<string>();
		foreach (var kvp in activeChunks)
		{
			string[] parts = kvp.Key.Split('_');
			Vector2 chunkPos = new(int.Parse(parts[0]), int.Parse(parts[1]));
			if (Mathf.Abs(chunkPos.X - centerChunk.X) > LoadDistance ||
				Mathf.Abs(chunkPos.Y - centerChunk.Y) > LoadDistance)
			{
				GD.Print($"[ChunkManager] UNLOAD  chunk ({chunkPos.X},{chunkPos.Y})  " +
					$"world=({kvp.Value.Position.X:F0},{kvp.Value.Position.Z:F0})");
				kvp.Value.QueueFree();
				toRemove.Add(kvp.Key);
			}
		}
		foreach (string key in toRemove)
			activeChunks.Remove(key);
	}

	private void LoadChunk(Vector2 chunkPos)
	{
		var maze = MazeData.Instance;
		if (maze == null) return;

		var chunkData = maze.GetChunkData((int)chunkPos.X, (int)chunkPos.Y, ChunkSize);
		if (chunkData == null) return;

		float cs = MazeData.CellWorldSize;
		var kit = EnvironmentKitRegistry.Get(maze.RegionEnvironment);
		var chunk = chunkScene.Instantiate<Chunk>();
		chunk.Position = new Vector3(
			chunkPos.X * ChunkSize * cs + maze.WorldOffsetX,
			0,
			chunkPos.Y * ChunkSize * cs + maze.WorldOffsetZ
		);
		chunk.MeshLibrary = meshLibrary;
		AddChild(chunk);
		chunk.Setup(chunkPos, chunkData, kit);

		GD.Print($"[ChunkManager] LOAD   chunk ({chunkPos.X},{chunkPos.Y})  " +
			$"size={ChunkSize * cs:F0}x{ChunkSize * cs:F0}  " +
			$"world=({chunk.Position.X:F0},{chunk.Position.Z:F0})  " +
			$"totalActive={activeChunks.Count + 1}");

		activeChunks[$"{chunkPos.X}_{chunkPos.Y}"] = chunk;
	}
}
