using Godot;
using System;

// Видоискатель старинного фотоаппарата (REQ-0013, US-13 / F-21, F-22, F-23).
//
// Полноэкранный оверлей: затемняет сцену и показывает по центру «окошко» с видом от
// первого лица (отдельная Camera3D на уровне глаз, смотрит горизонтально по yaw —
// см. решение в design.md), рамку «под старину», сепию и крупный таймер 3→2→1.
//
// Конечный автомат съёмки (надстройка над состоянием предмета Activated):
//   Inactive → (ЛКМ) → Blocked (фокус нарушен, ~1с, закрытие)
//                     → Counting (3×TickSeconds) → Fire (создать фото + уничтожить камеру)
// Прерывания: нарушение фокуса во время отсчёта → закрытие без съёмки (камера цела);
// внешняя отмена (drop/deactivate) → Cancel().
public partial class ViewfinderHud : Control
{
	[Export] public float TickSeconds = 0.6667f;    // длительность тика таймера (F-22) — отсчёт ускорен ×3
	[Export] public int StartCount = 3;             // 3 → 2 → 1
	[Export] public float FocusMinDistance = 1.8f;  // мин. фокусное = 3 корпуса × 0.6 (F-23)
	[Export] public float ViewfinderFov = 50.0f;    // суженный FOV объектива (F-21)
	[Export] public float BlockedDuration = 1.0f;   // сколько показывать «съёмка невозможна»
	[Export] public float WindowWidthFraction = 0.15f; // ширина окна как доля экрана (уменьшено ×2)

	private static readonly Color FrameWood  = new(0.12f, 0.08f, 0.04f);
	private static readonly Color FrameBrass = new(0.72f, 0.58f, 0.28f);
	private static readonly Color Sepia      = new(0.45f, 0.32f, 0.16f, 0.16f);
	private static readonly Color DigitColor = new(0.98f, 0.94f, 0.82f, 0.82f);
	private static readonly Color BlockColor = new(0.90f, 0.20f, 0.15f);
	private static readonly Color BlockText  = new(0.98f, 0.90f, 0.85f);

	private enum Phase { Inactive, Blocked, Counting }

	public bool Active => _phase != Phase.Inactive;

	private Phase _phase = Phase.Inactive;
	private Player _player;
	private Action _onFire;
	private float _elapsed;
	private float _blockedT;

	private SubViewport _vp;
	private Camera3D _vfCam;

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Ignore;
		Visible = false;

