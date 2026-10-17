namespace MandelbrotViewer;

// In-memory event log (last 200 lines with timestamp): benchmark errors
// and other reports, visible in the log/diagnostics dialog (Help menu).
internal static class AppLog
{
    private static readonly object _gate = new();
    private static readonly Queue<string> _lines = new();
    private const int Capacity = 200;

    public static void Add(string message)
    {
        lock (_gate)
        {
            _lines.Enqueue($"{DateTime.Now:HH:mm:ss}  {message}");
            while (_lines.Count > Capacity) _lines.Dequeue();
        }
    }

    public static string GetText()
    {
        lock (_gate)
            return _lines.Count > 0 ? string.Join(Environment.NewLine, _lines) + Environment.NewLine : "(empty)" + Environment.NewLine;
    }
}
