# VoiceMeeter AEC 1.3.0 — validation

[Français](VALIDATION.md) · Version 1.3.0

The automated checks pass for Gentle and Balanced. **Strong deliberately exposes the original, more aggressive suppressor and fails some near-end preservation checks.** Passing synthetic tests does not establish transparent speech or production stability.

## Automated evidence

- Eight Rust tests cover exact 48 kHz bypass framing across varying callback sizes, 44.1 kHz compatibility framing and AEC processing, bounded hold, missing reference/mute/non-finite input, fades, selective driver recovery, multi-reference parsing, and multi-strip Auto masks including microphone-return exclusions.
- Native transport tests cover Banana's 22 channels and Potato's 34 channels, double buffers, PCM16/24/32 and float32, preserving unselected channels bit-for-bit.
- The real C++ callback calls the Rust engine through FFI for 300 simulated blocks; framing, immediate mute, channel preservation and invalid-index detection pass.
- Native event checks cover reset, sample-rate change, latency-only notification, cancellation priority and aggregation of active/inactive/unknown Auto routes.
- A hidden child process receives M/B/A/T/Q through the same anonymous stdin pipe used by the tray. It verifies the actual native control handling, returns diagnostic output and exits gracefully without opening an audio device.
- English/French desktop checks cover both mixer layouts and channel maps, edition selection, Auto/Strong defaults, 44.1 kHz settings and engine arguments, settings migration, mode highlighting, locked audio controls, persistent notification-area closing, theme resources, update metadata/checksums, safe archive paths and copy rollback.
- New host checks feed actual status records through the parser, reject pre-callback startup messages, exercise native exit code 14 and retry eligibility through a real child process, verify latest requested mode preservation, and rotate small test logs without touching user logs/settings.
- Update argument round trips preserve all four modes; old updater arguments resume muted. Native reference checks cover Banana without GainLayer, Potato bus gains, muted routes, relative weights, channel mapping, simultaneous loud sources and a smooth transition to zero reference.
- Release builds remap local compiler paths; public archives are checked for developer account paths, saved settings, shortcuts, logs and build directories.

## Audio measurements

Each deterministic scenario is 48 seconds at 48 kHz: two independent colored-noise references, three filtered echo paths, a near-end signal during seconds 20–30, acoustic delay changed from 50 to 90 ms at second 30, and reference removal at second 46. Near-end material is modulated harmonics or independent stationary colored noise, not recorded human speech. Additional seeds use half and 1.5x near-end levels.

Original thresholds are unchanged: echo attenuation at least 10 dB, attenuation after delay change at least 8 dB, near-end gain at least 0.35, correlation at least 0.5, error improvement at least 0 dB, finite output, missing-reference detection and no DSP errors.

| Profile | Scenarios passing | Original stationary near-end: gain / correlation / error improvement |
|---|---|---|
| Gentle | 6/6 | 0.773 / 0.872 / +3.75 dB |
| Balanced | 6/6 | 0.733 / 0.856 / +3.29 dB |
| Strong | 3/6 | 0.119 / 0.390 / -1.77 dB |

Gentle preserves 0.1.2 tuning (thresholds 3/6). Balanced uses 2.25/4.5. Strong retains upstream tuning. All keep adaptive cancellation active. The quiet stationary case reaches gain about 0.386 with Balanced, versus about 0.001 with Strong: stronger suppression can remove wanted sound. Balanced was selected using this suite, so its pass is not independent validation.

Linear echo-only attenuation is approximately 37 dB after convergence and 34 dB after reconvergence in these tests. This model does not establish the benefit of a stronger profile against real speaker distortion or nonlinear residual echo. See the [local patch](patches/README.md) and raw `docs/self-test-0.1.3-gentle.txt`, `-balanced.txt`, and `-strong.txt`. Strong's self-test returns a nonzero exit code; that limitation is not hidden by the build's default checks.

## Timing, recovery and background operation

Framing adds exactly 10 ms: 480 samples at 48 kHz or 441 samples at 44.1 kHz, plus optional microphone hold up to 250 ms. AEC has additional internal delay; synthetic 48 kHz near-end alignment is around 19 ms overall at zero hold. This is not hardware end-to-end latency. AEC/bypass uses a 10 ms fade; mute is immediate and unmute fades over 5 ms.

Driver reset/resync or a two-second callback stall permits two retries (250 ms, then 1 s), with mode and profile preserved. Explicit quit and invalid format/buffer/sample-rate faults are not retried. Latency notifications refresh diagnostics without resetting audio. Recovery can interrupt audio and requires echo readaptation.

Sign-in startup waits for VoiceMeeter and retries selected initial driver failures for up to 90 seconds. This is separate from native recovery. Settings and shortcut tests use an isolated directory; the development machine's startup registration is not modified. The process wrapper uses CreateNoWindow and redirected stdin/stdout/stderr. Normal startup has no console; a direct CLI launch retains one. Diagnostics rotate at approximately 2 MiB plus one previous segment and contain no recorded audio.

The 1.3.0 build and desktop checks open no audio driver and do not change Windows startup registration. Meter telemetry is emitted every 100 ms and excluded from disk logs. Route weights are polled every 200 ms and faded over 10 ms inside the callback using a bounded atomic snapshot. The speaker-reference mix follows routing and gains; downstream EQ, panning, surround downmix and nonlinear speaker effects are not reconstructed. These integration checks do not constitute live acoustic validation of the new reference weighting.

## Remaining real-world validation

Initial live use of the previous release was reported as working, including microphone mute, with audible residual speaker sound. This is informal user feedback, not a controlled acoustic measurement.

No Banana or 44.1 kHz live audio stream, sign-out or reboot was performed for this update. The Banana driver registration, 22-channel software path and 44.1 kHz resampling path are covered automatically, but users should still confirm real streaming, route reads and PATCH INSERT behavior. Real sign-in ordering, hidden-host driver initialization, long-duration streaming, actual driver recovery, multiple-route Auto transitions, 44.1 kHz perceptual quality and CPU use, speech quality, preset comparisons and end-to-end latency still need field validation. Automated checks exercise the control and processing paths without taking over active audio.

Host arrays have fixed capacity and there are no host allocations, logging or Remote calls in the audio callback. Sonora's internal allocation behavior is not instrumented. Its upstream dev-test suite was not run because dev dependencies are outside the vendored application graph; the patch is covered by the application's integration and quality scenarios.

Compatibility target: Windows x64, VoiceMeeter Banana or Potato with its matching Insert x64 driver, native 48 kHz or opt-in 44.1 kHz compatibility resampling, and the Visual C++ x64 runtime. Other sample rates are rejected. The desktop app is published self-contained, so users do not need to install .NET or run PowerShell. The binaries are unsigned. Standard VoiceMeeter is not supported because it does not provide the required Insert layout.
