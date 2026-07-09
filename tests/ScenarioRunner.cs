#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using GdUnit4;
using Godot;

namespace MazeTests;

[TestSuite]
public class ScenarioRunner
{
    [TestCase]
    [RequireGodotRuntime]
    public async Task RunAllScenarios()
    {
        ScenarioLoader.RunId = System.DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var filter = new Regex(System.Environment.GetEnvironmentVariable("TC_FILTER") ?? ".*");
        var results = new List<TcResult>();

        foreach (var file in ScenarioLoader.Discover())
        {
            Scenario s;
            try { s = ScenarioLoader.Load(file); }
            catch (Exception e)
            {
                results.Add(new TcResult { Id = Path.GetFileNameWithoutExtension(file), Title = "", Status = "FAIL", Message = "load error: " + e.Message });
                continue;
            }
            if (!filter.IsMatch(s.Id)) continue;
            results.Add(ScenarioLoader.RequiresUnsupportedNav(s) ? SkipResult(s) : await RunScenario(s));
        }

        var reportPath = HtmlReport.Write(results, ScenarioLoader.ReportDir);
        int pass = results.Count(r => r.Status == "PASS");
        int fail = results.Count(r => r.Status == "FAIL");
        int skip = results.Count(r => r.Status == "SKIP");
        GD.Print($"[QA] {pass} PASS / {fail} FAIL / {skip} SKIP из {results.Count}");
        GD.Print($"[QA] HTML-отчёт: {reportPath}");

        if (fail > 0)
        {
            var detail = string.Join("\n", results.Where(r => r.Status == "FAIL").Select(r => $"  {r.Id}: {ShortMessage(r.Message)}"));
            GD.Print($"[QA] FAIL детали:\n{detail}");
            Assertions.AssertThat(fail).IsEqual(0);
        }
    }

    private static TcResult SkipResult(Scenario s)
        => new()
        {
            Id = s.Id,
            Title = s.Title,
            Status = "SKIP",
            Message = "Требует навигацию/монстр-харнес (find_monster/move_player/aim_at_monster) — за рамками MVP раннера."
        };

    private static async Task<TcResult> RunScenario(Scenario s)
    {
        var result = new TcResult { Id = s.Id, Title = s.Title };
        var capture = new LogCapture();
        ISceneRunner? runner = null;
        StepExecutor? ex = null;
        VideoRecorder? recorder = null;
        if (s.RecordVideo)
        {
            var mp4 = Path.Combine(ScenarioLoader.VideoDir, s.Id + ".mp4");
            recorder = new VideoRecorder(Path.Combine(ScenarioLoader.VideoDir, s.Id), mp4, s.VideoFps);
        }
        try
        {
            capture.Start();
            recorder?.Start();
            runner = ISceneRunner.Load(s.Scene, true, false);
            await runner.SimulateFrames(2, 16);

            ex = new StepExecutor(runner, capture);
            if (recorder != null) ex.SetRecorder(recorder);
            var evaluator = new AssertionEvaluator(runner, ex);

            await ex.RunSetup(s.Setup);
            evaluator.EvaluateAt(s.Assertions, "after_setup");

            int idx = 0;
            foreach (var n in s.Steps)
            {
                if (n is null) continue;
                await ex.RunStepMarked(n, $"step {idx + 1}");
                idx++;
                ex.MainStepEndFrames.Add(ex.Frame);
                evaluator.EvaluateAt(s.Assertions, $"after_step:{idx}");
                if (ex.Frame - ex.SetupEndFrame > s.MaxDurationFrames)
                {
                    result.Status = "FAIL";
                    result.Message = $"Превышен лимит кадров фазы шагов ({s.MaxDurationFrames}).";
                    break;
                }
            }

            if (result.Status != "FAIL")
            {
                capture.Stop();
                evaluator.EvaluateAt(s.Assertions, "end");
                result.Screenshots.AddRange(ex.Screenshots);
                result.ManualNotes.AddRange(evaluator.ManualNotes);
                result.Status = evaluator.Failures.Count == 0 ? "PASS" : "FAIL";
                result.Message = string.Join("\n", evaluator.Failures);
            }
        }
        catch (Exception e)
        {
            result.Status = "FAIL";
            result.Message = "runtime: " + e.Message;
        }
        finally
        {
            try { capture.Stop(); } catch { }
            if (result.Status == "FAIL" && runner != null)
            {
                var shot = Path.Combine(ScenarioLoader.ScreenshotDir, $"{s.Id}-FAIL.png");
                ScreenshotUtil.Capture(runner.Scene()?.GetViewport(), shot);
                if (File.Exists(shot)) result.Screenshots.Add(shot);
            }
            if (ex != null)
                DumpLog(s.Id, ex);
            if (recorder != null)
            {
                try { recorder.WriteTimeline(); } catch { }
                try
                {
                    if (recorder.Finish() && recorder.VideoPath != null)
                    {
                        result.VideoPath = recorder.VideoPath;
                        result.SrtPath = Path.ChangeExtension(recorder.VideoPath, ".srt");
                    }
                    else if (recorder.FramesKept)
                        result.ManualNotes.Add("Видео не собрано (ffmpeg недоступен) — сохранена PNG-последовательность в _video/" + s.Id + "/");
                }
                catch (Exception re) { result.ManualNotes.Add("Ошибка видео: " + re.Message); }
            }
            if (runner != null && runner.Scene() != null)
            {
                runner.Scene().QueueFree();
                try { await runner.SimulateFrames(2, 16); } catch { }
            }
        }

        return result;
    }

    private static void DumpLog(string id, StepExecutor ex)
    {
        try
        {
            Directory.CreateDirectory(ScenarioLoader.LogDir);
            var sb = new System.Text.StringBuilder();
            foreach (var l in ex.Log)
                sb.Append('[').Append(l.Frame).Append("] ").Append(l.Text).Append('\n');
            File.WriteAllText(Path.Combine(ScenarioLoader.LogDir, id + ".txt"), sb.ToString());
        }
        catch { }
    }

    private static string ShortMessage(string msg)
    {
        if (string.IsNullOrEmpty(msg)) return "";
        var first = msg.Split('\n')[0];
        return first.Length > 120 ? first[..120] + "…" : first;
    }
}
