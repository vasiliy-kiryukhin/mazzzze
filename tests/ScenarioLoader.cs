#nullable enable
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Godot;

namespace MazeTests;

internal static class ScenarioLoader
{
    public static string QaReq
        => System.Environment.GetEnvironmentVariable("QA_REQ")?.Trim('/') ?? "REQ-0021-tennis-ball";

    public static string QaRoot => ProjectSettings.GlobalizePath("res://qa").Replace("\\", "/");

    public static string QaDir => Path.Combine(QaRoot, QaReq).Replace("\\", "/");

    public static string RunId { get; set; } = "run";

    public static string RunsRoot => Path.Combine(QaRoot, "_runs");
    public static string RunDir => Path.Combine(RunsRoot, RunId);
    public static string ReportDir => RunDir;
    public static string LogDir => Path.Combine(RunDir, "logs");
    public static string ScreenshotDir => Path.Combine(RunDir, "screenshots");
    public static string VideoDir => Path.Combine(RunDir, "video");

    public static IEnumerable<string> Discover()
        => Directory.GetFiles(QaDir, "TC-*.json").OrderBy(p => p);

    public static Scenario Load(string filePath)
    {
        var text = File.ReadAllText(filePath);
        var root = JsonNode.Parse(text) ?? throw new InvalidDataException($"parse failed: {filePath}");
        var s = new Scenario
        {
            FilePath = filePath,
            Id = root.Str("id", Path.GetFileNameWithoutExtension(filePath)),
            Title = root.Str("title"),
            Scene = root.Str("scene", "res://test_scene.tscn"),
            MarkdownRef = root.Str("manual_verification_ref"),
            MaxDurationFrames = root.Int("max_duration_frames", 1500),
            RecordVideo = root.Bool("record_video", false),
            VideoFps = root.Int("video_fps", 60),
            Setup = root["setup"] as JsonArray,
            Steps = (root["steps"] as JsonArray) ?? new JsonArray(),
            Assertions = (root["assertions"] as JsonArray) ?? new JsonArray()
        };
        return s;
    }

    public static bool RequiresUnsupportedNav(Scenario s)
    {
        bool Scan(JsonArray? arr)
        {
            if (arr == null) return false;
            foreach (var n in arr)
            {
                if (n is null) continue;
                string t = n.Type();
                if (t == "find_monster" || t == "move_player") return true;
                if (t == "aim_camera" && !string.IsNullOrEmpty(n.Str("at_monster"))) return true;
            }
            return false;
        }
        return Scan(s.Setup) || Scan(s.Steps);
    }
}
