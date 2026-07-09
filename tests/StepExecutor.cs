#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using GdUnit4;
using Godot;

namespace MazeTests;

internal readonly struct LogLine
{
    public readonly int Frame;
    public readonly string Text;
    public LogLine(int frame, string text) { Frame = frame; Text = text; }
}

internal sealed class StepExecutor
{
    private const uint FrameMs = 16;

    private readonly ISceneRunner _runner;
    private readonly LogCapture _capture;

    public readonly List<LogLine> Log = new();
    public readonly List<string> Screenshots = new();
    public readonly Dictionary<string, object?> Captures = new();
    public readonly List<int> MainStepEndFrames = new();

    public int Frame;
    public int SetupEndFrame;

    private Node? _playerCache;
    private VideoRecorder? _recorder;

    public void SetRecorder(VideoRecorder recorder) => _recorder = recorder;

    public StepExecutor(ISceneRunner runner, LogCapture capture)
    {
        _runner = runner;
        _capture = capture;
    }

    public Node? Player
    {
        get
        {
            if (_playerCache is { } p && GodotObject.IsInstanceValid(p)) return p;
            _playerCache = _runner.Scene().GetNodeOrNull("/root/Main/Player");
            return _playerCache;
        }
    }

    public bool NodeExists(string path) => _runner.Scene().GetNodeOrNull(path) != null;

    public async Task RunSetup(JsonArray? setup)
    {
        if (setup == null) return;
        int i = 0;
        foreach (var n in setup)
        {
            if (n is not null) await RunStepMarked(n, $"setup {i}");
            i++;
        }
        DrainLog();
        SetupEndFrame = Frame;
    }

    public async Task RunStepMarked(JsonNode step, string label)
    {
        int f0 = Frame;
        await RunStep(step);
        DrainLog();
        _recorder?.AddSpan(label, step.Str("type"), step.Str("reason"), f0, Frame);
    }

    public void DrainLog()
    {
        foreach (var raw in _capture.Drain())
        {
            var line = raw.TrimEnd('\r');
            if (!string.IsNullOrEmpty(line))
                Log.Add(new LogLine(Frame, line));
        }
    }

    private async Task Simulate(uint frames)
    {
        if (frames == 0) return;
        if (_recorder != null)
        {
            var vp = _runner.Scene().GetViewport();
            int stride = _recorder.Stride;
            uint remaining = frames;
            while (remaining > 0)
            {
                uint chunk = (uint)Math.Min(stride, (int)remaining);
                await _runner.SimulateFrames(chunk, FrameMs);
                Frame += (int)chunk;
                _recorder.CaptureFrame(vp);
                remaining -= chunk;
            }
        }
        else
        {
            await _runner.SimulateFrames(frames, FrameMs);
            Frame += (int)frames;
        }
    }

    public async Task RunStep(JsonNode step)
    {
        switch (step.Type())
        {
            case "wait_frames":
                {
                    int f = step.Int("frames", 1);
                    await Simulate((uint)f);
                    DrainLog();
                    break;
                }
            case "wait_msec":
                await _runner.AwaitMillis((uint)step.Int("ms", 0));
                DrainLog();
                break;
            case "action":
                await DoAction(step);
                break;
            case "key":
                DoKey(step);
                break;
            case "mouse_motion":
                {
                    int dx = step.Int("dx", 0);
                    int dy = step.Int("dy", 0);
                    _runner.SimulateMouseMoveRelative(new Vector2(dx, dy), 0.0, Tween.TransitionType.Linear);
                    await Simulate(1);
                    DrainLog();
                    break;
                }
            case "mouse_button":
                await DoMouseButton(step);
                break;
            case "aim_camera":
                await DoAimCamera(step);
                break;
            case "screenshot":
                {
                    string path = step.Str("path");
                    ScreenshotUtil.Capture(_runner.Scene().GetViewport(), path);
                    Screenshots.Add(path);
                    DrainLog();
                    break;
                }
            case "node_property":
                {
                    string path = step.Str("path");
                    string prop = step.Str("property");
                    string cap = step.Str("capture_as");
                    var node = _runner.Scene().GetNodeOrNull(path);
                    if (node != null && !string.IsNullOrEmpty(cap))
                        Captures[cap] = Reflection.Read(node, prop);
                    DrainLog();
                    break;
                }
        }
    }

