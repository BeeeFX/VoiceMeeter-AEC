mod engine;
mod validation;
use engine::{Engine, ResidualSuppression};
use std::ffi::c_void;
unsafe extern "C" {
    fn asio_run(
        engine: *mut c_void,
        mic: i32,
        reference_left_mask: u64,
        reference_right_mask: u64,
        returns: u64,
        mode: i32,
        seconds: i32,
        probe: i32,
        auto_mask: i32,
        auto_bus: i32,
        final_mode: *mut i32,
    ) -> i32;
    fn asio_transport_test() -> i32;
    fn asio_callback_selftest(engine: *mut c_void) -> i32;
    fn remote_check() -> i32;
    fn asio_control_selftest() -> i32;
}
#[unsafe(no_mangle)]
unsafe extern "C" fn aec_block(
    ctx: *mut c_void,
    mic: *const f32,
    left: *const f32,
    right: *const f32,
    out: *mut f32,
    n: usize,
    mode: i32,
    stats: *mut f32,
) {
    let e = unsafe { &mut *(ctx as *mut Engine) };
    let (mic, left, right, out) = unsafe {
        (
            std::slice::from_raw_parts(mic, n),
            std::slice::from_raw_parts(left, n),
            std::slice::from_raw_parts(right, n),
            std::slice::from_raw_parts_mut(out, n),
        )
    };
    for i in 0..n {
        out[i] = e.tick(mic[i], left[i], right[i], mode);
    }
    unsafe {
        *stats = e.mic_peak;
        *stats.add(1) = e.ref_peak;
        *stats.add(2) = if e.missing { 1. } else { 0. };
        *stats.add(3) = e.errors as f32;
    }
}
fn main() {
    if let Err(e) = cli() {
        eprintln!("Error: {e}");
        std::process::exit(1);
    }
}
fn cli() -> Result<(), String> {
    let args: Vec<String> = std::env::args().skip(1).collect();
    if args.is_empty() || args.iter().any(|x| x == "--help") {
        println!(
            "VoiceMeeter AEC — audio engine · 48 kHz\nNo arguments: no audio device is opened.\n\
--self-test : synthetic validation without a driver\n--probe : query Insert Potato without streaming\n\
--run --mic 1 --ref 11,12[,19,20...] --returns 1,2 [--bypass] [--mute] [--auto]\n\
  [--delay-ms 0] [--hold-ms 0] [--suppression gentle|balanced|strong] [--seconds 60] [--auto-strips 6,7,8|all --auto-bus 2]\n\
Default mode: Auto, strip 6 to A2. --aec selects manual AEC.\n\
Channels, Auto strips (1..8) and Auto A bus (1..5) are numbered from 1.\n\
Keys: A=AEC B=bypass M=mute T=auto Q=quit.\n\
Does not change PATCH settings. Read GUIDE-EN.md before --run."
        );
        return Ok(());
    }
    if args == ["--auto-check"] {
        return if unsafe { remote_check() } == 0 {
            Ok(())
        } else {
            Err("Remote API read unavailable".into())
        };
    }
    if args == ["--control-self-test"] {
        return if unsafe { asio_control_selftest() } == 0 {
            Ok(())
        } else {
            Err("Control pipe test failed".into())
        };
    }
    if args.first().is_some_and(|x| x == "--self-test") {
        let profile = match args.as_slice() {
            [_] => ResidualSuppression::Gentle,
            [_, key, value] if key == "--suppression" => parse_suppression(value)?,
            _ => return Err("usage: --self-test [--suppression gentle|balanced|strong]".into()),
        };
        if unsafe { asio_transport_test() } != 0 {
            return Err("C++ transport test failed".into());
        }
        let mut e = Engine::new(0, 0);
        if unsafe { asio_callback_selftest(&mut e as *mut Engine as *mut c_void) } != 0 {
            return Err("Native callback integration test failed".into());
        }
        return validation::run(profile);
    }
    if args == ["--probe"] {
        let c = unsafe {
            asio_run(
                std::ptr::null_mut(),
                0,
                0,
                0,
                0,
                0,
                0,
                1,
                0,
                1,
                std::ptr::null_mut(),
            )
        };
        return if c == 0 {
            Ok(())
        } else {
            Err(format!("driver error {c}"))
        };
    }
    let (mut mic, mut reference, mut returns) = (1, None, vec![1, 2]);
    let (mut delay, mut hold, mut seconds, mut mode, mut run, mut auto_bus) =
        (0, 0, 0, 3, false, 2);
    let mut auto_strips = String::from("6");
    let mut suppression = ResidualSuppression::Gentle;
    let mut i = 0;
    while i < args.len() {
        match args[i].as_str() {
            "--run" => run = true,
            "--bypass" => mode = 1,
            "--mute" => mode = 2,
            "--auto" => mode = 3,
            "--aec" => mode = 0,
            "--mic" | "--ref" | "--returns" | "--delay-ms" | "--hold-ms" | "--seconds"
            | "--suppression" | "--auto-strip" | "--auto-strips" | "--auto-bus" => {
                let key = &args[i];
                i += 1;
                let v = args.get(i).ok_or("missing value")?;
                let parse = |s: &str| s.parse::<i32>().map_err(|_| format!("invalid integer {s}"));
                match key.as_str() {
                    "--mic" => mic = parse(v)?,
                    "--ref" => reference = Some(parse_reference_pairs(v)?),
                    "--returns" => {
                        returns = v.split(',').map(parse).collect::<Result<Vec<_>, _>>()?
                    }
                    "--delay-ms" => delay = parse(v)?,
                    "--hold-ms" => hold = parse(v)?,
                    "--auto-strip" | "--auto-strips" => auto_strips = v.clone(),
                    "--auto-bus" => auto_bus = parse(v)?,
                    "--suppression" => suppression = parse_suppression(v)?,
                    _ => seconds = parse(v)?,
                }
            }
            x => return Err(format!("unknown option {x}")),
        }
        i += 1;
    }
    if !run {
        return Err("--run is required".into());
    }
    let references = reference.ok_or("reference required: --ref L,R[,L,R...]")?;
    if std::iter::once(&mic)
        .chain(references.iter().flat_map(|(left, right)| [left, right]))
        .chain(&returns)
        .any(|x| !(1..=34).contains(x))
        || returns.is_empty()
    {
        return Err("channels must be within 1..34".into());
    }
    if references
        .iter()
        .flat_map(|(left, right)| [left, right])
        .any(|channel| *channel == mic || returns.contains(channel))
    {
        return Err("reference channels must not overlap the microphone or its returns".into());
    }
    if !returns.contains(&mic) {
        return Err("returns must include the microphone channel".into());
    }
    if !(0..=500).contains(&delay)
        || !(0..=250).contains(&hold)
        || seconds < 0
        || !(1..=5).contains(&auto_bus)
    {
        return Err("parameter out of range".into());
    }
    println!(
        "Mic {mic}; reference pairs {references:?}; returns {returns:?}.\nFixed buffer {} ms; AEC delay estimate {delay} ms.",
        10 + hold
    );
    println!("Residual suppression: {suppression:?} (Standard = Strong / upstream)");
    let auto_mask = auto_strip_mask(&auto_strips, &returns)?;
    println!(
        "Auto strips mask: {auto_mask:#04x} -> A{auto_bus}; route state, not audio-level detection."
    );
    let mask = returns.iter().fold(0u64, |a, c| a | (1u64 << (c - 1)));
    let reference_left_mask = references
        .iter()
        .fold(0u64, |mask, (channel, _)| mask | (1u64 << (channel - 1)));
    let reference_right_mask = references
        .iter()
        .fold(0u64, |mask, (_, channel)| mask | (1u64 << (channel - 1)));
    let started = std::time::Instant::now();
    let mut recovery = Recovery::default();
    loop {
        let mut e = Engine::with_suppression(delay, hold as usize, suppression);
        let remaining = if seconds == 0 {
            0
        } else {
            seconds - (started.elapsed().as_secs() as i32)
        };
        if seconds > 0 && remaining <= 0 {
            return Ok(());
        }
        let c = unsafe {
            asio_run(
                &mut e as *mut Engine as *mut c_void,
                mic - 1,
                reference_left_mask,
                reference_right_mask,
                mask,
                mode,
                remaining,
                0,
                auto_mask,
                auto_bus - 1,
                &mut mode,
            )
        };
        if c == 0 {
            return Ok(());
        } else {
            if let Some(wait) = recovery.retry_delay(c) {
                eprintln!(
                    "Driver interrupted. Retrying the same Insert driver in {wait} ms (attempt {}/2); mode {mode} preserved. Ctrl+C cancels.",
                    recovery.attempts
                );
                std::thread::sleep(std::time::Duration::from_millis(wait));
            } else {
                return Err(format!(
                    "driver stopped with code {c}; disable PATCH INSERT to restore the direct microphone"
                ));
            }
        }
    }
}

