using Godot;

// Минимальная сущность предмета для инвентаря (REQ-0011).
//
// Это НЕ полная реализация REQ-0012 (базовый предмет): здесь только те поля,
// что нужны инвентарю для отображения слота и делегирования «применения».
// Состояния InWorld/Activated, бронирование слота (F-19a), 3D-представление в
// мире и подбор (pickup) отложены в REQ-0012 / отдельные задачи.
public enum ItemCategory
{
	Consumable, // расходник (зелье, еда) — уничтожается при применении
	Key,        // ключ / квестовый / инструмент — остаётся после применения
}

// Паттерн использования (F-18):
//   ImmediateA — применяется напрямую из инвентаря одной командой (Item.Use).
//   ActivatedB — берётся в руку (Activated), слот бронируется (F-19a), затем
//                используется отдельным жестом (ЛКМ для фотоаппарата, «идти вперёд»
//                для фотографии).
public enum ItemUsage
{
	ImmediateA,
	ActivatedB,
}

public partial class Item : RefCounted
{
	public string TypeId { get; }        // ЧТО за предмет (F-17), неизменяем
	public string DisplayName { get; }
	public ItemCategory Category { get; }
	public ItemUsage Usage { get; }      // паттерн использования (F-18): A напрямую, B через руку
	public string ModelPath { get; }     // .glb модели: и для иконки, и для предмета в мире (REQ-0014)
	public Texture2D Icon { get; set; }  // визуал в ячейке; рендер glb-модели, ставится виджетом

	public Item(string typeId, string displayName, ItemCategory category, string modelPath,
		ItemUsage usage = ItemUsage.ImmediateA)
	{
		TypeId = typeId;
		DisplayName = displayName;
		Category = category;
		ModelPath = modelPath;
		Usage = usage;
	}

	// Применение из инвентаря (паттерн A, F-18). Возвращает true, если предмет
	// должен быть уничтожен (расходник). Реальные эффекты конкретных предметов —
	// в их требованиях (например, REQ-0013 для фотоаппарата); здесь — заглушка.
	public bool Use()
	{
		GameLog.Print($"[Inventory] Use item '{TypeId}' ({DisplayName})");
		return Category == ItemCategory.Consumable;
	}

	// Строит 3D-узел модели предмета — используется и для иконки (SubViewport),
	// и для предмета в мире (WorldItem). По умолчанию инстанцирует glb по ModelPath;
	// предметы без ассета (напр. PhotoItem) переопределяют процедурной моделью.
	public virtual Node3D BuildModel()
	{
		return GD.Load<PackedScene>(ModelPath).Instantiate<Node3D>();
	}
}
