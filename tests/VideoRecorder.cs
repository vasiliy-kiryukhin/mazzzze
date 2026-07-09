#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using Godot;

namespace MazeTests;

internal sealed class VideoRecorder
{
    private readonly string _framesDir;
    private readonly string _videoPath;
    private readonly string _basePath;
    private const int PhysicsFps = 60;
    private readonly int _stride;
    private readonly int _videoFps;
    private readonly List<Span> _spans = new();
    private int _frame;

    public string? VideoPath { get; private set; }
    public bool FramesKept { get; private set; }
    public int Stride => _stride;

    public VideoRecorder(string framesDir, string videoPath, int desiredFps)
    {
        _framesDir = framesDir;
        _videoPath = videoPath;
        _basePath = Path.ChangeExtension(videoPath, null);
        _stride = Math.Max(1, PhysicsFps / Math.Max(1, desiredFps));
        _videoFps = PhysicsFps / _stride;
    }

    public void Start()
    {
        if (Directory.Exists(_framesDir))
            Directory.Delete(_framesDir, true);
        Directory.CreateDirectory(_framesDir);
        _spans.Clear();
        _frame = 0;
    }

    public void CaptureFrame(Viewport? viewport)
    {
        if (viewport == null) return;
        try
        {
            var img = viewport.GetTexture()?.GetImage();
            if (img == null) return;
            const int maxW = 640;
            if (img.GetWidth() > maxW)
            {
                int h = Mathf.Max(1, (int)(img.GetHeight() * (float)maxW / img.GetWidth()));
                img.Resize(maxW, h, Image.Interpolation.Lanczos);
            }
            img.SaveJpg(Path.Combine(_framesDir, $"frame_{_frame:D5}.jpg"), 0.85f);
        }
        catch { }
        _frame++;
    }

    public bool Finish()
    {
        if (_frame == 0) return false;
        var args = $"-y -framerate {_videoFps} -i \"{_framesDir}/frame_%05d.jpg\" -c:v libx264 -pix_fmt yuv420p -r {_videoFps} \"{_videoPath}\"";
        try
        {
            var psi = new ProcessStartInfo("ffmpeg", args)
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p == null) return false;
            p.WaitForExit(180_000);
            if (p.ExitCode != 0) { FramesKept = true; return false; }
        }
        catch
        {
            FramesKept = true;
            return false;
        }
        VideoPath = _videoPath;
        try { Directory.Delete(_framesDir, true); } catch { }
        return true;
    }

    public void AddSpan(string label, string type, string reason, int startFrame, int endFrame)
        => _spans.Add(new Span(label, type, reason, startFrame, endFrame));

    public void WriteTimeline()
    {
        WriteSrt();
        WriteJson();
    }

    private void WriteSrt()
    {
        var sb = new StringBuilder();
        int n = 0;
        foreach (var s in _spans)
        {
            n++;
            double start = s.StartFrame / (double)PhysicsFps;
            double end = (s.EndFrame + 1) / (double)PhysicsFps;
            sb.Append(n).Append('\n');
            sb.Append(Ts(start)).Append(" --> ").Append(Ts(end)).Append('\n');
            sb.Append('[').Append(s.Label).Append("] ").Append(s.Type);
            if (!string.IsNullOrEmpty(s.Reason)) sb.Append(" — ").Append(s.Reason);
            sb.Append($"  (кадры {s.StartFrame}..{s.EndFrame})").Append('\n').Append('\n');
        }
        File.WriteAllText(_basePath + ".srt", sb.ToString(), Encoding.UTF8);
    }

    private void WriteJson()
    {
        var sb = new StringBuilder();
        sb.Append("{\"physics_fps\":").Append(PhysicsFps).Append(",\"video_fps\":").Append(_videoFps).Append(",\"stride\":").Append(_stride).Append(",\"spans\":[");
        for (int i = 0; i < _spans.Count; i++)
        {
            var s = _spans[i];
            if (i > 0) sb.Append(',');
            sb.Append('{');
            sb.Append("\"label\":").Append(Quote(s.Label)).Append(',');
            sb.Append("\"type\":").Append(Quote(s.Type)).Append(',');
            sb.Append("\"reason\":").Append(Quote(s.Reason)).Append(',');
            sb.Append("\"start_frame\":").Append(s.StartFrame).Append(',');
            sb.Append("\"end_frame\":").Append(s.EndFrame).Append(',');
            sb.Append("\"start_sec\":").Append((s.StartFrame / (double)PhysicsFps).ToString("0.###", CultureInfo.InvariantCulture)).Append(',');
            sb.Append("\"end_sec\":").Append(((s.EndFrame + 1) / (double)PhysicsFps).ToString("0.###", CultureInfo.InvariantCulture));
            sb.Append('}');
        }
        sb.Append("]}");
        File.WriteAllText(_basePath + ".timeline.json", sb.ToString(), Encoding.UTF8);
    }

    private static string Quote(string s)
        => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static string Ts(double seconds)
    {
        if (seconds < 0) seconds = 0;
        int ms = (int)((seconds - (int)seconds) * 1000);
        int total = (int)seconds;
        int h = total / 3600;
        int m = (total % 3600) / 60;
        int sec = total % 60;
        return $"{h:D2}:{m:D2}:{sec:D2},{ms:D3}";
    }

    private readonly struct Span
    {
        public readonly string Label;
        public readonly string Type;
        public readonly string Reason;
        public readonly int StartFrame;
        public readonly int EndFrame;
        public Span(string label, string type, string reason, int startFrame, int endFrame)
        { Label = label; Type = type; Reason = reason; StartFrame = startFrame; EndFrame = endFrame; }
    }
}
