# Projet Unity - Application XR pour Magic Leap 2

Cette application Unity est conçue pour être utilisée avec le casque **Magic Leap 2**, en exploitant les capacités de son contrôleur XR. Elle propose une interface interactive, des déplacements immersifs et des interactions avec des objets en réalité augmentée.

## ✨ Fonctionnalités principales

- **Menu interactif flottant** :
  - Affiché au démarrage de l'application.
  - Peut être affiché ou masqué à volonté.
  - Contient plusieurs actions essentielles (voir ci-dessous).
- **Téléportation intuitive** : déplacement rapide dans l'espace via le contrôleur.
- **Déplacement vertical** : possibilité de monter ou descendre à l'aide du trackpad.
- **Interaction avec objets grabbables** :
  - Indication visuelle lorsqu'un objet peut être saisi.
  - Possibilité de saisir et déplacer certains objets.
  - Les objets reviennent à leur position initiale une fois relâchés.

## 🎮 Commandes

### 📋 Navigation dans le menu
- **Afficher/Masquer le menu** : double appui rapide sur le **bumper**.
- **Sélection d'un élément du menu** : viser avec le contrôleur puis appuyer sur le **trigger**.

#### 📦 Contenu du menu
- **Quit App** : ferme immédiatement l'application.
- **Start/Stop Recording** :
  - Démarre ou arrête l'enregistrement en continu de la position du **casque** et du **contrôleur**.
  - Les données sont sauvegardées dans un fichier `.csv`.
- **Start/Reset Animation** :
  - Démarre une animation spécifique.
  - Si l'animation est en cours, le bouton permet de la réinitialiser.

### 🧭 Déplacements
- **Monter** : appuyer sur la partie haute du **trackpad**.
- **Descendre** : appuyer sur la partie basse du **trackpad**.
- **Téléportation** :
  - Maintenir le **bumper** enfoncé pour faire apparaître une ligne de visée.
  - Viser l'emplacement souhaité.
  - Appuyer sur le **trigger** tout en maintenant le bumper pour se téléporter.

### 🪄 Interaction avec les objets
- **Indication de grabbabilité** : un objet devient plus foncé lorsqu’il est ciblé et peut être saisi.
- **Saisir un objet** : viser un objet grabbable et appuyer sur le **trigger**.
- **Relâcher un objet** : relâcher le **trigger** pour que l'objet retourne à sa position d'origine.

