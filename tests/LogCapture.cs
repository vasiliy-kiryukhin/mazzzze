#nullable enable
using System.Collections.Generic;

namespace MazeTests;

internal sealed class LogCapture
{
    private int _index;

    public void Start()
    {
        GameLog.Begin();
        _index = 0;
    }

    public void Stop()
    {
        GameLog.End();
    }

    public IEnumerable<string> Drain()
    {
        var lines = GameLog.Lines;
        while (_index < lines.Count)
        {
            yield return lines[_index];
            _index++;
        }
    }
}
