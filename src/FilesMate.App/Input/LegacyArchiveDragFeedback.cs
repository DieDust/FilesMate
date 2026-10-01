using System.Runtime.InteropServices;

namespace FilesMate.App.Input;

// Compatibility with older CompactMate releases that extract themselves and
// cancel OLE instead of delivering Drop. The standard receiver does not use
// this adapter; remove it when those releases have a standard drag source.
internal sealed class LegacyArchiveDragFeedback : IDisposable
{
    private readonly Action _begin;
    private readonly Action _end;
    private readonly WinEventProcedure _callback;
    private readonly nint _hook;
    private nint _feedback;

    internal LegacyArchiveDragFeedback(Action begin, Action end)
    {
        _begin = begin; _end = end;
        _callback = (_, eventId, window, objectId, childId, _, _) =>
        {
            try { Changed(eventId, window, objectId, childId); }
            catch (Exception error)
            {
                try { _end(); } catch { }
                try { App.LogFailure("LegacyArchiveDrag", error); } catch { }
            }
        };
        _hook = SetWinEventHook(0x8000, 0x8003, 0, _callback, 0, 0, 0);
    }

    private void Changed(uint eventId, nint window, int objectId, int childId)
    {
        if (objectId != 0 || childId != 0) return;
        if (window == _feedback && eventId is 0x8001 or 0x8003)
        { _feedback = 0; _end(); return; }
        if (eventId is not (0x8000 or 0x8002)) return;
        var name = new System.Text.StringBuilder(80);
        GetClassNameW(window, name, name.Capacity);
        if (name.ToString() != "CompactMateDragFeedback" || !IsWindowVisible(window)) return;
        _feedback = window;
        _begin();
    }

    public void Dispose() { if (_hook != 0) UnhookWinEvent(_hook); _feedback = 0; }
    private delegate void WinEventProcedure(nint hook, uint eventId, nint window, int objectId, int childId, uint thread, uint time);
    [DllImport("user32.dll")] private static extern nint SetWinEventHook(uint first, uint last, nint module, WinEventProcedure callback, uint process, uint thread, uint flags);
    [DllImport("user32.dll")] private static extern bool UnhookWinEvent(nint hook);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint window);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassNameW(nint window, System.Text.StringBuilder text, int size);
}
