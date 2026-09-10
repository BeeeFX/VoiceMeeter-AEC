# GPL-3.0-only. Dot-sourced by Lanceur.ps1 after translations and controls exist.
if($TestDataPath -and -not $CheckUi){throw 'TestDataPath requires CheckUi'}
Add-Type -Path (Join-Path $PSScriptRoot 'LauncherHost.cs')
$script:child=$null;$script:exitRequested=$false;$script:startupPending=$false
$script:startupDeadline=[DateTime]::Now.AddSeconds(90)
$script:nextAttempt=[DateTime]::Now.AddSeconds(5)
$dataPath=Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'VoiceMeeterAEC'
$startupPath=[Environment]::GetFolderPath('Startup')
if($CheckUi){
 if($TestDataPath){$dataPath=[IO.Path]::GetFullPath($TestDataPath);$startupPath=Join-Path $dataPath 'startup'}
}else{
 $created=$false;$instance=New-Object Threading.Mutex($false,'Local\VoiceMeeterAECLauncher',([ref]$created))
 $showSignal=New-Object Threading.EventWaitHandle($false,[Threading.EventResetMode]::AutoReset,'Local\VoiceMeeterAECShowSettings')
 if(-not $created){
  if(-not $Startup){[void]$showSignal.Set()}
  $showSignal.Dispose();$instance.Dispose();$form.Dispose();exit
 }
}
$settingsPath=Join-Path $dataPath 'settings.json'
$shortcutPath=Join-Path $startupPath 'VoiceMeeter AEC.lnk'
$logPath=Join-Path $dataPath 'engine.log'
function ShowError([string]$message){[void][Windows.Forms.MessageBox]::Show((Tr $message),'VoiceMeeter AEC')}
function SetStatus([string]$message){$status.Tag=$message;$status.Text=Tr $message;$tray.Text=('VoiceMeeter AEC - '+$status.Text).Substring(0,[Math]::Min(63,('VoiceMeeter AEC - '+$status.Text).Length))}
function Notify([string]$message){$tray.ShowBalloonTip(5000,'VoiceMeeter AEC',(Tr $message),[Windows.Forms.ToolTipIcon]::Warning)}
function ReadSettings {
 if(-not (Test-Path -LiteralPath $settingsPath)){return}
 $saved=Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json
 if($saved.schema -ne 1){throw 'Unsupported settings version'}
 foreach($pair in @(@('mic',$mic),@('refL',$refL),@('refR',$refR),@('hold',$hold),@('delay',$delay))){
  $v=$saved.($pair[0]);$control=$pair[1]
  if($null -eq $v -or $v -notmatch '^\d+$' -or [decimal]$v -lt $control.Minimum -or [decimal]$v -gt $control.Maximum){throw ('Invalid saved setting: '+$pair[0])}
  $control.Value=[decimal]$v
 }
 if($saved.mode -notin @(0,1,2,3) -or $saved.suppression -notin @('gentle','balanced','strong')){throw 'Invalid saved mode/profile'}
 if($saved.bus -notin @(1,2,3,4,5) -or $saved.autoScope -notin @(0,1)){throw 'Invalid saved Auto settings'}
 $bus.SelectedIndex=[int]$saved.bus-1;$autoScope.SelectedIndex=[int]$saved.autoScope;$strip.Text=[string]$saved.strips
 $ret.Text=[string]$saved.returns;$mode.SelectedIndex=[int]$saved.mode
 $suppression.SelectedIndex=[Array]::IndexOf(@('gentle','balanced','strong'),[string]$saved.suppression)
 if(-not $script:languageExplicit){if($saved.language -in @('en','fr')){$languageBox.SelectedIndex=if($saved.language -eq 'fr'){1}else{0}}}
 [void](GetEngineArguments)
}
function SetStartupShortcut([bool]$enabled){
 $shell=New-Object -ComObject WScript.Shell
 try {
  if(Test-Path -LiteralPath $shortcutPath){
   $existing=$shell.CreateShortcut($shortcutPath)
   if($existing.Description -ne 'VoiceMeeter AEC startup'){throw 'The startup shortcut name is already used by another application'}
  }
  if($enabled){
   [void][IO.Directory]::CreateDirectory($startupPath)
   $shortcut=$shell.CreateShortcut($shortcutPath)
   $shortcut.TargetPath=Join-Path $PSHOME 'powershell.exe'
   $shortcut.Arguments='-NoProfile -STA -WindowStyle Hidden -ExecutionPolicy Bypass -File "'+(Join-Path $PSScriptRoot 'Lanceur.ps1')+'" -Startup'
   $shortcut.WorkingDirectory=$PSScriptRoot;$shortcut.WindowStyle=7
   $shortcut.Description='VoiceMeeter AEC startup';$shortcut.Save()
  }elseif(Test-Path -LiteralPath $shortcutPath){Remove-Item -LiteralPath $shortcutPath}
 }finally{[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)}
}
function SaveSettings {
 [void](GetEngineArguments)
 [void][IO.Directory]::CreateDirectory($dataPath)
 $settings=@{schema=1;language=$script:uiLanguage;mic=[int]$mic.Value;refL=[int]$refL.Value;refR=[int]$refR.Value;returns=$ret.Text;hold=[int]$hold.Value;delay=[int]$delay.Value;strips=$strip.Text;autoScope=$autoScope.SelectedIndex;bus=($bus.SelectedIndex+1);mode=$mode.SelectedIndex;suppression=@('gentle','balanced','strong')[$suppression.SelectedIndex]}
 $temporary=$settingsPath+'.tmp'
 $settings | ConvertTo-Json | Set-Content -LiteralPath $temporary -Encoding UTF8
 Move-Item -LiteralPath $temporary -Destination $settingsPath -Force
 SetStartupShortcut $startupBox.Checked
}
function StartEngine {
 if($script:child -and $script:child.Alive){return}
 $arguments=GetEngineArguments
 [void][IO.Directory]::CreateDirectory($dataPath)
 if($script:child){$script:child.Dispose()}
 $script:child=New-Object AecLauncherHost
 $script:child.Start((Join-Path $PSScriptRoot 'voicemeeter-aec.exe'),$arguments,$PSScriptRoot,$logPath)
 SetStatus 'Démarrage du moteur…'
 $start.Enabled=$false
}
function StopEngine {
 if($script:child -and $script:child.Alive){
  $answer=[Windows.Forms.MessageBox]::Show((Tr 'Désactivez PATCH INSERT dans VoiceMeeter avant de continuer. Continuer ?'),'VoiceMeeter AEC','OKCancel','Warning')
  if($answer -ne 'OK'){return $false}
  $script:child.Send('q')
  if(-not $script:child.WaitForExit(2000)){ShowError 'Le moteur ne répond pas encore. Consultez les diagnostics avant de réessayer.';return $false}
 }
 $script:startupPending=$false
 return $true
}
function SendControl([string]$key){try{if($script:child){$script:child.Send($key)}}catch{ShowError $_.Exception.Message}}
function ShowSettings {$form.Show();$form.WindowState='Normal';$form.Activate()}
function ShowDiagnostics {
 $diagnostics=New-Object Windows.Forms.Form;$diagnostics.Text='VoiceMeeter AEC - '+(Tr 'Diagnostics');$diagnostics.Size=New-Object Drawing.Size(850,550);$diagnostics.StartPosition='CenterScreen'
 $box=New-Object Windows.Forms.TextBox;$box.Multiline=$true;$box.ReadOnly=$true;$box.ScrollBars='Both';$box.WordWrap=$false;$box.Dock='Fill';$box.Font=New-Object Drawing.Font('Consolas',10)
 $box.Text=if($script:child){$script:child.Diagnostics}elseif(Test-Path -LiteralPath $logPath){Get-Content -LiteralPath $logPath -Raw}else{Tr 'Prêt — aucun flux audio ouvert'}
 $diagnostics.Controls.Add($box);[void]$diagnostics.ShowDialog();$diagnostics.Dispose()
}
$tray=New-Object Windows.Forms.NotifyIcon;$tray.Icon=[Drawing.SystemIcons]::Application;$tray.Text='VoiceMeeter AEC';$tray.Visible=-not $CheckUi
$menu=New-Object Windows.Forms.ContextMenuStrip
function TrayItem([string]$label,[scriptblock]$handler){$item=New-Object Windows.Forms.ToolStripMenuItem;$item.Tag=$label;$item.Text=Tr $label;$item.Add_Click($handler);[void]$menu.Items.Add($item);return $item}
$showItem=TrayItem 'Afficher les réglages' {ShowSettings}
$aecItem=TrayItem 'AEC manuel' {SendControl 'a'}
$bypassItem=TrayItem 'Bypass' {SendControl 'b'}
$muteItem=TrayItem 'Silence micro' {SendControl 'm'}
$autoItem=TrayItem 'Auto' {SendControl 't'}
[void]$menu.Items.Add((New-Object Windows.Forms.ToolStripSeparator))
$stopItem=TrayItem 'Arrêter le moteur' {try{[void](StopEngine)}catch{ShowError $_.Exception.Message}}
$diagItem=TrayItem 'Diagnostics' {ShowDiagnostics}
$quitItem=TrayItem 'Quitter' {try{if(StopEngine){$script:exitRequested=$true;$form.Close()}}catch{ShowError $_.Exception.Message}}
$tray.ContextMenuStrip=$menu;$tray.Add_DoubleClick({ShowSettings})
function UpdateTrayLanguage {foreach($item in $menu.Items){if($item.Tag){$item.Text=Tr ([string]$item.Tag)}}}
UpdateTrayLanguage
$start.Add_Click({try{$script:startupPending=$false;SaveSettings;StartEngine}catch{ShowError $_.Exception.Message}})
$save.Add_Click({try{SaveSettings;SetStatus 'Réglages enregistrés — appliqués au prochain lancement du moteur'}catch{ShowError $_.Exception.Message}})
$form.Add_FormClosing({
 if($_.CloseReason -in @('WindowsShutDown','TaskManagerClosing')){if($script:child -and $script:child.Alive){$script:child.Send('q')};return}
 if(-not $script:exitRequested){$_.Cancel=$true;$form.Hide()}
})
$form.Add_Resize({if($form.WindowState -eq 'Minimized'){$form.Hide()}})
$timer=New-Object Windows.Forms.Timer;$timer.Interval=500
$timer.Add_Tick({
 try {
  if($showSignal.WaitOne(0)){ShowSettings}
  $alive=$script:child -and $script:child.Alive
  $running=$script:child -and $script:child.Running
  foreach($item in @($aecItem,$bypassItem,$muteItem,$autoItem)){$item.Enabled=$running}
  $stopItem.Enabled=$alive -or $script:startupPending;$start.Enabled=-not $alive
  if($alive){
   $messages=@{starting='Démarrage du moteur…';running='Moteur en marche';aec='AEC actif';bypass='Bypass actif';mute='Micro coupé';auto='Auto actif';reconnecting='Reconnexion…';stopped='Reconnexion…'}
   SetStatus $messages[$script:child.Status]
   $tray.Icon=if($running -and $script:child.Status -ne 'mute'){[Drawing.SystemIcons]::Information}else{[Drawing.SystemIcons]::Warning}
   $aecItem.Checked=$script:child.Status -eq 'aec';$bypassItem.Checked=$script:child.Status -eq 'bypass';$muteItem.Checked=$script:child.Status -eq 'mute';$autoItem.Checked=$script:child.Status -eq 'auto'
   if($script:child.EverRunning){$script:startupPending=$false}
  }elseif($script:child){
   [void]$script:child.WaitForExit(0)
   $retry=$script:startupPending -and -not $script:child.EverRunning -and $script:child.Diagnostics -match 'driver stopped with code (13|14|20|35);'
   if($retry -and [DateTime]::Now -lt $script:startupDeadline){
    $script:child.Dispose();$script:child=$null;$script:nextAttempt=[DateTime]::Now.AddSeconds(5);SetStatus 'Attente de VoiceMeeter…'
   }else{
    $script:startupPending=$false;$tray.Icon=[Drawing.SystemIcons]::Warning
    if($status.Tag -ne 'Moteur arrêté — vérifiez les retours PATCH INSERT du micro'){
     SetStatus 'Moteur arrêté — vérifiez les retours PATCH INSERT du micro'
     if($script:child.ExitCode -ne 0){Notify 'Le moteur s''est arrêté. Consultez les diagnostics.'}
    }
   }
  }
  if($script:startupPending -and -not $script:child -and [DateTime]::Now -ge $script:nextAttempt){
   if([DateTime]::Now -ge $script:startupDeadline){$script:startupPending=$false;SetStatus 'VoiceMeeter n''est pas prêt. Ouvrez-le, puis démarrez le moteur depuis les réglages.';Notify $status.Tag}
   elseif(Get-Process -Name 'voicemeeter','voicemeeterpro','voicemeeter8' -ErrorAction SilentlyContinue){StartEngine}
  }
 }catch{$script:startupPending=$false;SetStatus 'Le moteur s''est arrêté. Consultez les diagnostics.';Notify $_.Exception.Message}
})
if(-not $CheckUi){
 try{ReadSettings;$startupBox.Checked=Test-Path -LiteralPath $shortcutPath}
 catch{$Startup=$false;ShowError $_.Exception.Message}
 if($Startup){
  if(-not (Test-Path -LiteralPath $settingsPath)){$Startup=$false;ShowError 'Save settings before enabling startup'}
  else{$script:startupPending=$true;SetStatus 'Attente de VoiceMeeter…';$form.Opacity=0;$form.Add_Shown({$form.Hide();$form.Opacity=1})}
 }
 $timer.Start()
}
function TestRuntime {
 # Isolated settings/shortcut round trip; never installs real startup or opens audio.
 $suppression.SelectedIndex=1;$mode.SelectedIndex=2;$hold.Value=7;$strip.Text='6,7,8';$autoScope.SelectedIndex=1;$bus.SelectedIndex=4;$startupBox.Checked=$true
 $expected=GetEngineArguments;SaveSettings
 $suppression.SelectedIndex=0;$mode.SelectedIndex=0;$hold.Value=0
 ReadSettings
 if((GetEngineArguments) -ne $expected){throw 'Saved audio settings did not round-trip'}
 $shell=New-Object -ComObject WScript.Shell
 try{$link=$shell.CreateShortcut($shortcutPath);if($link.Arguments -notmatch '-WindowStyle Hidden' -or $link.Arguments -notmatch '-Startup' -or $link.WorkingDirectory -ne $PSScriptRoot){throw 'Invalid startup shortcut'}}finally{[void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell)}
 $startupBox.Checked=$false;SaveSettings;if(Test-Path -LiteralPath $shortcutPath){throw 'Startup disabling failed'}
 $hostTest=New-Object AecLauncherHost
 try{
  $hostTest.Start((Join-Path $PSScriptRoot 'voicemeeter-aec.exe'),'--control-self-test',$PSScriptRoot,(Join-Path $dataPath 'control-test.log'))
  $hostTest.Send('m');$hostTest.Send('b');$hostTest.Send('a');$hostTest.Send('t');$hostTest.Send('q')
  if(-not $hostTest.WaitForExit(5000) -or $hostTest.ExitCode -ne 0 -or $hostTest.Diagnostics -notmatch 'Control pipe: PASS'){throw ('Hidden process/control pipe failed: '+$hostTest.Diagnostics)}
 }finally{$hostTest.Dispose()}
 $hold.Value=0;$mode.SelectedIndex=2;$suppression.SelectedIndex=0;$strip.Text='6';$autoScope.SelectedIndex=0;$bus.SelectedIndex=1
 $form.Opacity=0;$form.ShowInTaskbar=$false;$form.Show();$form.Close()
 if($form.IsDisposed -or $form.Visible){throw 'Closing must hide the launcher without disposing it'}
 $form.Show();if(-not $form.Visible){throw 'Tray restore failed'};$form.Hide()
 Write-Output 'Settings, startup shortcut enable/disable and hidden engine controls: PASS (isolated, no audio).'
}
