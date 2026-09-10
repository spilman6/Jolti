using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Jolti.Core;
using Jolti.Infrastructure;
using Jolti.Services;
using Jolti.ViewModels;
using Jolti.Views;
using System.IO;

internal static class Program
{
    private static int _checks;
    [STAThread]
    private static int Main(string[] args)
    {
        try
        {
            var root = Path.GetFullPath(Path.Combine("artifacts", "checks", Guid.NewGuid().ToString("N")));
            Directory.CreateDirectory(root);
            using (var cue = new NAudio.Wave.AudioFileReader(Path.Combine(AppContext.BaseDirectory, "Assets", "bloop.mp3")))
            {
                var samples = new float[4096];
                Check(cue.TotalTime.TotalSeconds > 0 && cue.TotalTime.TotalSeconds < 10 && cue.Read(samples, 0, samples.Length) > 0, "Bundled bloop decodes as a short local audio cue");
            }
            var input = new TestInput();
            var direct = new TextPaster(input);
            var unicode = "Hello é 世界 👋\nnext";
            direct.PasteAsync(unicode, 123, CancellationToken.None).GetAwaiter().GetResult();
            var events = input.Calls.Single();
            Check(events.Length == unicode.Length * 2 && events.All(x => x.Type == 1 && x.Data.Keyboard.VirtualKey == 0), "Dictation emits Unicode only, never Ctrl+V");
            Check(new string(events.Where((_, i) => i % 2 == 0).Select(x => (char)x.Data.Keyboard.Scan).ToArray()) == unicode, "Unicode accents, surrogate pairs and newlines preserved");
            Check(events.Where((_, i) => i % 2 == 0).All(x => x.Data.Keyboard.Flags == 4) && events.Where((_, i) => i % 2 == 1).All(x => x.Data.Keyboard.Flags == 6), "Every character has balanced Unicode down/up events");
            input.Calls.Clear(); input.Focused = false;
            try { direct.PasteAsync("no", 123, CancellationToken.None).GetAwaiter().GetResult(); throw new Exception("Wrong target accepted"); }
            catch (InvalidOperationException) { Check(input.Calls.Count == 0, "Focus change prevents text injection"); }
            input.Focused = true;
            try { direct.PasteAsync("no", 123, new CancellationToken(true)).GetAwaiter().GetResult(); throw new Exception("Canceled input sent"); }
            catch (OperationCanceledException) { Check(input.Calls.Count == 0, "Canceled insertion emits no input"); }
            input.Partial = true;
            try { direct.PasteAsync("abc", 123, CancellationToken.None).GetAwaiter().GetResult(); throw new Exception("Partial input accepted"); }
            catch (InvalidOperationException) { Check(input.Calls.Count == 2 && input.Calls[1].Length == 1 && input.Calls[1][0].Data.Keyboard.Flags == 6, "Partial input only releases character; no text retry or clipboard fallback"); }
            using var whisper = new WhisperTranscriptionService();
            var verifier = new VerifiedModel(root);
            try { using var rejected = verifier.Open(Path.Combine(root, "missing.bin")); throw new Exception("Missing model accepted"); }
            catch (InvalidOperationException) { Check(true, "Missing model gives actionable error"); }
            var invalid = Path.Combine(root, "invalid.bin");
            var damagedModel = new byte[2048]; BitConverter.GetBytes(0x67676d6c).CopyTo(damagedModel, 0); File.WriteAllBytes(invalid, damagedModel);
            try { using var rejected = verifier.Open(invalid); throw new Exception("Invalid model accepted"); }
            catch (InvalidDataException) { Check(true, "Valid GGML header cannot bypass SHA-256 verification"); }
            try { using var rejected = verifier.Open(Path.Combine(root, "..", "outside.bin")); throw new Exception("Outside path accepted"); }
            catch (InvalidOperationException) { Check(true, "Model outside application data directory rejected"); }
            Check(new AppSettings().ModelPath.StartsWith(VerifiedModel.DirectoryPath + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase), "Default model lives in application data");
            Check(Whisper.net.LibraryLoader.RuntimeOptions.RuntimeLibraryOrder.SequenceEqual(new[] { Whisper.net.LibraryLoader.RuntimeLibrary.Cpu }), "Whisper runtime is CPU only");
            whisper.Configure(new AppSettings { TranscriptionMode = "Fake (test only)", ModelPath = "" });
            Check(whisper.TranscribeAsync(new byte[100], CancellationToken.None).GetAwaiter().GetResult().StartsWith("um "), "Explicit fake provider works without a model");
            if (args.Length == 2)
            {
                whisper.Configure(new AppSettings { ModelPath = Path.GetFullPath(args[0]) });
                var modelCopy = Path.Combine(root, "verified-copy.bin");
                File.Copy(args[0], modelCopy);
                using (var lease = verifier.Open(modelCopy))
                {
                    try { using var write = File.OpenWrite(modelCopy); throw new Exception("Verified model permitted writes"); }
                    catch (IOException) { Check(true, "Verified model is protected against replacement during use"); }
                }
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(3));
                var recognized = whisper.TranscribeAsync(File.ReadAllBytes(args[1]), timeout.Token).GetAwaiter().GetResult();
                Check(recognized.Contains("country", StringComparison.OrdinalIgnoreCase) && recognized.Contains("ask", StringComparison.OrdinalIgnoreCase), "Real native Whisper recognizes the public Kennedy speech fixture");
                try { using var write = File.OpenWrite(args[0]); throw new Exception("Cached model allowed writes"); }
                catch (IOException) { Check(true, "Cached model remains protected between recordings"); }
                var again = whisper.TranscribeAsync(File.ReadAllBytes(args[1]), timeout.Token).GetAwaiter().GetResult();
                Check(again.Contains("country", StringComparison.OrdinalIgnoreCase), "Retained model supports another fresh dictation");
                whisper.Configure(new AppSettings { TranscriptionMode = "Fake (test only)" });
                using (var writable = new FileStream(args[0], FileMode.Open, FileAccess.Read, FileShare.None))
                    Check(true, "Changing provider releases cached model file");
                Console.WriteLine("Whisper fixture result: " + recognized);
            }
            var cleanup = new TextCleanupService();
            Check(cleanup.Clean("um hello   there i am here") == "Hello there I am here.", "Filler, whitespace, capitalization and punctuation");
            Check(cleanup.Clean("hello! how are you?") == "Hello! How are you?", "Sentence boundaries and existing punctuation");
            Check(cleanup.Clean("umbrella ultimate") == "Umbrella ultimate.", "Filler matching preserves words");
            Check(cleanup.Clean("um uh erm") == "", "Empty result after filler removal");
            var rules = new[] { new DictionaryEntry(Guid.NewGuid(), "jolty", "Jolti"), new DictionaryEntry(Guid.NewGuid(), "fox valley", "FVTC"), new DictionaryEntry(Guid.NewGuid(), "fox", "Fox"), new DictionaryEntry(Guid.NewGuid(), "Jolti", "Other") };
            Check(DictionaryCorrections.Apply("JOLTY, fox valley! foxy.", rules) == "Jolti, FVTC! foxy.", "Dictionary matches whole words, longest phrase and does not cascade");
            Check(DictionaryCorrections.Apply("c++ and anne", new[] { new DictionaryEntry(Guid.NewGuid(), "c++", "C++"), new DictionaryEntry(Guid.NewGuid(), "anne", "$Ann") }) == "C++ and $Ann", "Dictionary treats punctuation and replacement characters literally");
            var storage = new LocalStorage(root);
            var defaults = storage.Load();
            Check(!defaults.SaveHistory && defaults.Hotkey == "Ctrl + Win", "Privacy defaults");
            storage.Save(new AppSettings { Hotkey = "Ctrl + Alt", SaveHistory = true });
            Check(storage.Load().SaveHistory && storage.Load().Hotkey == "Ctrl + Alt", "Settings round trip");
            IHistoryRepository history = storage;
            var first = new HistoryEntry(Guid.NewGuid(), DateTimeOffset.Now, "raw", "Final.");
            var second = first with { Id = Guid.NewGuid(), RawTranscript = "second" };
            history.Add(first); history.Add(second);
            Check(history.Load().Count == 2 && history.Load()[0].Id == second.Id, "History persistence and newest-first ordering");
            history.Delete(first.Id);
            Check(history.Load().Single().Id == second.Id, "Individual history deletion");
            File.WriteAllText(Path.Combine(root, "history.json"), "broken");
            history.Clear();
            Check(history.Load().Count == 0 && File.Exists(Path.Combine(root, "settings.json")), "Clear damaged history without deleting settings");
            using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
            try { new FakeTranscriptionService().TranscribeAsync(new byte[100], cancellation.Token).GetAwaiter().GetResult(); throw new Exception("Cancellation ignored"); }
            catch (OperationCanceledException) { Check(true, "Transcription cancellation"); }

            // Load shared styles without constructing App, whose startup owns real hardware and tray services.
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            application.Resources = (ResourceDictionary)Application.LoadComponent(new Uri("/Jolti;component/Views/Styles.xaml", UriKind.Relative));
            var fakeAudio = new TestAudio(); var fakeHotkey = new TestHotkey(); var fakePaster = new TestPaster();
            var fakeSound = new TestSound();
            storage.Save(new AppSettings { TranscriptionMode = "Fake (test only)" });
            using var model = new MainViewModel(fakeAudio, new FakeTranscriptionService(), cleanup, fakePaster, fakeHotkey, storage, history, fakeSound);
            model.Initialize();
            var originalModelPath = model.ModelPath;
            foreach (var choice in model.AvailableModels)
            {
                model.SelectedModel = choice;
                Check(model.ModelPath == choice.Path && model.SelectedModel == choice, "Model selection updates the installed path: " + choice.Name);
            }
            Check(storage.Load().ModelPath == originalModelPath, "Model selection remains unsaved until Save settings");
            model.SaveSettingsCommand.Execute(null);
            Check(storage.Load().ModelPath == model.SelectedModel!.Path, "Selected model persists when settings are saved");
            model.ModelPath = originalModelPath;
            model.SaveSettingsCommand.Execute(null);
            var savedHotkey = model.ActiveHotkey;
            model.SelectedHotkey = "Ctrl + Alt";
            Check(model.ActiveHotkey == savedHotkey && model.SettingsNote.Contains("unsaved"), "Draft shortcut does not misrepresent active hotkey");
            model.SelectedHotkey = savedHotkey;
            Check(model.SettingsNote.Contains("up to date"), "Reverting edits clears unsaved settings notice");
            fakeHotkey.Press(); Check(model.Status == "Recording" && fakeAudio.Recording, "Press starts capture");
            fakeHotkey.Press(); Check(fakeAudio.Starts == 1, "Repeated press does not overlap capture");
            Check(fakeSound.Plays == 1, "Start cue plays once after capture starts");
            fakeHotkey.Release(); PumpUntil(() => model.CanEdit);
            Check(model.Status == "Text inserted" && fakePaster.Text == "This is a jolti test transcript.", "Release transcribes, cleans and pastes");
            Check(fakeSound.EndPlays == 1, "End cue plays once after hotkey release");
            Check(history.Load().Count == 0, "History disabled means no transcript file");
            fakeSound.Fail = true;
            fakeHotkey.Press();
            Check(model.Status == "Recording" && model.Message.Contains("recording continues"), "Playback failure does not interrupt recording");
            fakeHotkey.Release(); PumpUntil(() => model.CanEdit); fakeSound.Fail = false;
            fakeHotkey.Press(); fakeHotkey.Release(); model.CancelCommand.Execute(null); PumpUntil(() => model.CanEdit);
            Check(model.Status == "Idle" && model.RawTranscript == "", "Cancel processing prevents a new result and paste");
            model.SaveHistory = true; model.CleanupEnabled = false; model.RecordingSoundEnabled = false; model.SaveSettingsCommand.Execute(null);
            Check(model.SettingsNote.Contains("up to date") && model.HistoryNote.StartsWith("New transcripts"), "Saved preferences refresh settings and history guidance");
            var soundCount = fakeSound.Plays;
            fakeHotkey.Press(); fakeHotkey.Release(); PumpUntil(() => model.CanEdit);
            Check(fakeSound.Plays == soundCount && !storage.Load().RecordingSoundEnabled, "Muted start cue is persisted and not played");
            Check(fakePaster.Text.StartsWith("um ") && history.Load().Count == 1, "Cleanup toggle and opt-in history");
            fakePaster.Fail = true; fakeHotkey.Press(); fakeHotkey.Release(); PumpUntil(() => model.CanEdit);
            Check(model.Status == "Error" && model.FinalText.Length > 0, "Paste failure preserves recoverable text");
            fakeAudio.Fail = true; fakeHotkey.Press();
            Check(model.Status == "Error" && model.CanEdit, "Capture startup error recovers");
            model.ClearHistoryCommand.Execute(null);
            Check(model.FinalText == "" && history.Load().Count == 0 && model.History.Count == 0, "Delete all clears disk and current result");

            model.PreferredSpelling = "Jolti"; model.HeardWord = "jolti";
            model.SaveWordCommand.Execute(null);
            Check(storage.LoadDictionary().Single().Spelling == "Jolti", "Dictionary saves independently of settings");
            model.HeardWord = "JOLTI"; model.PreferredSpelling = "Duplicate"; model.SaveWordCommand.Execute(null);
            Check(storage.LoadDictionary().Count == 1 && model.Status == "Error", "Duplicate heard words rejected without overwriting");
            fakeAudio.Fail = false; fakePaster.Fail = false;
            fakeHotkey.Press(); fakeHotkey.Release(); PumpUntil(() => model.CanEdit);
            Check(model.RawTranscript.Contains("jolti") && fakePaster.Text.Contains("Jolti"), "Dictionary applies with cleanup off and preserves raw transcript");
            model.ClearHistoryCommand.Execute(null);
            Check(storage.LoadDictionary().Count == 1, "Deleting history preserves dictionary");
            model.SelectedWord = model.DictionaryEntries.Single();
            model.PreferredSpelling = "JOLTI"; model.SaveWordCommand.Execute(null);
            Check(storage.LoadDictionary().Single().Spelling == "JOLTI", "Selected dictionary entry can be edited");
            model.SelectedWord = model.DictionaryEntries.Single(); model.DeleteWordCommand.Execute(null);
            Check(storage.LoadDictionary().Count == 0, "Dictionary entry deletion persists");
            model.PreferredSpelling = "Jolti"; model.HeardWord = "jolty"; model.SaveWordCommand.Execute(null);

            var snippets = new[] {
                new SnippetEntry(Guid.NewGuid(), "my link", "https://example.org/$meet"),
                new SnippetEntry(Guid.NewGuid(), "my link signature", "Best,\n  Jolti\nmy link") };
            Check(SnippetExpansion.Apply("MY LINK.", snippets) == snippets[0].Expansion, "Standalone snippet drops transcription punctuation and preserves literal URL");
            Check(SnippetExpansion.Apply("my link signature!", snippets) == snippets[1].Expansion, "Longest snippet preserves multiline text and does not cascade");
            Check(SnippetExpansion.Apply("Use my link, not my links or xmy link.", snippets) == "Use https://example.org/$meet, not my links or xmy link.", "Inline snippets respect whole words and preserve surrounding punctuation");
            Check(SnippetExpansion.Apply("Unchanged.", Array.Empty<SnippetEntry>()) == "Unchanged.", "Empty snippet library leaves dictation unchanged");
            model.SnippetTrigger = "jolti test transcript"; model.SnippetText = "Saved\n  $Text";
            model.SaveSnippetCommand.Execute(null);
            Check(storage.LoadSnippets().Single().Expansion == "Saved\n  $Text", "Snippet storage round trip preserves whitespace");
            model.SnippetTrigger = "JOLTI TEST TRANSCRIPT"; model.SnippetText = "Duplicate"; model.SaveSnippetCommand.Execute(null);
            Check(model.Status == "Error" && storage.LoadSnippets().Count == 1, "Duplicate snippet trigger rejected without overwriting");
            model.NewSnippetCommand.Execute(null); model.SnippetText = " "; model.SaveSnippetCommand.Execute(null);
            Check(model.Status == "Error" && storage.LoadSnippets().Count == 1, "Empty snippet fields rejected");
            model.SnippetTrigger = "too long"; model.SnippetText = new string('x', 4001); model.SaveSnippetCommand.Execute(null);
            Check(model.Status == "Error" && storage.LoadSnippets().Count == 1, "Oversized snippet rejected");
            model.SelectedSnippet = model.Snippets.Single(); model.SnippetText = "Updated\n  $Text"; model.SaveSnippetCommand.Execute(null);
            Check(storage.LoadSnippets().Single().Expansion == "Updated\n  $Text", "Selected snippet can be edited");
            fakeHotkey.Press();
            Check(!model.SaveSnippetCommand.CanExecute(null) && !model.NewSnippetCommand.CanExecute(null), "Snippet editing disabled during recording");
            fakeHotkey.Release(); PumpUntil(() => model.CanEdit);
            Check(fakePaster.Text.Contains("Updated\n  $Text") && model.RawTranscript.Contains("jolti test transcript") && history.Load()[0].FinalText == fakePaster.Text, "Snippets expand with cleanup off before insertion and history, preserving raw speech");
            model.CleanupEnabled = true; model.SaveSettingsCommand.Execute(null);
            fakeHotkey.Press(); fakeHotkey.Release(); PumpUntil(() => model.CanEdit);
            Check(fakePaster.Text.Contains("Updated\n  $Text"), "Cleanup does not alter saved snippet formatting");
            model.ClearHistoryCommand.Execute(null);
            Check(storage.LoadSnippets().Count == 1, "Deleting history preserves snippets");
            using (var reloaded = new MainViewModel(new TestAudio(), new FakeTranscriptionService(), cleanup, new TestPaster(), new TestHotkey(), storage, history))
            {
                reloaded.Initialize();
                Check(reloaded.Snippets.Single().Expansion == "Updated\n  $Text", "Snippets reload at startup");
            }
            model.SelectedSnippet = model.Snippets.Single(); model.DeleteSnippetCommand.Execute(null);
            Check(storage.LoadSnippets().Count == 0 && model.SnippetTrigger == "" && model.SnippetText == "", "Snippet deletion persists and clears editor");
            model.SnippetTrigger = "my signature"; model.SnippetText = "Best regards,\nJolti"; model.SaveSnippetCommand.Execute(null);

            // Render only our own unshown WPF visual tree, never the user's screen or other windows.
            var window = new MainWindow(model);
            var content = (FrameworkElement)window.Content;
            var tabs = (TabControl)window.FindName("Tabs");
            for (var i = 0; i < tabs.Items.Count; i++)
            {
                tabs.SelectedIndex = i;
                content.Measure(new Size(884, 700)); content.Arrange(new Rect(0, 0, 884, 700)); content.UpdateLayout();
                var bitmap = new RenderTargetBitmap(884, 700, 96, 96, PixelFormats.Pbgra32); bitmap.Render(content);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var output = File.Create(Path.Combine(root, $"page-{i}.png")); encoder.Save(output);
            }
            Check(true, "All six WPF pages load and render");
            // Review populated content as well as empty pages at the new default window size.
            typeof(MainViewModel).GetProperty(nameof(MainViewModel.FinalText))!.SetValue(model, "Let's make room for the next great idea. I'll send the notes after our meeting.");
            typeof(MainViewModel).GetProperty(nameof(MainViewModel.RawTranscript))!.SetValue(model, "um let's make room for the next great idea i'll send the notes after our meeting");
            model.History.Add(new HistoryEntry(Guid.NewGuid(), DateTimeOffset.Now, model.RawTranscript, model.FinalText));
            foreach (var page in new[] { 0, 1, 2 })
            {
                tabs.SelectedIndex = page;
                content.Measure(new Size(1064, 740)); content.Arrange(new Rect(0, 0, 1064, 740)); content.UpdateLayout();
                if (page == 1 && tabs.SelectedContent is DockPanel settingsPanel)
                {
                    settingsPanel.Children.OfType<ScrollViewer>().Single().ScrollToBottom();
                    content.UpdateLayout();
                }
                var preview = new RenderTargetBitmap(1064, 740, 96, 96, PixelFormats.Pbgra32); preview.Render(content);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(preview));
                using var output = File.Create(Path.Combine(root, "detail-" + page + ".png")); encoder.Save(output);
            }
            // Preview the overlay's visual states without showing or capturing any desktop window.
            var indicator = new RecordingIndicator(model);
            foreach (var state in new[] { "Idle", "Recording", "Transcribing", "Text inserted", "Error" })
            {
                typeof(MainViewModel).GetProperty(nameof(MainViewModel.Status))!.SetValue(model, state);
                var visual = (FrameworkElement)indicator.Content;
                visual.Measure(new Size(indicator.Width, indicator.Height)); visual.Arrange(new Rect(0, 0, indicator.Width, indicator.Height)); visual.UpdateLayout();
                var until = DateTime.UtcNow.AddMilliseconds(550);
                PumpUntil(() => DateTime.UtcNow >= until);
                var preview = new RenderTargetBitmap((int)indicator.Width * 2, (int)indicator.Height * 2, 192, 192, PixelFormats.Pbgra32); preview.Render(visual);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(preview));
                using var output = File.Create(Path.Combine(root, "indicator-" + state + ".png")); encoder.Save(output);
            }
            indicator.Close();
            var hiddenToTray = false;
            var windowClosed = false;
            window.HiddenToTray += (_, _) => hiddenToTray = true;
            window.Closed += (_, _) => windowClosed = true;
            window.Close();
            Check(hiddenToTray && !windowClosed && !window.IsVisible, "Window close hides to tray without destroying the window");
            Check(Application.Current.ShutdownMode == ShutdownMode.OnExplicitShutdown, "Application lifetime remains explicit");
            Console.WriteLine($"PASS: {_checks} checks. UI renders: {root}");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static void Check(bool condition, string name) { if (!condition) throw new Exception(name); _checks++; Console.WriteLine("PASS " + name); }
    private static void PumpUntil(Func<bool> done)
    {
        var frame = new DispatcherFrame();
        var deadline = DateTime.UtcNow.AddSeconds(5);
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(10) };
        timer.Tick += (_, _) => { if (done() || DateTime.UtcNow > deadline) frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame); timer.Stop();
        if (!done()) throw new TimeoutException("Workflow did not finish");
    }
    private sealed class TestAudio : IAudioRecorder
    {
        public bool Recording, Fail; public int Starts;
        public event Action<Exception>? Failed { add { } remove { } }
        public IReadOnlyList<Microphone> GetMicrophones() => [new(-1, "Test microphone")];
        public void Start(int id) { if (Fail) throw new IOException("Test device failure"); Recording = true; Starts++; }
        public Task<byte[]> StopAsync() { Recording = false; return Task.FromResult(new byte[100]); }
        public void Dispose() { }
    }
    private sealed class TestSound : IRecordingSoundService
    {
        public int Plays;
        public int EndPlays;
        public bool Fail;
        public Task PlayStartAsync(CancellationToken token) { Plays++; if (Fail) throw new IOException("Test speaker failure"); return Task.CompletedTask; }
        public Task PlayEndAsync(CancellationToken token) { EndPlays++; if (Fail) throw new IOException("Test speaker failure"); return Task.CompletedTask; }
    }
    private sealed class TestInput : ITextInputBackend
    {
        public bool Focused = true, Partial;
        public List<NativeMethods.INPUT[]> Calls = [];
        public bool ModifiersDown() => false;
        public bool IsTargetFocused(nint target) => Focused;
        public uint Send(NativeMethods.INPUT[] inputs) { Calls.Add(inputs); return Partial ? 1u : (uint)inputs.Length; }
    }
    private sealed class TestHotkey : IHotkeyService
    {
        public event Action? Pressed; public event Action? Released;
        public void Press() => Pressed?.Invoke(); public void Release() => Released?.Invoke();
        public void Configure(string key) { } public void Dispose() { }
    }
    private sealed class TestPaster : ITextPaster
    {
        public string Text = ""; public bool Fail;
        public nint CaptureTarget() => 123;
        public void Copy(string text) => Text = text;
        public Task PasteAsync(string text, nint target, CancellationToken token) { if (Fail) throw new IOException("Test paste failure"); Text = text; return Task.CompletedTask; }
    }
}
