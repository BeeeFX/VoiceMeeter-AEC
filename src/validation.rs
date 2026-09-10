use crate::engine::Engine;
use std::time::Instant;
fn noise(seed: &mut u32) -> f32 {
    *seed = seed.wrapping_mul(1664525).wrapping_add(1013904223);
    ((*seed >> 8) as f32 / 8388608. - 1.) * 0.25
}
fn energy(x: &[f32]) -> f64 {
    x.iter().map(|x| (*x as f64).powi(2)).sum::<f64>() / x.len() as f64
}
fn db(a: f64, b: f64) -> f64 {
    10. * (a.max(1e-20) / b.max(1e-20)).log10()
}
fn at(x: &[f32], i: usize, d: usize) -> f32 {
    if i >= d { x[i - d] } else { 0. }
}
fn scenario(
    voiced: bool,
    seed: u32,
    near_scale: f32,
    profile: crate::engine::ResidualSuppression,
) -> Result<(), String> {
    let n = 48_000 * 48;
    let (mut l, mut r, mut near, mut microphone) =
        (vec![0.; n], vec![0.; n], vec![0.; n], vec![0.; n]);
    let (mut a, mut b, mut c) = (91 + seed, 982 + seed * 7, 731 + seed * 13);
    let (mut fl, mut fr, mut fnr) = (0., 0., 0.);
    let mut phase = 0f32;
    for i in 0..n {
        fl = 0.72 * fl + noise(&mut a);
        fr = 0.64 * fr + noise(&mut b);
        fnr = 0.75 * fnr + noise(&mut c);
        if i < 46 * 48000 {
            l[i] = fl;
            r[i] = fr;
        }
        if (20 * 48000..30 * 48000).contains(&i) || i >= 46 * 48000 {
            let t = i as f32 / 48000.;
            phase += std::f32::consts::TAU * (135. + 35. * (t * 3.1).sin()) / 48000.;
            let envelope = (0.5 + 0.5 * (t * 13.).sin()).powi(2);
            near[i] = if voiced {
                envelope
                    * (0.15 * phase.sin()
                        + 0.075 * (phase * 2.).sin()
                        + 0.04 * (phase * 3.).sin()
                        + 0.015 * fnr)
            } else {
                fnr * 0.7
            };
        }
        near[i] *= near_scale;
        let d = if i < 30 * 48000 { 2400 } else { 4320 };
        microphone[i] =
            0.45 * at(&l, i, d) + 0.18 * at(&l, i, d + 197) + 0.3 * at(&r, i, d + 53) + near[i];
    }
    let mut e = Engine::with_suppression(0, 0, profile);
    let mut out = vec![0.; n];
    let mut timings = Vec::new();
    let begin = Instant::now();
    for start in (0..n).step_by(192) {
        let t = Instant::now();
        for i in start..(start + 192).min(n) {
            out[i] = e.tick(microphone[i], l[i], r[i], 0);
        }
        timings.push(t.elapsed().as_micros());
    }
    let elapsed = begin.elapsed();
    timings.sort_unstable();
    let erle = db(
        energy(&microphone[15 * 48000..19 * 48000]),
        energy(&out[15 * 48000..19 * 48000]),
    );
    let changed = db(
        energy(&microphone[42 * 48000..45 * 48000]),
        energy(&out[42 * 48000..45 * 48000]),
    );
    // Find total synthetic near-voice lag, including framing and internal DSP filters.
    let start = 24 * 48000;
    let end = 29 * 48000;
    let mut lag = 0;
    let mut best = f64::NEG_INFINITY;
    for d in 0..=8000 {
        let mut xy = 0.;
        for i in (start..end).step_by(32) {
            xy += out[i] as f64 * near[i - d] as f64;
        }
        if xy > best {
            best = xy;
            lag = d;
        }
    }
    let mut xy = 0.;
    let mut xx = 0.;
    let mut yy = 0.;
    let mut err = 0.;
    let mut rawerr = 0.;
    for i in start..end {
        let x = near[i - lag] as f64;
        let y = out[i] as f64;
        xy += x * y;
        xx += x * x;
        yy += y * y;
        err += (y - x).powi(2);
        rawerr += (microphone[i - lag] as f64 - x).powi(2);
    }
    let gain = xy / xx;
    let correlation = xy / (xx * yy).sqrt();
    let improvement = db(rawerr, err);
    println!("Scenario seed offset={seed}, near-end scale={near_scale}");
    println!(
        "AEC3 stereo 48 s / 48 kHz: colored-noise reference; near-end {} (no speech recording)",
        if voiced {
            "modulated harmonic signal"
        } else {
            "independent colored noise"
        }
    );
    println!("Echo only after convergence: {erle:.2} dB attenuation");
    println!("Acoustic delay changed 50 -> 90 ms: {changed:.2} dB attenuation after reconvergence");
    println!(
        "Synthetic double talk: near-end gain {gain:.3}, correlation {correlation:.3}, error improvement {improvement:.2} dB"
    );
    println!(
        "Synthetic near-end alignment: {lag} samples = {:.3} ms (NOT hardware latency)",
        lag as f64 / 48.
    );
    println!(
        "DSP computation, 192-sample blocks: median {} us; p99 {} us; max {} us; total {:.3} s for 48 s audio",
        timings[timings.len() / 2],
        timings[timings.len() * 99 / 100],
        timings.last().unwrap(),
        elapsed.as_secs_f64()
    );
    println!(
        "Reference missing at the end: {}; engine errors: {}",
        e.missing, e.errors
    );
    if erle < 10.
        || changed < 8.
        || gain < 0.35
        || correlation < 0.5
        || improvement < 0.
        || !e.missing
        || e.errors != 0
        || out.iter().any(|x| !x.is_finite())
    {
        return Err("AEC validation threshold not met; review measurements".into());
    }
    println!("DSP VALIDATION: PASS");
    Ok(())
}

pub fn run(profile: crate::engine::ResidualSuppression) -> Result<(), String> {
    println!("Validation profile: {profile:?}");
    let voiced = scenario(true, 0, 1., profile);
    println!(
        "Harmonic subtest: {}",
        if voiced.is_ok() {
            "PASS at experimental thresholds"
        } else {
            "FAIL"
        }
    );
    let noise = scenario(false, 0, 1., profile);
    println!(
        "Stationary double-signal stress: {}",
        if noise.is_ok() {
            "OK"
        } else {
            "FAIL preservation criterion; quality limitation, do not hide"
        }
    );
    let mut holdout_failed = false;
    for (voiced, seed, scale) in [
        (true, 173, 0.5),
        (false, 819, 0.5),
        (true, 417, 1.5),
        (false, 631, 1.5),
    ] {
        if scenario(voiced, seed, scale, profile).is_err() {
            holdout_failed = true;
            println!("Additional scenario FAILED");
        }
    }
    if voiced.is_err() || noise.is_err() || holdout_failed {
        Err("quality not validated for all signals; read VALIDATION-EN.md".into())
    } else {
        Ok(())
    }
}