		_vp = new SubViewport
		{
			Size = new Vector2I(900, 700),
			RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
		};
		AddChild(_vp);
		_vfCam = new Camera3D { Fov = ViewfinderFov };
		_vp.AddChild(_vfCam);
	}

	// Начать использование камеры (ЛКМ по активированному фотоаппарату).
	public void Begin(Player player, Action onFire)
	{
		if (Active)
			return;
		_player = player;
		_onFire = onFire;

		// Видоискатель рендерит тот же мир, что и основная камера.
		_vp.World3D = _player.GetWorld3D();
		_vp.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
		UpdateViewfinderCamera();

		if (FocusDistanceOk())
		{
			_phase = Phase.Counting;
			_elapsed = 0.0f;
			GameLog.Print("[Camera] Viewfinder open, countdown start");
		}
		else
		{
			_phase = Phase.Blocked;
			_blockedT = 0.0f;
			GameLog.Print("[Camera] Focus blocked — too close");
		}
		Visible = true;
		QueueRedraw();
	}

	// Внешняя отмена (drop/deactivate активированной камеры) — без съёмки.
	public void Cancel()
	{
		if (Active)
			Close();
	}

	public override void _PhysicsProcess(double delta)
	{
		if (!Active)
			return;

		UpdateViewfinderCamera();
		float dt = (float)delta;

		if (_phase == Phase.Blocked)
		{
			_blockedT += dt;
			if (_blockedT >= BlockedDuration)
				Close();
			QueueRedraw();
			return;
		}

		// Counting: фокус проверяется каждый кадр (F-23) — при нарушении сброс без съёмки.
		if (!FocusDistanceOk())
		{
			GameLog.Print("[Camera] Focus lost during countdown — reset");
			Close();
			return;
		}

		_elapsed += dt;
		if (_elapsed >= TickSeconds * StartCount)
		{
			GameLog.Print("[Camera] Shutter — photo created");
			Action fire = _onFire;
			Close();
			fire?.Invoke(); // создаёт фото и уничтожает камеру (InventoryHud)
			return;
		}
		QueueRedraw();
	}

	private void Close()
	{
		_phase = Phase.Inactive;
		_vp.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
		Visible = false;
		QueueRedraw();
	}

	private void UpdateViewfinderCamera()
	{
		if (_player == null)
			return;
		Vector3 eye = _player.EyePosition;
		Vector3 f = _player.CameraYawForward;
		Vector3 level = new Vector3(f.X, 0, f.Z);
		if (level.LengthSquared() < 0.0001f)
			level = Vector3.Forward;
		_vfCam.GlobalPosition = eye;
		_vfCam.LookAt(eye + level.Normalized(), Vector3.Up);
	}

	// Фокус в норме, если до ближайшей преграды по взгляду ≥ минимального (F-23).
	private bool FocusDistanceOk()
	{
		Vector3 eye = _player.EyePosition;
		Vector3 f = _player.CameraYawForward;
		Vector3 level = new Vector3(f.X, 0, f.Z).Normalized();
		var query = PhysicsRayQueryParameters3D.Create(eye, eye + level * FocusMinDistance);
		query.CollisionMask = 1;
		query.Exclude = new Godot.Collections.Array<Rid> { _player.GetRid() };
		return _player.GetWorld3D().DirectSpaceState.IntersectRay(query).Count == 0;
	}

	public override void _Process(double delta)
	{
		// Держим оверлей во весь экран.
		Vector2 screen = GetViewport().GetVisibleRect().Size;
		Position = Vector2.Zero;
		Size = screen;
	}

	public override void _Draw()
	{
		if (!Active)
			return;

		Rect2 vf = WindowRect();
		float w = vf.Size.X, h = vf.Size.Y;

		// «Хвостик» к голове игрока — окно висит над головой (вид от третьего лица сохранён).
		Vector2 head = HeadScreen();
		float tailLo = vf.Position.X + 12, tailHi = Mathf.Max(tailLo, vf.End.X - 12);
		Vector2 tail = new Vector2(Mathf.Clamp(head.X, tailLo, tailHi), vf.End.Y);
		DrawColoredPolygon(new[] { tail + new Vector2(-14, 0), tail + new Vector2(14, 0), head }, FrameWood);

		DrawRect(vf.Grow(14), FrameWood);             // деревянная рамка
		DrawTextureRect(_vp.GetTexture(), vf, false); // живой вид от первого лица (объектив)
		DrawRect(vf, Sepia);                          // сепия-тон
		DrawRect(vf, FrameBrass, false, 4.0f);        // латунный кант
		DrawVignette(vf);

		Font font = GetThemeDefaultFont();
		if (_phase == Phase.Counting)
		{
			int tick = (int)(_elapsed / TickSeconds);
			int digit = Mathf.Max(1, StartCount - tick);
			int fs = (int)(h * 0.4f);
			var size = font.GetStringSize(digit.ToString(), HorizontalAlignment.Left, -1, fs);
			var pos = new Vector2(vf.Position.X + (w - size.X) * 0.5f,
				vf.Position.Y + h * 0.5f + size.Y * 0.32f);
			DrawString(font, pos, digit.ToString(), HorizontalAlignment.Left, -1, fs, DigitColor);
		}
		else if (_phase == Phase.Blocked)
		{
			Vector2 c = vf.Position + vf.Size * 0.5f;
			float r = h * 0.18f;
			DrawArc(c, r, 0, Mathf.Tau, 48, BlockColor, 6.0f);
			Vector2 d = new Vector2(1, 1).Normalized() * r;
			DrawLine(c - d, c + d, BlockColor, 6.0f);
			int fs = (int)(h * 0.07f);
			string t = "СЪЁМКА НЕВОЗМОЖНА";
			var size = font.GetStringSize(t, HorizontalAlignment.Left, -1, fs);
			DrawString(font, new Vector2(c.X - size.X * 0.5f, vf.Position.Y + h - 18),
				t, HorizontalAlignment.Left, -1, fs, BlockText);
		}
	}

	// Экранная точка над головой игрока (для якоря окна и «хвостика»).
	private Vector2 HeadScreen()
	{
		if (_player != null && _player.IsInFrontOfCamera(_player.HeadAnchor))
			return _player.UnprojectToScreen(_player.HeadAnchor);
		return new Vector2(Size.X * 0.5f, Size.Y * 0.5f);
	}

	// Прямоугольник окна: над головой игрока, прижат к границам экрана.
	private Rect2 WindowRect()
	{
		float w = Size.X * WindowWidthFraction;
		float h = w * 0.72f;
		Vector2 anchor = HeadScreen();
		float x = Mathf.Clamp(anchor.X - w * 0.5f, 8, Mathf.Max(8, Size.X - w - 8));
		float y = Mathf.Clamp(anchor.Y - h - 28, 8, Mathf.Max(8, Size.Y - h - 8));
		return new Rect2(x, y, w, h);
	}

	// Простая процедурная виньетка: затемняющие полосы-градиенты по краям окна.
	private void DrawVignette(Rect2 vf)
	{
		int bands = 6;
		float maxA = 0.5f;
		float bw = vf.Size.X * 0.08f;
		float bh = vf.Size.Y * 0.08f;
		for (int i = 0; i < bands; i++)
		{
			float a = maxA * (1.0f - (float)i / bands);
			var col = new Color(0.05f, 0.03f, 0.01f, a);
			float ox = bw * (i + 1) / bands;
			float oy = bh * (i + 1) / bands;
			// левый/правый
			DrawRect(new Rect2(vf.Position.X + ox - bw / bands, vf.Position.Y, bw / bands + 0.5f, vf.Size.Y), col);
			DrawRect(new Rect2(vf.Position.X + vf.Size.X - ox, vf.Position.Y, bw / bands + 0.5f, vf.Size.Y), col);
			// верх/низ
			DrawRect(new Rect2(vf.Position.X, vf.Position.Y + oy - bh / bands, vf.Size.X, bh / bands + 0.5f), col);
			DrawRect(new Rect2(vf.Position.X, vf.Position.Y + vf.Size.Y - oy, vf.Size.X, bh / bands + 0.5f), col);
		}
	}
}
