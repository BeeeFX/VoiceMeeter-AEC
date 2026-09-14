# VoiceMeeter AEC 1.1.0 — validation

[English](VALIDATION-EN.md) · Version 1.1.0

Les contrôles passent pour Douce et Équilibrée. **Forte expose la suppression d'origine, plus agressive, et échoue à certains critères de préservation du signal proche.** Des tests synthétiques réussis ne prouvent ni une voix transparente ni une stabilité de production.

## Contrôles automatisés

- Sept tests Rust : framing exact du bypass selon la taille des callbacks, maintien borné, référence absente/silence/valeurs non finies, fondus, reprise sélective, analyse de plusieurs références et masques Auto multipistes avec exclusion des retours micro.
- Transport natif : 34 canaux, doubles tampons, PCM16/24/32 et float32. Canaux non sélectionnés identiques bit à bit.
- Callback C++ réel vers moteur Rust via FFI, sur 300 blocs simulés : framing, silence immédiat, préservation et détection d'indice invalide.
- Événements natifs : reset, changement de fréquence, notification de latence seule, priorité de l'arrêt et combinaison des routes Auto actives/inactives/inconnues.
- Processus caché recevant M/B/A/T/Q par le même pipe que le menu de notification. Contrôles natifs réels, diagnostics et arrêt propre vérifiés, sans périphérique audio.
- Interface anglaise/française : Auto par défaut, changement de langue conservant mode/profil, sauvegarde/relecture, inscription isolée au démarrage Windows, masquage/restauration de la fenêtre, métadonnées et versions de mise à jour, somme de contrôle et refus des chemins d'archive dangereux.
- Les chemins locaux du compilateur sont remappés ; les archives publiques sont contrôlées pour exclure comptes locaux, réglages, raccourcis, journaux personnels et dossiers de compilation.

## Mesures audio

Chaque scénario déterministe dure 48 secondes à 48 kHz : deux références indépendantes de bruit coloré, trois trajets d'écho filtrés, signal proche entre 20 et 30 secondes, délai acoustique passant de 50 à 90 ms à 30 secondes, référence retirée à 46 secondes. Le signal proche utilise des harmoniques modulées ou du bruit coloré indépendant, pas de voix humaine enregistrée. D'autres graines utilisent des niveaux proches à 0,5x et 1,5x.

Critères inchangés : atténuation d'écho ≥10 dB, après changement de délai ≥8 dB, gain proche ≥0,35, corrélation ≥0,5, amélioration d'erreur ≥0 dB, sortie finie, détection de référence absente et aucune erreur DSP.

| Profil | Scénarios réussis | Cas stationnaire original : gain / corrélation / amélioration |
|---|---|---|
| Douce | 6/6 | 0,773 / 0,872 / +3,75 dB |
| Équilibrée | 6/6 | 0,733 / 0,856 / +3,29 dB |
| Forte | 3/6 | 0,119 / 0,390 / -1,77 dB |

Douce conserve les seuils 3/6 de la version 0.1.2. Équilibrée utilise 2,25/4,5. Forte conserve le réglage d'origine. Le filtre adaptatif reste actif. Dans le cas stationnaire le plus faible, le gain est d'environ 0,386 en Équilibrée et 0,001 en Forte : augmenter la suppression peut effacer du son utile. Équilibrée a été choisie à partir de cette suite, qui n'est donc pas une validation indépendante.

L'atténuation de l'écho linéaire seul est d'environ 37 dB après convergence et 34 dB après réadaptation. Ce modèle ne prouve pas l'efficacité supérieure d'un profil sur la distorsion réelle des enceintes. Voir le [correctif](patches/README.md) et les résultats bruts `docs/self-test-0.1.3-gentle.txt`, `-balanced.txt`, `-strong.txt`. L'autotest Forte renvoie un code d'échec ; cette limite n'est pas masquée.

## Délais, reprise et arrière-plan

Le framing ajoute 480 échantillons / 10 ms, plus un maintien facultatif jusqu'à 250 ms. L'AEC ajoute un délai interne ; alignement synthétique autour de 19 ms au total avec maintien nul. Ce n'est pas une mesure matérielle. Fondu AEC/bypass : 10 ms ; silence immédiat ; rétablissement : 5 ms.

Reset/resync ou absence de callbacks pendant deux secondes : deux reprises possibles, après 250 ms puis 1 s, mode et profil conservés. Arrêt explicite et défauts de format/tampon/fréquence ne sont pas retentés. Les notifications de latence actualisent les diagnostics sans reset. Une reprise peut couper l'audio et impose une nouvelle adaptation.

Le démarrage à la connexion attend VoiceMeeter et retente certains échecs initiaux pendant au plus 90 secondes, indépendamment de la reprise native. Les tests de sauvegarde/raccourci utilisent un dossier isolé sans modifier le démarrage du poste de développement. Le processus utilise CreateNoWindow avec entrées/sorties redirigées. Le démarrage normal n'a pas de console ; le lancement CLI direct en conserve une. Journaux texte limités par rotation à environ 2 Mio plus un segment précédent, sans audio enregistré.

## Validation réelle restante

Un premier usage de la version précédente a été signalé comme fonctionnel, notamment le silence micro, avec encore des sons d'enceintes audibles. Il s'agit d'un retour informel, pas d'une mesure acoustique contrôlée.

Aucun flux audio réel, déconnexion Windows ou redémarrage n'a été effectué pour cette mise à jour. Ordre réel au démarrage, initialisation du pilote sans console, stabilité longue durée, reprise réelle, transitions Auto multipistes, qualité vocale, comparaison perceptive des profils et latence doivent encore être validés sur le terrain. Les contrôles automatiques n'interrompent pas l'audio actif.

Les tableaux du programme ont une capacité fixe ; aucun appel Remote, journal ni allocation du programme dans le callback audio. Les allocations internes Sonora ne sont pas instrumentées. Sa suite de développement complète n'a pas été lancée car ces dépendances ne sont pas incluses ; le correctif est couvert par les scénarios et tests d'intégration de l'application.

Cible : Windows x64, VoiceMeeter Potato avec pilote Insert x64, 48 kHz et runtime Visual C++ x64. L'application de bureau est autonome : les utilisateurs n'ont pas besoin d'installer .NET ni de lancer PowerShell. Les binaires ne sont pas signés. Aucune autre édition VoiceMeeter n'est déclarée compatible.
