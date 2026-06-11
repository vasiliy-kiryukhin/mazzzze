using Godot;
using System;

public partial class Player : CharacterBody3D
{
	[Export] public float Speed = 5.0f;
	[Export] public float MouseSensitivity = 0.002f;
	[Export] public float ZoomStep = 1.0f;
	[Export] public float MinZoom = 6.0f;
	[Export] public float MaxZoom = 14.0f;
	[Export] public float Gravity = 15.0f;

	// Камера высоко над игроком и чуть сзади, направлена вниз под углом.
	// Угол крутой (ближе к виду сверху): так камера остаётся в открытом «колодце»
	// над клеткой игрока и не утыкается в боковые стены узкого коридора.
	[Export] public float DefaultPitchDeg = -60.0f;  // наклон вниз по умолчанию
	[Export] public float MinPitchDeg = -85.0f;      // почти строго вниз
	[Export] public float MaxPitchDeg = -25.0f;      // более пологий угол: видно высоту стен и небо
	[Export] public float CameraMargin = 0.4f;       // отступ камеры от стены при столкновении

	private Camera3D _camera;
	private Node3D _cameraYaw;
	private Node3D _cameraPitch;
	private ChunkManager _chunkManager;
	private Node3D _modelPivot;
	private float _zoomLevel;

	public override void _Ready()
	{
		Input.MouseMode = Input.MouseModeEnum.Captured;

		_camera = GetNode<Camera3D>("CameraYaw/CameraPitch/Camera3D");
		_cameraYaw = GetNode<Node3D>("CameraYaw");
		_cameraPitch = GetNode<Node3D>("CameraYaw/CameraPitch");
		_modelPivot = GetNode<Node3D>("ModelPivot");
		_chunkManager = GetNode<ChunkManager>("/root/Main/ChunkManager");

		_zoomLevel = _camera.Position.Z;

		// Камера по умолчанию смотрит вниз на игрока сверху-сзади
		_cameraPitch.Rotation = new Vector3(Mathf.DegToRad(DefaultPitchDeg), 0, 0);

		if (MazeData.Instance != null)
		{
			var m = MazeData.Instance;
			var start = m.PlayerStartCell;
			float cs = MazeData.CellWorldSize;
			// X/Z: центр клетки-коридора. GridMap центрирует ячейки по X/Z (cell_center_x/z = true),
			// поэтому формула с +cs/2 совпадает с центром плитки пола.
			// Y: пол сверху на Y≈0.1, низ коллизии-сферы на Position.Y+0.05 → ставим чуть выше пола,
			// чтобы игрок мягко встал НА пол (не над и не под ним).
			Position = new Vector3(
				start.X * cs + m.WorldOffsetX + cs / 2,
				0.3f,
				start.Y * cs + m.WorldOffsetZ + cs / 2
			);
			GD.Print($"[Player] Start cell=({start.X},{start.Y}) world=({Position.X:F1}, {Position.Y:F1}, {Position.Z:F1})");
		}
	}

	public override void _Input(InputEvent @event)
	{
		if (@event is InputEventMouseMotion mouseMotion && Input.MouseMode == Input.MouseModeEnum.Captured)
		{
			_cameraYaw.RotateY(-mouseMotion.Relative.X * MouseSensitivity);
			_cameraPitch.RotateX(-mouseMotion.Relative.Y * MouseSensitivity);
			// Камера всегда смотрит вниз под углом; верхний предел (MaxPitchDeg) держит её
			// ниже верхнего края стен — заглянуть поверх лабиринта нельзя.
			_cameraPitch.Rotation = new Vector3(
				Mathf.Clamp(_cameraPitch.Rotation.X, Mathf.DegToRad(MinPitchDeg), Mathf.DegToRad(MaxPitchDeg)),
				0, 0
			);
		}

		if (@event is InputEventMouseButton mb)
		{
			if (mb.ButtonIndex == MouseButton.WheelUp)
			{
				_zoomLevel = Mathf.Max(MinZoom, _zoomLevel - ZoomStep);
				_camera.Position = new Vector3(_camera.Position.X, _camera.Position.Y, _zoomLevel);
			}
			if (mb.ButtonIndex == MouseButton.WheelDown)
			{
				_zoomLevel = Mathf.Min(MaxZoom, _zoomLevel + ZoomStep);
				_camera.Position = new Vector3(_camera.Position.X, _camera.Position.Y, _zoomLevel);
			}
		}
	}

	public override void _PhysicsProcess(double delta)
	{
		float dt = (float)delta;

		Vector3 vel = Velocity;
		if (!IsOnFloor())
			vel.Y -= Gravity * dt;

		Vector2 inputDir = Input.GetVector("move_left", "move_right", "move_forward", "move_back");
		Vector3 camForward = -_cameraYaw.GlobalBasis.Z;
		Vector3 camRight = _cameraYaw.GlobalBasis.X;
		Vector3 moveDir = (camForward * -inputDir.Y + camRight * inputDir.X);
		moveDir.Y = 0;
		moveDir = moveDir.Normalized();

		if (moveDir != Vector3.Zero)
		{
			vel.X = moveDir.X * Speed;
			vel.Z = moveDir.Z * Speed;
			_modelPivot.Basis = Basis.LookingAt(moveDir, Vector3.Up);
		}
		else
		{
			vel.X = 0;
			vel.Z = 0;
		}

		Velocity = vel;
		MoveAndSlide();

		_chunkManager?.UpdateChunks(new Vector2(GlobalPosition.X, GlobalPosition.Z));

		UpdateCameraCollision(dt);
	}

	// «Пружинная рука»: камера не должна проникать в стены узкого коридора.
	// Пускаем луч от точки опоры (над игроком) к желаемой позиции камеры; если он
	// упирается в стену — придвигаем камеру к точке удара, иначе плавно отдаляем.
	private void UpdateCameraCollision(float dt)
	{
		Vector3 pivot = _cameraPitch.GlobalPosition;
		Vector3 dir = _cameraPitch.GlobalBasis.Z.Normalized();   // +Z в кадре наклона — куда смещена камера
		Vector3 desired = pivot + dir * _zoomLevel;

		var query = PhysicsRayQueryParameters3D.Create(pivot, desired);
		query.CollisionMask = 1;
		query.Exclude = new Godot.Collections.Array<Rid> { GetRid() };
		var hit = GetWorld3D().DirectSpaceState.IntersectRay(query);

		float target = _zoomLevel;
		if (hit.Count > 0)
			target = Mathf.Clamp(pivot.DistanceTo((Vector3)hit["position"]) - CameraMargin, 0.6f, _zoomLevel);

		float current = _camera.Position.Z;
		// Придвигаемся мгновенно (чтобы не показать стену), отдаляемся плавно.
		float z = target < current ? target : Mathf.MoveToward(current, target, 12.0f * dt);
		_camera.Position = new Vector3(_camera.Position.X, _camera.Position.Y, z);
	}
}
