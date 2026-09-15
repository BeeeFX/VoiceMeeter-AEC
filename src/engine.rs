pub use sonora::config::ResidualSuppression;
use sonora::{AudioProcessing, Config, StreamConfig, config::EchoCanceller};
pub const FRAME: usize = 480;

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
    mic: [f32; FRAME],
    left: [f32; FRAME],
    right: [f32; FRAME],
    output: [f32; FRAME],
    scratch_l: [f32; FRAME],
    scratch_r: [f32; FRAME],
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
}
impl Engine {
    pub fn new(delay: i32, hold_ms: usize) -> Self {
        Self::with_suppression(delay, hold_ms, ResidualSuppression::Gentle)
    }
    pub fn with_suppression(delay: i32, hold_ms: usize, suppression: ResidualSuppression) -> Self {
        assert!((0..=500).contains(&delay) && hold_ms <= 250);
        let config = Config {
            pipeline: sonora::config::Pipeline {
                maximum_internal_processing_rate: sonora::config::MaxProcessingRate::Rate48kHz,
                ..Default::default()
            },
            echo_canceller: Some(EchoCanceller {
                enforce_high_pass_filtering: false,
                residual_suppression: suppression,
                ..Default::default()
            }),
            ..Default::default()
        };
        let mut apm = AudioProcessing::builder()
            .config(config)
            .capture_config(StreamConfig::new(48000, 1))
            .render_config(StreamConfig::new(48000, 2))
            .build();
        apm.set_stream_delay_ms(delay).unwrap();
        Self {
            apm,
            mic: [0.; FRAME],
            left: [0.; FRAME],
            right: [0.; FRAME],
            output: [0.; FRAME],
            scratch_l: [0.; FRAME],
            scratch_r: [0.; FRAME],
            hold: vec![0.; hold_ms * 48],
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
        }
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
        if self.pos == FRAME {
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
            }
            // Crossfade for 10 ms, with aligned frame positions. The DSP's
            // internal group delay still differs from dry bypass.
            self.wet.target(
                if mode == 1 || self.missing || failed {
                    0.
                } else {
                    1.
                },
                FRAME,
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
            self.unmute.target(1., 240);
            result * self.unmute.next()
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;
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
