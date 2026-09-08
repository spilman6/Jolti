using System.Runtime.InteropServices;
namespace LocalFlow.Infrastructure;

internal interface ITextInputBackend
{
    bool ModifiersDown();
    bool IsTargetFocused(nint target);
    uint Send(NativeMethods.INPUT[] inputs);
}
internal sealed class TextInputBackend : ITextInputBackend
{
    public bool ModifiersDown() => NativeMethods.Down(0x11) || NativeMethods.Down(0x12) || NativeMethods.Down(0x10) || NativeMethods.Down(0x5B) || NativeMethods.Down(0x5C);
    public bool IsTargetFocused(nint target) => target != 0 && NativeMethods.IsWindow(target) && NativeMethods.GetForegroundWindow() == target;
    public uint Send(NativeMethods.INPUT[] inputs) => NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
}
