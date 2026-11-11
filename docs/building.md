# Building TESL: Everlasting from source

For developers. Players should use the release download.

## You need
- Windows, PowerShell 5.1, the .NET Framework 4.x C# compiler (`csc.exe`, part of Windows).
- *The Elder Scrolls: Legends* installed, with BepInEx 5.4.23 x86. The build compiles against the game's own assemblies
  (`Assembly-CSharp.dll`, `UnityEngine*.dll`) and BepInEx/HarmonyX from your installation. Those files are never copied
  into this repository.
- The target is Unity 2017.4 Mono (.NET 3.5 profile): C# 3 syntax only (no `$""` strings, `?.`, `=>` members, etc.).

## Where the source goes
The scripts find the game two folders up, so put this repository at **`<game folder>\.analysis\offline-engine`**, for
example `...\steamapps\common\The Elder Scrolls Legends\.analysis\offline-engine` (`git clone <repository>
".analysis\offline-engine"` inside the game folder). Install the release once first (or BepInEx 5.4.23 x86) so
`BepInEx\core` exists. `Publicize-Reference.ps1` (run by `Build.ps1`) makes `ref\Assembly-CSharp.dll`, a copy of the game's
assembly with a few types made public for compiling; it stays local and is never committed.

## Build and install
```
powershell -File Build.ps1            builds build/TESL.Offline.Engine.dll and the preloader patch
powershell -File Enable-Offline.ps1   installs the build into BepInEx/plugins|patchers/TESLOffline
```
`Disable-Offline.ps1 -Force -Forget` removes the installed plugin.

## Tests
`Run-Audit.ps1 -EngineTests -NativeGenericRules` runs the game hidden with the engine's self-tests (rules, commands,
mods, compatibility) and writes results to `exports/`. `Save-DiagnosticEvidence.ps1 -Label <name>` keeps a copy;
`Stop-Diagnostic.ps1` stops the hidden game.

## Original card data
`data/original-rules/` holds the original card rule graphs (Bethesda's data, published by crow2cr0w/tesl-offline; see
`NOTICE.md`). The engine needs them for the original rules of about 1,000 cards; without them it logs
"ORIGINAL GRAPHS: data absent" and those cards lose their effects. TESL Forge's card catalog reads the same file.