    private async Task DoAction(JsonNode step)
    {
        string name = step.Str("name");
        string mode = step.Str("mode", "tap");
        switch (mode)
        {
            case "press":
                _runner.SimulateActionPress(name);
                await Simulate(1);
                DrainLog();
                break;
            case "release":
                _runner.SimulateActionRelease(name);
                await Simulate(1);
                DrainLog();
                break;
            case "tap":
                _runner.SimulateActionPressed(name);
                await Simulate(1);
                DrainLog();
                break;
            case "hold":
                _runner.SimulateActionPress(name);
                await Simulate(1);
                DrainLog();
                int hold = step.Int("frames", 0);
                if (hold > 0)
                {
                    await Simulate((uint)hold);
                    DrainLog();
                }
                _runner.SimulateActionRelease(name);
                await Simulate(1);
                DrainLog();
                break;
        }
    }

    private async void DoKey(JsonNode step)
    {
        var key = Enum.Parse<Key>(step.Str("physical_keycode"), true);
        bool shift = HasModifier(step, "shift");
        bool ctrl = HasModifier(step, "ctrl");
        string mode = step.Str("mode", "tap");
        switch (mode)
        {
            case "press":
                _runner.SimulateKeyPress(key, shift, ctrl);
                await Simulate(1);
                DrainLog();
                break;
            case "release":
                _runner.SimulateKeyRelease(key, shift, ctrl);
                await Simulate(1);
                DrainLog();
                break;
            default:
                _runner.SimulateKeyPressed(key, shift, ctrl);
                await Simulate(1);
                DrainLog();
                break;
        }
    }

    private async Task DoMouseButton(JsonNode step)
    {
        var btn = step.Str("button", "left") switch
        {
            "right" => MouseButton.Right,
            "middle" => MouseButton.Middle,
            _ => MouseButton.Left
        };
        string mode = step.Str("mode", "tap");
        switch (mode)
        {
            case "press":
                _runner.SimulateMouseButtonPress(btn, false);
                await Simulate(1);
                DrainLog();
                break;
            case "release":
                _runner.SimulateMouseButtonRelease(btn);
                await Simulate(1);
                DrainLog();
                break;
            case "hold":
                _runner.SimulateMouseButtonPress(btn, false);
                await Simulate(1);
                DrainLog();
                int hold = step.Int("frames", 0);
                if (hold > 0)
                {
                    await Simulate((uint)hold);
                    DrainLog();
                }
                _runner.SimulateMouseButtonRelease(btn);
                await Simulate(1);
                DrainLog();
                break;
            default:
                _runner.SimulateMouseButtonPressed(btn, false);
                await Simulate(1);
                DrainLog();
                break;
        }
    }

    private async Task DoAimCamera(JsonNode step)
    {
        var player = Player;
        if (player == null) return;
        bool hasYaw = step["yaw_deg"] != null;
        bool hasPitch = step["pitch_deg"] != null;
        if (!hasYaw && !hasPitch) return;
        double sens = Convert.ToDouble(Reflection.Read(player, "MouseSensitivity") ?? 0.002f, CultureInfo.InvariantCulture);
        if (sens <= 0) sens = 0.002;
        for (int iter = 0; iter < 8; iter++)
        {
            double curYaw = Convert.ToDouble(Reflection.Read(player, "CameraYawDeg") ?? 0.0, CultureInfo.InvariantCulture);
            double curPitch = Convert.ToDouble(Reflection.Read(player, "CameraPitchDeg") ?? 0.0, CultureInfo.InvariantCulture);
            float dYaw = hasYaw ? (float)(step.Num("yaw_deg", curYaw) - curYaw) : 0f;
            float dPitch = hasPitch ? (float)(step.Num("pitch_deg", curPitch) - curPitch) : 0f;
            if (Math.Abs(dYaw) < 1f && Math.Abs(dPitch) < 1f) break;
            float dx = dYaw != 0 ? Mathf.Clamp(Mathf.DegToRad(dYaw) / (float)sens, -4000f, 4000f) : 0f;
            float dy = dPitch != 0 ? Mathf.Clamp(Mathf.DegToRad(dPitch) / (float)sens, -4000f, 4000f) : 0f;
            _runner.SimulateMouseMoveRelative(new Vector2(dx, dy), 0.0, Tween.TransitionType.Linear);
            await Simulate(1);
            DrainLog();
        }
    }

    private static bool HasModifier(JsonNode step, string mod)
    {
        var arr = step["modifiers"] as JsonArray;
        if (arr == null) return false;
        foreach (var m in arr)
            if (m?.GetValue<string>() == mod) return true;
        return false;
    }
}

internal static class Reflection
{
    public static object? Read(object target, string member)
    {
        var t = target.GetType();
        var f = t.GetField(member, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (f != null) return f.GetValue(target);
        var p = t.GetProperty(member, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        if (p != null) return p.GetValue(target);
        return null;
    }
}
