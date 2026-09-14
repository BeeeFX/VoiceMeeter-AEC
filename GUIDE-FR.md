# VoiceMeeter AEC — configuration Windows x64

[English](GUIDE-EN.md) · Version 1.1.0 · Windows x64 · VoiceMeeter Potato

VoiceMeeter AEC atténue les sons des enceintes repris par le micro. Il utilise **Voicemeeter Potato Insert Virtual ASIO**, avant les effets de piste. VoiceMeeter garde le contrôle du matériel. L'application ne modifie ni les routes, ni les PATCH INSERT, ni les périphériques Windows par défaut.

## Premier démarrage

1. Extraire le ZIP Windows dans un dossier permanent et ouvrir **VoiceMeeter AEC.exe**. L'application est autonome : PowerShell et .NET ne sont pas nécessaires.
2. Démarrer VoiceMeeter Potato à **48 kHz** et y configurer le micro et les enceintes normalement.
3. Dans l'application, choisir la colonne contenant le microphone et une ou plusieurs colonnes de lecture contenant tous les sons joués par les enceintes. Leur son devient la référence d'écho. Choisir le bus A relié aux enceintes afin qu'Auto sache quelle route surveiller. L'application traduit automatiquement ces colonnes en canaux Insert.
4. Garder les retours PATCH INSERT du micro désactivés et sélectionner **Démarrer l'annulation d'écho**. Ouvrir **Diagnostic** et vérifier que le pilote Insert indique 48 kHz et que le compteur `blocks` avance. Un seul client Insert peut fonctionner à la fois.
5. Dans VoiceMeeter, ouvrir **Menu → System Settings / Options → PATCH INSERT**. Au point PRE-FX, activer gauche et droite uniquement pour la colonne du microphone. Laisser tous les autres retours désactivés.
6. Dans une application recevant le bus micro VoiceMeeter, essayer **Couper le micro**, **Bypass** et **AEC actif** depuis l'application ou son icône. Ne pas envoyer le micro vers les enceintes.
7. Lire de la parole sur les enceintes, laisser environ 10–20 secondes d'adaptation, puis comparer à niveau identique. Parler en même temps et vérifier la clarté de la voix. Un écho résiduel peut subsister.

**Désactiver les retours PATCH INSERT du micro avant d'arrêter le moteur ou de mettre à jour l'application.** En cas de panne laissant le micro silencieux, cela rétablit le chemin direct. L'application ne modifie jamais ces cases automatiquement.

## Référence audio

La référence doit contenir tous les sons dont on veut retirer l'écho, et exclure le micro brut ou traité. Sélectionner autant de colonnes de lecture que nécessaire. L'application combine la première paire stéréo de chaque colonne choisie avant de l'envoyer à l'AEC.

Si toute la lecture passe par **VAIO**, sélectionner uniquement VAIO. Si le son des enceintes est réparti entre VAIO, AUX, VAIO3 ou des entrées matérielles, sélectionner chaque colonne concernée. Les sources lues directement hors de VoiceMeeter ne peuvent pas être incluses. Seule la première paire gauche/droite de chaque piste virtuelle est utilisée ; les canaux surround supplémentaires ne le sont pas.

L'Insert fournit un signal avant effets et fader, pas le mix final du bus. Des effets différents, de la saturation ou des changements brusques de volume peuvent réduire l'annulation.

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

**Pistes choisies :** dans Avancé, sélectionner par exemple VAIO, AUX et VAIO3. L'AEC est actif si au moins une route choisie vers le bus est active. Défaut : VAIO vers A2. Choisir le bus relié aux enceintes.

**Toutes les pistes vers le bus :** surveiller les routes des huit pistes vers A1, A2, A3, A4 ou A5. Les pistes recevant les retours micro configurés sont exclues. Si d'autres pistes portent des retours micro traités, utiliser Pistes choisies pour les exclure aussi.

Auto suit **le routage, pas le niveau sonore instantané** : boutons de route, silence, gains de piste/bus, gains propres au bus et solo. Une route active mais silencieuse ne désactive pas Auto. Si toutes les routes surveillées sont inactives, il utilise le bypass. Si la lecture du routage échoue, il garde l'AEC actif. Il ne change ni les sorties ni les routes.

