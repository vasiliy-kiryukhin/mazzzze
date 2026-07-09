using Godot;
using System.Collections.Generic;

// Бросок теннисного мячика — физический снаряд (REQ-0021 / F-48).
//
// Не `RigidBody3D`, а **кинематический** снаряд на `MoveAndCollide` — как весь остальной код
// проекта (Player/Monster двигаются вручную). Это даёт: гравитацию в темпе игры (`Gravity` = 15,
// а не движковые 9.8 — иначе мяч «плавает»), отскок **отражением** от нормали удара с потерей
// энергии (естественно), заметаемую коллизию (снаряд не проваливается сквозь тонкий пол/стены),
// и предсказуемую остановку. Попадание в монстра — по близости к `Monster.All` (монстры делят
// слой 1 со стенами, различить по слою нельзя). Один бросок гасит не более одного монстра.
public partial class ThrowProjectile : CharacterBody3D
{
	// Параметры полёта/остановки (REQ-0021 / 06-data). Значения — стартовые, калибруются.
	private const float Gravity = 15.0f;         // в темпе игры (Player.Gravity), не движковые 9.8
	private const float Restitution = 0.5f;      // доля нормальной скорости после отскока
	private const float ImpactFriction = 0.06f;  // потеря касательной скорости ∝ силе удара
	private const float MaxImpactFriction = 0.35f;
	private const float LinearDamp = 0.8f;       // мягкое гашение (докатывание/воздух), 1/с
	private const float GroundFriction = 5.0f;   // трение качения на полу (гасит горизонт), 1/с
	private const float StopThreshold = 0.9f;    // скорость «считается остановившимся», м/с
	private const float StopTime = 0.2f;         // сколько держаться ниже порога → приземление
	private const float MaxAirborneTime = 6.0f;  // предохранитель, с
	private const float HitRadiusPad = 0.4f;     // запас к радиусу мяча для попадания в тело монстра

	private Item _item;
	private float _targetHeight;
	private float _radius;
	private float _hitRadius;
	private HashSet<Monster> _awareAtThrow;

	private Vector3 _vel;
	private float _slowT;
	private float _liveT;
	private bool _landed;

	// spawn — точка выпуска (центр мяча в руке); dir — горизонтальное направление (yaw камеры);
	// force — сила; upwardBias — вертикальная добавка (дуга); aware — монстры, видевшие бросок.
	public void Setup(Vector3 spawn, Vector3 dir, float force, float upwardBias,
		Item item, float targetHeight, HashSet<Monster> aware)
	{
		_item = item;
		_targetHeight = targetHeight;
		_awareAtThrow = aware ?? new HashSet<Monster>();

		GlobalPosition = spawn;

		var model = item.BuildModel();
		AddChild(model);
		Aabb b = WorldItem.ComputeSceneAabb(model);
		float scale = b.Size.Y > 0.0001f ? targetHeight / b.Size.Y : 1.0f;
		model.Scale = Vector3.One * scale;
		model.Position = -(b.Position + b.Size * 0.5f) * scale; // центр модели в центре тела
		_radius = Mathf.Max(targetHeight * 0.5f, 0.05f);
		_hitRadius = _radius + HitRadiusPad;

		AddChild(new CollisionShape3D { Shape = new SphereShape3D { Radius = _radius } });
		CollisionLayer = 0; // снаряд никого не толкает
		CollisionMask = 1;  // упирается/отскакивает от стен, пола, тел

		_vel = dir.Normalized() * force + Vector3.Up * upwardBias;
		GameLog.Print($"[Throw] launch force={force:F1} dir=({dir.X:F2},{dir.Z:F2}) aware={_awareAtThrow.Count}");
	}

	public override void _PhysicsProcess(double delta)
	{
		if (_landed)
			return;
		float dt = (float)delta;
		_liveT += dt;

		// Попадание в монстра (по близости): гасим снаряд у первой цели (одна цель, без пробивания).
		Monster hit = NearestMonsterHit();
		if (hit != null)
		{
			if (_awareAtThrow.Contains(hit))
			{
				hit.Stun(); // ветвь А: видел бросок → стан (F-49)
				GameLog.Print($"[Throw] HIT '{hit.TypeId}' (aware) → stun");
			}
			else
			{
				GameLog.Print($"[Throw] HIT '{hit.TypeId}' (unaware) → no stun");
			}
			Land();
			return;
		}

		_vel.Y -= Gravity * dt;
		_vel *= Mathf.Max(0.0f, 1.0f - LinearDamp * dt); // мягкое гашение

		KinematicCollision3D col = MoveAndCollide(_vel * dt);
		if (col != null)
		{
			Vector3 n = col.GetNormal();
			float vdotn = _vel.Dot(n);
			if (vdotn < 0.0f) // летим в поверхность — отражаем нормальную часть, трём касательную
			{
				Vector3 vn = n * vdotn;
				Vector3 vt = _vel - vn;
				float tFric = Mathf.Clamp(-vdotn * ImpactFriction, 0.0f, MaxImpactFriction);
				_vel = vt * (1.0f - tFric) - vn * Restitution;
			}
			// На полу — трение качения: гасит горизонтальную скорость, чтобы мяч уверенно
			// останавливался, а не бесконечно «полз» на пороге (иначе рывок при передаче в WorldItem).
			if (n.Y > 0.7f)
			{
				float roll = Mathf.Max(0.0f, 1.0f - GroundFriction * dt);
				_vel.X *= roll;
				_vel.Z *= roll;
			}
			// докатываем остаток вдоль поверхности, чтобы не «залипать» в контакте
			Vector3 rem = col.GetRemainder().Slide(n);
			if (rem.LengthSquared() > 1e-6f)
				MoveAndCollide(rem);
		}

		// Остановка: скорость ниже порога заданное время → приземление. Предохранитель по времени.
		if (_vel.Length() < StopThreshold)
		{
			_slowT += dt;
			if (_slowT >= StopTime) { Land(); return; }
		}
		else
		{
			_slowT = 0.0f;
		}
		if (_liveT >= MaxAirborneTime)
			Land();
	}

	// Ближайший монстр, чьё тело пересекается со снарядом (планарно ≤ _hitRadius и по высоте тела).
	private Monster NearestMonsterHit()
	{
		Monster best = null;
		float bestD = float.MaxValue;
		foreach (Monster m in Monster.All)
		{
			if (!IsInstanceValid(m)) continue;
			Vector3 p = m.GlobalPosition;
			float planar = new Vector2(p.X - GlobalPosition.X, p.Z - GlobalPosition.Z).Length();
			if (planar > _hitRadius) continue;
			if (GlobalPosition.Y < p.Y - 0.5f || GlobalPosition.Y > p.Y + m.BodyHeight + 0.5f) continue;
			if (planar < bestD) { best = m; bestD = planar; }
		}
		return best;
	}

	// Снаряд остановился/попал → обычный предмет в мире (как DropProjectile.Land): мяч не расходуется.
	private void Land()
	{
		if (_landed)
			return;
		_landed = true;
		Vector3 pos = GlobalPosition;
		var world = new WorldItem();
		GetParent().AddChild(world);
		// Низ мяча (= центр − радиус) — на текущей высоте опоры, чтобы передача снаряд→предмет была
		// бесшовной по вертикали (без проседания в пол и без рывка), X/Z — ровно точка остановки.
		world.GlobalPosition = new Vector3(pos.X, pos.Y - _radius, pos.Z);
		world.Setup(_item, _targetHeight);
		GameLog.Print($"[Throw] land → WorldItem at ({pos.X:F1}, {pos.Z:F1})");
		QueueFree();
	}
}
