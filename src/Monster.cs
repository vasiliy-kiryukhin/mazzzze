using Godot;
using System.Collections.Generic;

// Базовая сущность монстра — шаблон (REQ-0019, US-19 / F-39..F-43).
//
// Абстрактный `CharacterBody3D`: восприятие (конус зрения + LoS, F-40), конечный автомат
// поведения (Cycle / Threat / Stun / Distract, F-41), доставка угрозы (контакт, F-42),
// единый статический реестр (F-43) по образцу `WorldItem.All`. Конкретный тип (Ifrit,
// REQ-0020) задаёт параметры (F-39), модель и форму цикла.
//
// Движение — поиск пути по клеткам лабиринта (BFS над `MazeData.IsFloor`): монстр надёжно
// обходит углы и в патруле, и в погоне за скрывшимся за угол игроком.
public enum MonsterSize { Large, Small }
public enum ThreatDelivery { Contact, Ranged }

public abstract partial class Monster : CharacterBody3D
{
	// F-43 — реестр всех живых монстров (таргетинг предметами, урон, состояния).
	public static readonly List<Monster> All = new();

	// F-42 — «монстр сообщает о попадании»; будущая система здоровья игрока подключится к сигналу.
	[Signal] public delegate void PlayerHitEventHandler(float damage);

	// F-39 — свойства шаблона (значения по умолчанию задаёт конкретный тип).
	[Export] public string TypeId = "monster";
	[Export] public MonsterSize Size = MonsterSize.Large;
	[Export] public float VisionRange = 18.0f;       // дальность зрения, wu
	[Export] public float VisionFovDeg = 100.0f;     // полный угол конуса зрения
	[Export] public float PatrolSpeed = 2.0f;        // скорость цикла
	[Export] public float ChaseSpeed = 4.0f;         // скорость преследования
	[Export] public ThreatDelivery Delivery = ThreatDelivery.Contact;
	[Export] public float ContactDamage = 10.0f;     // урон за одно касание
	[Export] public float ChaseDropDistance = 57.6f; // порог прекращения преследования (≈1 чанк)
	[Export] public float ContactInterval = 0.7f;    // мин. пауза между дозами урона
	[Export] public float StunDuration = 2.5f;       // длительность стана
	[Export] public float DistractReachRadius = 1.5f;// «достигнут» предмет отвлечения
	[Export] public int SegmentCells = 16;           // размер сегмента патруля (клеток), ≈1 чанк
	[Export] public string ModelPath = "";
	[Export] public bool ScaleByLength = false;      // true — по длине (низкие/длинные), иначе по высоте
	[Export] public float TargetLength = 2.8f;       // целевой горизонтальный габарит (если ScaleByLength)
	[Export] public float TargetHeight = 2.4f;       // целевая высота модели, wu
	[Export] public float Gravity = 15.0f;
	[Export] public float ModelYawOffsetDeg = 180.0f;// forward модели vs -Z у LookAt (калибруется)
	[Export] public float ModelUprightPitchDeg = 0.0f; // коррекция up-оси ассета (0 — не нужна)
	[Export] public string IdleAnim = "";            // зацикленный клип покоя (на месте)
	[Export] public string MoveAnim = "";            // зацикленный клип бега/ходьбы
	[Export] public string AttackAnim = "";          // одноразовый клип атаки (на контакте)
	[Export] public string StunAnim = "";            // одноразовый клип реакции на попадание (стан)
	[Export] public float MoveAnimBaseScale = 1.0f;  // темп клипа локомоции на скорости патруля
	public float EyeHeight = 1.2f;                    // выставляется по фактической высоте модели
	public float BodyHeight = 1.0f;

	protected enum MState { Cycle, Threat, Stun, Distract }
	protected MState State = MState.Cycle;

	private Vector2I _segMin, _segMax; // границы сегмента патруля, клетки [min, max)
	private Node3D _modelPivot;
	private AnimationPlayer _anim;
	protected Player Player;
	private DamageHud _damageHud;

	private float _contactCd;
	private float _stunT;
	private Vector3 _facing = Vector3.Forward;
	private float _repathCd;

