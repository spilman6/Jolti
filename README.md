# Jolti

A private, local Windows voice dictation app built with C# / .NET 8 / WPF. Hold **Ctrl + Win**, speak, and release to transcribe with local Whisper, clean up the text, and type it directly into the focused app without touching your clipboard. No audio leaves your device.

## Build and run

1. If the previous version is running, right-click its system tray icon and choose **Exit**. Closing its window only hides it.
2. On a fresh clone, install the .NET 8 SDK and publish first. Generated builds and model weights are not included in Git:

```powershell
dotnet publish src/Jolti/Jolti.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish/jolti-settings-spacing-win-x64
.\Start-Jolti.ps1
```

Or launch `artifacts/publish/jolti-settings-spacing-win-x64/Jolti.exe` directly. Keep the entire publish folder together. It includes .NET 8 and the native CPU runtime. Download a model in Settings before your first dictation.

3. Open **Settings**, choose Base, Small, or Medium English and click **Download selected model** if needed, then choose **Local Whisper** under Transcription mode, and click **Save settings**. Existing settings preserve your old Fake mode until you change it. The model path defaults to `%LOCALAPPDATA%\Jolti\models\ggml-base.en.bin`. Choose a model from the Whisper model dropdown to update an older saved path. Paths outside this folder are rejected.
4. Focus an ordinary text field in Notepad. Hold **Ctrl + Win**, speak clearly, and release both keys. Remain in that app while processing. Your actual words should appear.

The first transcription loads the model. CPU processing can take several seconds depending on your hardware and recording length. **Cancel transcription** stops processing without sending a paste; processing also times out after five minutes. Native model loading must finish before cancellation takes effect.

Manual **Start in 3 seconds** and the tray's **Start/Stop Dictation** let you record without holding keys. Focus your destination during the countdown and stop through the tray. Sessions stop after two minutes. Closing the window hides it; use tray **Exit** to quit.

By default, local voice activity detection also stops a recording after speech has begun and 1.5 seconds of silence. It uses only the captured PCM level, runs in memory, and can be disabled in Settings. Initial silence does not end a recording, and manual/hotkey stop still works.

You can also press and hold the small status pill above the primary taskbar to dictate, then release the mouse button to transcribe. The pill does not activate Jolti, so keep the destination text field focused before pressing it. Releasing outside the pill still stops recording. The tray and hotkey controls continue to work.
Double-click the pill to open Settings. A brief mouse hold threshold distinguishes dictation from a double-click; a quick single click does nothing.

Spoken editing commands are enabled by default and can be disabled when you need to dictate the command words literally. Supported commands are `comma`, `period`/`full stop`, `question mark`, `exclamation point`/`exclamation mark`, `colon`, `semicolon`, `new line`, `new paragraph`, `bullet point`/`bullet item`, `numbered item`, `end list`, and `scratch that`/`delete that`/`undo that`. Backtracking removes the current thought back to the previous sentence or line boundary. Raw transcript history retains the words Whisper heard; commands affect the final inserted text.

## Requirements and source build

