# Nexus Mods page — TESL: Everlasting (draft, not posted)

Paste the BBCode block into the Nexus description editor (switch it to BBCode/source mode first). The other fields go in
the matching boxes of the upload form.

## Form fields

- **Mod name:** TESL: Everlasting — The Elder Scrolls: Legends Offline Revival
- **Summary** (short text under the title):
  > The full Elder Scrolls: Legends, playable again. Stories, puzzles, arenas, ranked and casual vs the AI, quests,
  > rewards, progression and the store, in a complete offline mode with no server or account. Works alongside TESL-Reborn.
- **Category:** Overhauls (or Gameplay / Miscellaneous, whatever the game page offers)
- **Tags:** Offline, Overhaul, Single Player, Restoration, Card Game, BepInEx, AI, Quality of Life
- **Version:** 1.0.0
- **Requirements box:** BepInEx 5.4.23 x86 (off-site, https://github.com/BepInEx/BepInEx/releases; included in the
  "with BepInEx" file). Optional: TESL Forge (for making mods).
- **Permissions** (match our license):
  - Upload to other sites: **No** (one official source; link here instead)
  - Convert to other games: No
  - Modify / improve: **Yes, noncommercially**, credit required, versions must stay free
  - Use assets in other mods: Yes with credit, noncommercially
  - Use assets in mods or files that are being sold: **No**
  - Earn Donation Points: owner's choice
  - Custom text: "Licensed under PolyForm Noncommercial 1.0.0 with additional permissions (see LICENSE). Never sell or
    paywall this mod or versions of it. Mods you make for it are yours."
- **Files to upload:**
  1. `TESL-Everlasting-1.0.0-with-BepInEx.zip` — main file
  2. `TESL-Everlasting-1.0.0.zip` (plugin only) — main file, for folders that already have BepInEx (TESL-Reborn)
  3. Optional: `TESL-Everlasting-1.0.0-source.zip` (miscellaneous file), or just link GitHub
- **Header banner:** `docs/nexus/banner-1300x372.png` (no text; Nexus draws the title over the darker lower-left).
- **Images (1920x1080, captioned):** `docs/nexus/images/` — `00-*` is the title card (use as the primary image), then the numbered features in order.
- **Older images** (from `docs/screenshots/`): 01-story-picker (primary), 06-battle, 04-story-map, 03-story-dialogue,
  07-victory, 10-store-packs, 11-custom-mode, 13-extra-emotes, 14-mod-avatars.
- **Nexus caution:** keep this page free of anything about obtaining the game (the "LUA Tools and manifest files" line
  stays only in the GitHub disclaimer); Nexus removes pages that point toward piracy.

## Description (BBCode)

