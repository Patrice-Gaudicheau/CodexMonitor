# Refonte du panneau de détails — 13 octobre 2026

La fenêtre ouverte par clic sur le badge utilise désormais une présentation native Windows Forms inspirée de Windows 11 : fond clair, cartes blanches arrondies, accents menthe/turquoise, anneau de quota avec pourcentage central, titre du quota sélectionné et bouton « Actualiser » large en bas.

Les cartes « Réinitialisation » et « Dernière lecture » affichent les dates et heures réelles, en français et dans le fuseau Windows. La cadence et la règle de péremption restent visibles dans une zone plus discrète. Les autres quotas et informations de crédits sont conservés dans le contenu défilant. Le bouton reste accessible lorsque le contenu déborde ; la touche Échap ferme le panneau.

La fenêtre conserve sa bordure et son bouton de fermeture natifs. Ses coins arrondis utilisent le paramètre DWM documenté de Windows 11. Les cartes et l’anneau sont dessinés avec GDI+, sans image, WebView ni dépendance ajoutée. Le bouton est un véritable contrôle Windows Forms utilisable au clavier et via l’accessibilité Windows. Les cartes exposent leur contenu aux lecteurs d’écran.

## Portée

- Ajout de `DetailsForm.cs` et `DetailsViewModel.cs` ; remplacement du TextBox de détails dans `MonitorContext.cs`.
- Le modèle de présentation réutilise exactement la sélection et la fraîcheur définies dans `CodexMonitor.Core`. Une panne ou une valeur périmée donne un tiret et aucun arc de pourcentage.
- Les fichiers de calcul des quotas, du transport App Server, du rendu du badge, de la zone de notification, des préférences, du point d’entrée et de la configuration de publication sont **inchangés**, empreintes vérifiées avant/après.
- Aucune modification des intervalles, des méthodes App Server, du menu du badge ou du format de publication. Le même exécutable autonome x64 a été remplacé à `dist/win-x64/CodexMonitor.exe` puis relancé.
- Le démarrage automatique précédemment demandé demeure actif, avec la même commande de registre. Aucun changement de cette commande pendant la refonte.

## Vérification

- **22 groupes de tests réussis**, incluant les 20 groupes préexistants, les états sélectionné/manquant/périmé du nouveau panneau, les dates et le bouton natif.
- Rendus des cartes examinés à 100, 125, 150 et 200 %, avec 100/87/50/9/0 et donnée manquante. Le rendu à 100 % a également été vérifié dans la véritable fenêtre Windows. Les paramètres d’affichage de la machine n’ont pas été changés.
- Mise à l’échelle native `AutoScaleMode.Dpi`, dessin en coordonnées logiques, taille du panneau limitée à la hauteur disponible sur l’écran ; contenu défilant et bouton fixe. Pas de pourcentage tronqué à 100 %.
- Essai de 75 secondes de la publication : quotas réels, rafraîchissement automatique et manuel, reconnexion, restauration du badge, double instance et arrêt du serveur : réussi.
- Après installation, le clic sur l’icône a ouvert la vraie fenêtre. Windows UI Automation a ensuite actionné son vrai bouton « Actualiser » : nouvelle lecture réussie et nouvel horaire observés, puis fermeture normale du panneau. L’application reste active dans la zone de notification.
- Dernière vérification : **86 % restants, Semaine**, réinitialisation fournie le 18 octobre 2026 à 08:28, heure locale.

Capture finale : `artifacts/verification/details-redesign/installed/details-window.png`.

Rapport d’interaction : `artifacts/verification/details-redesign/installed/ui-interaction.json`.

Publication de la refonte avant la correction finale de hauteur, version 1.0.0, SHA-256 :

`609821028383F11E2771EBE13E5FD5D24D744DC952BF5B2A5B7ECDBB1591B53E`

Pour reproduire les contrôles : `scripts/Test.ps1 -Live`, puis `scripts/Verify-DetailsWindow.ps1` avec CodexMonitor déjà ouvert. Ce dernier ne consulte que la zone de notification et la fenêtre du programme, sans modification de réglage.

## Correction finale : barre de défilement

La hauteur de la fenêtre est maintenant calculée à partir du contenu mesuré à pleine largeur, du pied contenant le bouton et de l’espace réellement disponible sur l’écran. Aucun changement des marges, de la typographie, du dessin des cartes, de l’anneau ou du bouton. Le défilement reste actif uniquement lorsque la hauteur d’écran ne permet pas de tout afficher, et disparaît lorsque suffisamment d’espace redevient disponible.

**23 groupes de tests réussis.** Vérification du chemin Windows Forms `WM_DPICHANGED` à 96/120/144 DPI, avec simulation du DPI des contrôles enfants dans le programme de test seulement ; les réglages Windows globaux n’ont pas été modifiés. Les captures et mesures confirment tout le contenu visible sans barre :

| Échelle | Hauteur du contenu | Hauteur disponible | Barre verticale |
| --- | --- | --- | --- |
| 100 % | 585 px | 587 px | Absente |
| 125 % | 732 px | 735 px | Absente |
| 150 % | 878 px | 881 px | Absente |

La hauteur d’écran simulée de 720 px conserve le défilement, permet d’atteindre la dernière ligne et garde le bouton visible. Le retour à une hauteur suffisante supprime de nouveau la barre.

L’exécutable installé a été relancé et sa fenêtre réelle à 100 % a été contrôlée par Windows UI Automation et les styles natifs de son viewport : barre verticale absente, contenu de 585 px entièrement contenu dans les 587 px disponibles. Le bouton « Actualiser » a déclenché une nouvelle lecture réelle. La correction augmente ici la hauteur client de seulement 5 px.

La lecture des quotas, le rendu du badge, le transport, le rafraîchissement, le modèle de présentation et la publication sont inchangés, empreintes vérifiées. Le démarrage automatique reste actif au même emplacement.

Capture finale : `artifacts/verification/ui-polish/installed/details-window.png`.

Mesures DPI : `artifacts/verification/ui-polish/dpi-layout.json`.

Publication actuelle, SHA-256 : `364B3BAFE2F3D17A018AAFB47C6302404DB71147058900CAF0C8FC745D0218B3`.
