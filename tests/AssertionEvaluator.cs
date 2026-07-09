#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using GdUnit4;
using Godot;

namespace MazeTests;

internal sealed class AssertionEvaluator
{
    private readonly ISceneRunner _runner;
    private readonly StepExecutor _ex;
    private readonly List<string> _failures = new();
    private readonly List<string> _manual = new();
    private readonly HashSet<int> _done = new();

    public IReadOnlyList<string> Failures => _failures;
    public IReadOnlyList<string> ManualNotes => _manual;

    public AssertionEvaluator(ISceneRunner runner, StepExecutor ex)
    {
        _runner = runner;
        _ex = ex;
    }

    public void EvaluateAt(JsonArray assertions, string checkpoint)
    {
        for (int i = 0; i < assertions.Count; i++)
        {
            if (_done.Contains(i)) continue;
            var a = assertions[i];
            if (a is null) continue;
            string kind = a.Kind();
            if (IsNodeKind(kind))
            {
                if (ResolvedAt(a) == checkpoint)
                {
                    EvalOne(a);
                    _done.Add(i);
                }
            }
            else if (checkpoint == "end")
            {
                EvalOne(a);
                _done.Add(i);
            }
        }
    }

    private static bool IsNodeKind(string kind)
        => kind == "node" || kind == "node_by_type" || kind == "node_property";

    private static string ResolvedAt(JsonNode a)
    {
        var node = a["at"];
        if (node is null) return "end";
        string v = node.GetValue<string>();
        return string.IsNullOrEmpty(v) ? "end" : v;
    }

    private void EvalOne(JsonNode a)
    {
        try
        {
            switch (a.Kind())
            {
                case "log_contains": LogContains(a); break;
                case "log_not_contains": LogNotContains(a); break;
                case "log_order": LogOrder(a); break;
                case "node": NodeExists(a); break;
                case "node_by_type": NodeByType(a); break;
                case "node_property": NodeProperty(a); break;
                case "compare": Compare(a); break;
                case "manual_visual_check": ManualNote(a); break;
            }
        }
        catch (Exception e)
        {
            Fail($"[{a.Kind()}] исключение при проверке: {e.Message}");
        }
    }

    private (int start, int end) Window(JsonNode a)
    {
        int start = 0;
        int end = int.MaxValue;
        var after = a["after_step"];
        if (after is not null)
        {
            if (after is JsonValue av && av.TryGetValue<string>(out string? sv) && sv is not null)
            {
                if (sv == "setup" || sv == "end") start = 0;
                else if (int.TryParse(sv, out int n)) ApplyStep(ref start, n);
            }
            else if (after.AsValue().TryGetValue<int>(out int n)) ApplyStep(ref start, n);
        }
        int wf = a.Int("within_frames", 0);
        if (wf > 0) end = start + wf;
        return (start, end);

        void ApplyStep(ref int s, int n)
        {
            if (n >= 1 && n <= _ex.MainStepEndFrames.Count)
                s = _ex.MainStepEndFrames[n - 1];
        }
    }

    private string WindowedText(JsonNode a)
    {
        var (s, e) = Window(a);
        var sb = new System.Text.StringBuilder();
        foreach (var l in _ex.Log)
            if (l.Frame >= s && l.Frame <= e)
                sb.Append(l.Text).Append('\n');
        return sb.ToString();
    }

    private void LogContains(JsonNode a)
    {
        var rx = new Regex(a.Str("pattern"), RegexOptions.None, TimeSpan.FromSeconds(2));
        var text = WindowedText(a);
        var m = rx.Match(text);
        if (!m.Success)
        {
            Fail($"[log_contains] не найдено: {a.Str("pattern")}");
            return;
        }
        if (a["capture_group"] != null)
        {
            int g = a.Int("capture_group", 1);
            double v = double.Parse(m.Groups[g].Value, CultureInfo.InvariantCulture);
            double lo = a.Num("min", double.NegativeInfinity);
            double hi = a.Num("max", double.PositiveInfinity);
            if (v < lo || v > hi)
                Fail($"[log_contains] значение {v} вне [{lo};{hi}] для {a.Str("pattern")}");
        }
    }

    private void LogNotContains(JsonNode a)
    {
        var rx = new Regex(a.Str("pattern"), RegexOptions.None, TimeSpan.FromSeconds(2));
        var text = WindowedText(a);
        if (rx.IsMatch(text))
            Fail($"[log_not_contains] найдено запрещённое: {a.Str("pattern")}");
    }

