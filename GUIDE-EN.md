# VoiceMeeter AEC — Windows x64 setup

[Français](GUIDE-FR.md) · Version 0.1.3 · Windows x64 · VoiceMeeter Potato

VoiceMeeter AEC attenuates speaker playback picked up by a microphone. It processes the microphone through **Voicemeeter Potato Insert Virtual ASIO**, before VoiceMeeter strip effects. VoiceMeeter retains control of the hardware. The app does not change routes, patch settings or Windows default devices.

## First setup

1. Extract the Windows release into a permanent writable folder. Open **Start.vbs** for a launch without a console. Start.cmd and Demarrer.cmd are compatibility shortcuts. If Windows Script Host is unavailable, run `powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File .\Lanceur.ps1`.
2. Run VoiceMeeter Potato at **48 kHz** and configure its microphone and outputs. The example assumes a microphone on IN1; adapt the channels using the table below.
3. Leave the microphone PATCH INSERT returns disabled initially. Select microphone **1**, returns **1,2**, hold **0 ms**, delay estimate **0 ms**, and a playback reference that excludes your microphone.
4. The default starting mode is **Auto**. For the initial transport check choose **Bypass**, then **Open engine**. This saves the settings and starts the engine without a console. Hover over settings for simple explanations.
5. Check the running status. Right-click the tray icon near the clock and open **Diagnostics**: the Insert driver should be at 48 kHz and the `blocks` counter should advance. The driver accepts one Insert client at a time.
6. Enable only the microphone return channels in VoiceMeeter **PATCH INSERT**, at the PRE-FX insert point. For IN1: its left/right return switches. Leave other insert returns disabled.
7. In an application receiving the chosen VoiceMeeter microphone bus, select **Mute microphone** in the tray menu. The received microphone should become silent. Then try **Bypass** and **Manual AEC**, keeping the microphone out of speaker outputs.
8. Play speech through speakers, allow around 10–20 seconds for adaptation, and compare bypass/AEC at matching levels. Speak over the playback and check voice clarity. Some residual echo can remain.

**Disable the microphone PATCH INSERT returns before stopping the engine or updating the app.** If a fault leaves the microphone silent, disabling those returns restores VoiceMeeter's direct path. The app never changes them automatically.

## Reference: the sound to cancel

The reference must contain every playback source whose acoustic echo should be removed, and exclude the raw or processed microphone. Auto monitoring and the reference are independent.

For a simple configuration, route all speaker playback through **VAIO**, in stereo, and use reference **11,12**. Audio on AUX, VAIO3, other hardware strips or devices used directly outside VoiceMeeter is absent from that pair. Additional surround channels are also absent. The app accepts one stereo reference pair and does not mix reference strips itself.

The Insert signal is before effects and faders, not the final output-bus mix. Different effects, clipping or abrupt volume changes between reference and speakers can reduce cancellation.

### Optional combined reference

A virtual audio cable can collect several playback strips into one stereo reference. This is an example, not a required device or preconfigured route:

1. Assign an **unused** hardware output bus to the cable's playback endpoint.
2. Send the intended speaker-playback strips to that bus, matching relative listening levels. Exclude the microphone and every processed microphone return.
3. On a genuinely free hardware input strip, select the cable's recording endpoint. Do not replace an occupied input. Disable **all A and B sends** on this reference strip and leave its PATCH INSERT return disabled.
4. Use that strip's left/right channels as the reference; for example **IN5 = 9,10**. Never send this strip back to the cable bus, which would create a loop.
5. Playback should change `ref` in diagnostics; speaking alone with playback stopped should not.

The cable adds reference latency. Microphone hold may help a late reference, but also delays your voice. Start at 0 and verify the reference before increasing it. Prefer a direct virtual-input reference when it covers the playback. This optional route has not received controlled acoustic validation.

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

**Selected strips:** enter a list such as `6,7,8`. AEC is on if any selected strip has an active route to the chosen output bus. The default is strip 6 to A2; choose the bus actually connected to your speakers.

**All strips to output bus:** watch routes from all eight strips to A1, A2, A3, A4 or A5, excluding strips receiving the configured microphone returns. If other strips contain processed microphone returns, use Selected strips to exclude those too.

Auto reads route buttons, mute, strip/bus gain, bus-specific gain and solo. It follows **routing, not instantaneous audio level**. Silence on an active route does not turn Auto off. When all watched routes are inactive it selects bypass. If routing cannot be read, it keeps AEC on. It does not switch output devices or change routes.

Tray AEC, Bypass and Mute override Auto; choose Auto again to resume. Live controls do not overwrite the saved starting mode. Watching multiple strips does not add them to the audio reference.

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

Closing settings hides the window and leaves audio running. Double-click the tray icon or reopen Start.vbs to restore it. Right-click for AEC, Bypass, Mute, Auto, Stop engine, Diagnostics and Exit. Diagnostics are English in both languages. The icon tooltip shows status; Windows may place it under the hidden-icons arrow.

After testing, choose Auto or the desired starting mode, check **Start with Windows**, and **Save settings**. At sign-in, only the tray icon appears. The app waits for VoiceMeeter and starts using saved settings, with no Command Prompt window. Configure VoiceMeeter separately to start and restore the tested 48 kHz configuration and insert routing.

It waits up to 90 seconds for VoiceMeeter and retries selected initial driver failures at five-second intervals. Failure is reported through the tray. It does not endlessly restart after a crash; native driver reset/stall recovery stays limited to two attempts. A driver call that itself hangs may exceed the startup waiting deadline.

Settings are in `%LOCALAPPDATA%\VoiceMeeterAEC\settings.json`: channels, Auto scope/strips/bus, timing, starting mode, suppression and language. **Save settings** and **Open engine** save them. Saving does not reconfigure a running engine. Disable PATCH INSERT, stop the engine, then reopen it to apply changes.

Startup uses the current user's `VoiceMeeter AEC.lnk` in the Windows Startup folder, with no administrator rights or service. Uncheck the option and save to remove it. After moving or upgrading the app, open the new copy and save to update the shortcut. Existing saved modes are preserved; the new Auto default does not override them.

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
.\voicemeeter-aec.exe --run --mic 1 --ref 11,12 --returns 1,2 --auto-strips all --auto-bus 2 --suppression balanced
```

Starting modes: `--aec`, `--bypass`, `--mute`, `--auto` (default Auto). Console keys: A/B/M/T/Q. `--auto-strip` remains an alias for `--auto-strips`. `--auto-check` reads VAIO/A2; `--probe` queries the Insert driver without streaming and requires its client slot to be free. Help and self-tests open no audio device. Streaming requires `--run`.

Run **Build.cmd** from the source directory. Windows x64, Rust 1.91+ (tested 1.97.1), Visual C++ and Windows SDK are required. Dependencies, ASIO headers and the local Sonora patch are included for offline builds. The launcher uses Windows PowerShell 5.1 and .NET Framework WinForms; its optional VBS entry point uses Windows Script Host. The unsigned executable requires the Visual C++ x64 runtime.

See [validation](VALIDATION-EN.md), [changelog](CHANGELOG.md), and [licenses](THIRD-PARTY-EN.md). Independent experimental project; not affiliated with VB-Audio or Steinberg.
