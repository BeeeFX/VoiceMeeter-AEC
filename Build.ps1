param([switch]$SkipUiChecks)
$ErrorActionPreference='Stop'
Push-Location $PSScriptRoot
$previousFlags=$env:CARGO_ENCODED_RUSTFLAGS
try {
 # Keep developer account/folder names out of panic paths in public binaries.
 $flags=@()
 if($previousFlags){$flags+=$previousFlags.Split([char]31)}
 if($env:USERPROFILE){$flags+='--remap-path-prefix='+$env:USERPROFILE+'=/user'}
 $flags+='--remap-path-prefix='+$PSScriptRoot+'=/voicemeeter-aec'
 $flags+='--remap-path-prefix='+$PSScriptRoot.Replace('\','/')+'=/voicemeeter-aec'
 $env:CARGO_ENCODED_RUSTFLAGS=$flags -join [char]31
 & cargo build --offline --locked --release
 if($LASTEXITCODE -ne 0){throw 'Release build failed'}
 & cargo test --offline --locked --release
 if($LASTEXITCODE -ne 0){throw 'Unit tests failed'}
 foreach($profile in @('gentle','balanced')){
  & .\target\release\voicemeeter-aec.exe --self-test --suppression $profile
  if($LASTEXITCODE -ne 0){throw ('Audio validation failed: '+$profile)}
 }
 Copy-Item -LiteralPath target/release/voicemeeter-aec.exe -Destination voicemeeter-aec.exe
 if(-not $SkipUiChecks){
  $testDirectory=Join-Path ([IO.Path]::GetTempPath()) ('voicemeeter-aec-check-'+[Guid]::NewGuid().ToString('N'))
  foreach($language in @('en','fr')){
   & powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File .\Lanceur.ps1 -CheckUi -Language $language -TestDataPath (Join-Path $testDirectory $language)
   if($LASTEXITCODE -ne 0){throw ('Launcher checks failed: '+$language)}
  }
 }
 Write-Output 'Build and validation passed. Strong is optional and has documented quality failures.'
}finally{$env:CARGO_ENCODED_RUSTFLAGS=$previousFlags;Pop-Location}
