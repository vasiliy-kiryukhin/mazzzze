using Godot;
using System.Collections.Generic;

// Инвентарь (рюкзак) — HUD-виджет в правом нижнем углу (REQ-0011, US-11 / F-12..F-14).
//
// Два состояния отрисовки:
//   - compact (закрыт): иконка мешка + счётчик «N/12»
//   - expanded (открыт): сетка 3×4, пустые/заполненные ячейки, выбор ряд→ячейка
//
// Управление (F-14, F-27):
//   - двойное нажатие I  → открыть/закрыть
//   - при открытом: цифра 1–3 выбирает ряд (подсветка), затем 1–4 — ячейку (применение)
//   - Shift + цифра-колонки → выбросить ячейку (drop, REQ-0015)
//   - G → выбросить предмет под текущим курсором (drop, REQ-0015)
//
// Игра при открытии НЕ ставится на паузу, мышь остаётся захваченной, управление
// цифровое (ввод по клику в углу — отложен, т.к. курсор захвачен во время игры).
//
// Применение паттерна A делегируется предмету (Item.Use, F-18). Выброс (REQ-0015)
// создаёт летящую звёздочку (DropProjectile) → предмет в мире (WorldItem, REQ-0014).
// Подбор (REQ-0016) — обратный переход: автоматически, когда игрок близко к предмету
// с прямой видимостью; PickupProjectile летит от предмета к игроку, предмет ложится в слот.
//
// Паттерн B (F-18/B, F-19a): выбор ячейки с предметом Usage=ActivatedB берёт его «в руку»
// (Activated), слот бронируется; повторный выбор — деактивация. ЛКМ по активированному
// фотоаппарату (REQ-0013) открывает видоискатель (ViewfinderHud) → создаёт фотографию
// (PhotoItem) в забронированном слоте. Активированная фотография (REQ-0017) переносит
// игрока «входом вперёд» (UpdatePhotoEnter). Эксклюзивность активации — один предмет.
public partial class InventoryHud : Control
{
	[Export] public float Margin = 16.0f;
	[Export] public float CellSize = 72.0f;
	[Export] public float CellGap = 8.0f;
	[Export] public float Padding = 12.0f;
	[Export] public float TitleBarHeight = 28.0f;
	[Export] public float FlashDuration = 0.35f;

	// Конфигурация выброса (REQ-0015 / REQ-0014).
	[Export] public float DropDistanceBodies = 3.0f;    // дальность в «корпусах» игрока
	[Export] public float PlayerBodyDiameter = 0.6f;    // эталон «корпуса»
	[Export] public float WorldItemSizeFraction = 0.25f; // 1/4 роста игрока (модель в мире)
	[Export] public float PlayerHeight = 1.8f;          // эталон роста
	[Export] public float DropFlightDuration = 0.6f;    // время полёта звёздочки, сек
	[Export] public float DropArcHeight = 1.2f;         // высота параболы, м

	// Конфигурация подбора (REQ-0016).
	[Export] public float PickupRange = 1.1f;           // радиус срабатывания подбора, м
	[Export] public float PickupArcHeight = 1.0f;       // высота параболы обратной звезды, м
	[Export] public float PickupFlightDuration = 0.6f;  // время полёта звёздочки к игроку, сек

	// Вход в фотографию (REQ-0017 / F-33): сколько секунд идти вперёд для переноса (сокращено ×1.5).
	[Export] public float EnterDuration = 1.3333f;

	// Бросок теннисного мячика (REQ-0021 / F-48, F-50). Значения — стартовые, калибруются.
	// Скорости умеренные: коридор узкий (3.6 м), слишком быстрый мяч бесконечно скачет между стен.
	[Export] public float MinThrowForce = 5.0f;    // сила при тапе / минимуме заряда, м/с
	[Export] public float MaxThrowForce = 11.0f;   // сила при полном заряде, м/с
	[Export] public float MaxChargeTime = 1.0f;    // время выхода на максимум; далее плато, с
	[Export] public float ThrowUpwardBias = 3.0f;  // вертикальная добавка начальной скорости (дуга)
	[Export] public float MoveSpeedFactorWhileCharging = 0.45f; // множитель ходьбы во время заряда
	[Export] public float BallSize = 0.2f;         // физический размер мяча — един для руки/снаряда/мира

