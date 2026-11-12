pub use sonora::config::ResidualSuppression;
use sonora::{AudioProcessing, Config, StreamConfig, config::EchoCanceller};
/// A fixed-length ramp. Repeated requests for the same target do not restart it.
struct Ramp {
    value: f32,
    target: f32,
    remaining: usize,
}
impl Ramp {
    fn new(value: f32) -> Self {
        Self {
            value,
            target: value,
            remaining: 0,
        }
    }
    fn target(&mut self, target: f32, samples: usize) {
        if self.target != target {
            self.target = target;
            self.remaining = samples;
        }
    }
    fn next(&mut self) -> f32 {
        if self.remaining > 0 {
            self.value += (self.target - self.value) / self.remaining as f32;
            self.remaining -= 1;
        }
        self.value
    }
}

/// Fixed 10 ms framing, plus an optional explicit microphone hold.
/// No growing queues. The reference is never copied to a microphone output.
pub struct Engine {
    apm: AudioProcessing,
    sample_rate: usize,
    frame: usize,
    mic: Vec<f32>,
    left: Vec<f32>,
    right: Vec<f32>,
    output: Vec<f32>,
    scratch_l: Vec<f32>,
    scratch_r: Vec<f32>,
    hold: Vec<f32>,
    hold_pos: usize,
    pos: usize,
    wet: Ramp,
    unmute: Ramp,
    pub frames: u64,
    pub errors: u64,
    pub failed: bool,
    pub missing: bool,
    quiet_frames: usize,
    pub mic_peak: f32,
    pub ref_peak: f32,
    /// AEC3's instantaneous echo-delay estimate; -1 while unknown.
    pub delay_ms: i32,
    /// Echo return loss enhancement (dB): how much echo the linear filter removes.
    pub erle_db: f32,
    /// Echo return loss (dB): how much quieter the echo is than the reference.
    pub erl_db: f32,
}
impl Engine {
    pub fn new(delay: i32, hold_ms: usize) -> Self {
        Self::with_suppression(delay, hold_ms, ResidualSuppression::Gentle)
    }
    pub fn with_suppression(delay: i32, hold_ms: usize, suppression: ResidualSuppression) -> Self {
        Self::with_sample_rate(delay, hold_ms, suppression, 48_000)
            .expect("48 kHz must be a supported processing rate")
    }
    pub fn with_sample_rate(
        delay: i32,
        hold_ms: usize,
        suppression: ResidualSuppression,
        sample_rate: usize,
    ) -> Option<Self> {
        Self::with_delay_mode(delay, hold_ms, suppression, sample_rate, true)
    }
    pub fn with_delay_mode(
        delay: i32,
        hold_ms: usize,
        suppression: ResidualSuppression,
        sample_rate: usize,
        automatic_delay: bool,
    ) -> Option<Self> {
        assert!((0..=500).contains(&delay) && hold_ms <= 250);
        if !(8_000..=384_000).contains(&sample_rate) || sample_rate % 100 != 0 {
            return None;
        }
        let frame = sample_rate / 100;
        let config = Config {
            pipeline: sonora::config::Pipeline {
                maximum_internal_processing_rate: sonora::config::MaxProcessingRate::Rate48kHz,
                ..Default::default()
            },
            echo_canceller: Some(EchoCanceller {
                automatic_delay,
                enforce_high_pass_filtering: false,
                residual_suppression: suppression,
                ..Default::default()
            }),
            ..Default::default()
        };
        let mut apm = AudioProcessing::builder()
            .config(config)
            .capture_config(StreamConfig::new(sample_rate as u32, 1))
            .render_config(StreamConfig::new(sample_rate as u32, 2))
            .build();
        apm.set_stream_delay_ms(delay).unwrap();
        Some(Self {
            apm,
            sample_rate,
            frame,
            mic: vec![0.; frame],
            left: vec![0.; frame],
            right: vec![0.; frame],
            output: vec![0.; frame],
            scratch_l: vec![0.; frame],
            scratch_r: vec![0.; frame],
            hold: vec![0.; hold_ms * sample_rate / 1000],
            hold_pos: 0,
            pos: 0,
            wet: Ramp::new(0.),
            unmute: Ramp::new(1.),
            frames: 0,
            errors: 0,
            failed: false,
            missing: false,
            quiet_frames: 0,
            mic_peak: 0.,
            ref_peak: 0.,
            delay_ms: -1,
            erle_db: 0.,
            erl_db: 0.,
        })
    }
    pub fn tick(&mut self, mic: f32, left: f32, right: f32, mode: i32) -> f32 {
        let finite = |v: f32| if v.is_finite() { v.clamp(-1., 1.) } else { 0. };
        let mut mic = finite(mic);
        if !self.hold.is_empty() {
            std::mem::swap(&mut mic, &mut self.hold[self.hold_pos]);
            self.hold_pos = (self.hold_pos + 1) % self.hold.len();
        }
        let result = self.output[self.pos];
        self.mic[self.pos] = mic;
        self.left[self.pos] = finite(left);
        self.right[self.pos] = finite(right);
        self.pos += 1;
        if self.pos == self.frame {
            self.pos = 0;
            self.frames += 1;
            self.mic_peak = self.mic.iter().fold(0f32, |a, x| a.max(x.abs()));
            self.ref_peak = self
                .left
                .iter()
                .chain(&self.right)
                .fold(0f32, |a, x| a.max(x.abs()));
            self.quiet_frames = if self.ref_peak < 0.00001 {
                (self.quiet_frames + 1).min(1000)
            } else {
                0
            };
            // Allow 500 ms for the acoustic tail, then pass dry speech on absent reference.
            self.missing = self.quiet_frames >= 50;
            let render = self.apm.process_render_f32(
                &[&self.left, &self.right],
                &mut [&mut self.scratch_l, &mut self.scratch_r],
            );
            let capture = self
                .apm
                .process_capture_f32(&[&self.mic], &mut [&mut self.output]);
            if render.is_err() || capture.is_err() {
                self.errors += 1;
            }
            let failed = render.is_err() || capture.is_err();
            self.failed = failed;
            if failed {
                self.output.copy_from_slice(&self.mic);
            } else {
                let stats = self.apm.statistics();
                self.delay_ms = stats.delay_ms.unwrap_or(-1);
                let db = |v: Option<f64>| v.filter(|x| x.is_finite()).unwrap_or(0.) as f32;
                self.erle_db = db(stats.echo_return_loss_enhancement);
                self.erl_db = db(stats.echo_return_loss);
            }
            // Crossfade for 10 ms, with aligned frame positions. The DSP's
            // internal group delay still differs from dry bypass.
            self.wet.target(
                if mode == 1 || self.missing || failed {
                    0.
                } else {
                    1.
                },
                self.frame,
            );
            for (i, x) in self.output.iter_mut().enumerate() {
                let wet = self.wet.next();
                *x = finite(self.mic[i] * (1. - wet) + finite(*x) * wet);
            }
        }
        // Mute is immediate for privacy; unmute fades in over 5 ms.
        if mode == 2 {
            self.unmute = Ramp::new(0.);
            0.
        } else {
            self.unmute.target(1., self.sample_rate * 5 / 1000);
            result * self.unmute.next()
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
    #[test]
    fn automatic_delay_tracks_changes_while_manual_alignment_stays_fixed() {
        for rate in [48_000, 44_100] {
            let mut automatic = Engine::with_delay_mode(0, 0, ResidualSuppression::Gentle, rate, true).unwrap();
            let mut manual = Engine::with_delay_mode(40, 0, ResidualSuppression::Gentle, rate, false).unwrap();
            let mut history = vec![0f32; rate];
            let mut seed = 91u32;
            let mut first_auto = 0;
            let mut first_manual = 0;
            for i in 0..rate * 16 {
                seed = seed.wrapping_mul(1664525).wrapping_add(1013904223);
                let reference = ((seed >> 8) as f32 / 8388608. - 1.) * 0.2;
                let delay = rate * if i < rate * 8 { 40 } else { 100 } / 1000;
                let microphone = history[(i + rate - delay) % rate] * 0.5;
                history[i % rate] = reference;
                assert!(automatic.tick(microphone, reference, reference, 0).is_finite());
                assert!(manual.tick(microphone, reference, reference, 0).is_finite());
                if i == rate * 8 - 1 {
                    first_auto = automatic.apm.statistics().delay_ms.unwrap();
                    first_manual = manual.apm.statistics().delay_ms.unwrap();
                }
            }
            let last_auto = automatic.apm.statistics().delay_ms.unwrap();
            let last_manual = manual.apm.statistics().delay_ms.unwrap();
            assert!((20..=60).contains(&first_auto), "initial alignment {first_auto} at {rate}");
            assert!((80..=120).contains(&last_auto), "changed alignment {last_auto} at {rate}");
            assert_eq!(first_manual, last_manual, "manual alignment drifted at {rate}");
            assert!((20..=60).contains(&last_manual));
            assert_eq!(automatic.errors + manual.errors, 0);
            assert!((80..=120).contains(&automatic.delay_ms), "published delay {}", automatic.delay_ms);
            assert!(automatic.erle_db.is_finite() && automatic.erl_db.is_finite());
        }
    }
    /// Offline bench (not part of the normal suite): echo-path changes like clock-drift
    /// corrections between two audio devices, under harder room conditions. Run with
    /// `cargo test --release drift_bench -- --ignored --nocapture`.
    #[test]
    #[ignore]
    fn drift_bench() {
        struct Scenario { name: &'static str, delay: fn(f64) -> f64, echo: f32, noise: f32, drive: f32, playback: fn(f64) -> bool }
        let always: fn(f64) -> bool = |_| true;
        let scenarios = [
            Scenario { name: "baseline: stable, clean", delay: |_| 80., echo: 0.3, noise: 0.0005, drive: 0., playback: always },
            Scenario { name: "quiet echo + room noise", delay: |_| 80., echo: 0.03, noise: 0.003, drive: 0., playback: always },
            Scenario { name: "distorting speaker", delay: |_| 80., echo: 0.3, noise: 0.001, drive: 4., playback: always },
            Scenario { name: "quiet + noisy + 100 ppm drift", delay: |t| 80. + 0.1 * t, echo: 0.03, noise: 0.003, drive: 0., playback: always },
            Scenario { name: "40 ms jumps every 60 s", delay: |t| 80. + 40. * ((t / 60.).floor() % 2.), echo: 0.3, noise: 0.001, drive: 0., playback: always },
            Scenario { name: "quiet + noisy + 40 ms jumps", delay: |t| 80. + 40. * ((t / 60.).floor() % 2.), echo: 0.03, noise: 0.003, drive: 0., playback: always },
            Scenario { name: "2 min silence, then playback at new delay", delay: |t| if t < 150. { 80. } else { 110. }, echo: 0.3, noise: 0.001, drive: 0., playback: |t| !(30. ..150.).contains(&t) },
        ];
        let rate = 48_000usize;
        let seconds = 300usize;
        println!("
scenario                                   | settle | worst 10 s | 10 s windows < 20 dB | longest bad run | final");
        for sc in scenarios {
            let mut engine = Engine::with_delay_mode(0, 0, ResidualSuppression::Standard, rate, true).unwrap();
            let mut seed = 7u32;
            let mut rand = move || {
                seed = seed.wrapping_mul(1664525).wrapping_add(1013904223);
                (seed >> 8) as f32 / 8388608. - 1.
            };
            let taps = rate * 120 / 1000;
            let ir: Vec<f32> = (0..taps)
                .map(|i| if i == 0 { 1. } else { rand() * 0.25 * (-(i as f32) / (rate as f32 * 0.03)).exp() })
                .collect();
            let history_len = rate * 2;
            let mut history = vec![0f32; history_len];
            let mut lowpass = 0f32;
            let (mut mic_energy, mut out_energy, mut active) = (0f64, 0f64, 0usize);
            let (mut pause_energy, mut paused) = (0f64, 0usize);
            let mut windows = Vec::new();
            for i in 0..rate * seconds {
                let t = i as f64 / rate as f64;
                lowpass += 0.25 * (rand() - lowpass);
                let syllable = (0.5 + 0.5 * (t * 2. * std::f64::consts::PI * 4.).sin()) as f32;
                let talking = (sc.playback)(t) && (t % 4.) < 2.8;
                let reference = if talking { lowpass * syllable * 0.6 } else { 0. };
                // The speaker hears a possibly overdriven copy; AEC only sees the clean reference.
                let played = if sc.drive > 0. { (reference * sc.drive).tanh() / sc.drive.tanh() } else { reference };
                history[i % history_len] = played;
                let delay = (sc.delay)(t) * rate as f64 / 1000.;
                let mut echo = 0f32;
                for k in (0..taps).step_by(16) {
                    let pos = i as f64 - delay - k as f64;
                    if pos < 0. { break; }
                    let base = pos.floor();
                    let frac = (pos - base) as f32;
                    let a = history[base as usize % history_len];
                    let b = history[(base as usize + 1) % history_len];
                    echo += (a + (b - a) * frac) * ir[k];
                }
                let noise = rand() * sc.noise;
                let microphone = echo * sc.echo + noise;
                let output = engine.tick(microphone, reference, reference, 0);
                if talking && (t % 4.) > 0.3 {
                    // True echo energy in; output energy (echo residual + processed noise) out.
                    mic_energy += ((echo * sc.echo) as f64).powi(2);
                    out_energy += (output as f64).powi(2);
                    active += 1;
                }
                if (sc.playback)(t) && (t % 4.) > 3.1 {
                    // Pause after the echo tail: what the output looks like with noise only.
                    pause_energy += (output as f64).powi(2);
                    paused += 1;
                }
                if (i + 1) % (rate * 10) == 0 {
                    if active > 0 {
                        let floor = if paused > 0 { pause_energy / paused as f64 * active as f64 } else { 0. };
                        // Residual echo is what exceeds the noise-only output; cap at 60 dB.
                        let echo_out = (out_energy - floor).max(mic_energy * 1e-6);
                        windows.push(Some(10. * (mic_energy / echo_out).log10()));
                    } else {
                        windows.push(None);
                    }
                    (mic_energy, out_energy, active) = (0., 0., 0);
                    (pause_energy, paused) = (0., 0);
                }
            }
            let measured: Vec<f64> = windows.iter().flatten().copied().collect();
            let worst = measured.iter().skip(1).cloned().fold(f64::INFINITY, f64::min);
            let bad = measured.iter().skip(1).filter(|v| **v < 20.).count();
            let (mut run, mut longest) = (0, 0);
            for v in measured.iter().skip(1) { if *v < 20. { run += 1; longest = longest.max(run); } else { run = 0; } }
            println!("{:<42} | {:5.1} | {:8.1} dB | {:20} | {:12} s | {:5.1} dB",
                sc.name, measured[1], worst, bad, longest * 10, measured.last().unwrap());
        }
    }

    #[test]
    fn transitions_and_unmute_are_bounded() {
        let mut ramp = Ramp::new(0.);
        ramp.target(1., 480);
        let mut previous = 0.;
        for _ in 0..480 {
            ramp.target(1., 480);
            let x = ramp.next();
            assert!(x >= previous && x - previous < 0.0021);
            previous = x;
        }
        assert_eq!(previous, 1.);
        ramp.target(0., 480);
        for _ in 0..480 {
            let x = ramp.next();
            assert!(x <= previous && previous - x < 0.0021);
            previous = x;
        }
        assert_eq!(previous, 0.);
        let mut e = Engine::new(0, 0);
        for _ in 0..1000 {
            e.tick(0.5, 0., 0., 1);
        }
        assert_eq!(e.tick(0.5, 0., 0., 2), 0.);
        let first = e.tick(0.5, 0., 0., 1);
        assert!(first > 0. && first < 0.003);
        for _ in 1..240 {
            e.tick(0.5, 0., 0., 1);
        }
        assert_eq!(e.tick(0.5, 0., 0., 1), 0.5);
    }
    #[test]
    fn bypass_is_exact_fixed_delay_for_varied_callback_sizes() {
        for block in [32, 64, 192, 256, 480, 512, 1024] {
            let mut e = Engine::new(0, 0);
            let source: Vec<f32> = (0..10000).map(|x| (x % 997) as f32 / 1000.).collect();
            let mut out = Vec::new();
            for part in source.chunks(block) {
                for x in part {
                    out.push(e.tick(*x, 0.1, -0.1, 1));
                }
            }
            assert_eq!(&out[..480], &[0.; 480]);
            assert_eq!(&out[480..], &source[..source.len() - 480]);
        }
    }
    #[test]
    fn compatibility_rate_resamples_and_preserves_bypass_timing() {
        let mut e = Engine::with_sample_rate(0, 0, ResidualSuppression::Balanced, 44_100).unwrap();
        let source: Vec<f32> = (0..5000).map(|x| (x % 997) as f32 / 1000.).collect();
        let output: Vec<f32> = source.iter().map(|x| e.tick(*x, 0.1, -0.1, 1)).collect();
        assert_eq!(&output[..441], &[0.; 441]);
        assert_eq!(&output[441..], &source[..source.len() - 441]);
        assert_eq!(e.frames, (source.len() / 441) as u64);

        let mut aec = Engine::with_sample_rate(40, 0, ResidualSuppression::Balanced, 44_100).unwrap();
        for sample in 0..44_100 {
            let reference = ((sample as f32 * 0.013).sin() * 0.4).clamp(-1., 1.);
            assert!(aec.tick(reference * 0.2, reference, -reference, 0).is_finite());
        }
        assert_eq!(aec.frames, 100);
        assert_eq!(aec.errors, 0);
    }
    #[test]
    fn hold_is_bounded_and_real() {
        let mut e = Engine::new(0, 10);
        for i in 0..2000 {
            assert_eq!(
                e.tick(if i == 0 { 0.5 } else { 0. }, 0., 0., 1),
                if i == 960 { 0.5 } else { 0. }
            );
        }
        assert_eq!(e.hold.len(), 480);
    }
    #[test]
    fn silence_missing_reference_mute_and_nonfinite() {
        let mut e = Engine::new(0, 0);
        for _ in 0..30000 {
            assert!(e.tick(0.2, 0., 0., 0).is_finite());
        }
        assert!(e.missing);
        assert_eq!(e.tick(0.2, 0., 0., 0), 0.2);
        assert_eq!(e.tick(0.5, 0., 0., 2), 0.);
        for _ in 0..1000 {
            assert!(e.tick(f32::NAN, f32::INFINITY, 0., 0).is_finite());
        }
        assert_eq!(e.errors, 0);
    }
}
