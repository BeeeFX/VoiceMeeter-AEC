# Licenses and provenance

VoiceMeeter AEC project code is licensed under GPL-3.0-only. See LICENSE. This is an independent experimental project for VoiceMeeter Potato.

The three headers in vendor/asio come from audiosdk/asio commit 496a0765b8bb9c26f764f22f9a9712a937177db2. This distribution explicitly uses the GPL version 3 option of vendor/asio/LICENSE.txt, not its proprietary licensing option.

Rust dependencies are pinned in Cargo.lock and included as source in vendor/rust, with their own licenses (BSD-3-Clause, MIT, Apache-2.0 or the options specified by each manifest). vendor/SONORA-LICENSE.txt supplies attribution for sonora-aec3, whose crates.io archive declares BSD-3-Clause but omits the license file. It includes WebRTC Project Authors, Arun Raghavan and dignifiedquire notices.

Architecture inspiration and inspection: windows-aec-bridge commit c24505d85df88dce0d7335f6e24b80dd61d3de74, MIT. Its WASAPI transport and GUI are not incorporated. Its license is preserved in vendor/UPSTREAM-MIT.txt.

ASIO and VoiceMeeter names belong to their respective owners. Installed audio drivers are not redistributed. See Build.cmd and GUIDE-EN.md for offline reconstruction with Rust and Visual C++/Windows SDK already installed. Corresponding project source and vendored dependencies accompany the binary.

Version 0.1.3 selects the local BSD-3-Clause Sonora source in patches/sonora. Its modifications are documented in patches/README.md; pristine source remains in vendor/rust/sonora. Corresponding patched source is included in release archives.
