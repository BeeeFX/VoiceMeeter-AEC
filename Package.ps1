param([string]$OutputDirectory, [string]$StageDirectory)
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
if(-not $OutputDirectory){$OutputDirectory=Join-Path $PSScriptRoot 'dist'}
if(-not $StageDirectory){$StageDirectory=Join-Path ([IO.Path]::GetTempPath()) ('voicemeeter-aec-package-'+[Guid]::NewGuid().ToString('N'))}
$OutputDirectory=[IO.Path]::GetFullPath($OutputDirectory)
$StageDirectory=[IO.Path]::GetFullPath($StageDirectory)
if(Test-Path -LiteralPath $StageDirectory){throw 'Use a fresh staging directory'}
$manifest=Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Cargo.toml') -Raw
$version=[regex]::Match($manifest,'(?m)^version = "([0-9.]+)"').Groups[1].Value
if(-not $version){throw 'Package version missing'}
$sourceZip=Join-Path $OutputDirectory ('VoiceMeeter-AEC-'+$version+'-Source.zip')
$windowsZip=Join-Path $OutputDirectory ('VoiceMeeter-AEC-'+$version+'-Windows-x64.zip')
foreach($path in @($sourceZip,$windowsZip,($sourceZip+'.sha256'),($windowsZip+'.sha256'))){if(Test-Path -LiteralPath $path){throw ('Archive already exists: '+$path)}}
$executable=Join-Path $PSScriptRoot 'voicemeeter-aec.exe'
if(-not (Test-Path -LiteralPath $executable)){throw 'Run Build.cmd first'}
$exeBytes=[IO.File]::ReadAllBytes($executable)
foreach($encoding in @([Text.Encoding]::UTF8,[Text.Encoding]::Unicode)){
 if($encoding.GetString($exeBytes) -match '(?i)[A-Z]:[\\/]Users[\\/]'){throw 'Binary contains a local user path; rebuild with Build.cmd'}
}
$source=Join-Path $StageDirectory 'source';$windows=Join-Path $StageDirectory 'windows'
[void][IO.Directory]::CreateDirectory($source)
[void][IO.Directory]::CreateDirectory($OutputDirectory)
$files=@('.gitignore','.gitattributes','Cargo.toml','Cargo.lock','build.rs','Build.cmd','Build.ps1','Compiler.cmd','Package.ps1','Start.cmd','Start.vbs','Demarrer.cmd','Lanceur.ps1','UI-i18n.ps1','UI-runtime.ps1','UI-tips.ps1','LauncherHost.cs','README.md','GUIDE-EN.md','GUIDE-FR.md','VALIDATION-EN.md','VALIDATION.md','THIRD-PARTY-EN.md','THIRD-PARTY.md','CHANGELOG.md','PUBLISHING.md','LICENSE')
foreach($file in $files){Copy-Item -LiteralPath (Join-Path $PSScriptRoot $file) -Destination (Join-Path $source $file)}
foreach($directory in @('.cargo','src','vendor','patches','docs')){Copy-Item -LiteralPath (Join-Path $PSScriptRoot $directory) -Destination $source -Recurse}
foreach($file in Get-ChildItem -LiteralPath $source -File -Recurse -Force){
 $relative=$file.FullName.Substring($source.Length+1).Replace('\','/')
 if($relative -match '^(target|work|dist)/|(^|/)\.git(/|$)|(^|/)(ui-settings|settings)\.json(\.tmp)?$|\.(log|lnk|pdb|obj|exe|dll)$'){throw ('Unexpected source artifact: '+$relative)}
 if($file.Extension -in @('.rs','.cpp','.h','.cs','.ps1','.cmd','.vbs','.md','.toml','.json','.txt')){
  if([IO.File]::ReadAllText($file.FullName) -match '(?i)[A-Z]:[\\/]Users[\\/]|Documents[\\/]Codex'){throw ('Local path in source: '+$relative)}
 }
}
Copy-Item -LiteralPath $source -Destination $windows -Recurse
Copy-Item -LiteralPath $executable -Destination (Join-Path $windows 'voicemeeter-aec.exe')
foreach($pair in @(@($source,$sourceZip),@($windows,$windowsZip))){
 [IO.Compression.ZipFile]::CreateFromDirectory($pair[0],$pair[1],[IO.Compression.CompressionLevel]::Optimal,$false)
 $hash=(Get-FileHash -LiteralPath $pair[1] -Algorithm SHA256).Hash.ToLowerInvariant()
 ($hash+'  '+[IO.Path]::GetFileName($pair[1])) | Set-Content -LiteralPath ($pair[1]+'.sha256') -Encoding ASCII
 Write-Output ([IO.Path]::GetFileName($pair[1])+'  '+$hash)
}
