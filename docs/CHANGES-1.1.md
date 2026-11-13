# UltraLightWallpaper 1.1

## Boucles sur les deux écrans

La version 1.0 envoyait `input-repeat=-1` à libVLC 3.0.24. Dans cette version, une valeur négative désactive la répétition. La version 1.1 utilise le compteur natif valide, avec une reprise de secours à son épuisement. Les boucles ordinaires conservent le lecteur, la sortie vidéo et les fenêtres natives. Une pause reste prioritaire sur la reprise de secours.

Les essais ont observé 8 boucles complètes de la vidéo de 64,640 s sur l’écran principal à 1,5×, et 2 boucles complètes de la vidéo de 208,880 s sur l’écran de gauche à 1,2×. Les images présentées continuent d’augmenter après les retours au début. Les segments courts et les reprises après pause ont également été vérifiés.

## Début et fin de séquence

Dans **Fonds d’écran → Séquence vidéo** :

1. Réglez **Début** et **Fin** avec les deux poignées ou les champs en secondes, qui acceptent trois décimales.
2. Décochez **Jusqu’à la fin du fichier** pour choisir une fin personnalisée.
3. Les boutons **Début / Fin = position actuelle** prennent la position de lecture.
4. Cliquez sur **Appliquer la séquence**. Avec **Lire en boucle**, chaque retour reprend au début choisi.

**Toute la vidéo** prépare un retour aux bornes du fichier ; cliquez ensuite sur **Appliquer la séquence**. Les modifications de coupe restent en brouillon jusqu’à cette application, pour éviter une recréation du lecteur à chaque mouvement. Les bornes sont contrôlées par rapport à la durée réelle et enregistrées pour l’écran sélectionné. La durée minimale est de 0,1 s. La précision de saisie ne promet pas une coupe à l’image près : la présentation dépend des images, des images clés et du format source.

## Démarrage

La fenêtre n’attend plus le chargement du moteur vidéo : celui-ci démarre en arrière-plan, une seule fois. Les informations des fichiers sont lues de manière asynchrone. Le cache des 321 bibliothèques de plug-ins est préparé et inclus dans les compilations et le paquet portable ; sa correspondance avec les bibliothèques est vérifiée lors de la publication.

Sur la machine de test, le premier chargement sans cache a pris 18,6 s ; les chargements suivants avec cache ont pris 57 à 69 ms dans le banc de test et 451 ms au redémarrage de l’application réelle. La construction de l’interface a pris environ 1,1 à 1,8 s. Ces valeurs ne comprennent pas un éventuel contrôle de sécurité de Windows avant l’entrée dans le programme.

## Interface

Navigation et réglages en français, couleurs cohérentes, panneaux fluides, boutons qui passent à la ligne et défilement vertical. Les quatre pages ont passé les vérifications de débordement à 640 × 560, 800 × 650 et 1 200 × 900 pixels. Les modes de suspension restent dans **Performances**. **Application** contient aussi une commande pour quitter complètement le logiciel.

Vos vidéos ne sont pas copiées ni modifiées. Les anciens réglages restent compatibles ; une fin absente signifie la fin du fichier. Les rapports détaillés et les limites des essais sont dans `VERIFICATION.md`.
