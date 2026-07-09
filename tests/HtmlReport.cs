#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace MazeTests;

internal static class HtmlReport
{
    public static string Write(List<TcResult> results, string reportDir)
    {
        Directory.CreateDirectory(reportDir);
        int pass = results.Count(r => r.Status == "PASS");
        int fail = results.Count(r => r.Status == "FAIL");
        int skip = results.Count(r => r.Status == "SKIP");

        var sb = new StringBuilder();
        sb.Append("<!doctype html><html lang='ru'><head><meta charset='utf-8'>");
        sb.Append("<title>REQ-0021 QA Report</title><style>");
        sb.Append("body{font-family:system-ui,Segoe UI,sans-serif;margin:20px;background:#111;color:#eee}");
        sb.Append("h1{margin:0 0 4px}");
        sb.Append(".sum{display:flex;gap:12px;margin:14px 0;font-size:15px}");
        sb.Append(".badge{display:inline-block;padding:2px 8px;border-radius:4px;font-weight:600;font-size:13px}");
        sb.Append(".PASS{background:#1b5e20;color:#c8e6c9}.FAIL{background:#b71c1c;color:#ffcdd2}.SKIP{background:#455a64;color:#cfd8dc}");
        sb.Append(".tc{border:1px solid #333;border-radius:8px;padding:12px 14px;margin:12px 0;background:#1b1b1b}");
        sb.Append(".tc h3{margin:0 0 6px;font-size:15px}");
        sb.Append(".msg{white-space:pre-wrap;background:#000;color:#ffd0d0;padding:8px;border-radius:4px;font-family:ui-monospace,Consolas,monospace;font-size:12px;margin:6px 0}");
        sb.Append(".notes{color:#aaa;font-size:12px;margin:6px 0}");
        sb.Append("img{max-width:480px;border:1px solid #444;border-radius:4px;margin:4px;display:block}");
        sb.Append("</style></head><body>");

        sb.Append($"<h1>REQ-0021-tennis-ball — QA Report</h1>");
        sb.Append($"<div class='sum'><span class='badge PASS'>PASS {pass}</span><span class='badge FAIL'>FAIL {fail}</span><span class='badge SKIP'>SKIP {skip}</span><span>{results.Count} сценариев</span></div>");

        foreach (var r in results.OrderBy(x => x.Id))
        {
            sb.Append($"<div class='tc'><h3>{r.Id} <span class='badge {r.Status}'>{r.Status}</span></h3>");
            sb.Append($"<div>{System.Net.WebUtility.HtmlEncode(r.Title)}</div>");
            if (!string.IsNullOrEmpty(r.Message))
                sb.Append($"<div class='msg'>{System.Net.WebUtility.HtmlEncode(r.Message)}</div>");
            if (r.ManualNotes.Count > 0)
            {
                sb.Append("<div class='notes'>Визуальные проверки:<ul>");
                foreach (var n in r.ManualNotes)
                    sb.Append($"<li>{System.Net.WebUtility.HtmlEncode(n)}</li>");
                sb.Append("</ul></div>");
            }
            foreach (var shot in r.Screenshots.Distinct())
            {
                var rel = Relative(reportDir, shot);
                if (File.Exists(shot))
                    sb.Append($"<div><div style='font-size:11px;color:#888'>{Path.GetFileName(shot)}</div><img src='{rel}'/></div>");
            }
            if (!string.IsNullOrEmpty(r.VideoPath) && File.Exists(r.VideoPath))
            {
                var vrel = Relative(reportDir, r.VideoPath);
                var srel = !string.IsNullOrEmpty(r.SrtPath) && File.Exists(r.SrtPath) ? Relative(reportDir, r.SrtPath) : null;
                string subLink = srel != null ? $" + <a href='{srel}'>субтитры-таймлайн (SRT)</a>" : "";
                sb.Append($"<div><div style='font-size:11px;color:#888'>видео прогона{subLink}</div>");
                sb.Append($"<video controls width='480' src='{vrel}'></video></div>");
            }
            sb.Append("</div>");
        }

        sb.Append("</body></html>");

        var path = Path.Combine(reportDir, "index.html");
        File.WriteAllText(path, sb.ToString());
        return path;
    }

    private static string Relative(string fromDir, string toFile)
        => Path.GetRelativePath(fromDir, toFile).Replace("\\", "/");
}
