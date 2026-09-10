# VoiceMeeter AEC — prototype Windows x64

[English](GUIDE-EN.md) · Version 0.1.3 · Windows x64 · VoiceMeeter Potato

VoiceMeeter AEC atténue les sons des enceintes repris par le micro. Il utilise **Voicemeeter Potato Insert Virtual ASIO**, avant les effets de piste. VoiceMeeter garde le contrôle du matériel. L'application ne modifie ni les routes, ni les PATCH INSERT, ni les périphériques Windows par défaut.

## Premier démarrage

1. Extraire le ZIP Windows dans un dossier permanent accessible en écriture. Ouvrir **Start.vbs** pour démarrer sans console. Start.cmd et Demarrer.cmd restent disponibles. Si Windows Script Host est indisponible : `powershell.exe -NoProfile -STA -ExecutionPolicy Bypass -File .\Lanceur.ps1`.
2. Démarrer VoiceMeeter Potato à **48 kHz** et y configurer micro et sorties. L'exemple suppose un micro sur IN1 ; adapter les canaux si nécessaire.
3. Garder les retours PATCH INSERT du micro désactivés au début. Choisir micro **1**, retours **1,2**, maintien **0 ms**, estimation **0 ms**, et une référence qui exclut le micro.
4. Le mode initial par défaut est **Auto**. Pour le premier contrôle du transport, choisir **Bypass**, puis **Ouvrir le moteur**. Les réglages sont enregistrés et le moteur démarre sans console. Survoler les paramètres pour afficher les explications.
5. Vérifier le statut. Clic droit sur l'icône près de l'horloge, puis **Diagnostics** : pilote Insert à 48 kHz et compteur `blocks` qui avance. Un seul client Insert peut fonctionner à la fois.
6. Activer uniquement les retours micro dans **PATCH INSERT**, au point PRE-FX. Pour IN1 : gauche et droite. Laisser les autres retours désactivés.
7. Dans une application recevant le bus micro VoiceMeeter choisi, sélectionner **Silence micro** dans le menu de notification. Le micro reçu doit devenir silencieux. Essayer **Bypass**, puis **AEC manuel**, sans envoyer le micro aux enceintes.
8. Lire de la parole sur les enceintes, laisser environ 10–20 secondes d'adaptation, puis comparer à niveau identique. Parler en même temps et vérifier la clarté de la voix. Un écho résiduel peut subsister.

**Désactiver les retours PATCH INSERT du micro avant d'arrêter le moteur ou de mettre à jour l'application.** En cas de panne laissant le micro silencieux, cela rétablit le chemin direct. L'application ne modifie jamais ces cases automatiquement.

## Référence audio

La référence doit contenir tous les sons dont on veut retirer l'écho, et exclure le micro brut ou traité. La surveillance Auto est indépendante.

Pour un essai simple, faire passer toute la lecture par **VAIO**, en stéréo, et utiliser **11,12**. Les sons sur AUX, VAIO3, d'autres pistes ou un périphérique utilisé directement hors de VoiceMeeter sont absents de cette paire. Les autres canaux surround le sont aussi. L'application accepte une seule paire stéréo et ne mélange pas plusieurs références.

L'Insert fournit un signal avant effets et fader, pas le mix final du bus. Des effets différents, de la saturation ou des changements brusques de volume peuvent réduire l'annulation.

### Référence regroupée, facultative

Un câble audio virtuel peut regrouper plusieurs sources dans une référence stéréo. Exemple générique :

1. Affecter un bus matériel **libre** à l'entrée de lecture du câble.
2. Y envoyer les sources destinées aux enceintes, avec des niveaux relatifs cohérents. Exclure le micro et tous ses retours traités.
3. Sur une entrée matérielle réellement libre, sélectionner la sortie d'enregistrement du câble. Ne pas remplacer une entrée occupée. Désactiver **tous les envois A et B** de cette piste et laisser son retour PATCH INSERT désactivé.
4. Utiliser ses canaux gauche/droite comme référence ; exemple **IN5 = 9,10**. Ne jamais la renvoyer au bus du câble : cela crée une boucle.
5. La lecture doit faire varier `ref` dans les diagnostics ; parler seul sans lecture ne doit pas le faire varier.

