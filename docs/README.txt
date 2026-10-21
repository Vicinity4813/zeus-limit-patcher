ZEUS LIMIT PATCHER 1.0 - Zeus: Master of Olympus / Poseidon
==========================================================
(Version française : LISEZMOI.txt)

What it does
------------
Big cities hit fixed limits in the game: walkers stop appearing, workers cannot find
a path, no more buildings can be placed. This program doubles these limits in your
English Zeus.exe 2.1.4.0:
  - walkers (people and animals moving in the city): 2000 -> 4000
  - paths (routes computed for the walkers)         : 1000 -> 2000
  - buildings                                       : 4000 -> 8000
Nothing else changes (graphics, map size, gameplay).

How to use it
-------------
1. Close the game. Copy your saved games (Save folder) somewhere safe.
2. Run "Zeus Limit Patcher.exe" (nothing to install).
3. Click "Browse..." and choose the game's Zeus.exe. The window shows what it is:
   normal limits, already doubled, or exe not recognised.
4. Click "Double the limits" and confirm.

The original exe is copied into the "Zeus.exe original" folder next to the patcher.
To go back: put that copy back in place of Zeus.exe.
The "Language" list at the top right switches the program to French.

Saved games
-----------
- Existing saved games open normally with the patched exe.
- Saved games made with the patched exe CAN NO LONGER be opened by the original exe
  (the game crashes). Keep a copy of your saves before you patch if you might go back.

Supported exes
--------------
- English Zeus.exe 2.1.4.0 (Zeus with or without Poseidon): GOG, Steam, widescreen builds.
- With Zeus - EXE French Patch (https://github.com/Vicinity4813/zeus-exe-french-patch),
  before or after this patch (same result).
- With zeus-qol 1.0.0 (https://github.com/andrehamon/zeus-qol): install zeus-qol first
  (its installer only accepts the original GOG exe), then this patcher. To uninstall
  zeus-qol later, first put back the exe from "Zeus.exe original".
- The French retail exe is not supported (use the English exe with the French Patch).

Good to know
------------
- Before writing anything, the program checks that undoing the patch would give back
  the original exe byte for byte; otherwise nothing is changed.
- A new walker always gets the lowest free number.
- Two other tables of 2000 entries whose role is unknown are not enlarged; they were
  never full in the cities measured.
- The program never connects to the Internet. It only writes the chosen Zeus.exe and
  the "Zeus.exe original" folder. Its source code is in the "source" folder.
- Command line:
    "Zeus Limit Patcher.exe" [--lang fr|en] --info <Zeus.exe> | --patch <Zeus.exe>

Credits
-------
- Code: MIT License (LICENSE file). Vicinity4813, 2026.
- Reading saved games (source\ZeusPak.cs, SavCli tool) is adapted from zeus_mapper by
  zawapl (Jakub Zawadzki), MIT License (source\LICENSE-zeus_mapper.txt).
- Thanks to JackFuste for the widescreen exes the patch was developed on.
- Compatibility tested with zeus-qol by andrehamon (published release); no code used.
- Zeus: Master of Olympus / Poseidon are games by Impressions Games / Sierra.
  Fan project, not affiliated with them; no game file is included.
