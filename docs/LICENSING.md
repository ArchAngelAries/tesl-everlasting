# Licensing in plain language

TESL: Everlasting and TESL Forge use the same license: the **PolyForm Noncommercial License 1.0.0** plus a short list of
additional permissions from the author (Part A of `LICENSE`). This page explains what that means. It is a summary, not
the license, and **not legal advice**.

## What you may do

- **Use it for free**, for any personal, hobby, study, research or other noncommercial purpose.
- **Share it for free**: copy and pass on the software, with the license and the "Required Notice" line included.
- **Change it**: fix bugs, add features, make your own version, as long as that version also stays free and noncommercial.
- **Own and sell your own creations.** Cards, decks, art, remasters, sounds, music, voice lines, boards, avatars, emotes,
  card backs, modes, stories and mod packages that *you* make with TESL Forge or for TESL: Everlasting are yours. You may
  share them, license them however you like, and **sell them or monetize them** (for example on Patreon or a store), as
  long as they don't contain parts of our software.

## What you may not do

- **Sell or charge for our software** or any modified version of it: no selling copies, no paywalls, no paid
  "premium" or early-access builds, no subscription or required donation to download it, and no bundling it into
  something that is sold.
- Remove the license or the Required Notice line.
- Use our software for commercial purposes beyond making your own creations (for example, running it as part of a paid
  service).

## What we cannot give you

- **Rights in Bethesda's game.** Original card art, names, characters, music and audio from *The Elder Scrolls: Legends*
  belong to their owners. If a creation of yours includes their material, our license can't make it yours, and
  selling it may break their rules. Creations made entirely from your own work are the safest to sell.
- **Rights in other people's mods.** A mod made by someone else is governed by that person's terms.

## Why this license (and not GPL)

| Option | Stops others from selling our tools? | Lets modders sell their creations? | Notes |
|---|---|---|---|
| GPL-3.0 / AGPL-3.0 | **No.** The GPL explicitly allows selling copies; it only requires sharing source. | Yes (outputs aren't covered) | Strong "share-alike", but anyone may charge money for it. |
| MIT / Apache-2.0 | No | Yes | Lets anyone do almost anything, including closed paid forks. |
| CC BY-NC-SA 4.0 | Yes | Unclear for tools | Made for media, not software; Creative Commons advises against using it for code. |
| **PolyForm Noncommercial 1.0.0 + Part A** | **Yes** | **Yes** (Part A1 says so explicitly) | Written by lawyers for software; standard text; plain wording. |
| A fully custom license | Yes | Yes | Untested wording, harder for others to trust; only with a lawyer. |

GPL and AGPL are "free software" licenses: they guarantee everyone the freedom to run, share and sell. Requiring the
source to be shared does not stop anyone from charging for it, so they can't meet the goal "nobody may sell our tools".
PolyForm Noncommercial does, and Part A fixes the one problem it would otherwise cause: under plain PolyForm
Noncommercial, using the tool to make something you sell could count as commercial use. Part A1 allows it.

Things to know about PolyForm Noncommercial:
- It is "source-available", not "open source" by the OSI definition (open source licenses can't forbid commercial use).
  Some sites and Linux distributions only list OSI licenses.
- It lets charities, schools, public research and government bodies use the software regardless of how they are funded.
  Part A4 states the author's view that selling or paywalling the software is never a permitted purpose.
- It has a 32-day "cure" period: someone who breaks the license and fixes it within 32 days of notice keeps their license.
- Only the copyright holder can enforce it. Accepting outside contributions? Ask contributors to agree that their work is
  under the same license (a short line in a contributing guide is the usual way).

## Third-party parts

Libraries we use (BepInEx, HarmonyX, UnityPy, Pillow and others) keep their own licenses. See `NOTICE.md`.
The original card rule data included with TESL: Everlasting is Bethesda's data, shipped as separate files with their own
notice (`data/original-rules/`); our license doesn't cover it.

## A lawyer's review (optional, recommended before a big release)

This setup follows common practice, but no one has reviewed it for this project. If you want certainty, especially about
selling creations that use Bethesda material, ask a lawyer who knows software and games licensing.
