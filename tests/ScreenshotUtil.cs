#nullable enable
using System;
using System.IO;
using Godot;

namespace MazeTests;

internal static class ScreenshotUtil
{
    public static void Capture(Viewport? viewport, string path)
    {
        if (viewport == null) return;
        try
        {
            var tex = viewport.GetTexture();
            if (tex == null) return;
            var img = tex.GetImage();
            if (img == null) return;
            const int maxW = 512;
            if (img.GetWidth() > maxW)
            {
                int h = Mathf.Max(1, (int)(img.GetHeight() * (float)maxW / img.GetWidth()));
                img.Resize(maxW, h, Image.Interpolation.Lanczos);
            }
            EnsureParentDir(path);
            img.SavePng(path);
        }
        catch (Exception)
        {
        }
    }

    public static void EnsureParentDir(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);
    }
}