```bbcode
[center][size=6][b]TESL: Everlasting[/b][/size]
[size=4]The Elder Scrolls: Legends — playable again[/size][/center]

[line]
[size=5][b]Description[/b][/size]

When the official servers closed, [i]The Elder Scrolls: Legends[/i] stopped working: every match, story chapter and reward came from Bethesda's servers.

[b]TESL: Everlasting[/b] brings the whole game back in a complete, end-to-end [b]offline mode[/b]. It replaces the lost servers with a small service that runs inside the game itself, plus a rules engine that plays every match on your own PC. The original game does the rest: its own menus, animations, art, music and voice lines, exactly as you remember them.

[list]
[*]No account, no internet, no server. Your progress is saved on your PC.
[*]Doesn't change any game files. Remove it and the game is as it was.
[*]Works alongside the popular [b]TESL-Reborn[/b] project: use Reborn for online matches against other players and Everlasting for offline play, in the same installation. Switch with one click in the F10 menu.
[/list]

[line]
[size=5][b]Main features[/b][/size]

[b]Play[/b]
[list]
[*]The original first-run experience: the tutorial battles, name entry, then the main menu.
[*][b]Stories:[/b] The Forgotten Hero, The Dark Brotherhood, Clockwork City and Isle of Madness. All 187 campaign battles with their special lanes, scripted setups, dialogue, maps, choices and rewards.
[*][b]Puzzles[/b] with their original setups.
[*][b]Practice, Casual and Ranked[/b] against the AI (ladder, Serpent rank, seasons, monthly reward cards), [b]Solo Arena[/b] and [b]Versus Arena[/b].
[*]An AI opponent that plays real decks, emotes on its own with a personality, and answers your emotes.
[/list]

[b]Cards and rules[/b]
[list]
[*][b]Every card in the game (1,718)[/b] plays with its full rules: Prophecy, runes, lanes, Betray, Exalt, Consume, Plot, Assemble, Mobilize, Invade, Treasure Hunt and more.
[*]A rules engine checked by hundreds of automatic tests.
[/list]

[b]Rewards and progression[/b]
[list]
[*]Daily quests, Battle Victories, account levels 2–50 with rewards, 185 titles, all 72 card backs, gold, gems and tickets.
[*]The store: all six set packs with pack opening, premade decks, card backs and story acts.
[/list]

[b]Look and feel (F10 menu)[/b]
[list]
[*]Resizable window, windowed / maximized / fullscreen, quality presets up to [b]Ultra[/b].
[*]Frame cap 60/120/144/unlimited, VSync, MSAA up to 8×, texture filtering up to 16×, FPS counter.
[/list]

[b]Mods[/b] (made with [b]TESL Forge[/b], our free modding tool)
[list]
[*]New cards with rules, decks, avatars and emotes, card backs, boards, music, voice lines, remastered art, AI opponents, game modes and full story campaigns.
[*]Safe load order and compatibility checks: a broken mod item is skipped and reported instead of breaking the game.
[/list]

[b]Optional cheats (offline only)[/b]
[list]
[*]Gold, gems, tickets, unlock all cards and decks, and in-match helpers, for playing against the AI only.
[/list]

[line]
[size=5][b]Fair play[/b][/size]

Cheats and gameplay mods work [b]only offline against the in-game AI[/b]. In an online launch, only local visual and audio mods (remastered art, sounds, music, boards that only you see or hear) and graphics options load. If a game with Everlasting's offline code ever tries to join a remote match, it is closed immediately. No cheating against other players, by people or AI.

[line]
[size=5][b]Requirements[/b][/size]

[list]
[*]Windows 10 or 11.
[*][i]The Elder Scrolls: Legends[/i] for PC, installed (Steam or Bethesda.net launcher, final version). Not included.
[*][b]BepInEx 5.4.23 (x86)[/b]: included in the "with BepInEx" download. If TESL-Reborn is installed you already have it; use the "plugin only" download.
[*]Optional: [b]TESL Forge[/b] to make your own mods.
[/list]

[line]
[size=5][b]Installation instructions[/b][/size]

[list=1]
[*][b]Back up[/b] the folder [font=Courier New].offline-user-data[/font] in your game folder, if it exists.
[*]Find your game folder (Steam: right-click the game > Manage > Browse local files).
[*]Download the right file:
[list]
[*][b]with BepInEx[/b] if the game folder has no [font=Courier New]BepInEx[/font] folder yet;
[*][b]plugin only[/b] if it already has one (for example from TESL-Reborn).
[/list]
[*]Extract the download into the game folder (next to [font=Courier New]The Elder Scrolls Legends.exe[/font]). Mod managers aren't supported yet; install manually.
[*]Double-click [font=Courier New]Play TESL Everlasting.cmd[/font] in the game folder (or start the game normally). A new profile begins with the tutorial.
[*]Press [b]F10[/b] in game for options and the online/offline switch.
[/list]

[b]Uninstall:[/b] delete [font=Courier New]BepInEx/plugins/TESLOffline[/font] and [font=Courier New]BepInEx/patchers/TESLOffline[/font]. Your offline profile stays in [font=Courier New].offline-user-data[/font] until you delete it.

[line]
[size=5][b]Known limits[/b][/size]
[list]
[*]26 original cards don't have their effects yet; 55 titles can't be earned offline yet.
[*]Puzzles have no hint system. AI decks are rebuilt from colours and difficulty (the originals aren't in the game data).
[*]Custom game modes start from the F10 menu.
[/list]

[line]
[size=5][b]Shout outs[/b][/size]

[list]
[*][b]Dire Wolf Digital, Sparkypants and Bethesda[/b] for making The Elder Scrolls: Legends.
[*][b]crow2cr0w[/b] ([url=https://github.com/crow2cr0w/tesl-offline]tesl-offline[/url]) for publishing the original card rule data.
[*][b]The UESP editors[/b] and the [b]Legends Fandom wiki[/b] editors for years of careful documentation.
[*][b]The TESL-Reborn team[/b] for keeping online play alive, and a Reborn developer who kindly helped with a reference installation. (TESL-Reborn is a separate project; please send questions about Everlasting here, not to them.)
[*][b]BepInEx and HarmonyX[/b] developers.
[*]Everyone who never stopped loving this game.
[/list]

[line]
[size=5][b]Disclaimer[/b][/size]

Free fan project. Not affiliated with or endorsed by Bethesda, ZeniMax, Dire Wolf Digital, Sparkypants or TESL-Reborn. The game itself is not included; you need your own installed copy. Use at your own risk and back up your profile.

Source code, license and full docs: [url=https://github.com/ArchAngelAries/tesl-everlasting]github.com/ArchAngelAries/tesl-everlasting[/url]
Make your own mods: [url=https://github.com/ArchAngelAries/tesl-forge]TESL Forge[/url]

[center]If you enjoy it, you can support us on [url=https://ko-fi.com/archangelaries]Ko-fi[/url] or [url=https://www.patreon.com/c/ArchAngelAries]Patreon[/url]. Thank you![/center]
```
