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
 $previousDotnetHome=$env:DOTNET_CLI_HOME
 $previousTelemetry=$env:DOTNET_CLI_TELEMETRY_OPTOUT
 # Keep the runtime-pack cache stable across restore and publish. The folder is ignored by Git.
 $env:DOTNET_CLI_HOME=Join-Path $PSScriptRoot '.dotnet-home'
 $env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
 $appProject=Join-Path $PSScriptRoot 'app\VoiceMeeterAEC.App.csproj'
 $appOutput=Join-Path $PSScriptRoot 'target\release\app'
 & dotnet restore $appProject -r win-x64 --ignore-failed-sources
 if($LASTEXITCODE -ne 0){throw 'Desktop app restore failed'}
 & dotnet publish $appProject -c Release -r win-x64 --self-contained true --no-restore -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=None -p:DebugSymbols=false -o $appOutput
 if($LASTEXITCODE -ne 0){throw 'Desktop app build failed'}
 Copy-Item -LiteralPath target/release/voicemeeter-aec.exe -Destination (Join-Path $appOutput 'voicemeeter-aec.exe') -Force
 foreach($document in @('GUIDE-EN.md','GUIDE-FR.md','THIRD-PARTY-EN.md','THIRD-PARTY.md','LICENSE')){Copy-Item -LiteralPath $document -Destination $appOutput -Force}
 Copy-Item -LiteralPath 'vendor\DOTNET-LICENSE.txt' -Destination $appOutput -Force
 if(-not $SkipUiChecks){
  $appExecutable=Join-Path $appOutput 'VoiceMeeter AEC.exe'
  $uiCheck=Start-Process -FilePath $appExecutable -ArgumentList @('--check-ui','--dark') -WindowStyle Hidden -Wait -PassThru
  if($uiCheck.ExitCode -ne 0){throw 'Desktop app checks failed'}
  foreach($page in @('setup','guide','advanced')){
   $previewPath=Join-Path $PSScriptRoot ("docs\images\app-$page.png")
   $previewArguments=@('--preview',$previewPath,'--preview-page',$page,'--dark')
   if($page -eq 'guide'){$previewArguments+='--preview-bottom'}
   $preview=Start-Process -FilePath $appExecutable -ArgumentList $previewArguments -Wait -PassThru
   if($preview.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $previewPath)){throw "Desktop app $page preview failed"}
  }
 }
 Write-Output 'Audio engine, desktop app and validation passed. Strong is optional and has documented quality failures.'
}finally{
 $env:CARGO_ENCODED_RUSTFLAGS=$previousFlags
 if($null -eq $previousDotnetHome){Remove-Item Env:DOTNET_CLI_HOME -ErrorAction SilentlyContinue}else{$env:DOTNET_CLI_HOME=$previousDotnetHome}
 if($null -eq $previousTelemetry){Remove-Item Env:DOTNET_CLI_TELEMETRY_OPTOUT -ErrorAction SilentlyContinue}else{$env:DOTNET_CLI_TELEMETRY_OPTOUT=$previousTelemetry}
 Pop-Location
}
