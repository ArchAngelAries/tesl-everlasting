# Third-party notices — TESL: Everlasting

TESL: Everlasting is our own code (see `LICENSE`). It runs inside the original game through the components below, which
keep their own licenses.

## Required to run (bundled in the "with BepInEx" release; not our code)

| Component | Version tested | License | Use |
|---|---|---|---|
| [BepInEx](https://github.com/BepInEx/BepInEx) | 5.4.23.x (x86) | MIT (BepInEx 5.x; only BepInEx 6 is LGPL-2.1) | Plugin loader for Unity games. Our plugin and preloader patch load through it. |
| [HarmonyX](https://github.com/BepInEx/HarmonyX) (`0Harmony.dll`) | ships with BepInEx 5.4 | MIT | Runtime method patching. |
| [MonoMod](https://github.com/MonoMod/MonoMod) (used by HarmonyX) | ships with BepInEx 5.4 | MIT | Runtime detours used by HarmonyX. |
| [Mono.Cecil](https://github.com/jbevain/cecil) | ships with BepInEx 5.4 | MIT | Used by our preloader patch (`TESL.Offline.Access`) to adjust member access in memory at startup. |

**Bundling BepInEx: allowed.** BepInEx 5.4.23 and the HarmonyX, MonoMod and Mono.Cecil libraries inside it are all
MIT-licensed. MIT allows redistribution as long as each copyright notice and license text is included: see
`licenses/BepInEx-MIT.txt`, `licenses/HarmonyX-MIT.txt`, `licenses/MonoMod-MIT.txt` and `licenses/Mono.Cecil-MIT.txt`
(ship the `licenses/` folder in the release). The release "with BepInEx" ships the official, unmodified BepInEx 5.4.23
x86 files ([release page](https://github.com/BepInEx/BepInEx/releases)). Because TESL-Reborn also installs BepInEx, a
"plugin only" release is offered too, so nobody overwrites Reborn's copy.

## The original game

*The Elder Scrolls: Legends* (program, `Assembly-CSharp.dll`, asset bundles, card data, art, audio, text) is owned by
Bethesda Softworks / ZeniMax Media; the game was developed by Dire Wolf Digital, later by Sparkypants. TESL: Everlasting
is compiled against the game's own assemblies on the builder's computer and patches them in memory at runtime. **The game
itself (program, asset bundles, art, audio, text) is not included.** The card rule data and store pictures below are
included because the mod needs them.

## Reference data (facts and sources)

| Data in this repository | Source | Notes |
|---|---|---|
| `data/puzzle-setups.tsv`, `data/level-rewards.tsv`, story/arena/title notes | [UESP](https://en.uesp.net/wiki/Legends:Legends) (page content CC BY-SA 2.5) | Game facts (numbers, names) collected from UESP pages, with the page linked per row. Thanks to the UESP editors. |
| `data/original-rules/card-rule-graphs.jsonl.gz`, `card-fx-lookups.tsv`, `card-source-index.tsv` (original card rule graphs) | Bethesda's original game data, recovered and published by [crow2cr0w/tesl-offline](https://github.com/crow2cr0w/tesl-offline) (`data/retail-rule-graphs.jsonl.gz` and companions, commit a46dafb) | Included because the engine needs it for the original rules of about 1,000 cards (TESL Forge's card catalog reads it too). The data belongs to Bethesda/ZeniMax. The tesl-offline repository is GPL-3.0; to respect that, these are kept as separate data files with the GPL-3.0 text next to them (`data/original-rules/LICENSE-GPL-3.0.txt`). Our code reads them but isn't derived from tesl-offline's code. Thanks to crow2cr0w. |
| `data/original-rules/excluded.tsv`, `prefer-original.tsv`, `review-original-vs-spec.tsv` | Our own test results | Ours, under `LICENSE`. |
| `data/store-art/` (six store pack pictures) | Official in-game pack renders from [UESP](https://en.uesp.net/wiki/Legends:Card_Packs) | Bethesda artwork, included so the offline store can show the packs (the shipped game has no store art). UESP page content is CC BY-SA 2.5. |
| `docs/screenshots/` | Our own screenshots of the game running | Show copyrighted game content belonging to its owners, used to illustrate the mod. |

## Optional tools used by developers only

| Tool | License | Use |
|---|---|---|
| .NET Framework C# compiler (`csc`) | Microsoft | Builds the plugin (`Build.ps1`). Not distributed. |
