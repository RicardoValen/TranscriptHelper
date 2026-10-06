# Transcript Helper — Windows audio demo

Standalone C# / WPF app with a movable, resizable, always-on-top translucent black window. The original domain logic remains independent of the UI and audio integration.

## Start on Windows

Install the .NET 8 SDK. Extract this archive, open PowerShell in `TranscriptHelper`, and run:

```powershell
dotnet run --project src/TranscriptHelper.Windows
```

NuGet access is required to restore NAudio 2.2.1 and Azure Speech SDK 1.40.0. Windows desktop audio devices are required. This archive contains source, not a compiled executable.

## Audio test without credentials

1. Select the same output device used for Teams speakers/headphones.
2. Enable the default microphone checkbox to capture your own voice.
3. Leave the Speech key and region empty and select **Start listening**.
4. Play audio through that output; the Meeting audio meter should move. Speak; the You meter should move.
5. Select **Stop** to release capture. No raw audio is saved.

Speaker capture includes all sound played through the chosen output, not just Teams. If Teams uses a different device, change the selected device. Restart capture after changing devices. Mono and stereo formats are supported; surround outputs should be switched to stereo. Microphone capture uses Windows' default input device. Use headphones to reduce acoustic echo and duplicate text.

## Live transcription

Create an Azure AI Speech resource and provide its key and region in the app, or set them before launching:

```powershell
$env:AZURE_SPEECH_KEY = '<your resource key>'
$env:AZURE_SPEECH_REGION = '<your resource region>'
dotnet run --project src/TranscriptHelper.Windows
```

Set the recognition language (default `en-US`). With both credentials present, **Start listening** streams the selected speaker output and optional microphone to Azure Speech. Service usage can incur charges. The app displays interim recognition and appends finalized segments to its in-memory transcript with receipt timestamps. Export text saves a JSON snapshot using a Save dialog. Credentials are not stored in files or included in export.

The two streams are labeled **You** and **Meeting audio**. The latter combines all remote participants; the app does not identify their individual names or distinguish their voices. Timestamps reflect arrival of recognition results, not sample-accurate synchronized speech times. The transcript remains in memory across stop/start and is lost on exit unless exported. No automatic disk logging is implemented.

## Architecture

- `TranscriptHelper.Core`: sessions, transcript records, snapshots, insights, and AI-provider interface. Platform-independent.
- `TranscriptHelper.Audio`: Windows microphone and WASAPI loopback capture; streaming PCM conversion; optional Azure speech recognition. Does not depend on UI or Core.
- `TranscriptHelper.Windows`: WPF overlay, device selection, controls, transcript display, and export. Connects audio events to Core.
- `TranscriptHelper.Console`: original typed transcript demo remains available.
- `TranscriptHelper.Checks`: regression checks for Core.

Copilot assistance is not connected yet. Azure Speech handles speech-to-text; it is separate from Copilot. The existing `IMeetingAssistant` contract remains the integration point for later meeting assistance. The window is visible in normal screen sharing; click-through and hide/show hotkeys are not implemented.

## Validate and publish on Windows

```powershell
dotnet build src/TranscriptHelper.Windows
dotnet run --project checks/TranscriptHelper.Checks
dotnet publish src/TranscriptHelper.Windows -c Release -r win-x64 --self-contained true -o publish
```

Launch `publish/TranscriptHelper.Windows.exe`. Keep the entire publish folder together; the Speech SDK includes native runtime dependencies.

## Validation status

Project and XAML XML parsed, project references checked, event-handler wiring checked, and archive verified. The creation environment is Linux and has no .NET SDK, so compilation, WPF layout, microphone/loopback capture, NuGet restore, and Azure recognition have **not** been tested. Run the validation commands and device test above on Windows before demonstrating.

Implementation references:
- https://github.com/naudio/NAudio/blob/v2.2.1/Docs/WasapiLoopbackCapture.md
- https://learn.microsoft.com/en-us/azure/ai-services/speech-service/how-to-use-audio-input-streams

## Screenshot-inspired overlay update

The desktop UI now uses the supplied screenshot as its visual reference: floating pill toolbar, translucent charcoal panel, rounded borders, blue mode badge, action row, and bottom prompt field. Audio configuration is in a collapsible section. Hide/Show collapses the panel into the toolbar without stopping capture; Stop ends capture. Ctrl+H toggles the panel and Ctrl+Enter submits the prompt while the app has focus. These are not system-wide hotkeys.

Assist displays the live transcript. Recap and prompt submission use the explicit demo assistant, which returns transcript excerpts. Suggested replies and follow-up questions show a clear unavailable message until a real AI provider is connected. No screenshot/screen-content capture is implemented.

This is a native WPF implementation; its rendered appearance has not been inspected on Windows.
