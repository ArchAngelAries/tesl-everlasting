# Troubleshooting — TESL: Everlasting

Always start with the logs: `.offline-user-data\logs` in the game folder (TESL: Everlasting's logs and reports;
`mod-report.json` in `.offline-user-data` lists what each mod loaded or skipped) and `BepInEx/LogOutput.log` (BepInEx's
own log; lines starting with `OFFLINE` come from TESL: Everlasting).

| Problem | What to try |
|---|---|
| The game starts but shows a login or "could not connect" screen | The game started in online mode. Open F10 > *Switch to offline mode*, or set `.offline-user-data/launch.json` to `{"mode": "offline"}`, or use `Play TESL Everlasting.cmd`. |
| Nothing changes, no F10 menu | BepInEx isn't loading. Check `winhttp.dll` and `doorstop_config.ini` are next to the exe, that you installed the **x86** BepInEx, and that `BepInEx/LogOutput.log` is updated when you start the game. |
| "Could not get Steam credentials" in online mode | That's TESL-Reborn's online sign-in, not our mod. Switch to offline mode, or ask TESL-Reborn's own support about online play. |
| The game closes as soon as an online match starts | The fair-play check found offline code in a remote match. Switch to online mode properly (F10) instead of forcing a remote connection from an offline launch. |
| A mod doesn't show up | Is it enabled in TESL Forge? Open TESL Forge once so it records where it is (`%APPDATA%\TESL Forge\location.txt`); otherwise the game only looks for a `TESL Forge` or `TESL-Modding-Suite` folder next to the game folder. Check `.offline-user-data\mod-report.json`: skipped items are listed with the reason (missing dependency, conflict, newer engine needed). |
| A mod deck shows a warning sign | The deck breaks the game's deck rules (50–70 cards, or 75–100 with three attributes; max 3 copies, 1 for unique cards). Fix it in TESL Forge. |
| A match stops responding during a turn | Rare rules-engine stall (seen once with a rune breaking during a Prophecy). Use the game's *Concede* or F10 > *Win the match*, and report it with the log. |
| Game window is under the taskbar / can't resize | F10 > Display > *maximized*, or drag the window edges. |
| Low frame rate | F10 > Quality preset *Medium* or *Low*, anti-aliasing 2×, frame cap 60. |
| My profile looks reset | Restore your backup of `.offline-user-data`. If the profile file was damaged, the game starts a new one. |
| I want to start over | Close the game, move `.offline-user-data` somewhere else, start the game. |

Still stuck? Open an issue on this project's tracker with the log, what you did and what you expected.
**Please don't contact the TESL-Reborn team about TESL: Everlasting.**
