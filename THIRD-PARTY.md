# Licences et provenance

Le code propre de VoiceMeeter AEC 0.1.3 est distribué sous GPL-3.0-only. Voir LICENSE.
Les trois en-têtes ASIO dans vendor/asio proviennent du SDK audiosdk/asio,
commit 496a0765b8bb9c26f764f22f9a9712a937177db2. Nous choisissons explicitement
l'option GPL version 3 du fichier vendor/asio/LICENSE.txt, et non la licence propriétaire.

Toutes les dépendances Rust verrouillées dans Cargo.lock et leurs sources sont
incluses dans vendor/rust, avec leurs licences respectives (BSD-3-Clause, MIT,
Apache-2.0 et autres options indiquées dans chaque Cargo.toml). Le fichier
vendor/SONORA-LICENSE.txt complète notamment l'attribution de sonora-aec3,
dont l'archive crates.io déclare BSD-3-Clause mais omet le fichier LICENSE.
Il reproduit les mentions WebRTC Project Authors, Arun Raghavan et dignifiedquire.

Inspiration et inspection : windows-aec-bridge, commit
c24505d85df88dce0d7335f6e24b80dd61d3de74 (MIT). Son transport WASAPI et son interface
ne sont pas incorporés. Une copie de sa licence est jointe dans vendor/UPSTREAM-MIT.txt.
Le nom ASIO est une marque de Steinberg ; VoiceMeeter et les pilotes restent la
propriété de leurs auteurs respectifs. Les pilotes installés ne sont pas redistribués.

Reconstruction : Compiler.cmd / GUIDE-FR.md. L'ensemble du code correspondant
au binaire et les dépendances de compilation sont livrés, sans téléchargement requis
une fois Rust et Visual C++/Windows SDK installés. Le runtime standard Rust et les
outils de compilation restent ceux de leurs distributions.

La version 0.1.3 utilise la source locale Sonora BSD-3-Clause dans patches/sonora. Les modifications sont décrites dans patches/README.md ; l'original reste dans vendor/rust/sonora. La source corrigée correspondante est incluse dans les archives.
