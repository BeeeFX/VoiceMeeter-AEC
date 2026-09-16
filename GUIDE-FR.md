# VoiceMeeter AEC — configuration Windows x64

[English](GUIDE-EN.md) · Version 1.3.0 · Windows x64 · VoiceMeeter Banana ou Potato

VoiceMeeter AEC atténue les sons des enceintes repris par le micro. Il utilise le pilote **VoiceMeeter Banana ou Potato Insert Virtual ASIO** correspondant, avant les effets de piste. VoiceMeeter garde le contrôle du matériel. L'application ne modifie ni les routes, ni les PATCH INSERT, ni les périphériques Windows par défaut.

## Premier démarrage

1. Lancer **VoiceMeeter-AEC-Setup.exe**, puis ouvrir VoiceMeeter AEC depuis le menu Démarrer. L’installation concerne le compte Windows actuel, ne demande pas de droits administrateur et inclut .NET. Un ZIP portable reste disponible sur la page de version.
2. Démarrer VoiceMeeter Banana ou Potato à **48 kHz** si possible et y configurer le micro et les enceintes normalement. L'application détecte l'édition ouverte ; le sélecteur permet aussi de la choisir lorsque VoiceMeeter est fermé. Une configuration existante à 44,1 kHz peut utiliser le mode de compatibilité facultatif décrit plus bas.
3. Dans l'application, choisir la colonne contenant le microphone et une ou plusieurs colonnes de lecture contenant tous les sons joués par les enceintes. Leur son devient la référence d'écho. Choisir le bus A relié aux enceintes afin qu'Auto sache quelle route surveiller. L'application affiche uniquement les colonnes et bus de cette édition, puis les traduit automatiquement en canaux Insert.
4. Garder les retours PATCH INSERT du micro désactivés et sélectionner **Démarrer l'annulation d'écho**. Ouvrir **Diagnostic** et vérifier que le pilote Insert indique la fréquence attendue et que le compteur `blocks` avance. Un seul client Insert peut fonctionner à la fois.
5. Dans VoiceMeeter, ouvrir **Menu → System Settings / Options → PATCH INSERT**. Au point PRE-FX, activer gauche et droite uniquement pour la colonne du microphone. Laisser tous les autres retours désactivés.
6. Dans une application recevant le bus micro VoiceMeeter, essayer **Couper le micro**, **Bypass** et **AEC actif** depuis l'application ou son icône. Ne pas envoyer le micro vers les enceintes.
7. Lire de la parole sur les enceintes, laisser environ 10–20 secondes d'adaptation, puis comparer à niveau identique. Parler en même temps et vérifier la clarté de la voix. Un écho résiduel peut subsister.

**Désactiver les retours PATCH INSERT du micro avant d'arrêter le moteur ou de mettre à jour l'application.** En cas de panne laissant le micro silencieux, cela rétablit le chemin direct. L'application ne modifie jamais ces cases automatiquement.

## Référence audio

La référence doit contenir tous les sons dont on veut retirer l'écho, et exclure le micro brut ou traité. Sélectionner autant de colonnes de lecture que nécessaire. L'application combine la première paire stéréo de chaque colonne choisie avant de l'envoyer à l'AEC.

Si toute la lecture passe par **VAIO**, sélectionner uniquement VAIO. Si le son des enceintes est réparti entre VAIO, AUX, des entrées matérielles ou VAIO3 sur Potato, sélectionner chaque colonne concernée. Les sources lues directement hors de VoiceMeeter ne peuvent pas être incluses. Seule la première paire gauche/droite de chaque piste virtuelle est utilisée ; les canaux surround supplémentaires ne le sont pas.

L'Insert fournit un signal avant effets et fader, pas le mix final du bus. Des effets différents, de la saturation ou des changements brusques de volume peuvent réduire l'annulation.

La version 1.2.0 pondère les sources sélectionnées selon le routage, les mutes, les solos et les niveaux du bus haut-parleurs, même en AEC manuel. Banana utilise le gain de piste ; Potato utilise GainLayer pour le bus choisi. Une normalisation commune évite la saturation du mix de référence et les changements de pondération suivent un fondu de 10 ms. Le routage est lu toutes les 200 ms. S'il est indisponible, les sources sélectionnées sont mélangées à niveau égal avec normalisation et un avertissement apparaît. L'EQ, le panoramique, le downmix surround et la distorsion des enceintes restent hors de ce modèle.

## Canaux et Auto

Numérotation à partir de 1 : canaux Insert de l'édition choisie, pas canaux physiques de la carte son.

### Banana

| Piste | Canaux Insert | Numéro Auto |
|---|---|---|
| IN1 | 1,2 | 1 |
| IN2 | 3,4 | 2 |
| IN3 | 5,6 | 3 |
| VAIO | 7–14 ; stéréo 7,8 | 4 |
| AUX | 15–22 ; stéréo 15,16 | 5 |

Auto peut surveiller A1, A2 ou A3 sur Banana.

