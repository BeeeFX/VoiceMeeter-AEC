# Publishing VoiceMeeter AEC

The Source archive is the repository content. Extract it and commit the files inside it, with README.md and Cargo.toml at the repository root. A suitable repository name is `voicemeeter-aec`.

The Windows-x64 archive is the downloadable release. It contains the self-contained **VoiceMeeter AEC.exe** desktop app, the audio engine, setup guides and licenses. The separate Source archive contains the corresponding source and vendored Rust dependencies. Attach both archives and their SHA-256 files to the GitHub release. Do not commit ZIP or executable build outputs to the source repository.

Run Build.cmd on Windows x64 with Rust, Visual C++, Windows SDK and the .NET 8 SDK or newer installed. It remaps local source paths out of the native engine, publishes a self-contained x64 desktop app, runs unit and synthetic audio checks for Gentle/Balanced, validates the bilingual interface, and renders the README screenshot without opening audio. Strong has documented near-end preservation failures and is not included in the passing quality gate.

Run `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Package.ps1` after a successful build. It creates fresh Source and Windows-x64 archives under `dist`, with SHA-256 files. Packaging uses an explicit file list and never reads the user's saved application settings. Existing archives with the same version are not overwritten; use a new output directory when repackaging.

Keep the GPL license, third-party notices and dependency license files. Review VALIDATION-EN.md before describing the release: the automated engine and interface checks pass, while controlled room, real-speech, long-duration and hardware-latency measurements remain outstanding. The project is independent of VB-Audio and Steinberg.

The public package contains generic column examples and synthetic validation transcripts. Personal routing presets, Windows startup entries, local diagnostic logs, build outputs and repository metadata are excluded. The runtime saves user settings separately under the user's local application-data folder.
