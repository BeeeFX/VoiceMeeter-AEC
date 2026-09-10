param([switch]$CheckUi, [string]$PreviewPath, [ValidateSet('en','fr')][string]$Language, [switch]$Startup, [string]$TestDataPath)
$ErrorActionPreference='Stop'
$script:languageExplicit=$PSBoundParameters.ContainsKey('Language')
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
[System.Windows.Forms.Application]::EnableVisualStyles()
$form=New-Object System.Windows.Forms.Form
$form.Text='VoiceMeeter AEC — préfiltre ASIO expérimental'
$form.ClientSize=New-Object System.Drawing.Size(690,866)
$form.StartPosition='CenterScreen'
$form.FormBorderStyle='FixedDialog'
$form.MaximizeBox=$false
$form.Font=New-Object System.Drawing.Font('Segoe UI',10)
$form.BackColor=[System.Drawing.Color]::FromArgb(247,249,252)
$form.Padding=New-Object System.Windows.Forms.Padding(12)
function LabelAt([string]$text,[int]$x,[int]$y,[int]$w=650,[int]$h=25){
 $c=New-Object System.Windows.Forms.Label; $c.Text=$text;$c.Location=New-Object System.Drawing.Point($x,$y);$c.Size=New-Object System.Drawing.Size($w,$h);$c.ForeColor=[System.Drawing.Color]::FromArgb(35,45,60);$form.Controls.Add($c)
}
function NumberAt([string]$label,[int]$y,[int]$min,[int]$max,[int]$value){
 LabelAt $label 22 $y 430
 $c=New-Object System.Windows.Forms.NumericUpDown;$c.Minimum=$min;$c.Maximum=$max;$c.Value=$value
 $c.Location=New-Object System.Drawing.Point(475,$y);$c.Size=New-Object System.Drawing.Size(175,26);$form.Controls.Add($c);return $c
}
LabelAt 'VOICEMEETER AEC' 22 16 350 30
$title=$form.Controls[$form.Controls.Count-1];$title.Font=New-Object System.Drawing.Font('Segoe UI Semibold',16);$title.ForeColor=[System.Drawing.Color]::FromArgb(34,83,145)
LabelAt 'Préfiltre AEC3 • Insert ASIO • 48 kHz' 22 45 400 24
LabelAt 'Aucun réglage audio n''est modifié. Commencez en bypass pour vérifier le transport.' 22 69 646 24
$mic=NumberAt 'Canal micro physique (IN1 gauche = 1)' 86 1 34 1
$refL=NumberAt 'Canal de référence gauche' 122 1 34 11
$refR=NumberAt 'Canal de référence droite' 158 1 34 12
LabelAt 'Canaux de retour micro (séparés par virgule)' 22 194 430
$ret=New-Object System.Windows.Forms.TextBox;$ret.Text='1,2';$ret.Location=New-Object System.Drawing.Point(475,194);$ret.Size=New-Object System.Drawing.Size(175,26);$form.Controls.Add($ret)
$hold=NumberAt 'Maintien réel du micro, en ms (départ : 0)' 230 0 250 0
$delay=NumberAt 'Estimation du délai AEC, en ms (départ : 0)' 266 0 500 0
LabelAt 'Pistes surveillées en Auto' 22 302 430
$strip=New-Object System.Windows.Forms.TextBox;$strip.Text='6';$strip.Location=New-Object System.Drawing.Point(475,302);$strip.Size=New-Object System.Drawing.Size(175,26);$form.Controls.Add($strip)
LabelAt 'Bus de sortie surveillé' 22 338 430
$bus=New-Object System.Windows.Forms.ComboBox;$bus.DropDownStyle='DropDownList';$bus.Items.AddRange(@('A1','A2','A3','A4','A5'));$bus.SelectedIndex=1;$bus.Location=New-Object System.Drawing.Point(475,338);$bus.Size=New-Object System.Drawing.Size(175,26);$form.Controls.Add($bus)
LabelAt 'Mode au démarrage' 22 374 430
$mode=New-Object System.Windows.Forms.ComboBox;$mode.DropDownStyle='DropDownList';$mode.Items.AddRange(@('Bypass','AEC manuel','Auto','Silence micro'));$mode.SelectedIndex=2;$mode.Location=New-Object System.Drawing.Point(420,374);$mode.Size=New-Object System.Drawing.Size(230,26);$form.Controls.Add($mode)
LabelAt 'RÉFÉRENCE ET AUTOMATISME' 22 416 646 22
$section=$form.Controls[$form.Controls.Count-1];$section.Font=New-Object System.Drawing.Font('Segoe UI Semibold',9);$section.ForeColor=[System.Drawing.Color]::FromArgb(34,83,145)
LabelAt 'La référence et Auto sont indépendants. La référence doit exclure le micro.' 22 440 646 25
LabelAt 'Menu de notification : AEC · bypass · silence · Auto · diagnostics · quitter' 22 468 646 25
LabelAt 'Avant de quitter : désactivez les retours PATCH INSERT du micro.' 22 496 646 25
$status=LabelAt 'Prêt — aucun flux audio ouvert' 22 548 400 25
$status=$form.Controls[$form.Controls.Count-1];$status.ForeColor=[System.Drawing.Color]::FromArgb(70,90,110)
$start=New-Object System.Windows.Forms.Button;$start.Text='Ouvrir le moteur';$start.Location=New-Object System.Drawing.Point(450,585);$start.Size=New-Object System.Drawing.Size(200,40);$start.BackColor=[System.Drawing.Color]::FromArgb(34,112,190);$start.ForeColor=[System.Drawing.Color]::White;$start.FlatStyle='Flat';$form.Controls.Add($start)
$guide=New-Object System.Windows.Forms.Button;$guide.Text='Lire le guide';$guide.Location=New-Object System.Drawing.Point(22,585);$guide.Size=New-Object System.Drawing.Size(170,40);$guide.FlatStyle='Flat';$form.Controls.Add($guide)
$guide.Add_Click({$file=if($script:uiLanguage -eq 'fr'){'GUIDE-FR.md'}else{'GUIDE-EN.md'};Start-Process notepad.exe -ArgumentList ('"'+(Join-Path $PSScriptRoot $file)+'"')})
function GetEngineArguments {
  if($ret.Text -notmatch '^\d{1,2}(,\d{1,2})*$'){throw 'Retours : saisir par exemple 1,2 sans espaces.'}
  $channels=@($ret.Text.Split(',') | ForEach-Object {[int]$_})
  if(@($channels | Where-Object {$_ -lt 1 -or $_ -gt 34}).Count -gt 0){throw 'Retours : canaux entre 1 et 34.'}
  if($channels -notcontains [int]$mic.Value){throw 'Inclure le canal micro dans les retours.'}
  if($refL.Value -eq $mic.Value -or $refR.Value -eq $mic.Value -or $channels -contains [int]$refL.Value -or $channels -contains [int]$refR.Value){throw 'Les canaux de référence doivent être séparés du micro et de ses retours.'}
  $autoStrips=if($autoScope.SelectedIndex -eq 1){'all'}else{$strip.Text}
  if($autoStrips -ne 'all' -and $autoStrips -notmatch '^[1-8](,[1-8])*$'){throw 'Pistes Auto : saisir des numéros de 1 à 8 séparés par des virgules.'}
  $engineArgs=@('--run','--mic',"$($mic.Value)",'--ref',"$($refL.Value),$($refR.Value)",'--returns',$ret.Text,'--hold-ms',"$($hold.Value)",'--delay-ms',"$($delay.Value)",'--auto-strips',$autoStrips,'--auto-bus',"$($bus.SelectedIndex+1)",'--suppression',@('gentle','balanced','strong')[$suppression.SelectedIndex])
  if($mode.SelectedIndex -eq 0){$engineArgs+='--bypass'}
  if($mode.SelectedIndex -eq 1){$engineArgs+='--aec'}
  if($mode.SelectedIndex -eq 2){$engineArgs+='--auto'}
  if($mode.SelectedIndex -eq 3){$engineArgs+='--mute'}
  return $engineArgs -join ' '
}
foreach($control in $form.Controls){if($control.Top -ge 416){$control.Top+=150}}
LabelAt 'Suppression de l''écho résiduel' 22 410 350
$suppression=New-Object System.Windows.Forms.ComboBox;$suppression.DropDownStyle='DropDownList';$suppression.Location=New-Object System.Drawing.Point(420,410);$suppression.Size=New-Object System.Drawing.Size(230,26);$form.Controls.Add($suppression)
$suppression.Items.AddRange(@('Douce (défaut)','Équilibrée','Forte'));$suppression.SelectedIndex=0
LabelAt 'Plus forte : moins d''écho, mais votre voix peut être altérée. Appliqué au prochain lancement.' 22 442 646 42
$startupBox=New-Object System.Windows.Forms.CheckBox;$startupBox.Text='Démarrer avec Windows, dans la zone de notification';$startupBox.Location=New-Object System.Drawing.Point(22,492);$startupBox.Size=New-Object System.Drawing.Size(646,30);$form.Controls.Add($startupBox)
LabelAt 'Enregistrez vos réglages après un premier test réussi. La croix masque cette fenêtre.' 22 527 646 30
$save=New-Object System.Windows.Forms.Button;$save.Text='Enregistrer';$save.Location=New-Object System.Drawing.Point(220,735);$save.Size=New-Object System.Drawing.Size(210,40);$save.FlatStyle='Flat';$form.Controls.Add($save)
foreach($control in $form.Controls){if($control.Top -ge 302){$control.Top+=36}}
LabelAt 'Surveillance Auto' 22 302 350
$autoScope=New-Object System.Windows.Forms.ComboBox;$autoScope.DropDownStyle='DropDownList';$autoScope.Items.AddRange(@('Pistes choisies','Toutes les pistes vers le bus'));$autoScope.SelectedIndex=0;$autoScope.Location=New-Object System.Drawing.Point(420,302);$autoScope.Size=New-Object System.Drawing.Size(230,26);$form.Controls.Add($autoScope)
$autoScope.Add_SelectedIndexChanged({$strip.Enabled=$autoScope.SelectedIndex -eq 0})
. (Join-Path $PSScriptRoot 'UI-i18n.ps1')
. (Join-Path $PSScriptRoot 'UI-tips.ps1')
. (Join-Path $PSScriptRoot 'UI-runtime.ps1')
if($CheckUi){
 $before=@($mic.Value,$refL.Value,$refR.Value,$ret.Text,$hold.Value,$delay.Value,$strip.Text,$bus.SelectedIndex,$autoScope.SelectedIndex)-join '|'
 if($mode.SelectedIndex -ne 2){throw 'Default starting mode must be Auto'}
 $initialLanguage=$languageBox.SelectedIndex;$mode.SelectedIndex=3;$suppression.SelectedIndex=2
 $languageBox.SelectedIndex=1-$initialLanguage
 $languageBox.SelectedIndex=$initialLanguage
 $after=@($mic.Value,$refL.Value,$refR.Value,$ret.Text,$hold.Value,$delay.Value,$strip.Text,$bus.SelectedIndex,$autoScope.SelectedIndex)-join '|'
 if($before -ne $after -or $mode.SelectedIndex -ne 3 -or $suppression.SelectedIndex -ne 2){throw 'Language switching changed routing values or the selected mode/profile.'}
 foreach($entry in $script:tipEntries){$expectedTip=if($script:uiLanguage -eq 'fr'){$entry[2]}else{$entry[1]};if($tips.GetToolTip($entry[0]) -ne $expectedTip){throw 'Missing or incorrect localized hover tip'}}
 foreach($c in $form.Controls){if($c.Tag -and $c.Tag -ne 'VOICEMEETER AEC' -and -not $script:english.ContainsKey([string]$c.Tag)){throw "Missing translation: $($c.Tag)"}}
 $mode.SelectedIndex=2;$suppression.SelectedIndex=0
 if($TestDataPath){TestRuntime}
 $form.ShowInTaskbar=$false; $form.Opacity=0; $form.Show(); [System.Windows.Forms.Application]::DoEvents()
 if($PreviewPath){$bitmap=New-Object System.Drawing.Bitmap($form.Width,$form.Height);$form.DrawToBitmap($bitmap,(New-Object System.Drawing.Rectangle(0,0,$form.Width,$form.Height)));$bitmap.Save($PreviewPath);$bitmap.Dispose()}
 Write-Output "Launcher controls built in $script:uiLanguage without opening audio."
 $tips.Dispose();$tray.Dispose();$form.Dispose();exit
}
try{[void]$form.ShowDialog()}finally{$tips.Dispose();$timer.Dispose();$tray.Dispose();$showSignal.Dispose();$instance.Dispose()}


