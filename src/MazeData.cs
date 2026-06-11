using Godot;
using System;

public partial class MazeData : Node
{
	public static MazeData Instance { get; private set; }
	public override void _EnterTree() { Instance = this; }

	public const int WorldWidth = 10000;
	public const int WorldHeight = 10000;
	// Ширина коридора = 6 × диаметр игрока (коллизия: сфера r=0.3 → Ø0.6) = 3.6
	public const float CellWorldSize = 3.6f;  // размер клетки в мировых единицах
	// Высота стен: уходят высоко в небо (canyon-style), полностью блокируют обзор
	public const float WallHeight = 30.0f;

	public Vector2I PlayerStartCell { get; private set; }
	public float WorldOffsetX => -WorldWidth * CellWorldSize / 2.0f;
	public float WorldOffsetZ => -WorldHeight * CellWorldSize / 2.0f;

	public override void _Ready()
	{
		// Вход и выход фиксированы
		PlayerStartCell = new Vector2I(1, 1);
		GD.Print($"[MazeData] World {WorldWidth}x{WorldHeight}, cellSize={CellWorldSize}, offset=({WorldOffsetX:F0}, {WorldOffsetZ:F0})");
	}

	// Детерминированная проверка: является ли клетка коридором (false = стена)
	public static bool IsFloor(int wx, int wz)
	{
		// Границы — всегда стена
		if (wx <= 0 || wz <= 0 || wx >= WorldWidth - 1 || wz >= WorldHeight - 1)
			return false;

		// Вход
		if (wx == 1 && wz == 0) return true;
		// Выход
		if (wx == WorldWidth - 2 && wz == WorldHeight - 1) return true;

		// Все нечётные-нечётные клетки — коридоры (гарантирует связность)
		if ((wx & 1) == 1 && (wz & 1) == 1)
			return true;

		// Хеш для стен между коридорами
		uint h = (uint)(wx * 0x45d9f3b + wz * 0x119de1f3);
		h = (h ^ (h >> 16)) * 0x85ebca6b;
		h = (h ^ (h >> 13)) * 0xc2b2ae35;
		h = h ^ (h >> 16);

		// Стена между двумя коридорами по X (чёт X, нечёт Z): 70% проход
		if ((wx & 1) == 0 && (wz & 1) == 1)
			return (h % 100) < 70;

		// Стена между двумя коридорами по Z (нечёт X, чёт Z): 70% проход
		if ((wx & 1) == 1 && (wz & 1) == 0)
			return (h % 100) < 70;

		// Столб (чёт-чёт): 5% проход
		return (h % 100) < 5;
	}

	// Генерация данных чанка
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
