#nullable enable
using System.Collections.Generic;
using System.Text.Json.Nodes;

namespace MazeTests;

internal sealed class Scenario
{
    public string FilePath = "";
    public string Id = "";
    public string Title = "";
    public string Scene = "res://test_scene.tscn";
    public string MarkdownRef = "";
    public int MaxDurationFrames = 1500;
    public bool RecordVideo;
    public int VideoFps = 60;
    public JsonArray? Setup;
    public JsonArray Steps = new();
    public JsonArray Assertions = new();
}

internal sealed class TcResult
{
    public string Id = "";
    public string Title = "";
    public string Status = "";
    public string Message = "";
    public List<string> Screenshots = new();
    public List<string> ManualNotes = new();
    public string? VideoPath;
    public string? SrtPath;
}

internal static class JsonExt
{
    public static string Str(this JsonNode? n, string key, string def = "")
        => n is not null && n[key] is { } v ? v.GetValue<string>() : def;

    public static int Int(this JsonNode? n, string key, int def = 0)
    {
        if (n is null || n[key] is not { } v) return def;
        return (int)v.AsValue().GetValue<System.Decimal>();
    }

    public static double Num(this JsonNode? n, string key, double def = 0.0)
    {
        if (n is null || n[key] is not { } v) return def;
        return (double)v.AsValue().GetValue<double>();
    }

    public static bool Bool(this JsonNode? n, string key, bool def = false)
        => n is not null && n[key] is { } v && v.GetValue<bool>();

    public static string Type(this JsonNode step) => step.Str("type");
    public static string Kind(this JsonNode assertion) => assertion.Str("kind");
}