	private readonly List<Vector2I> _path = new();
	private int _pathIdx;
	private Vector2I _goalCell = new(int.MinValue, int.MinValue);
	private Vector2I _lastSeenCell;
	private WorldItem _distractTarget;
	private bool _hasApproach;   // прямой подход к точке, когда путь по клеткам исчерпан
	private Vector3 _approach;

	private static readonly Vector2I[] Dirs4 =
		{ new(1, 0), new(-1, 0), new(0, 1), new(0, -1) };

	public override void _EnterTree() => All.Add(this);
	public override void _ExitTree() => All.Remove(this);

	public override void _Ready()
	{
		CollisionLayer = 1; // на слое стен → игрок (маска 1) упирается в монстра (непроходим, F-44)
		CollisionMask = 1;  // сам упирается в стены/пол
		Player = GetNodeOrNull<Player>("/root/Main/Player");
		_damageHud = GetNodeOrNull<DamageHud>("/root/Main/HUD/DamageFlash");
		BuildBody();
		InitSegment();
		_facing = -GlobalTransform.Basis.Z;
		PickPatrolGoal();
		GameLog.Print($"[Monster] Spawn '{TypeId}' cell={CellAt(GlobalPosition)} seg=[{_segMin}..{_segMax})");
	}

	// Внешний стан (F-41): попадание предметом-снарядом (напр. мячом). Триггера-мячика пока нет —
	// метод публичный, вызывается будущим снарядом ([[req-0019]] IDEA-0025).
	public void Stun()
	{
		State = MState.Stun;
		_stunT = StunDuration;
		Velocity = Vector3.Zero;
		PlayOneShot(StunAnim); // реакция на попадание (BeHit)
		GameLog.Print($"[Monster] '{TypeId}' stunned {StunDuration:F1}s");
	}

	// Видит ли монстр игрока прямо сейчас (конус зрения + LoS, F-40). Используется броском мяча
	// (REQ-0021 / F-49), чтобы на момент выпуска зафиксировать «монстр видел бросок» → стан при попадании.
	public bool SeesPlayerNow() => Player != null && CanSee(Player.EyePosition);

	public override void _PhysicsProcess(double delta)
	{
		if (Player == null || MazeData.Instance == null)
			return;
		float dt = (float)delta;
		_contactCd = Mathf.Max(0.0f, _contactCd - dt);
		_repathCd = Mathf.Max(0.0f, _repathCd - dt);

		// Стан имеет приоритет над всем (F-41): стоим на месте, только гравитация.
		if (State == MState.Stun)
		{
			_stunT -= dt;
			ApplyMove(Vector3.Zero, dt);
			if (_stunT <= 0.0f)
				EnterCycle();
			return;
		}

		bool seesPlayer = CanSee(Player.EyePosition);
		float speed = PatrolSpeed;

		// Приоритет: видимость игрока проверяется до отвлечений (F-41).
		switch (State)
		{
			case MState.Cycle:
				if (seesPlayer) { EnterThreat(); goto case MState.Threat; }
				WorldItem lure = FindVisibleLure();
				if (lure != null) { EnterDistract(lure); goto case MState.Distract; }
				if (_pathIdx >= _path.Count) PickPatrolGoal();
				speed = PatrolSpeed;
				break;

			case MState.Distract:
				if (seesPlayer) { EnterThreat(); goto case MState.Threat; }
				if (_distractTarget == null || !IsInstanceValid(_distractTarget)
					|| PlanarDist(_distractTarget.GlobalPosition) <= DistractReachRadius)
				{ EnterCycle(); speed = PatrolSpeed; break; }
				if (_repathCd <= 0.0f) RepathTo(CellAt(_distractTarget.GlobalPosition), true);
				_hasApproach = true; _approach = _distractTarget.GlobalPosition; // финальный подход к предмету
				speed = PatrolSpeed;
				break;

			case MState.Threat:
				float pdist = PlanarDist(Player.GlobalPosition);
				if (seesPlayer)
				{
					_lastSeenCell = CellAt(Player.GlobalPosition);
					if (_repathCd <= 0.0f) RepathTo(_lastSeenCell, false);
				}
				// Прекращение (F-46): игрок не виден И (далеко ИЛИ дошли до последней виденной клетки).
				else if (pdist >= ChaseDropDistance || _pathIdx >= _path.Count)
				{ EnterCycle(); speed = PatrolSpeed; break; }
				speed = ChaseSpeed;
				if (Delivery == ThreatDelivery.Contact) ContactCheck(pdist);
				break;
		}

		FollowPath(speed, dt);
		_hasApproach = false;
		UpdateAnim(new Vector2(Velocity.X, Velocity.Z).Length());
	}

