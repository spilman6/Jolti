using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Jolti.Services;

// Session-wide standard cursors, so feedback is visible over the destination app.
internal sealed class BusyCursorService : IDisposable
{
    private readonly Func<bool> _show;
    private readonly Func<bool> _restore;
    private bool _changed, _disposed;

    public BusyCursorService() : this(ShowNative, RestoreNative)
    {
        // Recover the user's cursor scheme after a previous forced process stop.
        RestoreNative();
    }

    internal BusyCursorService(Func<bool> show, Func<bool> restore)
    {
        _show = show;
        _restore = restore;
    }

    public void SetBusy(bool busy)
    {
        if (_disposed) return;
        try
        {
            if (busy && !_changed)
            {
                // Restore even if only some cursor replacements succeeded.
                _changed = true;
                if (!_show()) Restore();
            }
            else if (!busy) Restore();
        }
        catch (Exception ex)
        {
            Trace.TraceWarning("Busy cursor: " + ex.Message);
            Restore();
        }
    }

    private void Restore()
    {
        if (!_changed) return;
        try { if (_restore()) _changed = false; }
        catch (Exception ex) { Trace.TraceWarning("Cursor restoration: " + ex.Message); }
    }

    public void Dispose()
    {
        Restore();
        _disposed = true;
    }

    private static bool ShowNative()
    {
        var wait = LoadCursorW(0, (nint)32514); // IDC_WAIT; shared, never destroyed.
        if (wait == 0) return false;
        foreach (uint id in new uint[] { 32512, 32513, 32649 }) // Arrow, text, link.
        {
            var copy = CopyImage(wait, 2, 0, 0, 0); // IMAGE_CURSOR, owned copy.
            if (copy == 0) return false;
            // SetSystemCursor consumes the copy; never pass the shared handle.
            if (!SetSystemCursor(copy, id)) return false;
        }
        return true;
    }

    private static bool RestoreNative() => SystemParametersInfoW(0x57, 0, 0, 0); // SPI_SETCURSORS

    [DllImport("user32.dll", ExactSpelling = true)] private static extern nint LoadCursorW(nint instance, nint name);
    [DllImport("user32.dll")] private static extern nint CopyImage(nint image, uint type, int width, int height, uint flags);
    [DllImport("user32.dll")] private static extern bool SetSystemCursor(nint cursor, uint id);
    [DllImport("user32.dll", ExactSpelling = true)] private static extern bool SystemParametersInfoW(uint action, uint parameter, nint value, uint flags);
}
