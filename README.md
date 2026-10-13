# CodexMonitor

Utilitaire Windows portable affichant le **pourcentage de quota Codex restant** directement dans la zone de notification. Application C# / .NET 10, interface Windows Forms et intégration native documentée `Shell_NotifyIcon`. Aucun paquet applicatif tiers.

## Utilisation

L’exécutable est un **artefact de compilation**, exclu du dépôt Git. Après un clone des sources, exécutez `scripts\Build.ps1` avec le SDK indiqué ci-dessous pour le produire. L’exécutable obtenu fonctionne ensuite sans SDK ni runtime .NET installé.

1. Lancez `dist\win-x64\CodexMonitor.exe`. Aucun runtime .NET à installer, aucune console ni fenêtre principale. L’exécutable contient son runtime ; son premier lancement peut extraire des bibliothèques natives dans le répertoire temporaire Windows.
2. Windows peut placer une nouvelle icône dans le menu de débordement. Faites-la glisser une fois vers la zone visible de la barre des tâches, ou activez sa visibilité dans les paramètres de barre des tâches Windows. L’application ne force pas cette préférence Windows.
3. Le badge affiche le pourcentage restant, sans signe `%` pour préserver la lisibilité. Vert menthe au-dessus de 50 %, orange de 20 à 50 %, rouge en dessous de 20 %. Un tiret gris indique une valeur indisponible ou périmée.
4. Clic droit : actualiser, sélectionner une fenêtre de quota, choisir le démarrage à l’ouverture de session, consulter la version ou quitter. Clic gauche : détails et dates de remise à zéro, dans le fuseau local Windows.

Le démarrage automatique est **désactivé par défaut**. Le menu l’enregistre seulement après votre choix, dans `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, sans administrateur. Gardez l’exécutable à son emplacement final avant de l’activer ; réactivez l’option si vous déplacez le fichier.

La fenêtre de détails présente le quota sélectionné dans un anneau, avec des cartes de réinitialisation et de dernière lecture, des accents menthe et un grand bouton « Actualiser ». Son contenu défile si plusieurs quotas sont disponibles. Le bouton se désactive pendant une lecture ; Échap ferme la fenêtre. Voir [la note de refonte et ses vérifications](docs/UI-REDESIGN.md).

## Prérequis pour les quotas

Un Codex CLI **natif Windows**, connecté à un compte fournissant des limites ChatGPT, doit être installé. Version vérifiée : `codex-cli 0.159.0`. CodexMonitor trouve d’abord `%LOCALAPPDATA%\Programs\OpenAI\Codex\bin\codex.exe`, puis un `codex.exe` dans le `PATH`. Un chemin absolu personnalisé peut être fourni via la variable `CODEX_MONITOR_CODEX_PATH`.

En cas d’indisponibilité, vérifiez `codex login status`, votre connexion réseau et la version du CLI. Les comptes authentifiés uniquement par clé API peuvent ne pas fournir de pourcentages ChatGPT.

## Lecture et exactitude

Le programme lance son propre `codex app-server --listen stdio://`, indépendant de l’interface Codex, effectue `initialize` / `initialized`, puis lit **uniquement** `account/rateLimits/read`. Le protocole a été vérifié dans les schémas générés par le CLI installé et dans la [documentation officielle OpenAI](https://learn.chatgpt.com/docs/app-server#6-rate-limits-chatgpt). Aucune conversation, aucun appel d’inférence, aucune API privée, aucune lecture manuelle des jetons.