	// Управление анимацией: одноразовые клипы (атака/стан) доигрывают, не перебиваясь; иначе —
	// бег при движении / покой на месте. Темп бега ускоряется с фактической скоростью.
	private void UpdateAnim(float planarSpeed)
	{
		if (_anim == null)
			return;
		if (IsOneShotPlaying())
			return;
		bool moving = planarSpeed > 0.15f;
		string clip = moving ? MoveAnim : IdleAnim;
		if (string.IsNullOrEmpty(clip)) clip = MoveAnim;
		if (!string.IsNullOrEmpty(clip) && _anim.CurrentAnimation != clip)
			_anim.Play(clip);
		_anim.SpeedScale = moving
			? Mathf.Clamp(MoveAnimBaseScale * (0.5f + planarSpeed / Mathf.Max(PatrolSpeed, 0.1f)), 0.4f, 3.0f)
			: 1.0f;
	}

	private bool IsOneShotPlaying() =>
		_anim.IsPlaying() && (_anim.CurrentAnimation == AttackAnim || _anim.CurrentAnimation == StunAnim);

	// Проиграть одноразовый клип (атака/стан), если задан.
	private void PlayOneShot(string clip)
	{
		if (_anim == null || string.IsNullOrEmpty(clip) || _anim.CurrentAnimation == clip)
			return;
		_anim.SpeedScale = 1.0f;
		_anim.Play(clip);
	}

	private void PlayAttack() => PlayOneShot(AttackAnim);

	// Ставит клипу зацикливание (клипы .glb обычно импортируются как one-shot).
	private void SetLoop(string clip)
	{
		if (_anim == null || string.IsNullOrEmpty(clip) || !_anim.HasAnimation(clip))
			return;
		_anim.GetAnimation(clip).LoopMode = Animation.LoopModeEnum.Linear;
	}

	private void EnterCycle()
	{
		State = MState.Cycle;
		_distractTarget = null;
		RepathToSegment();
	}

	private void EnterThreat()
	{
		State = MState.Threat;
		_distractTarget = null;
		_lastSeenCell = CellAt(Player.GlobalPosition);
		RepathTo(_lastSeenCell, false);
		GameLog.Print($"[Monster] '{TypeId}' → Threat (chase)");
	}

	private void EnterDistract(WorldItem item)
	{
		State = MState.Distract;
		_distractTarget = item;
		RepathTo(CellAt(item.GlobalPosition), true);
		GameLog.Print($"[Monster] '{TypeId}' → Distract (item '{item.Item?.TypeId}')");
	}

	// Контактный урон (F-42/F-44): касание тела игрока снимает дозу не чаще ContactInterval.
	private void ContactCheck(float pdist)
	{
		float touch = 0.5f + 0.35f; // радиус капсулы монстра + радиус игрока + небольшой запас
		if (pdist > touch || _contactCd > 0.0f)
			return;
		_contactCd = ContactInterval;
		_damageHud?.Flash();
		PlayAttack(); // клип укуса/атаки
		EmitSignal(SignalName.PlayerHit, ContactDamage); // будущая система здоровья
		GameLog.Print($"[Monster] '{TypeId}' CONTACT → player -{ContactDamage:F0} hp");
	}

	// --- Восприятие (F-40) -------------------------------------------------

	protected Vector3 EyePos => GlobalPosition + Vector3.Up * EyeHeight;