Le câble ajoute de la latence à la référence. Le maintien micro peut aider une référence tardive, mais retarde aussi la voix. Commencer à 0 et vérifier d'abord la référence. Préférer une entrée virtuelle directe si elle couvre toute la lecture. Cette route facultative n'a pas reçu de validation acoustique contrôlée.

## Canaux et Auto

Numérotation à partir de 1 : canaux Insert Potato, pas canaux physiques de la carte son.

| Piste | Canaux Insert | Numéro Auto |
|---|---|---|
| IN1 | 1,2 | 1 |
| IN2 | 3,4 | 2 |
| IN3 | 5,6 | 3 |
| IN4 | 7,8 | 4 |
| IN5 | 9,10 | 5 |
| VAIO | 11–18 ; stéréo 11,12 | 6 |
| AUX | 19–26 ; stéréo 19,20 | 7 |
| VAIO3 | 27–34 ; stéréo 27,28 | 8 |

**Pistes choisies :** saisir une liste, par exemple `6,7,8`. L'AEC est actif si au moins une route choisie vers le bus est active. Défaut : piste 6 vers A2. Choisir le bus relié aux enceintes.

**Toutes les pistes vers le bus :** surveiller les routes des huit pistes vers A1, A2, A3, A4 ou A5. Les pistes recevant les retours micro configurés sont exclues. Si d'autres pistes portent des retours micro traités, utiliser Pistes choisies pour les exclure aussi.

Auto suit **le routage, pas le niveau sonore instantané** : boutons de route, silence, gains de piste/bus, gains propres au bus et solo. Une route active mais silencieuse ne désactive pas Auto. Si toutes les routes surveillées sont inactives, il utilise le bypass. Si la lecture du routage échoue, il garde l'AEC actif. Il ne change ni les sorties ni les routes.

AEC manuel, Bypass et Silence remplacent Auto jusqu'à ce qu'on le sélectionne à nouveau. Ces commandes ne modifient pas le mode initial enregistré. Surveiller plusieurs pistes ne les ajoute pas à la référence audio.

## Suppression et délais

| Réglage | Effet |
|---|---|
| Douce | Défaut ; suppression moins agressive de la version précédente. |
| Équilibrée | Renforcement modéré ; à essayer d'abord si Douce laisse trop d'écho. |
| Forte | Suppression d'origine ; peut fortement atténuer la voix pendant les superpositions difficiles. |
| Maintien micro | Ajoute réellement 0–250 ms de retard au micro. Garder normalement 0. |
| Estimation AEC | Indication de délai distincte, 0–500 ms. Commencer à 0 ; ce n'est pas un autre tampon. |

La suppression s'applique au prochain lancement du moteur. Elle ne corrige pas une référence absente ou incorrecte. Douce et Équilibrée passent les six scénarios synthétiques ; Forte échoue à certains critères de préservation. Ces profils restent expérimentaux.

Le tampon ajoute 480 échantillons / 10 ms, plus le maintien, même en bypass. L'AEC ajoute un délai interne. L'alignement synthétique d'environ 19 ms avec maintien nul n'est pas une mesure matérielle. Fondu AEC/bypass : 10 ms ; silence immédiat ; rétablissement : 5 ms.

## Icône, sauvegarde et démarrage Windows

Fermer les réglages masque la fenêtre et laisse l'audio actif. Double-cliquer sur l'icône ou rouvrir Start.vbs pour la retrouver. Clic droit : AEC, Bypass, Silence, Auto, Arrêter le moteur, Diagnostics et Quitter. Les diagnostics restent en anglais. Le survol de l'icône montre le statut ; elle peut être dans les icônes masquées de Windows.

Après un essai réussi, choisir Auto ou le mode initial souhaité, cocher **Démarrer avec Windows**, puis **Enregistrer**. À la connexion, seule l'icône apparaît, sans fenêtre de commandes. Configurer séparément le démarrage de VoiceMeeter et la restauration des réglages 48 kHz et des retours Insert testés.