fn parse_reference_pairs(value: &str) -> Result<Vec<(i32, i32)>, String> {
    let channels = value
        .split(',')
        .map(|entry| {
            entry
                .parse::<i32>()
                .map_err(|_| "reference channels must be comma-separated integers".to_string())
        })
        .collect::<Result<Vec<_>, _>>()?;
    if channels.len() < 2 || channels.len() % 2 != 0 {
        return Err("--ref requires one or more L,R pairs".into());
    }
    let pairs = channels
        .chunks_exact(2)
        .map(|pair| (pair[0], pair[1]))
        .collect::<Vec<_>>();
    if pairs.iter().any(|pair| pair.0 == pair.1) {
        return Err("each reference pair requires different left and right channels".into());
    }
    Ok(pairs)
}

fn auto_strip_mask(value: &str, returns: &[i32]) -> Result<i32, String> {
    if value == "all" {
        let mut mask = 255;
        for c in returns {
            let strip = match c {
                1..=10 => (c - 1) / 2,
                11..=18 => 5,
                19..=26 => 6,
                _ => 7,
            };
            mask &= !(1 << strip);
        }
        if mask == 0 {
            return Err("Auto has no playback strips after excluding microphone returns".into());
        }
        return Ok(mask);
    }
    let mut mask = 0;
    for entry in value.split(',') {
        let strip = entry
            .parse::<i32>()
            .map_err(|_| "Auto strips must be a comma-separated list or all")?;
        if !(1..=8).contains(&strip) {
            return Err("Auto strips must be within 1..8".into());
        }
        mask |= 1 << (strip - 1);
    }
    Ok(mask)
}

