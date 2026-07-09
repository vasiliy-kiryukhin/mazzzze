using Godot;

// Визуальное сопровождение фотографии (REQ-0017, US-17 / F-33, F-34).
//
// Пока фотография активирована, по центру экрана висит **простое окно** с живым видом
// запечатлённой точки: отдельная Camera3D стоит в CapturedWorldPos и смотрит вдоль
// запечатлённого yaw, рендеря тот же мир (если в этот момент там проходит монстр — он
// виден в окне). Вид **полноцветный** (без монохрома/сепии). Окно оформлено простой рамкой
// (дерево + латунный кант). По мере входа (Progress 0→1) окно растёт из центра.
//
// «Оживание» фото (F-34): при входе фотография не просто увеличивается — она **оживает** за
// счёт освещения. При Progress 0 вид тускло подсвечен (тёплая ambient-заливка минимальна) —
// «спящий» снимок; по мере входа энергия заливки нарастает к максимуму, и сцена «загорается»,
// будто игрок уже стоит в запечатлённой точке. Это же убирает резкий скачок света при переносе.
// Заливка — собственный Environment камеры превью (свет не «утекает» в общий мир, т.к. SubViewport
// делит World3D с игрой). В момент переноса — лёгкое жёлтое мерцание экрана.
public partial class PhotoEnterHud : Control
{
	[Export] public float FlashDuration = 0.6f;
	[Export] public float PreviewFov = 50.0f;
	[Export] public float WindowWidthMin = 0.22f;  // доля экрана в начале входа
	[Export] public float WindowWidthMax = 0.85f;  // доля экрана перед переносом
	[Export] public int FlashFlickers = 3;         // число всплесков мерцания за переход (F-34)
	[Export] public float FlashMaxAlpha = 0.35f;   // предел прозрачности жёлтой вспышки — «лёгкий» свет
	[Export] public float AmbientEnergyMin = 0.2f; // заливка «спящего» снимка (Progress 0)
	[Export] public float AmbientEnergyMax = 1.6f; // заливка «ожившей» сцены (Progress 1)

	private static readonly Color FrameWood  = new(0.12f, 0.08f, 0.04f);
	private static readonly Color FrameBrass = new(0.72f, 0.58f, 0.28f);
	private static readonly Color FlashColor = new(1.0f, 0.95f, 0.55f);  // тёплое жёлтое мерцание переноса
	// Тёплая заливка окна под HeadLight игрока (player.tscn: OmniLight3D, цвет 1,0.96,0.86).
	private static readonly Color FillLight  = new(1.0f, 0.96f, 0.86f);

	public float Progress { get; set; } // 0..1, ставится InventoryHud каждый физ.кадр
	private float _flashT;               // 1 → 0

	private bool _active;                // окно «сквозь фото» показывается

	private SubViewport _vp;             // живой (полноцветный) вид запечатлённой точки
	private Camera3D _cam;

	public override void _Ready()
	{
		MouseFilter = MouseFilterEnum.Ignore;

		// Живой полноцветный вид объектива.
		_vp = new SubViewport
		{
			Size = new Vector2I(720, 540),
			RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled,
		};
		AddChild(_vp);
		_cam = new Camera3D { Fov = PreviewFov };
		// Собственный Environment камеры превью: только тёплая ambient-заливка, энергия которой
		// нарастает с входом (см. _Process) — фото «оживает». Действует лишь на эту камеру, свет
		// не попадает в общий мир (SubViewport делит World3D с игрой ради живых монстров).
		_cam.Environment = new Godot.Environment
		{
			AmbientLightSource = Godot.Environment.AmbientSource.Color,
			AmbientLightColor = FillLight,
			AmbientLightEnergy = AmbientEnergyMin,
		};
		_vp.AddChild(_cam);
	}

	// Открыть живое окно вида запечатлённой точки (при активации фотографии).
	public void BeginPreview(Player player, PhotoItem photo)
	{
		_vp.World3D = player.GetWorld3D();
		_cam.Position = new Vector3(photo.CapturedWorldPos.X, 1.5f, photo.CapturedWorldPos.Y);
		_cam.Rotation = new Vector3(0, Mathf.DegToRad(photo.CapturedYawDeg), 0);
		_vp.RenderTargetUpdateMode = SubViewport.UpdateMode.Always;
		_active = true;
	}

	// Закрыть окно (деактивация / выброс / расход фотографии).
	public void EndPreview()
	{
		_active = false;
		_vp.RenderTargetUpdateMode = SubViewport.UpdateMode.Disabled;
	}

	// Вызвать в момент срабатывания переноса.
	public void Flash() => _flashT = 1.0f;

	public override void _Process(double delta)
	{
		Position = Vector2.Zero;
		Size = GetViewport().GetVisibleRect().Size;

		// «Оживание»: с ростом входа тёплая заливка окна нарастает — сцена «загорается».
		if (_active)
			_cam.Environment.AmbientLightEnergy =
				Mathf.Lerp(AmbientEnergyMin, AmbientEnergyMax, Mathf.Clamp(Progress, 0.0f, 1.0f));

		if (_flashT > 0.0f)
			_flashT = Mathf.Max(0.0f, _flashT - (float)delta / FlashDuration);

		QueueRedraw();
	}

	public override void _Draw()
	{
		if (_active)
			DrawPreviewWindow(Mathf.Clamp(Progress, 0.0f, 1.0f));

		// Лёгкое жёлтое мерцание при переносе: несколько всплесков, затухающих к концу (F-34).
		if (_flashT > 0.0f)
		{
			float flicker = 0.5f + 0.5f * Mathf.Sin(_flashT * Mathf.Tau * FlashFlickers);
			float a = FlashMaxAlpha * _flashT * flicker;
			DrawRect(new Rect2(Vector2.Zero, Size), new Color(FlashColor, a));
		}
	}

	// Растущее простое окно по центру экрана: увеличивается по мере входа (p: мелко → крупно).
	private void DrawPreviewWindow(float p)
	{
		float frac = Mathf.Lerp(WindowWidthMin, WindowWidthMax, p);
		float w = Size.X * frac;
		float h = w * 0.75f;

		Vector2 tl = new Vector2(Size.X * 0.5f, Size.Y * 0.5f) - new Vector2(w, h) * 0.5f;
		var vf = new Rect2(tl, new Vector2(w, h));

		DrawRect(vf.Grow(14), FrameWood);             // деревянная рамка
		DrawTextureRect(_vp.GetTexture(), vf, false); // живой полноцветный вид
		DrawRect(vf, FrameBrass, false, 4.0f);        // латунный кант
	}
}