	// В конусе зрения (угол + дальность) и на прямой видимости (нет стены по лучу).
	protected bool CanSee(Vector3 target)
	{
		Vector3 to = target - EyePos; to.Y = 0.0f;
		float dist = to.Length();
		if (dist < 0.001f) return true;
		if (dist > VisionRange) return false;
		Vector3 dir = to / dist;
		Vector3 look = new Vector3(_facing.X, 0, _facing.Z).Normalized();
		if (look.Dot(dir) < Mathf.Cos(Mathf.DegToRad(VisionFovDeg * 0.5f)))
			return false;
		var q = PhysicsRayQueryParameters3D.Create(EyePos, new Vector3(target.X, EyePos.Y, target.Z));
		q.CollisionMask = 1;
		q.Exclude = new Godot.Collections.Array<Rid> { GetRid(), Player.GetRid() };
		return GetWorld3D().DirectSpaceState.IntersectRay(q).Count == 0;
	}

	// Ближайший привлекающий предмет в конусе зрения внутри сегмента (для отвлечения, F-41).
	private WorldItem FindVisibleLure()
	{
		WorldItem best = null;
		float bestD = float.MaxValue;
		foreach (WorldItem w in WorldItem.All)
		{
			if (!InSegment(CellAt(w.GlobalPosition))) continue;
			float d = PlanarDist(w.GlobalPosition);
			if (d <= DistractReachRadius) continue; // уже достигнут — не отвлекаемся повторно
			if (d >= bestD || !CanSee(w.GlobalPosition)) continue;
			best = w; bestD = d;
		}
		return best;
	}

	// --- Движение и путь (BFS по клеткам) ----------------------------------

	private void FollowPath(float speed, float dt)
	{
		Vector3 horiz = Vector3.Zero;
		Vector3? target = null;
		if (_pathIdx < _path.Count)
		{
			Vector3 wp = CellCenter(_path[_pathIdx]);
			if (new Vector2(wp.X - GlobalPosition.X, wp.Z - GlobalPosition.Z).Length() < 0.3f) _pathIdx++;
			else target = wp;
		}
		// Путь по клеткам исчерпан — финальный прямой подход к точке (предмет вне центра клетки).
		if (target == null && _hasApproach
			&& new Vector2(_approach.X - GlobalPosition.X, _approach.Z - GlobalPosition.Z).Length() > 0.4f)
			target = _approach;

		if (target != null)
		{
			Vector3 to = new Vector3(target.Value.X - GlobalPosition.X, 0, target.Value.Z - GlobalPosition.Z);
			if (to.LengthSquared() > 0.0001f)
			{
				Vector3 dir = to.Normalized();
				horiz = dir * speed;
				_facing = dir;
				FaceMovement();
			}
		}
		ApplyMove(horiz, dt);
	}

	private void ApplyMove(Vector3 horiz, float dt)
	{
		Velocity = new Vector3(horiz.X, Velocity.Y - Gravity * dt, horiz.Z);
		MoveAndSlide();
	}

	private void FaceMovement()
	{
		Vector3 f = new Vector3(_facing.X, 0, _facing.Z);
		if (f.LengthSquared() < 0.0001f) return;
		_modelPivot.LookAt(_modelPivot.GlobalPosition + f, Vector3.Up);
		_modelPivot.RotateY(Mathf.DegToRad(ModelYawOffsetDeg));
	}

	private void RepathTo(Vector2I goal, bool restrictSegment)
	{
		_repathCd = 0.25f;
		if (goal == _goalCell && _pathIdx < _path.Count) return;
		List<Vector2I> path = FindPath(CellAt(GlobalPosition), goal, restrictSegment, 1200);
		if (path.Count >= 1)
		{
			_path.Clear(); _path.AddRange(path);
			_pathIdx = _path.Count > 1 ? 1 : 0;
			_goalCell = goal;
		}
	}

	private void RepathToSegment()
	{
		Vector2I c = CellAt(GlobalPosition);
		if (InSegment(c)) { PickPatrolGoal(); return; }
		Vector2I center = new((_segMin.X + _segMax.X) / 2, (_segMin.Y + _segMax.Y) / 2);
		RepathTo(NearestFloor(center), false);
	}

