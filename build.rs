fn main() {
    cc::Build::new()
        .cpp(true)
        .file("src/asio_host.cpp")
        .include("vendor/asio")
        .flag("/std:c++17")
        .compile("asio_host");
    println!("cargo:rustc-link-lib=ole32");
    println!("cargo:rustc-link-lib=user32");
    println!("cargo:rustc-link-lib=advapi32");
    println!("cargo:rerun-if-changed=src/asio_host.cpp");
}
