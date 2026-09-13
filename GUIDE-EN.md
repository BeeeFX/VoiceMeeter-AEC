# VoiceMeeter AEC — Windows x64 setup

[Français](GUIDE-FR.md) · Version 1.0.0 · Windows x64 · VoiceMeeter Potato

VoiceMeeter AEC attenuates speaker playback picked up by a microphone. It processes the microphone through **Voicemeeter Potato Insert Virtual ASIO**, before VoiceMeeter strip effects. VoiceMeeter retains control of the hardware. The app does not change routes, patch settings or Windows default devices.

## First setup

1. Extract the Windows release into a permanent folder and open **VoiceMeeter AEC.exe**. It is a self-contained Windows app; no PowerShell or .NET installation is needed.
2. Run VoiceMeeter Potato at **48 kHz** and configure its microphone and speaker output as usual.
3. In the app, select the VoiceMeeter column containing your microphone and one or more playback columns containing every sound played through your speakers. Their audio becomes the echo reference. Select the A bus connected to those speakers so Auto mode knows which route to watch. The app converts column names to Insert channels automatically.
4. Leave the microphone PATCH INSERT returns disabled and select **Start echo cancellation**. Open **Diagnostics** and confirm that the Insert driver reports 48 kHz and the `blocks` counter advances. The driver accepts one Insert client at a time.
5. In VoiceMeeter, open **Menu → System Settings / Options → PATCH INSERT**. At the PRE-FX insert point, enable left and right only for the microphone column. Leave every other return disabled.
6. In an application receiving your VoiceMeeter microphone bus, try **Mute**, **Bypass**, and **AEC on** from the app or tray menu. Keep the microphone out of your speaker routes.
7. Play speech through speakers, allow around 10–20 seconds for adaptation, and compare bypass/AEC at matching levels. Speak over the playback and check voice clarity. Some residual echo can remain.

**Disable the microphone PATCH INSERT returns before stopping the engine or updating the app.** If a fault leaves the microphone silent, disabling those returns restores VoiceMeeter's direct path. The app never changes them automatically.

## Reference: the sound to cancel

The reference must contain every playback source whose acoustic echo should be removed, and exclude the raw or processed microphone. Select as many playback columns as needed. The app combines the first stereo pair from each selected column into the reference sent to AEC.

For a simple configuration where all playback passes through **VAIO**, select only VAIO. If speaker audio is split across VAIO, AUX, VAIO3 or hardware strips, select every relevant column. Sources played directly outside VoiceMeeter cannot be included. Only the first left/right pair of each virtual strip is used; additional surround channels are not included.

The Insert signal is before effects and faders, not the final output-bus mix. Different effects, clipping or abrupt volume changes between reference and speakers can reduce cancellation.

## Channel map and Auto

Numbers start at 1 and refer to Potato Insert channels, not sound-card channels.

| Strip | Insert channels | Auto strip |
|---|---|---|
| IN1 | 1,2 | 1 |
| IN2 | 3,4 | 2 |
| IN3 | 5,6 | 3 |
| IN4 | 7,8 | 4 |
| IN5 | 9,10 | 5 |
| VAIO | 11–18; stereo 11,12 | 6 |
| AUX | 19–26; stereo 19,20 | 7 |
| VAIO3 | 27–34; stereo 27,28 | 8 |

**Selected strips:** under Advanced, select columns such as VAIO, AUX and VAIO3. AEC is on if any selected strip has an active route to the chosen output bus. The default is VAIO to A2; choose the bus actually connected to your speakers.

**All strips to output bus:** watch routes from all eight strips to A1, A2, A3, A4 or A5, excluding strips receiving the configured microphone returns. If other strips contain processed microphone returns, use Selected strips to exclude those too.

Auto reads route buttons, mute, strip/bus gain, bus-specific gain and solo. It follows **routing, not instantaneous audio level**. Silence on an active route does not turn Auto off. When all watched routes are inactive it selects bypass. If routing cannot be read, it keeps AEC on. It does not switch output devices or change routes.

Tray AEC, Bypass and Mute override Auto; choose Auto again to resume. Live controls do not overwrite the saved starting mode. Auto monitoring choices can be edited independently under Advanced and do not change the selected audio-reference columns.

## Suppression and timing

| Setting | Effect |
|---|---|
| Gentle | Default; preserves the previous release's less aggressive suppression. |
| Balanced | A moderate increase; try first if Gentle leaves too much echo. |
| Strong | Original upstream suppression; can heavily attenuate your voice in difficult overlap. Listen before adopting it. |
| Microphone hold | Adds real microphone delay, 0–250 ms. Normally leave at 0. |
| AEC delay estimate | A separate timing hint, 0–500 ms. Start at 0; it is not another delay buffer. |

