using System.Windows.Threading;
using LocalFlow.Core;
using LocalFlow.Infrastructure;

namespace LocalFlow.Services;

public sealed class HotkeyService : IHotkeyService
{
    public static readonly string[] SupportedHotkeys = ["Ctrl + Win", "Ctrl + Alt", "Ctrl + Shift", "Alt + Shift"];
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(20) };
    private string _hotkey = SupportedHotkeys[0];
    private bool _held;
    public event Action? Pressed;
    public event Action? Released;
    public HotkeyService() { _timer.Tick += Tick; }
    public void Configure(string hotkey)
    {
        if (!SupportedHotkeys.Contains(hotkey)) throw new ArgumentException("Choose a supported modifier hotkey.");
        if (_held) { _held = false; Released?.Invoke(); }
        _hotkey = hotkey;
        _timer.Start();
    }
    private void Tick(object? sender, EventArgs e)
    {
        // RegisterHotKey cannot express a modifier-only hold/release gesture. Poll only
        // the selected modifiers, never character keys, input events, or typed text.
        var down = _hotkey switch
        {
            "Ctrl + Win" => NativeMethods.Down(0x11) && (NativeMethods.Down(0x5B) || NativeMethods.Down(0x5C)),
            "Ctrl + Alt" => NativeMethods.Down(0x11) && NativeMethods.Down(0x12),
            "Ctrl + Shift" => NativeMethods.Down(0x11) && NativeMethods.Down(0x10),
            "Alt + Shift" => NativeMethods.Down(0x12) && NativeMethods.Down(0x10),
            _ => false
        };
        if (down == _held) return;
        _held = down;
        if (down) Pressed?.Invoke(); else Released?.Invoke();
    }
    public void Dispose() { _timer.Stop(); _timer.Tick -= Tick; }
}