	// Палитра в тон миникарте (тёплый пергамент).
	private static readonly Color PanelBg      = new(0.10f, 0.08f, 0.06f, 0.85f);
	private static readonly Color BorderColor  = new(0.18f, 0.12f, 0.07f);
	private static readonly Color EmptyOutline = new(0.42f, 0.36f, 0.28f, 0.7f);
	private static readonly Color FilledBg     = new(0.30f, 0.25f, 0.18f);
	private static readonly Color RowSelect    = new(0.85f, 0.68f, 0.28f);
	private static readonly Color CursorColor  = new(0.95f, 0.85f, 0.55f);
	private static readonly Color TextColor    = new(0.90f, 0.83f, 0.68f);
	private static readonly Color HintColor    = new(0.60f, 0.53f, 0.42f);
	private static readonly Color ConsumableDot= new(0.35f, 0.75f, 0.35f);
	private static readonly Color KeyDot       = new(0.90f, 0.72f, 0.25f);
	private static readonly Color FlashColor   = new(1.0f, 0.97f, 0.85f);
	private static readonly Color HandColor    = new(0.45f, 0.78f, 1.0f); // «в руке» (активирован)

	private readonly Inventory _inv = new();

	private bool _open;
	private int _selectedRow = -1;    // -1 = ряд ещё не выбран
	private int _cursorSlot = -1;     // последняя выбранная ячейка (курсор для drop по G)

	private int _flashSlot = -1;      // ячейка, которую сейчас «вспыхиваем»
	private float _flashT;            // 1 → 0

	private readonly HashSet<int> _incomingSlots = new(); // слоты с «летящим» предметом (pickup)

	// Активация в руку (F-18/B) и бронь слота (F-19a).
	private Item _activatedItem;      // предмет «в руке», или null
	private int _reservedSlot = -1;   // забронированный слот активированного предмета
	private float _enterProgress;     // прогресс входа в фотографию, сек (REQ-0017 / F-33)

	private bool _charging;           // идёт зарядка броска мяча (REQ-0021 / F-50)
	private float _chargeT;           // накопленное время удержания ЛКМ, сек

	private SubViewport _iconViewport;
	private Player _player;
	private ViewfinderHud _viewfinder;
	private PhotoEnterHud _photoEnter;

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Ignore; // не перехватываем клики у игры
		_player = GetNodeOrNull<Player>("/root/Main/Player");
		_viewfinder = GetNodeOrNull<ViewfinderHud>("../Viewfinder");
		_photoEnter = GetNodeOrNull<PhotoEnterHud>("../PhotoEnter");
		// Радиус «взвода» чуть больше радиуса подбора — гистерезис (REQ-0016 / F-29).
		WorldItem.ArmingRadius = PickupRange + 0.4f;

		// Засев: один предмет — «Старинный фотоаппарат» (REQ-0013). Паттерн B: активируется
		// в руку, использование (ЛКМ) открывает видоискатель и создаёт фотографию.
		var camera = new Item("vintage_camera", "Старинный фотоаппарат", ItemCategory.Key,
			"res://art/old_kodak_camera.glb", ItemUsage.ActivatedB);
		camera.Icon = BuildIcon(camera);
		_inv.PutAt(0, camera);