Suppression changes apply when the engine next starts and cannot compensate for a missing or incorrect reference. Gentle and Balanced pass the six synthetic scenarios; Strong fails some near-end preservation criteria. These are experimental profiles, not a guarantee for a room or voice.

The host adds 480 samples / 10 ms of framing, plus hold, even in bypass. AEC adds internal delay. Synthetic alignment is around 19 ms overall with zero hold; this is not a measured hardware latency. AEC/bypass transitions fade over 10 ms; mute is immediate and unmute fades over 5 ms.

## Tray, settings and Windows startup

Closing the window while audio is running leaves the engine in the system tray. Double-click the custom tray icon or reopen **VoiceMeeter AEC.exe** to restore it. Right-click for AEC, Bypass, Mute, Auto, Diagnostics and Exit. Diagnostics are English in both languages. The icon tooltip shows status; Windows may place it under the hidden-icons arrow.

Settings save automatically. After testing, choose Auto or the desired starting mode under **Advanced**, then enable **Start with Windows** if wanted. At sign-in, only the tray icon appears. The app waits for VoiceMeeter and starts using saved settings. Configure VoiceMeeter separately to start and restore the tested 48 kHz configuration and insert routing.

It waits up to 90 seconds for VoiceMeeter and retries selected initial driver failures at five-second intervals. Failure is reported through the tray. It does not endlessly restart after a crash; native driver reset/stall recovery stays limited to two attempts. A driver call that itself hangs may exceed the startup waiting deadline.

Settings are in `%LOCALAPPDATA%\VoiceMeeterAEC\settings.json`: selected columns, Auto scope, speaker bus, timing, starting mode, suppression and language. They save automatically. Changes that affect engine startup apply the next time the engine starts; disable PATCH INSERT, stop, and reopen it first.

Startup uses the current user's Windows Run setting, with no administrator rights or service. Disable the option to remove it. After moving or upgrading the app, open the new copy once so the saved startup path can be refreshed.

To remove the app: disable startup and save, disable PATCH INSERT, exit from the tray, then remove its folder. Local settings/logs can be removed separately. Diagnostics are text only, never audio; rotation is approximately 2 MiB plus one previous segment.

## Diagnostics and limits

- `blocks`: callbacks; `max`: worst full callback duration; `overruns`: callbacks exceeding the buffer period (4 ms at 192 samples / 48 kHz).
- `mic` and `ref`: last-frame peaks. `ref_missing=1` follows 500 ms of near-silent reference and selects delayed dry bypass. A wrong but non-silent reference cannot be detected automatically.
- `errors`: DSP failures, causing delayed dry microphone fallback. Driver reset/resync or a two-second callback stall can retry twice, preserving mode/profile. Other faults stop the engine. Disable PATCH INSERT if recovery fails.
- Only 48 kHz and little-endian float32/PCM16/PCM24/PCM32 are supported. Unselected channels remain unchanged. Sonora's internal allocations have not been proven safe for every real-time workload.
- Applications capturing hardware directly bypass this filter. Downstream effects and communication software have their own latency and capture behavior. Test the actual receiving application's path.

## Console and building

Direct executable launches retain a diagnostic console:

```powershell
.\voicemeeter-aec.exe --help
.\voicemeeter-aec.exe --self-test
.\voicemeeter-aec.exe --self-test --suppression balanced
.\voicemeeter-aec.exe --run --mic 1 --ref 11,12 --returns 1,2 --auto-strips 6,7,8 --auto-bus 2
.\voicemeeter-aec.exe --run --mic 1 --ref 11,12,19,20,27,28 --returns 1,2 --auto-strips 6,7,8 --auto-bus 2
.\voicemeeter-aec.exe --run --mic 1 --ref 11,12 --returns 1,2 --auto-strips all --auto-bus 2 --suppression balanced
```

Starting modes: `--aec`, `--bypass`, `--mute`, `--auto` (default Auto). Console keys: A/B/M/T/Q. `--auto-strip` remains an alias for `--auto-strips`. `--auto-check` reads VAIO/A2; `--probe` queries the Insert driver without streaming and requires its client slot to be free. Help and self-tests open no audio device. Streaming requires `--run`.

Run **Build.cmd** from the source directory. Windows x64, Rust 1.91+, Visual C++, the Windows SDK and the .NET 8 SDK or newer are required. Rust dependencies, ASIO headers and the local Sonora patch are included. The build publishes a self-contained WPF desktop app; users do not need to install .NET. The unsigned audio engine requires the Visual C++ x64 runtime.

See [validation](VALIDATION-EN.md), [changelog](CHANGELOG.md), and [licenses](THIRD-PARTY-EN.md). Independent early-access project; not affiliated with VB-Audio or Steinberg.
