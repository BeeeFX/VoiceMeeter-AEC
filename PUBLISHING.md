# Publishing VoiceMeeter AEC

The Source archive is the repository content. Extract it and commit the files inside it, with README.md and Cargo.toml at the repository root. A suitable repository name is `voicemeeter-aec`.

The Windows-x64 archive is the downloadable release. It includes the executable, launcher, corresponding source, vendored dependencies and licenses. Attach it and its SHA-256 file to the GitHub release. Do not commit the ZIP or the executable to the source repository.

Run Build.cmd on Windows x64 with Rust, Visual C++ and Windows SDK installed. It builds offline, remaps local source paths out of the binary, runs unit and synthetic audio checks for Gentle/Balanced, and checks the bilingual launcher and background control path without opening audio. Strong has documented near-end preservation failures and is not included in the passing quality gate.

Run `powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\Package.ps1` after a successful build. It creates fresh Source and Windows-x64 archives under `dist`, with SHA-256 files. Packaging uses an explicit file list and never reads the user's saved application settings. Existing archives with the same version are not overwritten; use a new output directory when repackaging.

Keep the GPL license, third-party notices and dependency license files. Review VALIDATION-EN.md before describing the release: it is experimental, and real startup/driver/speech tests remain outstanding. The project is independent of VB-Audio and Steinberg.

The public package contains generic channel examples and synthetic validation transcripts. Personal routing presets, startup shortcuts, local diagnostic logs, build outputs and repository metadata are excluded. The runtime saves user settings separately under the user's local application-data folder.
