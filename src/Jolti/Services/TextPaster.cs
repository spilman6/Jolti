using System.Runtime.InteropServices;
using System.Windows;
using Jolti.Core;
using Jolti.Infrastructure;

namespace Jolti.Services;

public sealed class TextPaster : ITextPaster
{
    private readonly ITextInputBackend _input;
    public TextPaster() : this(new TextInputBackend()) { }
    internal TextPaster(ITextInputBackend input) => _input = input;
    public nint CaptureTarget()
    {
        var target = NativeMethods.GetForegroundWindow();
        NativeMethods.GetWindowThreadProcessId(target, out var process);
        return process == Environment.ProcessId ? 0 : target;
    }
    public void Copy(string text)
    {
        // Never inspect or preserve the previous clipboard. Windows clipboard managers
        // are asked not to retain or upload the text using the documented format flags.
        var data = new DataObject();
        data.SetText(text, TextDataFormat.UnicodeText);
        data.SetData("CanIncludeInClipboardHistory", new MemoryStream(BitConverter.GetBytes(0)));
        data.SetData("CanUploadToCloudClipboard", new MemoryStream(BitConverter.GetBytes(0)));
        data.SetData("ExcludeClipboardContentFromMonitorProcessing", new MemoryStream(BitConverter.GetBytes(1)));
        Clipboard.SetDataObject(data, true);
    }
    public async Task PasteAsync(string text, nint target, CancellationToken cancellationToken)
    {
        // Despite the legacy interface name, dictation types Unicode directly. It never
        // calls Copy, reads the clipboard, or falls back to a clipboard paste.
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrEmpty(text)) return;
        var deadline = DateTime.UtcNow.AddSeconds(3);
        while (_input.ModifiersDown())
        {
            if (DateTime.UtcNow >= deadline) throw new InvalidOperationException("Release all modifier keys, then copy the result manually.");
            await Task.Delay(25, cancellationToken);
        }
        // Send one sequence to avoid interleaving batches with focus changes. UTF-16
        // surrogate pairs remain in order. Newlines are Unicode characters, not Enter
        // shortcuts, so they do not deliberately submit forms or trigger commands.
        var inputs = new NativeMethods.INPUT[checked(text.Length * 2)];
        for (var i = 0; i < text.Length; i++)
        {
            inputs[i * 2] = Character(text[i]);
            inputs[i * 2 + 1] = Character(text[i], true);
        }
        cancellationToken.ThrowIfCancellationRequested();
        EnsureTarget(target); // Do not steal focus or redirect text if the user changed windows.
        var sent = _input.Send(inputs);
        if (sent != inputs.Length)
        {
            // A partial call may stop after a character-down. Release only that Unicode
            // character; never retry text, which could duplicate already inserted words.
            if (sent > 0 && sent < inputs.Length && sent % 2 == 1) _input.Send([inputs[sent]]);
            throw new InvalidOperationException("Windows did not accept all text input. Some text may already be inserted; review the destination before retrying. Your clipboard is unchanged. Elevated apps may reject input.");
        }
    }
    private void EnsureTarget(nint target)
    {
        if (!_input.IsTargetFocused(target))
            throw new InvalidOperationException("The original target is no longer focused. Use Copy final text and paste where you want it.");
    }
    private static NativeMethods.INPUT Character(char character, bool up = false) => new()
    {
        Type = 1,
        // KEYEVENTF_UNICODE requires wVk=0 and a UTF-16 code unit in wScan.
        Data = new NativeMethods.InputUnion { Keyboard = new NativeMethods.KEYBDINPUT { VirtualKey = 0, Scan = character, Flags = up ? 6u : 4u } }
    };
}