L'application attend VoiceMeeter jusqu'à 90 secondes et retente certains échecs initiaux toutes les cinq secondes. Une panne est signalée par l'icône. Elle ne redémarre pas indéfiniment : la reprise native après reset/stall reste limitée à deux essais. Un appel de pilote bloqué peut dépasser le délai d'attente.

Les réglages sont dans `%LOCALAPPDATA%\VoiceMeeterAEC\settings.json` : canaux, surveillance Auto, délais, mode, suppression et langue. **Enregistrer** et **Ouvrir le moteur** les sauvegardent. Cela ne reconfigure pas un moteur actif. Désactiver PATCH INSERT, arrêter puis relancer le moteur pour appliquer les changements.

Le démarrage utilise `VoiceMeeter AEC.lnk` dans le dossier Démarrage de l'utilisateur, sans droits administrateur ni service. Décocher et enregistrer pour le retirer. Après déplacement ou mise à jour, ouvrir la nouvelle copie et enregistrer pour actualiser le raccourci. Un mode déjà sauvegardé est conservé ; le nouveau défaut Auto ne le remplace pas.

Pour désinstaller : désactiver le démarrage et enregistrer, désactiver PATCH INSERT, quitter par l'icône, puis retirer le dossier. Les réglages/journaux locaux peuvent être retirés séparément. Les journaux contiennent du texte, jamais d'audio ; rotation à environ 2 Mio plus un segment précédent.

## Diagnostics et limites

- `blocks` : callbacks ; `max` : durée maximale complète ; `overruns` : dépassements du budget (4 ms à 192 échantillons / 48 kHz).
- `mic`, `ref` : crêtes du dernier bloc. Après 500 ms de référence presque silencieuse, `ref_missing=1` sélectionne un bypass retardé. Une référence incorrecte mais non silencieuse n'est pas détectée.
- `errors` : erreurs DSP, avec retour au micro direct retardé. Reset/resync ou absence de callbacks pendant deux secondes : au plus deux reprises, mode et profil conservés. Les autres erreurs arrêtent le moteur. Désactiver PATCH INSERT si la reprise échoue.
- Uniquement 48 kHz et float32/PCM16/PCM24/PCM32 little-endian. Les canaux non sélectionnés restent inchangés. Les allocations internes Sonora ne sont pas garanties adaptées à toute charge temps réel.
- La capture directe du matériel contourne le filtre. Les traitements et logiciels de communication en aval ajoutent leur latence et leur comportement de capture. Tester le chemin réellement reçu.

## Console et compilation

Le lancement direct de l'exécutable conserve une console :

```powershell
.\voicemeeter-aec.exe --help
.\voicemeeter-aec.exe --self-test
.\voicemeeter-aec.exe --self-test --suppression balanced
.\voicemeeter-aec.exe --run --mic 1 --ref 11,12 --returns 1,2 --auto-strips 6,7,8 --auto-bus 2
.\voicemeeter-aec.exe --run --mic 1 --ref 11,12 --returns 1,2 --auto-strips all --auto-bus 2 --suppression balanced
```

Modes : `--aec`, `--bypass`, `--mute`, `--auto` (défaut Auto). Touches : A/B/M/T/Q. `--auto-strip` reste un alias de `--auto-strips`. `--auto-check` lit VAIO/A2 ; `--probe` interroge le pilote sans flux, lorsque sa place client est libre. Aide et autotests n'ouvrent aucun périphérique audio. Un flux exige `--run`.

Lancer **Build.cmd** depuis les sources. Windows x64, Rust 1.91+ (testé avec 1.97.1), Visual C++ et Windows SDK sont requis. Dépendances, en-têtes ASIO et correctif Sonora sont inclus pour compiler hors ligne. Le lanceur utilise PowerShell 5.1, WinForms de .NET Framework et Windows Script Host pour l'entrée VBS facultative. Le binaire non signé nécessite le runtime Visual C++ x64.

Voir [validation](VALIDATION.md), [historique](CHANGELOG.md), [licences](THIRD-PARTY.md). Projet indépendant, sans affiliation à VB-Audio ou Steinberg.