    private void LogOrder(JsonNode a)
    {
        var arr = a["patterns"] as JsonArray;
        if (arr == null || arr.Count == 0) return;
        var text = WindowedText(a);
        int pos = 0;
        foreach (var p in arr)
        {
            if (p is null) continue;
            var rx = new Regex(p.GetValue<string>(), RegexOptions.None, TimeSpan.FromSeconds(2));
            var m = rx.Match(text, pos);
            if (!m.Success)
            {
                Fail($"[log_order] нарушена последовательность, не найдено после позиции {pos}: {p}");
                return;
            }
            pos = m.Index + m.Length;
        }
    }

    private void NodeExists(JsonNode a)
    {
        string path = a.Str("path");
        bool want = a.Bool("exists", true);
        var node = _runner.Scene().GetNodeOrNull(path);
        if (node != null != want)
            Fail($"[node] '{path}' exists={node != null}, ожидалось exists={want}");
    }

    private void NodeByType(JsonNode a)
    {
        string parent = a.Str("parent", "/root/Main");
        string type = a.Str("type");
        var p = _runner.Scene().GetNodeOrNull(parent);
        if (p == null)
        {
            Fail($"[node_by_type] родитель не найден: {parent}");
            return;
        }
        int count = CountByType(p, type);
        int lo = a.Int("count_min", 0);
        int hi = a.Int("count_max", int.MaxValue);
        if (count < lo || count > hi)
            Fail($"[node_by_type] {type} под {parent}: найдено {count}, ожидалось [{lo};{hi}]");
    }

    private static int CountByType(Node root, string type)
    {
        int count = 0;
        foreach (var child in root.GetChildren())
        {
            if (child is Node cn)
            {
                if (cn.GetClass() == type || cn.GetType().Name == type) count++;
                count += CountByType(cn, type);
            }
        }
        return count;
    }

    private void NodeProperty(JsonNode a)
    {
        string path = a.Str("path");
        string prop = a.Str("property");
        var node = _runner.Scene().GetNodeOrNull(path);
        if (node == null)
        {
            Fail($"[node_property] узел не найден: {path}");
            return;
        }
        var val = Reflection.Read(node, prop);
        if (a["equals"] == null) return;
        double expected = ((JsonNode)a["equals"]!).AsValue().GetValue<double>();
        double actual = ToDouble(val);
        double tol = a.Num("tolerance", 0.001);
        if (Math.Abs(actual - expected) > tol)
            Fail($"[node_property] {path}.{prop}={actual}, ожидалось ≈{expected} (tol {tol})");
    }

    private void Compare(JsonNode a)
    {
        double x = ResolveValue(a["a"]);
        var bnode = a["b"];
        double y = bnode is JsonValue ? bnode!.AsValue().GetValue<double>() : ResolveValue(bnode);
        string op = a.Str("op", "eq");
        double tol = a.Num("tolerance", 0.001);
        bool ok = op switch
        {
            "lt" => x < y,
            "gt" => x > y,
            "eq" => Math.Abs(x - y) <= tol,
            "approx" => Math.Abs(x - y) <= tol,
            _ => false
        };
        if (!ok)
            Fail($"[compare] {a["a"]}={x} {op} {y} (tol {tol}) — не выполнено");
    }

    private double ResolveValue(JsonNode? n)
    {
        if (n is JsonValue v)
        {
            if (v.TryGetValue<string>(out string? s) && s is not null)
                return _ex.Captures.TryGetValue(s, out var obj) ? ToDouble(obj) : 0;
            if (v.TryGetValue<double>(out double d)) return d;
            return 0;
        }
        if (n is null) return 0;
        string key = n.GetValue<string>();
        return _ex.Captures.TryGetValue(key, out var obj2) ? ToDouble(obj2) : 0;
    }

    private static double ToDouble(object? val)
    {
        return val switch
        {
            null => 0,
            float f => f,
            double d => d,
            int i => i,
            long l => l,
            Vector3 v => v.Length(),
            Vector2 v2 => v2.Length(),
            _ => Convert.ToDouble(val, CultureInfo.InvariantCulture)
        };
    }

    private void ManualNote(JsonNode a)
    {
        string desc = a.Str("description");
        _manual.Add(desc);
    }

    private void Fail(string msg) => _failures.Add(msg);
}