	// Выбор новой точки патруля: случайная достижимая клетка пола внутри сегмента (F-45).
	private void PickPatrolGoal()
	{
		for (int tries = 0; tries < 24; tries++)
		{
			var cell = new Vector2I(
				GD.RandRange(_segMin.X, _segMax.X - 1),
				GD.RandRange(_segMin.Y, _segMax.Y - 1));
			if (!MazeData.IsFloor(cell.X, cell.Y)) continue;
			List<Vector2I> path = FindPath(CellAt(GlobalPosition), cell, true, 600);
			if (path.Count > 1)
			{
				_path.Clear(); _path.AddRange(path);
				_pathIdx = 1; _goalCell = cell;
				return;
			}
		}
	}

	// BFS по клеткам пола. restrictSegment ограничивает поиск сегментом патруля.
	private List<Vector2I> FindPath(Vector2I start, Vector2I goal, bool restrictSegment, int maxNodes)
	{
		var result = new List<Vector2I>();
		if (start == goal) { result.Add(start); return result; }
		var came = new Dictionary<Vector2I, Vector2I> { [start] = start };
		var queue = new Queue<Vector2I>();
		queue.Enqueue(start);
		int expanded = 0;
		bool found = false;
		while (queue.Count > 0 && expanded < maxNodes)
		{
			Vector2I c = queue.Dequeue();
			expanded++;
			foreach (Vector2I d in Dirs4)
			{
				Vector2I n = c + d;
				if (came.ContainsKey(n)) continue;
				if (!MazeData.IsFloor(n.X, n.Y)) continue;
				if (restrictSegment && !InSegment(n)) continue;
				came[n] = c;
				if (n == goal) { found = true; queue.Clear(); break; }
				queue.Enqueue(n);
			}
			if (found) break;
		}
		if (!found) return result;
		for (Vector2I c = goal; c != start; c = came[c]) result.Add(c);
		result.Add(start);
		result.Reverse();
		return result;
	}

	private Vector2I NearestFloor(Vector2I c)
	{
		if (MazeData.IsFloor(c.X, c.Y)) return c;
		for (int r = 1; r < 8; r++)
			for (int dx = -r; dx <= r; dx++)
				for (int dz = -r; dz <= r; dz++)
				{
					var n = new Vector2I(c.X + dx, c.Y + dz);
					if (MazeData.IsFloor(n.X, n.Y)) return n;
				}
		return c;
	}

	// --- Сегмент, клетки, тело --------------------------------------------

	private void InitSegment()
	{
		Vector2I c = CellAt(GlobalPosition);
		int cx = Mathf.FloorToInt((float)c.X / SegmentCells) * SegmentCells;
		int cz = Mathf.FloorToInt((float)c.Y / SegmentCells) * SegmentCells;
		_segMin = new Vector2I(cx, cz);
		_segMax = new Vector2I(cx + SegmentCells, cz + SegmentCells);
	}

	private bool InSegment(Vector2I c) =>
		c.X >= _segMin.X && c.X < _segMax.X && c.Y >= _segMin.Y && c.Y < _segMax.Y;

	protected static Vector2I CellAt(Vector3 p)
	{
		float cs = MazeData.CellWorldSize;
		var m = MazeData.Instance;
		return new Vector2I(
			Mathf.FloorToInt((p.X - m.WorldOffsetX) / cs),
			Mathf.FloorToInt((p.Z - m.WorldOffsetZ) / cs));
	}

	private Vector3 CellCenter(Vector2I c)
	{
		float cs = MazeData.CellWorldSize;
		var m = MazeData.Instance;
		return new Vector3((c.X + 0.5f) * cs + m.WorldOffsetX, GlobalPosition.Y, (c.Y + 0.5f) * cs + m.WorldOffsetZ);
	}

	private float PlanarDist(Vector3 p) =>
		new Vector2(p.X - GlobalPosition.X, p.Z - GlobalPosition.Z).Length();