		// Теннисный мячик (REQ-0021): многоразовый, паттерн B. Активируется в руку, зарядка/бросок ЛКМ.
		var ball = new Item("tennis_ball", "Теннисный мячик", ItemCategory.Key,
			"res://art/base_ball.glb", ItemUsage.ActivatedB);
		ball.Icon = BuildIcon(ball);
		_inv.PutAt(1, ball);
		GameLog.Print($"[Inventory] Seeded {_inv.Count}/{Inventory.Capacity} items");
	}

	// Рендерит модель предмета в текстуру для иконки слота (glb или процедурную — BuildModel).
	private Texture2D BuildIcon(Item item)
	{
		_iconViewport = new SubViewport
		{
			Size = new Vector2I(192, 192),
			TransparentBg = true,
			OwnWorld3D = true,
			RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
		};
		AddChild(_iconViewport);

		var model = item.BuildModel();
		_iconViewport.AddChild(model);

		var key = new DirectionalLight3D { LightEnergy = 1.6f };
		key.RotationDegrees = new Vector3(-45, -35, 0);
		_iconViewport.AddChild(key);
		var fill = new OmniLight3D { LightEnergy = 1.2f, OmniRange = 100 };
		fill.Position = new Vector3(-2, 1, 2);
		_iconViewport.AddChild(fill);

		var cam = new Camera3D();
		_iconViewport.AddChild(cam);
		FrameCamera(cam, model);

		return _iconViewport.GetTexture();
	}

	// Ставит камеру на 3/4-вид так, чтобы модель целиком поместилась в кадр
	// (модель произвольного масштаба/ориентации — считаем её общий AABB).
	private static void FrameCamera(Camera3D cam, Node3D model)
	{
		Aabb bounds = WorldItem.ComputeSceneAabb(model);
		Vector3 center = bounds.Position + bounds.Size * 0.5f;
		float radius = Mathf.Max(bounds.Size.Length() * 0.5f, 0.01f);
		Vector3 dir = new Vector3(0.6f, 0.45f, 1.0f).Normalized();
		// Плотная посадка: модель почти заполняет ячейку (на 2K иконки были мелкими).
		cam.Fov = 30.0f;
		cam.Position = center + dir * radius * 3.4f;
		cam.LookAt(center, Vector3.Up);
	}

	public override void _Input(InputEvent @event)
	{
		// Теннисный мячик (REQ-0021 / F-50): зажать ЛКМ — заряд, отпустить — бросок, RMB — отмена.
		if (_activatedItem != null && _activatedItem.TypeId == "tennis_ball")
		{
			if (@event.IsActionPressed("use_activated"))
			{
				_charging = true;
				_chargeT = 0.0f;
				GetViewport().SetInputAsHandled();
				return;
			}
			if (@event.IsActionReleased("use_activated") && _charging)
			{
				ThrowActivated();
				GetViewport().SetInputAsHandled();
				return;
			}
			if (@event.IsActionPressed("throw_cancel") && _charging)
			{
				CancelCharge();
				GetViewport().SetInputAsHandled();
				return;
			}
		}

		// ЛКМ — использовать активированный фотоаппарат: открыть видоискатель (REQ-0013 / F-21).
		// Повторная ЛКМ во время видоискателя игнорируется. Фотография на ЛКМ не реагирует.
		if (@event.IsActionPressed("use_activated"))
		{
			if (_activatedItem != null && _activatedItem.TypeId == "vintage_camera"
				&& _viewfinder != null && !_viewfinder.Active)
			{
				_viewfinder.Begin(_player, OnCameraFired);
				GetViewport().SetInputAsHandled();
			}
			return;
		}

		// Во время видоискателя инвентарь и цифровой ввод заблокированы (F-21).
		if (_viewfinder != null && _viewfinder.Active)
			return;

		// Нажатие I — открыть/закрыть (F-14).
		if (@event.IsActionPressed("inventory_toggle"))
		{
			Toggle();
			GetViewport().SetInputAsHandled();
			return;
		}

		if (!_open || @event is not InputEventKey key || !key.Pressed || key.Echo)
			return;

		// G — выбросить предмет под текущим курсором (REQ-0015 / F-27).
		if (key.PhysicalKeycode == Key.G)
		{
			if (_cursorSlot >= 0)
				DropSlot(_cursorSlot);
			GetViewport().SetInputAsHandled();
			return;
		}

		// Физический код устойчив к раскладке и к Shift (иначе верхний ряд даёт !@#$).
		int digit = DigitOf(key.PhysicalKeycode);
		if (digit < 0)
			return;

		HandleDigit(digit, key.ShiftPressed);
		GetViewport().SetInputAsHandled();
	}

	// Возвращает 1–4 для цифровых клавиш (верхний ряд и цифровой блок), иначе -1.
	private static int DigitOf(Key kc)
	{
		switch (kc)
		{
			case Key.Key1: case Key.Kp1: return 1;
			case Key.Key2: case Key.Kp2: return 2;
			case Key.Key3: case Key.Kp3: return 3;
			case Key.Key4: case Key.Kp4: return 4;
			default: return -1;
		}
	}

	// Первое нажатие (1–3) выбирает ряд; второе (1–4) — колонку. Без Shift — применить,
	// с Shift — выбросить (REQ-0015 / F-27). Ячейка запоминается как курсор для drop по G.
	private void HandleDigit(int digit, bool shift)
	{
		if (_selectedRow < 0)
		{
			if (digit <= Inventory.Rows) // ряды только 1–3
			{
				_selectedRow = digit - 1;
				QueueRedraw();
			}
			return;
		}

		int col = digit - 1; // 0–3
		int slot = _selectedRow * Inventory.Cols + col;
		_cursorSlot = slot; // курсор остаётся на выбранной ячейке

		if (shift)
		{
			// Выброс: активированный предмет — как drop activated (Activated → InWorld).
			if (slot == _reservedSlot)
				DropActivated();
			else
				DropSlot(slot);
		}
		else if (slot == _reservedSlot)
		{
			Deactivate(); // повторный выбор активированного — вернуть в руку → слот (toggle, F-19)
		}
		else
		{
			Item item = _inv.Get(slot);
			if (item != null && item.Usage == ItemUsage.ActivatedB)
				ActivateSlot(slot); // взять в руку, слот бронируется (F-18/B, F-19a)
			else
				ApplySlot(slot);    // применить напрямую (паттерн A)
		}

		_selectedRow = -1; // возвращаемся к выбору ряда
		QueueRedraw();
	}

	private void ApplySlot(int slot)
	{
		Item item = _inv.Get(slot);
		if (item == null) // пустая ячейка — ничего не происходит (F-14)
			return;

		_flashSlot = slot; // анимированная подсветка применения (F-13)
		_flashT = 1.0f;

		if (item.Use()) // расходник — уничтожается, слот освобождается
			_inv.RemoveAt(slot);
	}

	// Выброс (REQ-0015 / F-26): убрать из слота и запустить летящую звёздочку в мир.
	private void DropSlot(int slot)
	{
		Item item = _inv.RemoveAt(slot);
		if (item == null) // пустая ячейка — ничего не происходит
			return;

		SpawnDrop(item);
	}

	private void SpawnDrop(Item item)
	{
		if (_player == null)
			return;

		// Направление — куда смотрит игрок; при вырождении берём направление камеры.
		Vector2 facing2 = _player.PlanarFacing;
		if (facing2.LengthSquared() < 0.0001f) facing2 = _player.PlanarCamForward;
		if (facing2.LengthSquared() < 0.0001f) facing2 = new Vector2(0, -1);
		facing2 = facing2.Normalized();
		Vector3 dir = new Vector3(facing2.X, 0, facing2.Y);

		float distance = DropDistanceBodies * PlayerBodyDiameter;

		// Кламп к стене: луч на уровне корпуса, чтобы не улететь за стену.
		Vector3 origin = _player.GlobalPosition + Vector3.Up * 0.5f;
		var query = PhysicsRayQueryParameters3D.Create(origin, origin + dir * distance);
		query.CollisionMask = 1;
		query.Exclude = new Godot.Collections.Array<Rid> { _player.GetRid() };
		var hit = _player.GetWorld3D().DirectSpaceState.IntersectRay(query);
		if (hit.Count > 0)
			distance = Mathf.Max(0.3f, origin.DistanceTo((Vector3)hit["position"]) - 0.4f);

		Vector3 start = _player.GlobalPosition + Vector3.Up * 1.0f;
		float lx = _player.GlobalPosition.X + dir.X * distance;
		float lz = _player.GlobalPosition.Z + dir.Z * distance;
		Vector3 land = new Vector3(lx, FloorYAt(lx, lz, _player.GlobalPosition.Y), lz);

		float targetHeight = WorldHeightFor(item);

		var projectile = new DropProjectile();
		_player.GetParent().AddChild(projectile);
		projectile.Setup(start, land, DropArcHeight, DropFlightDuration, item, targetHeight);
		GameLog.Print($"[Inventory] Drop '{item.TypeId}' → world at ({land.X:F1}, {land.Z:F1})");
	}

	// Активация (InInventory → Activated, F-18/B): взять предмет в руку. Слот бронируется
	// (F-19a): предмет остаётся в ячейке (блокирует её), но помечен как «в руке».
	// Эксклюзивность (F-19): предыдущий активированный сначала деактивируется.
	private void ActivateSlot(int slot)
	{
		Item item = _inv.Get(slot);
		if (item == null || item.Usage != ItemUsage.ActivatedB)
			return;
		if (_reservedSlot >= 0)
			Deactivate();
		_activatedItem = item;
		_reservedSlot = slot;
		_enterProgress = 0.0f;
		_player?.PlayPickupGesture(); // анимация взятия предмета в руку (F-15/анимация)
		if (item is PhotoItem photo)
			_photoEnter?.BeginPreview(_player, photo); // живое окно вида «сквозь фото» (REQ-0017)
		if (item.TypeId == "tennis_ball") // мяч виден в руке как обратная связь при зарядке (REQ-0021)
			_player?.ShowHandItem(item.BuildModel(), BallSize);
		GameLog.Print($"[Inventory] Activate '{item.TypeId}' → hand (slot {slot} reserved)");
		QueueRedraw();
	}

	// Деактивация (Activated → InInventory, F-19): вернуть предмет в забронированный слот.
	private void Deactivate()
	{
		if (_reservedSlot < 0)
			return;
		if (_viewfinder != null && _viewfinder.Active)
			_viewfinder.Cancel();
		GameLog.Print($"[Inventory] Deactivate '{_activatedItem?.TypeId}' → slot {_reservedSlot}");
		ClearActivated();
	}

	// Выброс активированного (Activated → InWorld, F-20): слот освобождается, предмет летит в мир.
	private void DropActivated()
	{
		if (_reservedSlot < 0)
			return;
		if (_viewfinder != null && _viewfinder.Active)
			_viewfinder.Cancel();
		int slot = _reservedSlot;
		Item item = _inv.RemoveAt(slot);
		ClearActivated();
		if (item != null)
			SpawnDrop(item);
	}

	// Бросок активированного мяча (Activated → InWorld, REQ-0021 / F-47, F-48): слот освобождается
	// (как drop activated), но мяч НЕ расходуется — он летит физическим снарядом и вернётся авто-подбором.
	private void ThrowActivated()
	{
		if (_reservedSlot < 0 || _activatedItem == null || _activatedItem.TypeId != "tennis_ball")
		{
			CancelCharge();
			return;
		}
		float force = Mathf.Lerp(MinThrowForce, MaxThrowForce,
			Mathf.Clamp(_chargeT / Mathf.Max(MaxChargeTime, 0.001f), 0.0f, 1.0f));
		int slot = _reservedSlot;
		Item item = _inv.RemoveAt(slot); // слот освобождается (бронь снимается), но item жив — летит в мир
		ClearActivated();
		SpawnThrow(item, force);
	}

	// Высота модели предмета в мире. Мяч имеет собственный единый размер (`BallSize`) во всех
	// состояниях (рука/снаряд/лежит), иначе он «прыгал» бы в размере при приземлении (REQ-0021).
	private float WorldHeightFor(Item item) =>
		item != null && item.TypeId == "tennis_ball" ? BallSize : WorldItemSizeFraction * PlayerHeight;

	// Создаёт физический снаряд-мяч: направление — yaw камеры; «видел бросок» фиксируется по реестру
	// монстров в момент выпуска (REQ-0021 / F-49).
	private void SpawnThrow(Item item, float force)
	{
		if (_player == null || item == null)
			return;
		Vector3 dir = _player.CameraYawForward;
		dir.Y = 0.0f;
		if (dir.LengthSquared() < 0.0001f) dir = Vector3.Forward;
		dir = dir.Normalized();

		var aware = new HashSet<Monster>();
		foreach (Monster m in Monster.All)
			if (IsInstanceValid(m) && m.SeesPlayerNow()) aware.Add(m);

		// Выпуск из руки (центр мяча в кисти) чуть вперёд по направлению броска, чтобы не задеть игрока.
		Vector3 spawn = _player.HandThrowOrigin + dir * 0.35f;
		float targetHeight = WorldHeightFor(item);

		var proj = new ThrowProjectile();
		_player.GetParent().AddChild(proj);
		proj.Setup(spawn, dir, force, ThrowUpwardBias, item, targetHeight, aware);
		GameLog.Print($"[Inventory] Throw '{item.TypeId}' force={force:F1} (aware monsters={aware.Count})");
	}

	// Уничтожение активированного с заменой (F-19a): камера → фотография в тот же слот,
	// либо освобождение слота (replacement == null, напр. израсходованная фотография).
	private void ConsumeActivated(Item replacement)
	{
		if (_reservedSlot < 0)
			return;
		int slot = _reservedSlot;
		_inv.RemoveAt(slot);
		ClearActivated();
		if (replacement != null)
		{
			replacement.Icon = BuildIcon(replacement);
			_inv.PutAt(slot, replacement);
		}
		QueueRedraw();
	}

	private void ClearActivated()
	{
		_activatedItem = null;
		_reservedSlot = -1;
		_enterProgress = 0.0f;
		CancelCharge(); // сброс зарядки броска и восстановление скорости/модели в руке (REQ-0021)
		if (_photoEnter != null)
		{
			_photoEnter.Progress = 0.0f;
			_photoEnter.EndPreview();
		}
		QueueRedraw();
	}

	// Сброс заряда броска без выпуска мяча (RMB или деактивация): скорость и модель в руке восстановлены.
	private void CancelCharge()
	{
		_charging = false;
		_chargeT = 0.0f;
		if (_player != null)
			_player.ExternalSpeedFactor = 1.0f;
		_player?.HideHandItem();
		if (_activatedItem != null && _activatedItem.TypeId == "tennis_ball")
			_player?.ShowHandItem(_activatedItem.BuildModel(), BallSize); // мяч остаётся в руке
	}

	// Срабатывание фотоаппарата (конец таймера видоискателя, F-22): создаём фотографию с
	// запечатлёнными позицией и направлением (F-32), кладём в слот, уничтожаем камеру.
	private void OnCameraFired()
	{
		if (_player == null)
			return;
		var pos = new Vector2(_player.GlobalPosition.X, _player.GlobalPosition.Z);
		var photo = new PhotoItem(pos, _player.CameraYawDeg, _player.CameraPitchDeg);
		int slot = _reservedSlot;
		ConsumeActivated(photo);
		_player.PlayPickupGesture(); // жест подбора: игроку очевидно, что в инвентаре новый предмет
		_flashSlot = slot;           // вспышка на ячейке, куда легла фотография (F-30)
		_flashT = 1.0f;
		GameLog.Print($"[Camera] Photo created at ({pos.X:F1}, {pos.Y:F1}) yaw={photo.CapturedYawDeg:F0} → slot {slot}");
	}

	// Подбор (REQ-0016 / F-29): автоматически, когда игрок близко к взведённому предмету
	// с прямой видимостью и есть свободный слот. Берём ближайший подходящий.
	public override void _PhysicsProcess(double delta)
	{
		if (_player == null)
			return;
		UpdateThrowCharge((float)delta);
		UpdatePhotoEnter((float)delta);
		TryPickup();
	}

	// Зарядка броска (REQ-0021 / F-50): пока зажата ЛКМ на активированном мяче — копим время (плато
	// на MaxChargeTime), занижаем скорость игрока и пульсируем модель в руке как обратную связь.
	private void UpdateThrowCharge(float dt)
	{
		if (!_charging)
			return;
		_chargeT = Mathf.Min(_chargeT + dt, MaxChargeTime);
		_player.ExternalSpeedFactor = MoveSpeedFactorWhileCharging;
		_player.SetHandItemCharge(Mathf.Clamp(_chargeT / Mathf.Max(MaxChargeTime, 0.001f), 0.0f, 1.0f));
	}

	// Вход в фотографию (REQ-0017 / F-33): прогресс растёт, пока активирована фотография,
	// зажат «вперёд» и игрок реально продвигается вперёд (не упёрся в стену). По достижении
	// EnterDuration — перенос в запечатлённую точку, фотография расходуется.
	private void UpdatePhotoEnter(float dt)
	{
		if (_activatedItem is not PhotoItem photo)
			return;

		bool forward = Input.IsActionPressed("move_forward");
		var vel = new Vector2(_player.Velocity.X, _player.Velocity.Z);
		Vector2 fwd = _player.PlanarCamForward;
		float advance = fwd.LengthSquared() > 0.0001f ? vel.Dot(fwd.Normalized()) : 0.0f;
		bool advancing = forward && advance > _player.Speed * 0.4f;

		_enterProgress = advancing ? _enterProgress + dt : 0.0f;
		if (_photoEnter != null)
			_photoEnter.Progress = _enterProgress / EnterDuration;

		if (_enterProgress >= EnterDuration)
		{
			_player.TeleportTo(photo.CapturedWorldPos, photo.CapturedYawDeg, photo.CapturedPitchDeg);
			_photoEnter?.Flash();
			ConsumeActivated(null); // одноразовая: слот освобождается (F-33)
			GameLog.Print("[Photo] Entered → teleported");
		}
	}

	// Подбор: ближайший взведённый предмет в радиусе с прямой видимостью, если есть слот.
	private void TryPickup()
	{
		if (_inv.IsFull)
			return;

		WorldItem best = null;
		float bestD = float.MaxValue;
		foreach (WorldItem w in WorldItem.All)
		{
			if (!w.Armed)
				continue;
			float d = PlanarDistance(_player.GlobalPosition, w.GlobalPosition);
			if (d > PickupRange || d >= bestD)
				continue;
			if (!HasLineOfSight(w.GlobalPosition))
				continue;
			best = w;
			bestD = d;
		}

		if (best != null)
			StartPickup(best);
	}

	private static float PlanarDistance(Vector3 a, Vector3 b) =>
		new Vector2(a.X - b.X, a.Z - b.Z).Length();

	// Прямая видимость = нет стены между игроком и предметом. Луч ГОРИЗОНТАЛЬНЫЙ на высоте
	// груди (не опускается к предмету у пола), поэтому детектирует стены, а не пол.
	private bool HasLineOfSight(Vector3 itemPos)
	{
		Vector3 origin = _player.GlobalPosition + Vector3.Up * 0.6f;
		Vector3 target = new Vector3(itemPos.X, origin.Y, itemPos.Z);
		if (origin.DistanceSquaredTo(target) < 0.0004f)
			return true; // стоим прямо на предмете — стены между нет
		var query = PhysicsRayQueryParameters3D.Create(origin, target);
		query.CollisionMask = 1;
		query.Exclude = new Godot.Collections.Array<Rid> { _player.GetRid() };
		return _player.GetWorld3D().DirectSpaceState.IntersectRay(query).Count == 0;
	}

	// Уровень пола в точке XZ (луч вниз) — чтобы предмет лёг на пол, не проваливаясь.
	private float FloorYAt(float x, float z, float refY)
	{
		var from = new Vector3(x, refY + 1.0f, z);
		var to = new Vector3(x, refY - 3.0f, z);
		var query = PhysicsRayQueryParameters3D.Create(from, to);
		query.CollisionMask = 1;
		query.Exclude = new Godot.Collections.Array<Rid> { _player.GetRid() };
		var hit = _player.GetWorld3D().DirectSpaceState.IntersectRay(query);
		return hit.Count > 0 ? ((Vector3)hit["position"]).Y : refY - 0.3f;
	}

	private void StartPickup(WorldItem w)
	{
		Item item = w.Item;
		int slot = _inv.TryAdd(item);
		if (slot < 0) // на всякий случай (инвентарь полон)
			return;

		item.Icon = BuildIcon(item); // иконка по 3D-модели предмета (F-29)
		_incomingSlots.Add(slot);               // прячем содержимое до прилёта звезды

		Vector3 start = w.GlobalPosition + Vector3.Up * 0.2f;
		w.Take(); // убрать из мира и реестра сразу, чтобы не подобрать дважды

		var projectile = new PickupProjectile();
		_player.GetParent().AddChild(projectile);
		projectile.Setup(start, _player, this, slot, PickupArcHeight, PickupFlightDuration);
		GameLog.Print($"[Inventory] Pickup '{item.TypeId}' → slot {slot}");
		QueueRedraw();
	}

	// Звезда долетела до игрока: раскрываем ячейку и подсвечиваем вспышкой (F-30).
	public void OnPickupArrived(int slot)
	{
		_incomingSlots.Remove(slot);
		_flashSlot = slot;
		_flashT = 1.0f;
		QueueRedraw();
	}

	private void Toggle()
	{
		_open = !_open;
		_selectedRow = -1;
		QueueRedraw();
	}

	public override void _Process(double delta)
	{
		if (_flashT > 0.0f)
		{
			_flashT = Mathf.Max(0.0f, _flashT - (float)delta / FlashDuration);
			if (_flashT <= 0.0f)
				_flashSlot = -1;
			QueueRedraw();
		}

		// Держим виджет в правом нижнем углу и подгоняем размер под состояние.
		Vector2 content = _open ? ExpandedSize() : CompactSize();
		Vector2 screen = GetViewport().GetVisibleRect().Size;
		Size = content;
		Position = new Vector2(screen.X - content.X - Margin, screen.Y - content.Y - Margin);
	}

	private Vector2 CompactSize() => new(_reservedSlot >= 0 ? 210 : 150, 54);

	private Vector2 ExpandedSize()
	{
		float w = Padding * 2 + Inventory.Cols * CellSize + (Inventory.Cols - 1) * CellGap;
		float h = TitleBarHeight + Padding * 2 + Inventory.Rows * CellSize + (Inventory.Rows - 1) * CellGap;
		return new Vector2(w, h);
	}

	public override void _Draw()
	{
		if (_open)
			DrawExpanded();
		else
			DrawCompact();
	}

	private void DrawCompact()
	{
		var rect = new Rect2(Vector2.Zero, Size);
		DrawRect(rect, PanelBg);
		DrawRect(rect, BorderColor, false, 2.0f);

		// Простой значок мешка: округлое тело + горловина.
		var bag = new Rect2(12, 16, 26, 28);
		DrawRect(new Rect2(bag.Position + new Vector2(4, -6), new Vector2(18, 10)), FilledBg);
		DrawRect(bag, FilledBg);
		DrawRect(bag, BorderColor, false, 2.0f);

		Font font = GetThemeDefaultFont();
		int fs = 20;
		string text = $"{_inv.Count}/{Inventory.Capacity}";
		DrawString(font, new Vector2(52, 36), text, HorizontalAlignment.Left, -1, fs, TextColor);

		// Индикатор активированного предмета «в руке» (F-16): иконка справа с подсветкой.
		if (_reservedSlot >= 0)
		{
			var iconRect = new Rect2(158, 8, 40, 40);
			DrawRect(iconRect, FilledBg);
			if (_activatedItem?.Icon != null)
				DrawTextureRect(_activatedItem.Icon, iconRect.Grow(-3), false);
			DrawRect(iconRect, HandColor, false, 2.0f);
			DrawString(font, new Vector2(152, 52), "в руке", HorizontalAlignment.Left, -1, 11, HandColor);
		}
	}

	private void DrawExpanded()
	{
		var rect = new Rect2(Vector2.Zero, Size);
		DrawRect(rect, PanelBg);
		DrawRect(rect, BorderColor, false, 2.0f);

		Font font = GetThemeDefaultFont();
		DrawString(font, new Vector2(Padding, 20), $"Рюкзак  {_inv.Count}/{Inventory.Capacity}",
			HorizontalAlignment.Left, -1, 16, TextColor);
		DrawString(font, new Vector2(Padding, 20), "Shift/G — выбросить",
			HorizontalAlignment.Right, Size.X - Padding * 2, 12, HintColor);

		// Подсветка выбранного ряда — рамка вокруг всей полосы ряда.
		if (_selectedRow >= 0)
		{
			float y = CellTop(_selectedRow);
			float bandW = Inventory.Cols * CellSize + (Inventory.Cols - 1) * CellGap;
			DrawRect(new Rect2(Padding - 4, y - 4, bandW + 8, CellSize + 8), RowSelect, false, 3.0f);
		}

		for (int row = 0; row < Inventory.Rows; row++)
		for (int col = 0; col < Inventory.Cols; col++)
			DrawSlot(row, col, font);
	}

	private float CellTop(int row) =>
		TitleBarHeight + Padding + row * (CellSize + CellGap);

	private float CellLeft(int col) =>
		Padding + col * (CellSize + CellGap);

	private void DrawSlot(int row, int col, Font font)
	{
		int slot = row * Inventory.Cols + col;
		var cell = new Rect2(CellLeft(col), CellTop(row), CellSize, CellSize);
		Item item = _inv.Get(slot);

		if (item == null)
		{
			// Пустая ячейка: приглушённый контур без содержимого (F-13).
			DrawRect(cell, EmptyOutline, false, 2.0f);
		}
		else if (_incomingSlots.Contains(slot))
		{
			// Предмет ещё «летит» (pickup, REQ-0016): резервируем ячейку без содержимого.
			DrawRect(cell, FilledBg);
			DrawRect(cell.Grow(-3), CursorColor, false, 2.0f);
		}
		else
		{
			DrawRect(cell, FilledBg);
			DrawRect(cell, BorderColor, false, 2.0f);

			if (item.Icon != null)
			{
				// Минимальный отступ от краёв ячейки — 3% высоты (предмет заполняет ячейку почти целиком).
				var pad = new Vector2(CellSize * 0.03f, CellSize * 0.03f);
				DrawTextureRect(item.Icon, new Rect2(cell.Position + pad, cell.Size - pad * 2), false);
			}

			// Индикатор типа: цветная точка в углу (расходник ↔ ключ/квест, F-13).
			Color dot = item.Category == ItemCategory.Consumable ? ConsumableDot : KeyDot;
			DrawCircle(cell.Position + new Vector2(CellSize - 10, 10), 5.0f, dot);

			// Забронированный слот активированного предмета (F-19a): помечаем «рука».
			if (slot == _reservedSlot)
			{
				DrawRect(cell, new Color(0.0f, 0.0f, 0.0f, 0.28f));
				DrawRect(cell.Grow(-2), HandColor, false, 3.0f);
				DrawString(font, cell.Position + new Vector2(6, 18), "рука",
					HorizontalAlignment.Left, -1, 12, HandColor);
			}
		}

		// Порядковый номер колонки для навигации цифрами.
		DrawString(font, cell.Position + new Vector2(5, CellSize - 6),
			(col + 1).ToString(), HorizontalAlignment.Left, -1, 12, EmptyOutline);

		// Курсор — последняя выбранная ячейка (цель для выброса по G).
		if (slot == _cursorSlot)
			DrawRect(cell.Grow(-1), CursorColor, false, 2.0f);

		// Вспышка применения.
		if (slot == _flashSlot && _flashT > 0.0f)
			DrawRect(cell, new Color(FlashColor, _flashT * 0.6f));
	}
}
