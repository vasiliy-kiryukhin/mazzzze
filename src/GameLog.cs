using System.Collections.Generic;
using Godot;

// Тестовый лог-тап для GdUnit4-раннера (qa/REQ-0021-tennis-ball).
// В обычной игре ведёт себя идентично GD.Print. Когда Recording=true (ставит раннер),
// дублирует строки в статический буфер — раннер читает его для log_-проверок сценария.
public static class GameLog
{
    public static readonly List<string> Lines = new();
    public static bool Recording;

    public static void Print(string s)
    {
        GD.Print(s);
        if (Recording)
            Lines.Add(s);
    }

    public static void Begin()
    {
        Lines.Clear();
        Recording = true;
    }

    public static void End()
    {
        Recording = false;
    }
}
