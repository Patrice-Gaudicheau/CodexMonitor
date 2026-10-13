# Vérifications — 13 octobre 2026

La publication actuelle et les vérifications du nouveau panneau sont décrites dans [UI-REDESIGN.md](UI-REDESIGN.md). Les résultats ci-dessous archivent la version initiale avant cette refonte visuelle.

## Version initiale

- `dist/win-x64/CodexMonitor.exe`, version 1.0.0, Windows x64, .NET 10 autonome et fichier unique.
- SHA-256 : `328DD6F4D3195B24916B45B6E8B1423E3EA85D2B01978AEBEF901D734305EE14`.
- SDK 10.0.401, runtime de publication 10.0.12, Codex CLI natif 0.159.0.
- Dossier de distribution : un seul fichier, environ 111 Mio. Sous-système PE Windows GUI confirmé (`2`). Aucun runtime externe requis pour le lancement.

## Tests automatisés

**20 groupes réussis**, exécutés avec `scripts/Test.ps1 -Live`, sans framework tiers :

- Conversion utilisé/restant pour 100, 87, 50, 9 et 0 %, limites de couleur et arrondi conservateur.
- Valeurs manquantes ou nulles, catégories multiples, priorité 5 h, semaine uniquement, sélection disparue, autorité de la vue multi-catégorie et repli historique.
- Péremption après 3 minutes, remise à zéro atteinte, recul d’horloge, panne puis récupération.
- Champs inconnus, nombres hors plage, types inattendus, timestamp invalide, budget individuel et séparation des crédits.
- Infobulle limitée à 127 caractères avec date de remise à zéro.
- Rendus 16/20/24/32/40/48 px pour 100/87/50/9/0 et indisponibilité ; planche examinée visuellement. Pas de fuite GDI/HICON après 1 500 créations/destructions.
- Transport simulé : réponse normale, JSON malformé, erreur d’authentification, silence jusqu’au délai, fermeture du serveur et rejet d’un callback de jetons inattendu. Le faux secret de l’erreur serveur n’est pas propagé.
- Arrêt brutal d’un serveur suivi d’une reconnexion ; arrêt forcé du processus propriétaire sans `Kill(entireProcessTree)` et preuve que le Job Object termine son serveur.
- Lecture réelle de quotas par le client C#, puis preuve d’arrêt de son App Server.

## Exécutable publié et Windows réel

`scripts/Verify-Published.ps1`, exécuté sur l’exécutable correspondant à l’empreinte ci-dessus, pendant 75 secondes : **réussi**.

- Quatre lectures réelles, dont deux automatiques et deux déclenchées par les actions du menu. Reconnexion avec un nouveau processus serveur et passage temporaire au gris.
- Dernière valeur de cette vérification : **86 % restants**, fenêtre hebdomadaire de 10 080 minutes, réinitialisation annoncée le **18 octobre 2026 à 08:28:15, Europe/Paris**. Aucune fenêtre de 5 h fournie par le service pour ce compte lors du test.
- Une vérification précédente a observé le passage réel de 87 à 86 % au rafraîchissement automatique.
- Icône enregistrée dans Explorer, rectangle obtenu par `Shell_NotifyIconGetRect`, rendu 16 px sur cette configuration. Menu et panneau réellement affichés, actions de sélection et d’actualisation exercées.
- Réenregistrement de l’icône après suppression de **sa propre icône** et simulation du message documenté `TaskbarCreated` : réussi. Explorer n’a pas été redémarré.
- Aucun console ni fenêtre principale ; deuxième instance refusée avec code 10. Relancements successifs réussis. Tous les serveurs de l’essai fermé ont disparu.
- Lancement avec `DOTNET_ROOT` pointant vers un dossier inexistant et `DOTNET_MULTILEVEL_LOOKUP=0` : réussi. Ces vérifications et le contenu de la publication confirment l’utilisation du runtime inclus ; aucun essai sur une autre machine dépourvue de .NET n’a été réalisé.
- Environ **71 Mio** pour le moniteur et **0,80 seconde CPU sur 75 secondes**, y compris les captures et interactions de test. Ces mesures excluent le processus App Server.

Une vérification supplémentaire par **UI Automation Windows**, ciblée sur le panneau de débordement, a retrouvé l’icône CodexMonitor, capturé son **badge réel « 86 » vert menthe** et déclenché le clic gauche via Explorer : le panneau « Quotas Codex » s’est ouvert, puis a été fermé. L’application a ensuite été laissée en fonctionnement normal.

## Résultats consultables

- `artifacts/verification/final/verification.json` et `duplicate-instance.json` : essai publié et double instance.
- `artifacts/verification/final/context-menu.png` et `details-panel.png` : captures des fenêtres du programme.
- `artifacts/verification/interactive/actual-tray-badge.png` et `shell-interaction.json` : badge réellement rendu par Explorer et clic gauche.
- `artifacts/verification/icons/contact-sheet.png` : badges aux différentes tailles.
- `scripts/Verify-ShellBadge.ps1` reproduit le contrôle visuel dans le débordement, lorsque CodexMonitor est déjà ouvert.

## Limites explicites

Windows a initialement placé cette nouvelle icône dans le **débordement**. Pour une lecture permanente sans clic, l’utilisateur doit la faire glisser vers la zone visible ou modifier sa visibilité dans les paramètres Windows. L’application ne modifie pas cette préférence et ne remplace pas la barre des tâches. Dans ce cas, `Shell_NotifyIconGetRect` retourne le rectangle de la flèche de débordement tant que celui-ci est fermé ; la preuve visuelle du badge provient donc du contrôle ciblé réalisé débordement ouvert.

Les tailles correspondant aux mises à l’échelle 100/125/150/200/250/300 % ont été rendues et examinées, **sans changer les réglages d’affichage de la machine**. Les changements réels entre ces configurations restent à tester sur les écrans concernés. La récupération après redémarrage réel d’Explorer et un vrai incident réseau externe prolongé n’ont pas été imposés à la machine ; leurs mécanismes ont été exercés par les scénarios contrôlés décrits ci-dessus.

Le démarrage automatique est resté **désactivé** : lecture de son état vérifiée, aucune écriture du registre déclenchée pendant les essais. L’option activée par un utilisateur et la publication ARM64 n’ont pas été vérifiées ici.
