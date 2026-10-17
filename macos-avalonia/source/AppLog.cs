using System.Collections.Generic;

namespace MandelbrotViewer;

// In-memory ring of the last 200 timestamped log lines (§7.2, §1.3).
public static class AppLog
{
    public const int Capacity = 200;
    private static readonly object Gate = new();
    private static readonly Queue<string> Lines = new();

    public static void Write(string message)
    {
        string line = $"{DateTime.Now:HH:mm:ss}  {message}";
        lock (Gate)
        {
            Lines.Enqueue(line);
            while (Lines.Count > Capacity)
                Lines.Dequeue();
        }
    }

    public static string[] Snapshot()
    {
        lock (Gate)
            return Lines.ToArray();
    }
}