	// Строит модель (glb типа, масштаб к TargetHeight, низ на y=0) + капсульную коллизию.
	private void BuildBody()
	{
		_modelPivot = new Node3D { Name = "ModelPivot" };
		AddChild(_modelPivot);

		Node3D model = null;
		var packed = GD.Load<PackedScene>(ModelPath);
		if (packed != null) model = packed.Instantiate<Node3D>();
		model ??= new Node3D();
		_modelPivot.AddChild(model);
		_anim = model.FindChildren("*", "AnimationPlayer", true, false) is { Count: > 0 } aps
			? (AnimationPlayer)aps[0] : null;
		SetLoop(IdleAnim); // покой/локомоция должны зацикливаться (клипы импортированы как one-shot)
		SetLoop(MoveAnim);

		// AABB считаем в ЛОКАЛЬНОЙ системе модели (малые числа): монстр стоит на мировых ~−18000,
		// поэтому глобальные трансформы в float32 теряют точность — берём только локальные цепочки.
		Aabb b = ModelLocalAabb(model);
		if (Mathf.Abs(ModelUprightPitchDeg) > 0.01f)
		{
			model.RotationDegrees = new Vector3(ModelUprightPitchDeg, 0, 0);
			b = RotateAabb(b, model.Basis);
		}
		// Гуманоид — масштаб по высоте (TargetHeight). Низкая/длинная модель (ScaleByLength) —
		// по горизонтальному габариту, иначе масштаб по высоте раздул бы её (см. design.md).
		float span = Mathf.Max(b.Size.X, b.Size.Z);
		float scale = ScaleByLength
			? (span > 0.0001f ? TargetLength / span : 1.0f)
			: (b.Size.Y > 0.0001f ? TargetHeight / b.Size.Y : 1.0f);
		model.Scale = Vector3.One * scale;
		model.Position = new Vector3(
			-(b.Position.X + b.Size.X * 0.5f) * scale,
			-b.Position.Y * scale,
			-(b.Position.Z + b.Size.Z * 0.5f) * scale);

		BodyHeight = b.Size.Y * scale;
		EyeHeight = BodyHeight * 0.7f;

		var shape = new CollisionShape3D();
		float h = Mathf.Max(BodyHeight, 1.0f);
		shape.Shape = new CapsuleShape3D { Radius = 0.5f, Height = h };
		shape.Position = new Vector3(0, h * 0.5f, 0);
		AddChild(shape);
	}

	// AABB всех визуалов модели в ЛОКАЛЬНОЙ системе `model` (через цепочку локальных трансформов —
	// без глобальных координат, чтобы не терять float32-точность на мировых ~−18000).
	private static Aabb ModelLocalAabb(Node3D model)
	{
		Aabb bounds = new();
		bool has = false;
		foreach (Node n in model.FindChildren("*", "VisualInstance3D", true, false))
		{
			var vi = (VisualInstance3D)n;
			Aabb la = vi.GetAabb();
			Transform3D t = RelativeTransform(model, vi);
			for (int i = 0; i < 8; i++)
			{
				Vector3 corner = t * (la.Position + la.Size * new Vector3(i & 1, (i >> 1) & 1, (i >> 2) & 1));
				bounds = has ? bounds.Expand(corner) : new Aabb(corner, Vector3.Zero);
				has = true;
			}
		}
		return has ? bounds : new Aabb(new Vector3(-0.5f, 0, -0.5f), Vector3.One);
	}

	// Трансформ узла в системе координат предка (произведение локальных Transform по цепочке).
	private static Transform3D RelativeTransform(Node ancestor, Node3D node)
	{
		Transform3D t = Transform3D.Identity;
		for (Node cur = node; cur != ancestor && cur is Node3D n3; cur = cur.GetParent())
			t = n3.Transform * t;
		return t;
	}

	// Осевой AABB после поворота базисом (переносим 8 углов).
	private static Aabb RotateAabb(Aabb a, Basis basis)
	{
		Aabb r = new();
		for (int i = 0; i < 8; i++)
		{
			Vector3 corner = basis * (a.Position + a.Size * new Vector3(i & 1, (i >> 1) & 1, (i >> 2) & 1));
			r = i == 0 ? new Aabb(corner, Vector3.Zero) : r.Expand(corner);
		}
		return r;
	}
}
