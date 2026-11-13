# Getting started with TESL: Everlasting

> Draft for the first public release. The release package is still being finalised; folder names below match the current
> build.

## 1. Before you start

- You need *The Elder Scrolls: Legends* for PC installed (Steam or Bethesda.net launcher, final version 1.5x). We don't
  distribute game files; see the [DISCLAIMER](../DISCLAIMER.md#we-do-not-redistribute-the-game).
- Find your game folder. On Steam: right-click the game > *Manage* > *Browse local files*. It contains
  `The Elder Scrolls Legends.exe`.
- **Back up** the folder `.offline-user-data` inside the game folder, if it exists (it holds your offline profile and
  settings). Copy it somewhere outside the game folder.

## 2. Pick the right download

TESL: Everlasting loads through [BepInEx](https://github.com/BepInEx/BepInEx) 5.4.23 (x86, because the game is 32-bit).

- **Your game folder has no `BepInEx` folder:** download **`TESL-Everlasting-1.0.2-with-BepInEx.zip`**. It includes the official,
  unmodified BepInEx 5.4.23 x86.
- **Your game folder already has `BepInEx` and `winhttp.dll`** (TESL-Reborn installs them): download **`TESL-Everlasting-1.0.2.zip`**
  (plugin only), so you don't replace Reborn's copy.

Prefer to install BepInEx yourself? Download `BepInEx_win_x86_5.4.23.x.zip` from the
[official releases page](https://github.com/BepInEx/BepInEx/releases), extract it into the game folder (so `winhttp.dll`,
`doorstop_config.ini` and `BepInEx` sit next to `The Elder Scrolls Legends.exe`), start the game once, close it, then use
the plugin-only download.

## 3. Install TESL: Everlasting

1. Extract the download into the game folder. You should now have:
   - `BepInEx/plugins/TESLOffline/TESL.Offline.Engine.dll`, its `data/` folder (card rule data) and the README,
     license files and guides
   - `BepInEx/patchers/TESLOffline/TESL.Offline.Access.dll`
   - `Play TESL Everlasting.cmd`
   - with the BepInEx download also `winhttp.dll`, `doorstop_config.ini` and `BepInEx/core/`
2. That's it. No game files are changed.

## 4. First launch

1. Double-click **`Play TESL Everlasting.cmd`** in the game folder (it always starts offline), or start the game the
   usual way.
2. A new offline profile starts like the original game did: the tutorial battles of *The Forgotten Hero*, then you choose
   your name. Your name and avatar can be changed later on **Profile > Overview** (click the name).
3. Everything is unlocked the way the original game unlocked it: by playing. If you'd rather have everything now, use the
   F10 cheats (offline only).

## 5. Online or offline

- Without TESL-Reborn, the game always starts offline.
- With TESL-Reborn installed, open **F10** and pick **Switch to online mode** or **Switch to offline mode**. The game
  saves the choice in `.offline-user-data/launch.json` and restarts. The next launch uses the same mode.
- Online, only local visual/audio mods (remasters, sounds, music, boards) and graphics options are active. Cheats and
  gameplay mods never load online, and the fair-play check closes the game if offline code ever reaches a remote match.
- Problems with online play belong to TESL-Reborn's own support; problems with TESL: Everlasting belong to our issue
  tracker. Please don't mix them up.

## 6. Mods

1. Install [TESL Forge](https://github.com/ArchAngelAries/tesl-forge) anywhere outside the game folder and open it once
   (it tells TESL: Everlasting where it is). A `TESL Forge` folder next to the game folder is also found automatically.
2. Make or install a mod in TESL Forge and switch it on (**Load order & compatibility** page, or `tesl mod enable <id>`).
3. Start the game. Enabled mods load at startup. Problems are written to the logs in `.offline-user-data\logs` and the faulty
   item is skipped.

## 7. Updating and uninstalling

- **Update:** close the game, back up `.offline-user-data`, extract the new release over the old one.
- **Uninstall:** delete `BepInEx/plugins/TESLOffline` and `BepInEx/patchers/TESLOffline` (and `Play TESL Everlasting.cmd`). To
  remove your offline profile too, delete `.offline-user-data`. To remove BepInEx, delete `BepInEx`, `winhttp.dll` and
  `doorstop_config.ini` (only if TESL-Reborn doesn't need them).