- La vue `rateLimitsByLimitId` est prioritaire ; repli sur `rateLimits` si elle manque. Les catégories supplémentaires et les deux fenêtres de chaque catégorie sont proposées dans le menu. Les budgets individuels utilisent leur champ `remainingPercent` directement. Les crédits sont décrits séparément, sans conversion inventée en pourcentage.
- Pour une fenêtre classique : `restant = clamp(100 - usedPercent, 0, 100)`. Les fractions éventuelles sont arrondies vers le bas sur le badge. Une valeur absente reste indisponible.
- Sélection automatique : priorité à la catégorie `codex`, puis à sa fenêtre de 5 heures, sa semaine et ses autres fenêtres disponibles. La durée réelle détermine le libellé : une fenêtre `primary` peut être hebdomadaire. Une sélection explicite disparue reste indisponible jusqu’à un nouveau choix.
- Actualisation au lancement puis environ toutes les **60 secondes**, après la fin de la lecture précédente. Une lecture prend au maximum 25 secondes. Les tentatives échouées ne provoquent pas de boucle rapide ; une nouvelle connexion est créée au prochain essai.
- Le badge devient gris dès une erreur, après **3 minutes** sans lecture valide ou à la date de réinitialisation fournie. Aucune remise à 100 % n’est déduite de l’horloge.
- L’infobulle est limitée aux 127 caractères de Windows ; les détails complets sont accessibles par clic gauche si plusieurs catégories ne tiennent pas.

Seul le choix du quota est enregistré dans `%LOCALAPPDATA%\CodexMonitor\settings.json`. Aucun quota obsolète, jeton, identifiant de compte ou journal brut n’est persisté en fonctionnement normal. L’App Server gère lui-même son authentification Codex habituelle. Son stderr est drainé sans conservation. Les messages d’erreur du serveur sont réduits à un code et une indication locale afin de ne pas exposer de secrets.

Une mutex par utilisateur empêche les doubles instances. Un Job Object Windows arrête l’App Server et ses descendants à la fermeture ou en cas de terminaison brutale du moniteur. L’icône est recréée sur le message documenté `TaskbarCreated` et sa taille est recalculée pour la résolution de la barre des tâches.

## Construire et vérifier

SDK requis **pour compiler seulement** : .NET 10.0.401. PowerShell natif, depuis le dossier du projet :

```powershell
dotnet build .\CodexMonitor.sln -c Release
.\scripts\Test.ps1              # tests de logique, rendu et transport simulé
.\scripts\Test.ps1 -Live        # ajoute une lecture réelle et vérifie l’arrêt du serveur
.\scripts\Build.ps1             # publication autonome x64 dans dist\win-x64
.\scripts\Verify-Published.ps1  # essai réel de 75 secondes, puis fermeture automatique
.\scripts\Verify-ShellBadge.ps1 # contrôle visuel Windows, application déjà ouverte dans le débordement
.\scripts\Verify-DetailsWindow.ps1 # contrôle du panneau et de son vrai bouton Actualiser
```

Le cache de compilation est local au projet (`.dotnet-home`, `.nuget`). La première restauration nécessite l’accès à NuGet pour les composants de publication .NET. Le répertoire de distribution contient uniquement `CodexMonitor.exe` (environ 111 Mio, runtime inclus).

`Build.ps1 -Arm64` permet une publication croisée optionnelle ; ARM64 n’a pas été validé sur matériel. `Verify-Published.ps1` nécessite une session Windows interactive et aucun autre CodexMonitor déjà ouvert. Le mode diagnostique `--verify-live <dossier>` ouvre brièvement le menu et le panneau, teste la reconnexion et la restauration de sa propre icône, enregistre un rapport sans credentials et des captures ciblées, puis quitte. Il ne modifie ni le démarrage automatique ni les préférences enregistrées. Les résultats et captures sont dans `artifacts\verification`.

## Organisation

- `src/CodexMonitor.Core` : interprétation des quotas, sélection, fraîcheur et présentation.
- `src/CodexMonitor` : transport App Server, badge GDI+, zone de notification, menu et préférences.
- `tests/CodexMonitor.Tests` : tests autonomes sans framework tiers, avec serveur simulé.
- `scripts` : compilation, tests et vérification de l’exécutable publié.
- `docs/TEST-RESULTS.md` : résultats et limites de la vérification sur cette machine.
- `docs/protocol` : schémas de la version CLI effectivement inspectée.
- `CodexMonitor.sln` : solution réunissant l’application, le cœur et les tests.

Les fichiers d’icône sont dessinés à l’exécution par le code du projet ; aucune ressource graphique externe n’est nécessaire. Les captures, résultats détaillés et exécutables mentionnés dans la documentation sont des artefacts locaux non versionnés, recréés par les scripts de vérification.