- Windows 10/11 x64, microphone, and desktop microphone access enabled under Windows Settings > Privacy & security > Microphone.
- The portable build includes the .NET 8 runtime. Building from source requires the .NET 8 SDK or a newer SDK capable of targeting .NET 8. Framework-dependent execution also requires the .NET 8 Windows Desktop Runtime.
- Whisper native libraries may require the [Microsoft Visual C++ x64 Redistributable](https://learn.microsoft.com/en-us/cpp/windows/latest-supported-vc-redist). The bundled CPU runtime requires a compatible CPU; no GPU is needed.
- NuGet dependencies: NAudio 2.2.1, Microsoft.Extensions.DependencyInjection 8.0.1, Whisper.net 1.9.1, and Whisper.net.Runtime 1.9.1. Restore and explicit model downloads need network access; transcription works offline.

```powershell
dotnet restore Jolti.sln
dotnet build Jolti.sln -c Release --no-restore
dotnet run --project src/Jolti -c Release --no-build
```

Source and portable runs use models in AppData. Download Base (148 MB), Small (488 MB), or Medium (1.53 GB) English from Settings, then save settings. Downloads show progress, support cancellation, and verify SHA-256 before installation. Larger models need more memory and CPU time. Install an already-downloaded model with `./scripts/Install-WhisperModel.ps1 -Source ./artifacts/models/ggml-base.en.bin`. The installer is offline and checks SHA-256 before and after copying.

To obtain the English model again, explicitly run:

```powershell
.\scripts\Get-WhisperModel.ps1
```

This downloads about 148 MB from the [whisper.cpp model repository](https://huggingface.co/ggerganov/whisper.cpp) and verifies SHA-256 before accepting it. No audio or transcripts are sent. Settings also offers explicit model downloads. There is no automatic model acquisition or cloud fallback.

```powershell
dotnet publish src/Jolti/Jolti.csproj -c Release -r win-x64 --self-contained true -o artifacts/publish/jolti-settings-spacing-win-x64
.\scripts\Install-WhisperModel.ps1 -Source artifacts/models/ggml-base.en.bin
```

## Structure

```text
src/Jolti/
  Core/             Models and service interfaces
  Infrastructure/   Atomic JSON storage and narrow Win32 interop
  Services/         Audio, hotkeys, paste, cleanup, Whisper/test routing
  ViewModels/       MVVM state, commands, recording workflow, cancellation
  Views/            Main window, model picker, floating indicator
  App.xaml.cs       Dependency injection, lifecycle, tray
scripts/            Explicit, checksum-verified model acquisition
tests/Jolti.Checks/  Executable regression checks and optional real inference test
```

Interfaces include IAudioRecorder, ITranscriptionService, IConfigurableTranscriptionService, ITextCleanupService, ITextPaster, IHotkeyService, IHistoryRepository, and ISettingsStore. Whisper model loading/inference run off the UI thread. Each session uses a fresh processor with prior context disabled. Settings apply only when saved and cannot be edited during a session. Missing/wrong models are rejected before microphone capture. Native load, microphone, transcription, and paste errors remain visible and recoverable.

Fake mode remains explicitly available for regression testing and returns a fixed sentence. Failures never switch providers silently. Future providers can implement the same transcription interface without rewriting the recording or paste UI; any cloud provider would require explicit selection and disclosure.

## Privacy

- Microphone capture is explicit and indicated by the floating status window. Audio is 16 kHz mono 16-bit WAV held in memory. Recordings are never saved, including when transcript history is enabled. Buffers are cleared where practical after processing. OS paging and managed memory do not provide secure-erasure guarantees.
- Settings, including the model path, live in `%LOCALAPPDATA%\Jolti\settings.json`. Optional raw/final transcript history lives beside it in `history.json`. History is **off by default**. Files are plain JSON protected by your Windows account, outside this source tree even if the source is in OneDrive.
- Delete All History deletes saved entries and the temporary history file, and clears the current result. It does not securely erase recovery data or backups, clear the clipboard, delete model files, or remove text from other apps. Disabling history stops future saves but keeps older entries.
- No screenshots, screen contents, window titles/text, accessibility-tree inspection, typed characters, password inspection, clipboard reading/history access, telemetry, accounts, sync, or automatic sharing.
- Only modifier states are polled for push-to-talk and safe pasting. There is no keyboard hook or character-key logging. The original window handle and process ID are used transiently to check the destination.
- Dictation never reads or changes the clipboard and has no clipboard fallback. Only explicit Copy buttons replace the previous clipboard without reading/restoring it. Windows clipboard-history/cloud exclusion flags are set; third-party clipboard managers may ignore them. Other apps can still read the current clipboard.
- Direct insertion uses one SendInput Unicode sequence after checking focus and waiting for modifiers to be released. It preserves UTF-16 characters, including surrogate pairs, and does not send Ctrl+V. It never forces focus or bypasses Windows privilege boundaries.
- Whisper uses only the CPU runtime and local models under `%LOCALAPPDATA%\Jolti\models`. Model files and directory ancestors cannot be symbolic links or junctions. SHA-256 is verified once per cached model; the model remains opened with read-only sharing throughout inference to prevent ordinary writes/replacement. Network requests occur only for explicitly requested model downloads from Hugging Face. No audio or transcripts are uploaded.

## Known limitations

- Only the English base.en model matching the pinned SHA-256 digest is currently accepted. Additional models require a reviewed digest in code. User-editable checksum sidecars are not trusted. GGUF and unrelated formats are unsupported.
- Whisper may misrecognize speech or hallucinate words during silence/background noise. Review important text. Cleanup uses English rules for punctuation, capitalization, standalone i, and um/uh/erm; it is not a grammar model and can be disabled.
- Hotkeys are Ctrl+Win, Ctrl+Alt, Ctrl+Shift, or Alt+Shift. Modifier gestures are not suppressed and may conflict with Windows/accessibility/language shortcuts. Arbitrary character-key shortcuts are unsupported.
- Keep the original destination focused. Focus changes cause a recoverable error; Copy final text is available. Caret/control changes within the same window cannot be detected without inspecting its content. There is a small unavoidable focus race immediately before foreground input.
- Text inserted means Windows accepted the input events, not that the destination confirmed insertion. Elevated apps, secure desktops, games, remote sessions, and apps that reject Unicode simulated input may require manual pasting. Password fields are not inspected or identified: select an ordinary text field before recording.
- Microphone indices may change after reconnecting devices. Refresh/reselect if needed. Devices that reject 16 kHz capture produce an error.
- The verified model stays loaded between recordings for speed; each recording uses a fresh processor with prior context disabled. There is no streaming transcription, GPU acceleration, model manager, installer, auto-update, or auto-start.
- The indicator is a small navy pill centered above the primary screen taskbar. The capsule is 40 by 8 at rest and smoothly grows to 80 by 32 while recording, with thicker activity bars reaching 24 units tall, then shrinks on release over 180 ms. Its bottom edge stays anchored, with rounded ends and no text labels. With Windows animations disabled, size changes are immediate. The dot is red during recording, yellow during transcription, and green after insertion. There are no hover effects. Matching red bars animate during recording and yellow bars pulse during transcription. This is a decorative activity pattern, not an audio-level meter. It remains click-through, never takes focus, and respects the Windows client-area animation preference.

## Validation

The updated Release build passes with 0 warnings and 0 errors. All 34 checks passed on the bundled .NET 8 runtime, including native Whisper recognition of the public speech fixture, cancellation, and the existing workflow regression checks. The English model's SHA-256 matches the upstream metadata.

```powershell
dotnet run --project tests/Jolti.Checks -c Release
```

Tests use isolated files under `artifacts/checks`, fake microphone/hotkey/paste adapters, and offscreen rendering of Jolti's own visual tree. They never inspect the desktop or use the real microphone/clipboard. Coverage includes privacy defaults, JSON storage, history deletion, cleanup, hold/release, failure recovery, cancellation, provider routing, and invalid/missing model errors.

The optional native integration test accepts a model and the public JFK sample from [whisper.cpp](https://github.com/ggml-org/whisper.cpp/tree/master/samples):

```powershell
dotnet run --project tests/Jolti.Checks -c Release -- "$env:LOCALAPPDATA/Jolti/models/ggml-base.en.bin" artifacts/models/kennedy.wav
```

The self-contained validation runner is at `artifacts/direct-input-checks/Jolti.Checks.exe`. It can accept those same two arguments without a separate .NET installation.

The previous real microphone-to-paste workflow was manually confirmed. This update additionally verifies actual native Whisper inference against the public speech sample; real microphone accuracy remains dependent on your device and environment.

## References

- [Whisper.net implementation and examples](https://github.com/sandrohanea/whisper.net)
- [SendInput restrictions](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput)
- [NAudio](https://github.com/naudio/NAudio)

Direct input preserves the clipboard so you can dictate and then press Ctrl+V to paste what you copied earlier. Some controls may ignore Unicode newlines or other characters. A partial insertion is reported without retrying text; review the destination before retrying to avoid duplicates. Copy buttons still intentionally replace the clipboard.

Jolti's source paths, namespaces, and solution file (`src/Jolti`, `Jolti.sln`, `tests/Jolti.Checks`) match the Jolti name throughout. Settings, history, and the installed model live under `%LOCALAPPDATA%\Jolti`. The single-instance guard only prevents multiple Jolti instances from running together.

The supplied Jolti.png artwork is packaged in src/Jolti/Assets/Jolti.ico for the executable, window, and tray. Run ./scripts/Build-JoltiIcon.ps1 to regenerate its eight resolution variants after updating the source image.

The supplied bloop.mp3 is bundled under Assets and plays through the default Windows output device after microphone capture starts successfully. Settings > Play a bloop when recording starts is enabled by default; save settings after changing it. Playback is asynchronous, stops when capture ends, and a playback error does not stop dictation. The cue is local and makes no network requests. Speakers may feed the cue back into the microphone; headphones or muting the cue avoid this. No microphone audio is saved. Validation: 36 checks passed, including MP3 decoding, once-per-start behavior, playback failure recovery, and persistent muting; speaker playback itself requires a manual check.

## Jolti interface refresh

The main window uses Jolti's cyan/violet artwork, a navy navigation sidebar, rounded controls, and a dark navy workspace. Dictation shows the saved hotkey, latest cleaned result, and expandable original transcript. Cancel appears only during processing. Settings are grouped with a persistent Save button and unsaved-changes notice. History has an empty state, copy/delete actions, and expandable originals. Privacy is readable in the app, and Hide to tray is available directly in the sidebar. The compact recording indicator, sound, verified local model, clipboard-preserving input, and existing data locations remain unchanged.

Validation for this refresh: Release build succeeded with no warnings or errors; 39 checks passed. All pages were rendered and inspected at a smaller window size, plus populated result/history and scrolled settings at the default size. Native microphone and text delivery behavior was preserved; interactive desktop acceptance remains a manual check.

Closing with X or choosing **Hide to tray** keeps dictation running. A notification explains this on the first close. If the tray icon is hidden, open the Windows **^** menu beside the clock. Left-click Jolti to reopen it; right-click and choose **Exit** to quit.
Recording sounds: the bundled bloop.mp3 plays on both start and stop. The stop cue begins after microphone capture finishes. The Recording sounds setting controls both cues; bloop_end.mp3 is no longer bundled.
Settings > Mute playback while recording silences the current default Windows output device after the start cue finishes and restores its previous mute state when capture ends, fails, or Jolti exits. This setting is off by default. Playback remains audible for the brief start cue, which may enter the microphone through speakers. The stop cue plays after playback is restored. Apps keep playing in the background, so their playback position advances while muted. If you switch output devices mid-recording, the new device is not automatically muted.
Performance: model verification happens at launch and the read-only sharing lease remains held until the provider/model changes or Jolti exits. The first transcription loads native model weights; later dictations reuse them. This uses more idle RAM and prevents replacing the model file while Jolti is using it. Speech context is not reused.

## Personal dictionary

Open Dictionary, enter the preferred spelling (for example Jolti) and what Jolti hears (jolty), then choose Save word. Leave the heard field empty to normalize capitalization. Add separate entries for multiple mishearings. Select an existing entry to edit or delete it.

Corrections apply after optional cleanup and before insertion/history saving, including when cleanup is off. Matching ignores case, respects whole-word boundaries, and prefers longer phrases. Replacements are literal and do not cascade. Raw transcripts remain unchanged. This is a local correction list, not speech-model training; unlisted mishearings are not corrected automatically.

Up to 500 entries of 120 characters each are saved immediately in %LOCALAPPDATA%\Jolti\dictionary.json, independently of settings and transcript history. Delete All History preserves this dictionary. Remove entries on the Dictionary page.

## Voice snippets

Open **Snippets**, enter a phrase you will say (such as `my meeting link`) and the text to insert, then choose **Save snippet**. Select a saved snippet to edit or delete it; **New snippet** clears the editor. Changes apply immediately to future dictations.

Triggers match whole words and phrases, ignoring case, after cleanup and dictionary corrections. Longer triggers win and expansions do not trigger other snippets. Saying only a trigger, optionally followed by sentence-ending punctuation, inserts exactly the saved text. Inside a sentence, surrounding text and punctuation remain. Choose distinctive triggers and avoid dictionary corrections that change them.

Saved text preserves capitalization, spacing, tabs, and line breaks, including when cleanup is enabled. Destination apps may handle tabs or newlines differently. Up to 500 snippets, with triggers of 120 characters and expansions of 4,000 characters, are stored locally as plain JSON in `%LOCALAPPDATA%\Jolti\snippets.json`. No additional model or network access is used. Delete All History preserves snippets; delete them on the Snippets page. Optional history records the original transcript and the expanded final text.

Validation for snippets: Release build completed with zero warnings and errors; all 65 regression checks passed on the bundled .NET 8 runtime. All six pages rendered, and the Snippets page was visually inspected. Live microphone-to-target acceptance remains a manual check. The launcher now opens the snippets portable build; exit the previous tray instance before restarting.
