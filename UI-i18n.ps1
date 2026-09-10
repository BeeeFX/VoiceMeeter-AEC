# Dot-sourced by the launcher. Never changes routing or opens audio.
$script:english=@{
 'VoiceMeeter AEC — préfiltre ASIO expérimental'='VoiceMeeter AEC — experimental ASIO prefilter'
 'Préfiltre AEC3 • Insert ASIO • 48 kHz'='AEC3 prefilter • Potato ASIO Insert • 48 kHz'
 'Aucun réglage audio n''est modifié. Commencez en bypass pour vérifier le transport.'='Audio settings stay under your control. Start in bypass to check the signal path.'
 'Canal micro physique (IN1 gauche = 1)'='Microphone channel (IN1 left = 1)'
 'Canal de référence gauche'='Reference left channel'
 'Canal de référence droite'='Reference right channel'
 'Canaux de retour micro (séparés par virgule)'='Microphone return channels (comma-separated)'
 'Maintien réel du micro, en ms (départ : 0)'='Microphone hold, ms (start at 0)'
 'Estimation du délai AEC, en ms (départ : 0)'='AEC delay estimate, ms (start at 0)'
 'Pistes surveillées en Auto'='Auto: monitored strips'
 'Bus de sortie surveillé'='Auto: output bus'
 'Surveillance Auto'='Auto monitoring'
 'Pistes choisies'='Selected strips'
 'Toutes les pistes vers le bus'='All strips to output bus'
 'Pistes Auto : saisir des numéros de 1 à 8 séparés par des virgules.'='Auto strips: enter numbers 1 to 8 separated by commas.'
 'Mode au démarrage'='Starting mode'
 'Suppression de l''écho résiduel'='Residual echo suppression'
 'Douce (défaut)'='Gentle (default)'
 'Équilibrée'='Balanced'
 'Forte'='Strong'
 'Plus forte : moins d''écho, mais votre voix peut être altérée. Appliqué au prochain lancement.'='Stronger settings may affect your voice. Applied when the engine next starts.'
 'Démarrer avec Windows, dans la zone de notification'='Start with Windows, in the system tray'
 'Enregistrez vos réglages après un premier test réussi. La croix masque cette fenêtre.'='Save after a successful first test. Closing this window keeps the app in the tray.'
 'Enregistrer'='Save settings'
 'Bypass'='Bypass'
 'AEC manuel'='Manual AEC'
 'Auto'='Auto'
 'Silence micro'='Mute microphone'
 'RÉFÉRENCE ET AUTOMATISME'='REFERENCE & AUTOMATION'
 'La référence et Auto sont indépendants. La référence doit exclure le micro.'='Reference and Auto monitoring are independent. Keep your mic out of the reference.'
 'Menu de notification : AEC · bypass · silence · Auto · diagnostics · quitter'='Tray menu: AEC · bypass · mute · Auto · diagnostics · exit'
 'Avant de quitter : désactivez les retours PATCH INSERT du micro.'='Before quitting: disable the microphone PATCH INSERT returns.'
 'Prêt — aucun flux audio ouvert'='Ready — no audio stream opened'
 'Ouvrir le moteur'='Open engine'
 'Lire le guide'='Read setup guide'
 'Le moteur lancé par cette fenêtre fonctionne déjà.'='The engine launched by this window is already running.'
 'Retours : saisir par exemple 1,2 sans espaces.'='Returns: enter channel numbers such as 1,2, without spaces.'
 'Retours : canaux entre 1 et 34.'='Return channels must be between 1 and 34.'
 'Inclure le canal micro dans les retours.'='Include the microphone channel in the return channels.'
 'Les canaux de référence doivent être séparés du micro et de ses retours.'='Reference channels must be separate from the microphone and its returns.'
 'Processus ouvert — consultez les compteurs dans la console'='Process opened — check the counters in the console'
 'Moteur arrêté — vérifiez les retours PATCH INSERT du micro'='Engine stopped — check the microphone PATCH INSERT returns'
 'Afficher les réglages'='Show settings'
 'Diagnostics'='Diagnostics'
 'Quitter'='Exit'
 'Arrêter le moteur'='Stop engine'
 'Désactivez PATCH INSERT dans VoiceMeeter avant de continuer. Continuer ?'='Disable PATCH INSERT in VoiceMeeter before continuing. Continue?'
 'Réglages enregistrés — appliqués au prochain lancement du moteur'='Settings saved — applied when the engine next starts'
 'Attente de VoiceMeeter…'='Waiting for VoiceMeeter…'
 'Démarrage du moteur…'='Starting engine…'
 'Moteur en marche'='Engine running'
 'AEC actif'='AEC active'
 'Bypass actif'='Bypass active'
 'Micro coupé'='Microphone muted'
 'Auto actif'='Auto active'
 'Reconnexion…'='Reconnecting…'
 'Le moteur s''est arrêté. Consultez les diagnostics.'='Engine stopped. Open diagnostics for details.'
 'VoiceMeeter n''est pas prêt. Ouvrez-le, puis démarrez le moteur depuis les réglages.'='VoiceMeeter is not ready. Open it, then start the engine from settings.'
 'VoiceMeeter AEC est déjà ouvert. Utilisez son icône de notification.'='VoiceMeeter AEC is already open. Use its tray icon.'
 'Le moteur ne répond pas encore. Consultez les diagnostics avant de réessayer.'='The engine has not stopped yet. Check diagnostics before trying again.'
}
$script:uiLanguage='en'
$script:modeText=@('Bypass','AEC manuel','Auto','Silence micro')
$script:autoScopeText=@('Pistes choisies','Toutes les pistes vers le bus')
$script:suppressionText=@('Douce (défaut)','Équilibrée','Forte')
function Tr([string]$text){
 if($script:uiLanguage -eq 'en' -and $script:english.ContainsKey($text)){return $script:english[$text]}
 return $text
}
foreach($control in $form.Controls){
 if($control -is [System.Windows.Forms.Label] -or $control -is [System.Windows.Forms.Button] -or $control -is [System.Windows.Forms.CheckBox]){$control.Tag=$control.Text}
 if($control.Top -ge 86){$control.Top+=20}
}
$status.Width=646
$languageBox=New-Object System.Windows.Forms.ComboBox
$languageBox.DropDownStyle='DropDownList';$languageBox.Items.AddRange(@('English','Français'))
$languageBox.AccessibleName='Language / Langue'
$languageBox.Location=New-Object System.Drawing.Point(490,20);$languageBox.Size=New-Object System.Drawing.Size(160,26)
$form.Controls.Add($languageBox)
$settingsPath=Join-Path $PSScriptRoot 'ui-settings.json'
if(-not $Language){
 $Language='en'
 if(-not $CheckUi -and (Test-Path -LiteralPath $settingsPath)){
  try{$saved=Get-Content -LiteralPath $settingsPath -Raw | ConvertFrom-Json;if($saved.language -in @('en','fr')){$Language=$saved.language}}catch{}
 }
}
function ApplyLanguage {
 $script:uiLanguage=if($languageBox.SelectedIndex -eq 1){'fr'}else{'en'}
 $form.Text=Tr 'VoiceMeeter AEC — préfiltre ASIO expérimental'
 foreach($control in $form.Controls){if($control.Tag){$control.Text=Tr ([string]$control.Tag)}}
 $selected=$mode.SelectedIndex;$mode.Items.Clear()
 foreach($name in $script:modeText){[void]$mode.Items.Add((Tr $name))}
 $mode.SelectedIndex=$selected
 $selected=$suppression.SelectedIndex;$suppression.Items.Clear()
 foreach($name in $script:suppressionText){[void]$suppression.Items.Add((Tr $name))}
 $suppression.SelectedIndex=$selected
 $selected=$autoScope.SelectedIndex;$autoScope.Items.Clear()
 foreach($name in $script:autoScopeText){[void]$autoScope.Items.Add((Tr $name))}
 $autoScope.SelectedIndex=$selected
 if(Get-Command UpdateTrayLanguage -ErrorAction SilentlyContinue){UpdateTrayLanguage}
 if(Get-Command UpdateTips -ErrorAction SilentlyContinue){UpdateTips}
}
$languageBox.Add_SelectedIndexChanged({
 ApplyLanguage
})
$languageBox.SelectedIndex=if($Language -eq 'fr'){1}else{0}