AEC manuel, Bypass et Silence remplacent Auto jusqu'à ce qu'on le sélectionne à nouveau. Ces commandes ne modifient pas le mode initial enregistré. Les pistes surveillées par Auto peuvent être modifiées séparément dans Avancé et ne changent pas les colonnes de référence audio.

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

Fermer la fenêtre pendant que l'audio fonctionne laisse le moteur actif dans la zone de notification. Double-cliquer sur l'icône personnalisée ou rouvrir **VoiceMeeter AEC.exe** restaure la fenêtre. Clic droit : AEC, Bypass, Silence, Auto, Diagnostic et Quitter. Les diagnostics restent en anglais. Le survol de l'icône montre le statut ; Windows peut la ranger parmi les icônes masquées.

Les réglages sont enregistrés automatiquement. Après un essai réussi, choisir Auto ou le mode initial souhaité dans **Avancé**, puis activer **Démarrer avec Windows** si nécessaire. À la connexion, seule l'icône apparaît. Configurer séparément le démarrage de VoiceMeeter et la restauration des réglages 48 kHz et des retours Insert testés.

L'application attend VoiceMeeter jusqu'à 90 secondes et retente certains échecs initiaux toutes les cinq secondes. Une panne est signalée par l'icône. Elle ne redémarre pas indéfiniment : la reprise native après reset/stall reste limitée à deux essais. Un appel de pilote bloqué peut dépasser le délai d'attente.

Les réglages sont dans `%LOCALAPPDATA%\VoiceMeeterAEC\settings.json` : colonnes choisies, surveillance Auto, bus des enceintes, délais, mode, suppression et langue. Ils sont enregistrés automatiquement. Les changements liés au démarrage s'appliquent au prochain lancement du moteur ; désactiver PATCH INSERT, arrêter, puis relancer.

Le démarrage utilise le réglage Windows Run de l'utilisateur, sans droits administrateur ni service. Désactiver l'option pour le retirer. Après déplacement ou mise à jour, ouvrir une fois la nouvelle copie pour actualiser son chemin de démarrage.

Dans **Avancé → Mises à jour**, l'application peut rechercher la dernière version stable sur GitHub une fois par jour ou à la demande. Elle propose uniquement une version plus récente, demande confirmation avant l'installation, vérifie le ZIP Windows avec sa somme SHA-256 publiée, se ferme brièvement, remplace les fichiers portables puis se rouvre. Si le moteur fonctionnait, l'application mise à jour le redémarre. Les réglages restent dans les données locales et ne sont pas remplacés.

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
.\voicemeeter-aec.exe --run --mic 1 --ref 11,12,19,20,27,28 --returns 1,2 --auto-strips 6,7,8 --auto-bus 2
.\voicemeeter-aec.exe --run --mic 1 --ref 11,12 --returns 1,2 --auto-strips all --auto-bus 2 --suppression balanced
```

Modes : `--aec`, `--bypass`, `--mute`, `--auto` (défaut Auto). Touches : A/B/M/T/Q. `--auto-strip` reste un alias de `--auto-strips`. `--auto-check` lit VAIO/A2 ; `--probe` interroge le pilote sans flux, lorsque sa place client est libre. Aide et autotests n'ouvrent aucun périphérique audio. Un flux exige `--run`.

Lancer **Build.cmd** depuis les sources. Windows x64, Rust 1.91+, Visual C++, Windows SDK et .NET 8 SDK ou plus récent sont requis. Les dépendances Rust, les en-têtes ASIO et le correctif Sonora sont inclus. La compilation produit une application WPF autonome ; les utilisateurs n'ont pas besoin d'installer .NET. Le moteur audio non signé nécessite le runtime Visual C++ x64.

Voir [validation](VALIDATION.md), [historique](CHANGELOG.md), [licences](THIRD-PARTY.md). Projet indépendant en accès anticipé, sans affiliation à VB-Audio ou Steinberg.