### Potato

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

**Pistes choisies :** dans Avancé, sélectionner par exemple VAIO, AUX et, sur Potato, VAIO3. L'AEC est actif si au moins une route choisie vers le bus est active. Défaut : VAIO vers A2. Choisir le bus relié aux enceintes.

**Toutes les pistes vers le bus :** surveiller les cinq pistes Banana vers A1–A3 ou les huit pistes Potato vers A1–A5. La piste recevant le retour micro configuré est exclue. Si une autre piste porte un retour micro traité, utiliser Pistes choisies pour l'exclure aussi.

Auto suit **le routage, pas le niveau sonore instantané** : boutons de route, silence, gains de piste/bus, gains propres au bus et solo. Une route active mais silencieuse ne désactive pas Auto. Si toutes les routes surveillées sont inactives, il utilise le bypass. Si la lecture du routage échoue, il garde l'AEC actif. Il ne change ni les sorties ni les routes.

AEC manuel, Bypass et Silence remplacent Auto jusqu'à ce qu'on le sélectionne à nouveau. Ces commandes ne modifient pas le mode initial enregistré. Les pistes surveillées par Auto peuvent être modifiées séparément dans Avancé et ne changent pas les colonnes de référence audio.

## Suppression et délais

| Réglage | Effet |
|---|---|
| Douce | Suppression moins agressive qui préserve le mieux la voix proche dans les tests synthétiques. |
| Équilibrée | Suppression modérée qui préserve mieux les sons utiles dans les tests synthétiques. |
| Forte | Réglage par défaut des nouvelles configurations ; suppression d’origine qui peut fortement atténuer la voix pendant les superpositions difficiles. À tester avant adoption. |
| Maintien micro | Ajoute réellement 0–250 ms de retard au micro. Garder normalement 0. |
| Estimation AEC | Indication de délai distincte, 0–500 ms. Commencer à 0 ; ce n'est pas un autre tampon. |

La suppression s'applique au prochain lancement du moteur. Elle ne corrige pas une référence absente ou incorrecte. Douce et Équilibrée passent les six scénarios synthétiques ; Forte échoue à certains critères de préservation. Ces profils restent expérimentaux.

Le tampon ajoute 480 échantillons / 10 ms, plus le maintien, même en bypass. L'AEC ajoute un délai interne. L'alignement synthétique d'environ 19 ms avec maintien nul n'est pas une mesure matérielle. Fondu AEC/bypass : 10 ms ; silence immédiat ; rétablissement : 5 ms.

### Compatibilité 44,1 kHz

Le mode natif 48 kHz reste recommandé et n'utilise aucune conversion de fréquence. Si VoiceMeeter doit rester à 44,1 kHz, activer **Avancé → Autoriser le rééchantillonnage de compatibilité à 44,1 kHz** avant de démarrer le moteur. L'application convertit uniquement le micro et la référence stéréo à 48 kHz pour l'AEC, puis renvoie le micro nettoyé à 44,1 kHz. Elle ne modifie pas la fréquence de VoiceMeeter et ne rééchantillonne pas les autres tranches ou bus. L'option est désactivée par défaut, car ce chemin est expérimental et peut ajouter un peu de charge processeur ou de latence. Les autres fréquences ne sont pas prises en charge.

## Icône, sauvegarde et démarrage Windows

Fermer la fenêtre laisse toujours VoiceMeeter AEC actif dans la zone de notification, que le moteur audio fonctionne ou soit arrêté. Un clic gauche sur l’icône personnalisée ou la réouverture de **VoiceMeeter AEC.exe** restaure la fenêtre. Clic droit : AEC, Bypass, Silence, Auto, Diagnostic et Quitter. Utiliser **Quitter** dans ce menu pour fermer complètement l’application. Les diagnostics restent en anglais. Le survol de l’icône montre le statut ; Windows peut la ranger parmi les icônes masquées.

Les réglages sont enregistrés automatiquement. Après un essai réussi, choisir Auto ou le mode initial souhaité dans **Avancé**, puis activer **Démarrer VoiceMeeter AEC à l’ouverture de ma session Windows** si nécessaire. L’option séparée **Démarrer automatiquement le moteur après l’ouverture de session** détermine si ce démarrage masqué attend aussi VoiceMeeter avant de lancer le moteur avec les réglages enregistrés. Configurer séparément le démarrage de VoiceMeeter et la restauration de la fréquence et des retours Insert testés.

L'application attend VoiceMeeter jusqu'à 90 secondes et retente certains échecs initiaux toutes les cinq secondes. Une panne est signalée par l'icône. Elle ne redémarre pas indéfiniment : la reprise native après reset/stall reste limitée à deux essais. Un appel de pilote bloqué peut dépasser le délai d'attente.

Les réglages sont dans `%LOCALAPPDATA%\VoiceMeeterAEC\settings.json` : édition VoiceMeeter, colonnes choisies, surveillance Auto, bus des enceintes, délais, mode, suppression et langue. Ils sont enregistrés automatiquement. Les changements liés au démarrage s'appliquent au prochain lancement du moteur ; désactiver PATCH INSERT, arrêter, puis relancer.

