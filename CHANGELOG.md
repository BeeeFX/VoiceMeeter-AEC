# Changelog

## 0.1.3 — tray, startup and suppression profiles

- Make Auto the default starting mode and add monitoring for multiple strips or all playback routes to A1–A5, excluding configured microphone-return strips in the latter mode. Monitoring remains based on routing state, independent of the audio reference.
- Add bilingual hover tips, simplify Bypass, and replace setup-specific labels and documentation with generic examples.
- Produce separate source and Windows release archives; remap developer paths out of the binary.

- Add Gentle (unchanged default), Balanced and Strong residual suppression choices. Settings apply on the next engine start and survive driver recovery. Strong has measured near-end preservation failures; it is optional.
- Launch the audio engine without a console. Add an English/French tray menu for settings, AEC, bypass, mute, Auto, stop, diagnostics and exit. Closing settings hides it; reopening the app restores the existing launcher.
- Save routing, delays, mode, suppression and language in `%LOCALAPPDATA%\VoiceMeeterAEC\settings.json`.
- Add opt-in current-user Windows sign-in startup, using saved settings with only the tray icon visible. Wait up to 90 seconds for VoiceMeeter and retry selected initial driver failures at five-second intervals. Startup never launches VoiceMeeter or changes its patches/routes.
- Provide `Start.vbs` for a console-free manual launch; keep CMD aliases. Rotate diagnostics at approximately 2 MiB with one previous segment; no audio is recorded.
- Test hidden process control through the real native stdin pipe, settings round trips, isolated startup shortcut creation/removal, window hide/restore, and both language layouts. No live stream or sign-out/reboot was performed during this update.

## 0.1.2 — experimental stabilization

- Tune residual suppression in a documented local Sonora patch to preserve the near-end signal. All six deterministic AEC scenarios pass unchanged criteria; this does not establish real-speech or hardware quality.
- Add a 10 ms AEC/bypass crossfade and a 5 ms unmute fade; mute remains immediate.
- Retry driver reset/resync or callback stall at most twice (250 ms, 1 s), recreating the engine and preserving manual/Auto/mute mode. Never retry explicit quit, incompatible sample rate or invalid buffer faults.
- Clear session state before reconnection; detect null driver buffers and output-ready failures.
- Add full native callback/Rust integration checks, cancellation priority and recovery bounds tests.

## 0.1.1 — experimental

- Rename the tool to VoiceMeeter AEC; Potato is the only supported VoiceMeeter edition.
- Add an English/French launcher with English as the default and a saved language preference.
- Preserve channel values and selected mode while switching languages.
- Open the matching setup guide; standardize engine diagnostics in English.
- Add English setup, validation and licensing documents plus Start.cmd / Build.cmd.
- Preserve the then-current AEC quality limitations and unchanged DSP algorithm.

## 0.1.0 — experimental

- Initial Potato ASIO Insert prototype with Sonora AEC3, selectable reference and microphone channels, bypass, mute and read-only Auto routing control.
- Simulated transport and bounded buffering tests; no hardware streaming validation.
