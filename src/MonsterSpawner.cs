using Godot;
using System.Collections.Generic;

// Минимальный отладочный спавнер монстров (REQ-0019/0020).
//
// Полноценный спавнер — отдельная будущая фича; здесь лишь размещаем несколько Ifrit'ов
// рядом со стартовой клеткой игрока, чтобы фичу можно было увидеть и проверить в игре.
// Также создаёт `DamageHud` (красная вспышка при контакте) в слое HUD.
public partial class MonsterSpawner : Node3D
{
	[Export] public int Count = 3;              // сколько Ifrit'ов заспавнить
	[Export] public int MinCellDistance = 4;    // ближняя граница кольца спавна (клеток)
	[Export] public int MaxCellDistance = 11;   // дальняя граница кольца спавна (клеток)

	public override void _Ready()
	{
		var hud = GetNodeOrNull<CanvasLayer>("/root/Main/HUD");
		if (hud != null && hud.GetNodeOrNull("DamageFlash") == null)
			hud.AddChild(new DamageHud { Name = "DamageFlash" });

		CallDeferred(nameof(SpawnMonsters)); // после _Ready мира: MazeData/Player готовы
	}

	private void SpawnMonsters()
	{
		var maze = MazeData.Instance;
		Node main = GetNodeOrNull("/root/Main");
		if (maze == null || main == null)
			return;

		Vector2I start = maze.PlayerStartCell;
		List<Vector2I> cells = PickSpawnCells(start, Count);
		float cs = MazeData.CellWorldSize;

		foreach (Vector2I c in cells)
		{
			var monster = new Ifrit();
			monster.Position = new Vector3(
				(c.X + 0.5f) * cs + maze.WorldOffsetX,
				0.5f,
				(c.Y + 0.5f) * cs + maze.WorldOffsetZ);
			main.AddChild(monster);
		}
		GameLog.Print($"[MonsterSpawner] Spawned {cells.Count} Ifrit near start {start}");
	}

	// Клетки пола в кольце [Min..Max] клеток от старта, не слишком близко к игроку.
	private List<Vector2I> PickSpawnCells(Vector2I start, int count)
	{
		var result = new List<Vector2I>();
		for (int tries = 0; tries < 400 && result.Count < count; tries++)
		{
			int dx = GD.RandRange(-MaxCellDistance, MaxCellDistance);
			int dz = GD.RandRange(-MaxCellDistance, MaxCellDistance);
			var c = new Vector2I(start.X + dx, start.Y + dz);
			int cheb = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dz));
			if (cheb < MinCellDistance || cheb > MaxCellDistance) continue;
			if (!MazeData.IsFloor(c.X, c.Y)) continue;
			if (result.Contains(c)) continue;
			result.Add(c);
		}
		return result;
	}
}