fn parse_suppression(value: &str) -> Result<ResidualSuppression, String> {
    match value {
        "gentle" => Ok(ResidualSuppression::Gentle),
        "balanced" => Ok(ResidualSuppression::Balanced),
        "strong" => Ok(ResidualSuppression::Standard),
        _ => Err("suppression must be gentle, balanced or strong".into()),
    }
}

#[derive(Default)]
struct Recovery {
    attempts: usize,
}
impl Recovery {
    fn retry_delay(&mut self, code: i32) -> Option<u64> {
        // Only a driver reset/resync or callback stall is retryable. Never retry
        // user quit, a sample-rate change, invalid buffers, or an unknown error.
        if ![34, 35].contains(&code) || self.attempts >= 2 {
            return None;
        }
        let wait = [250, 1000][self.attempts];
        self.attempts += 1;
        Some(wait)
    }
}
#[cfg(test)]
mod recovery_tests {
    use super::Recovery;
    #[test]
    fn auto_masks_cover_lists_and_exclude_microphone_return_strips() {
        assert_eq!(super::auto_strip_mask("6,7,8,6", &[1, 2]).unwrap(), 224);
        assert_eq!(super::auto_strip_mask("all", &[1, 2]).unwrap(), 254);
        assert_eq!(
            super::auto_strip_mask("all", &[11, 12, 19, 34]).unwrap(),
            31
        );
        for invalid in ["", "0", "9", "6,", "6.5", "-1"] {
            assert!(super::auto_strip_mask(invalid, &[1, 2]).is_err());
        }
    }
    #[test]
    fn reference_parser_accepts_multiple_stereo_pairs() {
        assert_eq!(
            super::parse_reference_pairs("11,12").unwrap(),
            vec![(11, 12)]
        );
        assert_eq!(
            super::parse_reference_pairs("11,12,19,20").unwrap(),
            vec![(11, 12), (19, 20)]
        );
        for invalid in ["", "11", "11,12,19", "11,left", "11,11"] {
            assert!(super::parse_reference_pairs(invalid).is_err());
        }
    }
    #[test]
    fn recovery_is_bounded_and_selective() {
        for code in [0, 13, 16, 17, 20, 22, 31, 32, 33, 36] {
            assert_eq!(Recovery::default().retry_delay(code), None);
        }
        let mut r = Recovery::default();
        assert_eq!(r.retry_delay(34), Some(250));
        assert_eq!(r.retry_delay(35), Some(1000));
        assert_eq!(r.retry_delay(34), None);
    }
}
