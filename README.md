# Zeus Limit Patcher

**Doubles the data limits of *Zeus: Master of Olympus* / *Poseidon* (English Zeus.exe 2.1.4.0): walkers, paths, buildings and army companies**
· [Version française plus bas](#français)

![Zeus Limit Patcher window](docs/images/window.png)

Big cities hit fixed limits in the game: walkers stop appearing, workers cannot find a path, no more buildings can be placed, big invasions stay small. This patcher doubles these limits in your own `Zeus.exe`:

| Limit | Original exe | Patched exe |
|---|---|---|
| Walkers (people and animals moving in the city) | 2000 | 4000 |
| Paths (routes computed for the walkers) | 1000 | 2000 |
| Buildings | 4000 | 8000 |
| Army companies (soldiers, invaders and monsters grouped in companies) | 100 | 200 |

Nothing else changes: graphics, map size, gameplay and the number of cities on the world map stay the same. No game file is provided.

## Download

Get `Zeus-Limit-Patcher-1.1.zip` from the **[Releases](../../releases)** page: the patcher, Zeus Save Check, instructions in English and French, and the source code.

> Windows may show *"Windows protected your PC"* the first time, because the program is new and unsigned: click **More info**, then **Run anyway**.

## Use

1. Close the game. Copy your saved games somewhere safe (see below).
2. Run `Zeus Limit Patcher.exe`, click **Browse…** and choose the game's `Zeus.exe`. The window shows what it is (normal limits, already doubled, not recognised).
3. Click **Double the limits** and confirm.

The original exe is copied into the `Zeus.exe original` folder next to the patcher. **To go back**, put that copy back in place of `Zeus.exe`.

Command line: `"Zeus Limit Patcher.exe" [--lang fr|en] --info <Zeus.exe> | --patch <Zeus.exe>`

## Saved games

- Existing saved games open normally with the patched exe.
- **Saved games made with the patched exe can no longer be opened by the original exe (the game crashes).** Keep a copy of your saves before you patch if you might go back.
- Saved games made with a 1.1 exe cannot be opened by a 1.0 exe either. Saved games made with a 1.0 exe open with a 1.1 exe.
- **Zeus Save Check** (`Zeus Save Check.exe`, in the zip): drop .sav files on it to see which exe wrote them (normal, patched 1.0 or 1.1) and how many walkers, buildings, paths and companies are in use out of the limit.

## Upgrading from 1.0

Version 1.0 did not double the army companies. The window recognises a 1.0 exe (*Patched with 1.0*): put the original exe from the `Zeus.exe original` folder back in place of `Zeus.exe`, then patch it again with 1.1.

## Supported exes

- English `Zeus.exe` 2.1.4.0 (Zeus with or without Poseidon): GOG, Steam, widescreen builds. Tested on 86 different English 2.1.4.0 exes.
- With **[Zeus - EXE French Patch](https://github.com/Vicinity4813/zeus-exe-french-patch)**, before or after this patch (same result).
- With **[zeus-qol](https://github.com/andrehamon/zeus-qol)** 1.0.0: install zeus-qol first (its installer only accepts the original GOG exe), then run this patcher. To uninstall zeus-qol later, first put back the exe from `Zeus.exe original`.
- The French retail exe is not supported (use the English exe with the French Patch).

## How it works

- The walker, path, building and company tables are moved into a new, larger section of the exe, and every reference to them in the game's code is updated (18 558 references in the GOG exe), along with the related counts and the saved game table.
- The patched exe always gives a new walker the lowest free number, so walker numbers stay as low as in the original game.
- Every patch is checked before anything is written: undoing it must give back the original exe byte for byte, otherwise nothing is changed.
- Tested in game: cities with more than 4000 buildings, paths no longer saturated, zeus-qol zoom and resolution changes, a big invasion with 152 companies and 2338 walkers in use at the same time (saved and loaded again).
- Two other tables of 2000 entries whose role is unknown are not enlarged; they were never full in the cities measured.
- The program never connects to the Internet. It only writes the chosen `Zeus.exe` and the `Zeus.exe original` folder.

## Building

Run `build.cmd`: it uses the C# compiler that ships with Windows (.NET Framework 4) and produces `dist\Zeus Limit Patcher.exe`, `dist\Zeus Save Check.exe` and the development tools (`PatchCli`, `Refs`, `SavCli`, `LowestTest`, `Garde`). `tools\make_release.ps1` builds the release zip.

## Credits and license

- Code: MIT License, see [LICENSE](LICENSE).
- `src/ZeusPak.cs` (used by Zeus Save Check and the saved game tool `SavCli`, not by the patcher) is adapted from **zeus_mapper** by zawapl (Jakub Zawadzki), MIT License, see [LICENSE-zeus_mapper.txt](LICENSE-zeus_mapper.txt).
- Thanks to **JackFuste** for the widescreen exes the patch was developed and tested on.
- Thanks to **Loic2402** for the in-game tests of the walker and company limits.
- Compatibility with **zeus-qol** by andrehamon was tested on its published release; no code from it is used.
- *Zeus: Master of Olympus* / *Poseidon* are games by Impressions Games / Sierra. This is a fan project, not affiliated with them; it includes no game file (only the screenshot of the patcher's window in `docs/images`).

---

## Français

**Double les limites de données de *Le Maître de l'Olympe : Zeus* / *Poséidon* (Zeus.exe anglais 2.1.4.0) : walkers, chemins, bâtiments et compagnies d'armée**

Les grandes cités butent sur des limites fixes du jeu : les walkers n'apparaissent plus, les ouvriers ne trouvent plus de chemin, on ne peut plus poser de bâtiment, les grosses invasions restent petites. Ce patcheur double ces limites dans votre propre `Zeus.exe` :

| Limite | Exe d'origine | Exe patché |
|---|---|---|
| Walkers (personnages et animaux qui circulent dans la cité) | 2000 | 4000 |
| Chemins (trajets calculés pour les walkers) | 1000 | 2000 |
| Bâtiments | 4000 | 8000 |
| Compagnies d'armée (soldats, envahisseurs et monstres regroupés en compagnies) | 100 | 200 |

Rien d'autre ne change : graphismes, taille de la carte, règles du jeu et nombre de cités sur la mappemonde restent les mêmes. Aucun fichier du jeu n'est fourni.

### Téléchargement

`Zeus-Limit-Patcher-1.1.zip` sur la page **[Releases](../../releases)** : le patcheur, Zeus Save Check, les notices en français et en anglais, et le code source.

> Au premier lancement, Windows peut afficher *« Windows a protégé votre ordinateur »*, car le programme est récent et non signé : cliquez sur **Informations complémentaires**, puis **Exécuter quand même**.

### Utilisation

1. Fermez le jeu. Copiez vos sauvegardes en lieu sûr (voir plus bas).
2. Lancez `Zeus Limit Patcher.exe`, cliquez sur **Parcourir…** et choisissez le `Zeus.exe` du jeu. La fenêtre indique ce qu'il est (limites normales, déjà doublées, non reconnu).
3. Cliquez sur **Doubler les limites** et confirmez.

L'exe d'origine est copié dans le dossier `Zeus.exe original`, à côté du patcheur. **Pour revenir en arrière**, remettez cette copie à la place de `Zeus.exe`.

Ligne de commande : `"Zeus Limit Patcher.exe" [--lang fr|en] --info <Zeus.exe> | --patch <Zeus.exe>`

### Sauvegardes

- Les sauvegardes existantes s'ouvrent normalement avec l'exe patché.
- **Les sauvegardes faites avec l'exe patché ne peuvent plus être ouvertes par l'exe d'origine (le jeu plante).** Gardez une copie de vos sauvegardes avant de patcher si vous pensez revenir en arrière.
- Les sauvegardes faites avec un exe 1.1 ne s'ouvrent pas non plus avec un exe 1.0. Celles faites avec un exe 1.0 s'ouvrent avec un exe 1.1.
- **Zeus Save Check** (`Zeus Save Check.exe`, dans le zip) : glissez-y des .sav pour voir quel exe les a écrites (normal, patché 1.0 ou 1.1) et combien de walkers, bâtiments, chemins et compagnies sont en service sur la limite.

### Passer de la 1.0 à la 1.1

La 1.0 ne doublait pas les compagnies d'armée. La fenêtre reconnaît un exe 1.0 (*Patché en 1.0*) : remettez l'exe d'origine du dossier `Zeus.exe original` à la place de `Zeus.exe`, puis patchez-le à nouveau avec la 1.1.

### Exe pris en charge

- `Zeus.exe` anglais 2.1.4.0 (Zeus avec ou sans Poséidon) : GOG, Steam, exe widescreen. Testé sur 86 exe anglais 2.1.4.0 différents.
- Avec **[Zeus - EXE French Patch](https://github.com/Vicinity4813/zeus-exe-french-patch)**, avant ou après ce patch (même résultat).
- Avec **[zeus-qol](https://github.com/andrehamon/zeus-qol)** 1.0.0 : installez d'abord zeus-qol (son installateur n'accepte que l'exe GOG d'origine), puis lancez ce patcheur. Pour désinstaller zeus-qol plus tard, remettez d'abord l'exe du dossier `Zeus.exe original`.
- L'exe français du commerce n'est pas pris en charge (utilisez l'exe anglais avec le French Patch).

### Fonctionnement

- Les tableaux des walkers, des chemins, des bâtiments et des compagnies sont déplacés dans une nouvelle section de l'exe, plus grande, et chaque référence du code du jeu est corrigée (18 558 références dans l'exe GOG), ainsi que les nombres liés et la table des sauvegardes.
- L'exe patché donne toujours à un nouveau walker le plus petit numéro libre : les numéros restent aussi bas que dans le jeu d'origine.
- Chaque patch est vérifié avant toute écriture : son annulation doit redonner l'exe d'origine octet pour octet, sinon rien n'est modifié.
- Testé en jeu : cités de plus de 4000 bâtiments, chemins qui ne saturent plus, zoom et changements de résolution de zeus-qol, grosse invasion avec 152 compagnies et 2338 walkers en service en même temps (sauvegardée puis rechargée).
- Deux autres tableaux de 2000 cases, au rôle inconnu, ne sont pas agrandis ; ils n'ont jamais été pleins dans les cités mesurées.
- Le programme ne se connecte jamais à Internet. Il n'écrit que le `Zeus.exe` choisi et le dossier `Zeus.exe original`.

### Compilation

Lancez `build.cmd` : il utilise le compilateur C# fourni avec Windows (.NET Framework 4) et produit `dist\Zeus Limit Patcher.exe`, `dist\Zeus Save Check.exe` et les outils de développement (`PatchCli`, `Refs`, `SavCli`, `LowestTest`, `Garde`). `tools\make_release.ps1` prépare le zip de la release.

### Crédits et licence

- Code : licence MIT, voir [LICENSE](LICENSE).
- `src/ZeusPak.cs` (utilisé par Zeus Save Check et l'outil de sauvegardes `SavCli`, pas par le patcheur) est adapté de **zeus_mapper** de zawapl (Jakub Zawadzki), licence MIT, voir [LICENSE-zeus_mapper.txt](LICENSE-zeus_mapper.txt).
- Merci à **JackFuste** pour les exe widescreen sur lesquels le patch a été mis au point et testé.
- Merci à **Loic2402** pour les tests en jeu des limites des walkers et des compagnies.
- La compatibilité avec **zeus-qol** d'andrehamon a été testée sur sa version publiée ; aucun code n'en est repris.
- *Le Maître de l'Olympe : Zeus* / *Poséidon* sont des jeux d'Impressions Games / Sierra. Projet de fan, sans lien avec eux ; aucun fichier du jeu n'est inclus (seulement la capture de la fenêtre du patcheur dans `docs/images`).
