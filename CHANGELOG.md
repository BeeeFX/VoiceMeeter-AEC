# Changelog

## 1.2.2 — keep the app available after closing

- Make the window’s close button hide VoiceMeeter AEC in the notification area whether the audio engine is running or stopped.
- Keep the tray icon and background app available for reopening with one left click. Exit remains an explicit tray-menu action.
- Add a desktop regression check for the close-to-notification-area policy.

## 1.2.1 — restore Strong as the default

- Restore Strong as the default for new, missing or invalid suppression settings and CLI streams. Preserve explicitly saved Gentle, Balanced and Strong choices.
- Update the interface and setup guides to match the default. Keep all 1.2.0 reliability, reference-mixing and usability improvements.
- Strong's documented synthetic near-end preservation limitations and the Gentle/Balanced quality checks are unchanged.

## 1.2.0 — reliable startup, safer updates and clearer audio feedback

- Default new and missing suppression settings to Balanced; preserve explicitly saved profiles. Strong remains optional and fails three synthetic near-end preservation checks.
- Preserve the current Auto/AEC/Bypass/Mute mode through updates and failed-update recovery, without changing the saved startup mode. Older updaters that cannot supply a mode resume muted.
- Confirm startup only after audio callbacks arrive and preserve native driver exit codes so transient sign-in failures can retry.
- Read Banana routes without Potato-only GainLayer parameters. Match selected reference sources to the speaker bus's routes, mute/solo state and fader levels in every mode.
- Normalize the reference mix before summation to preserve stereo balance and headroom; smooth weight changes over 10 ms. If routing is unavailable, use a normalized equal-level reference and show a warning.
- Show live microphone/reference meters and distinguish Auto cancellation, Auto bypass, unavailable routing, absent reference and DSP fallback.
- Lock audio configuration while running so the selected microphone and PATCH INSERT guide match the active engine. Keep the existing window dimensions.
- Send engine-warning notification clicks to Diagnostics and rotate text logs at 2 MiB with one previous segment. Meter telemetry is not stored in the log.
- Add driver-free regression checks for startup failure/retry, control commands, update mode transfer, log rotation, reference headroom, Banana/Potato routing and UI status/settings locks. Real-room and recorded-speech validation remain outstanding.

## 1.1.3 — startup control and clearer defaults

- Add a separate saved option to start echo cancellation automatically after Windows sign-in. The app waits for VoiceMeeter and uses the saved starting mode and settings.
- Use Strong echo suppression as the default for new or missing settings while preserving every existing user’s saved choice.
- Replace the update-available arrow with a Fluent icon aligned with the other sidebar navigation icons.

## 1.1.2 — clearer status and stopping

- Highlight the active Auto, AEC, Bypass or Mute button and check the matching command in the notification menu.
- Add a colour status badge to the notification-area and taskbar icons: cyan for Auto, green for AEC, amber for Bypass/starting/reconnecting, red for Mute and grey when stopped.
- Restore the app with one left click on its notification icon.
- Replace the native stop warning with a bilingual in-app confirmation that explains the PATCH INSERT step and keeps cancellation running by default.

## 1.1.1 — simple Windows installer

- Add one `VoiceMeeter-AEC-Setup.exe` as the recommended download, with a per-user installation, Start menu shortcut and standard uninstall entry.
- Keep the portable ZIP for advanced users and for automatic updates, while reducing its visible contents to the application, audio engine and grouped licence notices.
- Keep the integrated setup guide in the app and the full documentation on GitHub instead of placing README screenshots and Markdown guides beside the executable.

## 1.1.0 — Banana support and automatic updates

- Add VoiceMeeter Banana support with its native five-strip, three-bus and 22-channel Insert layout.
- Detect the running Banana or Potato edition automatically, with a saved manual override for configuring the app while VoiceMeeter is closed.
- Adapt column cards, custom labels, Auto routing, channel validation, startup waiting and the PATCH INSERT guide to the selected edition.
- Exercise both Banana's 22-channel and Potato's 34-channel transport layouts in the native preservation tests.

- Check the latest stable GitHub release quietly at most once a day, with an optional manual check under Advanced.
- Show a compact in-app and system-tray notification only when a newer version is available. Installation always requires a click and confirmation.
- Download the matching Windows x64 package, verify it against the release's SHA-256 file, reject unsafe archive paths, replace the portable app with rollback protection, and reopen it automatically.
- Restore the audio engine after updating when it was running before the update, and preserve all user settings in the local application-data folder.

## 1.0.0 — first polished release

- Replace the PowerShell settings form with a self-contained modern Windows desktop app, a new application/tray icon, and a dark-first visual design with a saved light theme.
- Present VoiceMeeter's eight columns directly: IN1–IN5, VAIO, AUX and VAIO3, including custom strip labels read from an already-running VoiceMeeter instance. The app translates those choices to Insert channel numbers.
- Allow several playback columns to be selected and mix their stereo Insert pairs into one complete echo reference.
- Reduce first setup to microphone column, playback-reference column and speaker A bus. Move suppression, timing, startup mode and multi-strip Auto rules to Advanced.
- Keep Auto, AEC, bypass and mute available in the window and tray; save settings automatically and restore the existing app when it is opened twice.
- Add the full setup walkthrough inside the app, including a real VoiceMeeter PATCH INSERT view that highlights the selected microphone's L/R boxes, and replace native-looking selectors, checkboxes, sliders and scrollbars with consistent themed controls.
- Redesign the README around download and setup, and update the English and French guides for the new application.
- Publish the .NET runtime with the app so release users do not need PowerShell, Windows Script Host or a separate .NET installation.

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