Les sélecteurs audio, la suppression et les délais sont verrouillés pendant le fonctionnement. Les boutons de mode restent immédiats. Les niveaux micro/référence et l'état sont visibles sur chaque page : annulation Auto, bypass Auto, référence absente ou erreur de traitement. Le moteur actif ne prouve pas que PATCH INSERT est connecté : suivre le test Silence du guide.

Le démarrage utilise le réglage Windows Run de l'utilisateur, sans droits administrateur ni service. Désactiver l'option pour le retirer. Après déplacement ou mise à jour, ouvrir une fois la nouvelle copie pour actualiser son chemin de démarrage.

Dans **Avancé → Mises à jour**, l'application peut rechercher la dernière version stable sur GitHub une fois par jour ou à la demande. Elle propose uniquement une version plus récente, demande confirmation avant l'installation, vérifie le ZIP Windows avec sa somme SHA-256 publiée, se ferme brièvement, remplace les fichiers portables puis se rouvre. Si le moteur fonctionnait, l'application mise à jour le redémarre. Les réglages restent dans les données locales et ne sont pas remplacés.

Les mises à jour lancées depuis 1.2.0 conservent le mode actif, y compris Silence, sans modifier le mode de démarrage enregistré. Une ancienne version qui ne transmet pas ce mode redémarre en Silence : choisir Auto ou AEC après réouverture. Les profils de suppression déjà enregistrés sont conservés.

Pour désinstaller : désactiver le démarrage et enregistrer, désactiver PATCH INSERT, quitter par l'icône, puis retirer le dossier. Les réglages/journaux locaux peuvent être retirés séparément. Les journaux contiennent du texte, jamais d'audio ; rotation à environ 2 Mio plus un segment précédent.

## Diagnostics et limites

- `blocks` : callbacks ; `max` : durée maximale complète ; `overruns` : dépassements du budget (4 ms à 192 échantillons / 48 kHz).
- `mic`, `ref` : crêtes du dernier bloc. Après 500 ms de référence presque silencieuse, `ref_missing=1` sélectionne un bypass retardé. Une référence incorrecte mais non silencieuse n'est pas détectée.
- `errors` : erreurs DSP, avec retour au micro direct retardé. Reset/resync ou absence de callbacks pendant deux secondes : au plus deux reprises, mode et profil conservés. Les autres erreurs arrêtent le moteur. Désactiver PATCH INSERT si la reprise échoue.
- 48 kHz en natif et 44,1 kHz avec le mode de compatibilité facultatif, en float32/PCM16/PCM24/PCM32 little-endian. Les autres fréquences sont refusées. Les canaux non sélectionnés restent inchangés. Les allocations internes Sonora ne sont pas garanties adaptées à toute charge temps réel.
- La capture directe du matériel contourne le filtre. Les traitements et logiciels de communication en aval ajoutent leur latence et leur comportement de capture. Tester le chemin réellement reçu.

## Console et compilation

Le lancement direct de l'exécutable conserve une console :

```powershell
.\voicemeeter-aec.exe --help
.\voicemeeter-aec.exe --self-test
.\voicemeeter-aec.exe --self-test --suppression balanced
.\voicemeeter-aec.exe --probe --edition banana
.\voicemeeter-aec.exe --run --edition banana --mic 1 --ref 7,8,15,16 --returns 1,2 --auto-strips 4,5 --auto-bus 2
.\voicemeeter-aec.exe --run --edition potato --mic 1 --ref 11,12,19,20,27,28 --returns 1,2 --auto-strips 6,7,8 --auto-bus 2
.\voicemeeter-aec.exe --run --edition potato --mic 1 --ref 11,12 --returns 1,2 --auto-strips all --auto-bus 2 --suppression balanced
```

Modes : `--aec`, `--bypass`, `--mute`, `--auto` (défaut Auto). Touches : A/B/M/T/Q. `--auto-strip` reste un alias de `--auto-strips`. `--probe --edition banana|potato` interroge le pilote choisi sans flux, lorsque sa place client est libre. Aide et autotests n'ouvrent aucun périphérique audio. Un flux exige `--run` ; la ligne de commande utilise Potato si `--edition` est omis.

Lancer **Build.cmd** depuis les sources. Windows x64, Rust 1.91+, Visual C++, Windows SDK et .NET 8 SDK ou plus récent sont requis. Les dépendances Rust, les en-têtes ASIO et le correctif Sonora sont inclus. La compilation produit une application WPF autonome ; les utilisateurs n'ont pas besoin d'installer .NET. Le moteur audio non signé nécessite le runtime Visual C++ x64.

Voir [validation](VALIDATION.md), [historique](CHANGELOG.md), [licences](THIRD-PARTY.md). Projet indépendant en accès anticipé, sans affiliation à VB-Audio ou Steinberg.
