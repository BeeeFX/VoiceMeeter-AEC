# Local Sonora patch

`sonora/` is based on the vendored Sonora 0.2.0 source (BSD-3-Clause). Cargo selects it through `[patch.crates-io]`. The pristine vendored dependency remains included for comparison.

Local changes made 2026-09-10:

- `src/config.rs`: add `EchoCanceller::residual_suppression` with `Standard` (upstream default), `Balanced`, and `Gentle` profiles. Version 0.1.3 replaces the earlier `conservative_suppression` boolean.
- `src/audio_processing_impl.rs`: Gentle uses near-end tuning for both normal and near-end residual suppression, with echo-to-near-end transparent/suppress thresholds 3/6 in LF and HF. Balanced uses 2.25/4.5. Standard retains upstream tuning, exposed as Strong in the application. Apply consistently to mono and detected stereo. Apply the requested transparent-mode setting to stereo too.

VoiceMeeter AEC 1.2.1 defaults to Strong for new app settings and CLI streams; saved profiles remain unchanged. Gentle preserves 0.1.2 behavior and remains the default for the legacy no-profile synthetic self-test. Profiles apply on engine start and survive driver recovery. The adaptive echo cancellation filter remains active; this is not dry bypass or mixing in the reference. The less aggressive residual suppressor may leave more nonlinear residual echo in situations not covered by the synthetic suite. Thresholds are an experimental application-specific tradeoff, not upstream recommendations.

The original stationary stress changes from gain 0.119/correlation 0.390/error improvement -1.77 dB to approximately 0.773/0.872/+3.75 dB. The original pass criteria are unchanged. Additional deterministic seeds and half/1.5x near-end levels pass, but real speech and acoustics remain unverified.

Gentle and Balanced pass the six deterministic scenarios. Strong fails all three stationary near-end preservation scenarios (including near-end gain approximately 0.001 in the quietest case). It is an optional aggressive tradeoff, not a validated voice-preserving preset. The intermediate Balanced thresholds were selected using this synthetic suite; passing it is not independent evidence of real-speech quality. Raw measurements for each profile are in `docs/self-test-0.1.3-*.txt`.

All original license and attribution files are retained. This patch should be reviewed independently before proposing an upstream change.
