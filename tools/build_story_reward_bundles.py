"""Build data/story-reward-bundles.json: installed reward bundle keys -> reconstructed contents.

Inputs: exports/native-campaign-content.tsv (installed ChapterEvent.RewardsBundleKey per event) and
a research-notes file of UESP mission rewards (path given as the first argument). Bundle keys are named after
the missions (rewards_<story>_actN_epN_<mission>[_A|_B|_C]); a mission's list is split across its lettered
keys (first letter: the first card; later letters: the rest), which is an assumption. Generic puzzle keys
(rewards_<n>_soul_gems, rewards_puzzle_<set>_pack) map by name. Unmatched keys are reported.
Usage: python -I tools/build_story_reward_bundles.py   (from .analysis/offline-engine)
"""
import csv, json, os, re, sys

here = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
notes = sys.argv[1] if len(sys.argv) > 1 else "other-story-rewards.txt"   # research notes (UESP mission rewards)

def norm(s):
    return re.sub(r"[^a-z0-9]", "", s.lower())

missions = {}
for line in open(notes, encoding="utf-8"):
    line = line.strip()
    if not line or line.startswith("#"):
        continue
    story, mission, rewards = line.split("|")
    ep = None
    m = re.match(r"ep(\d) (.*)", mission)
    if m:
        ep, mission = m.group(1), m.group(2)
    entries = []
    for part in rewards.split(";"):
        part = part.strip()
        c = re.match(r"(\d+)x (.+)", part)
        if c:
            entries.append("card:%s*%s" % (c.group(2), c.group(1)))
        elif part.startswith("gems:"):
            entries.append(part)
    missions.setdefault((story, norm(mission)), []).append((ep, entries))

keys = []
with open(os.path.join(here, "exports", "native-campaign-content.tsv"), encoding="utf-8") as f:
    for row in csv.DictReader(f, delimiter="\t"):
        for key in (row.get("rewards") or "").split(","):
            key = key.strip()
            if key and key not in keys:
                keys.append(key)

bundles, unmatched = {}, []
# Master rewards are absent from the Normal-only campaign export.
bundles.update({
    "rewards_30_soul_gems": ["gems:30"],
    "rewards_40_soul_gems": ["gems:40"],
    "rewards_the_animal_lover_title": ["title:title_the_animal_lover"],
    "rewards_alt_swims_at_night": ["premiumcardid:-79175348*1"],
    "rewards_isle_of_madness_saint_card_back": ["cardback:cardback_isle_of_madness_saint"],
})
groups = {}
for key in keys:
    m = re.match(r"rewards_(\d+)_soul_gems$", key)
    if m:
        bundles[key] = ["gems:" + m.group(1)]; continue
    m = re.match(r"rewards_puzzle_([a-z_]+)_pack$", key)
    if m:
        bundles[key] = ["pack:" + m.group(1) + "*1"]; continue
    m = re.match(r"rewards_(dbh|cwc|iom)_act\d_ep(\d)_(.+?)(?:_([abc]))?$", key, re.I)
    if not m:
        unmatched.append(key); continue
    story, ep, slug, letter = m.group(1).lower(), m.group(2), norm(m.group(3)), (m.group(4) or "").upper()
    groups.setdefault((story, ep, slug), []).append((letter, key))

for (story, ep, slug), lettered in groups.items():
    found = missions.get((story, slug))
    if not found:
        for k in lettered: unmatched.append(k[1])
        continue
    entries = None
    for mission_ep, e in found:
        if mission_ep is None or mission_ep == ep:
            entries = e
    if entries is None:
        for k in lettered: unmatched.append(k[1])
        continue
    lettered.sort()
    if len(lettered) == 1:
        bundles[lettered[0][1]] = entries
    else:
        bundles[lettered[0][1]] = entries[:1]
        rest = entries[1:]
        tail = lettered[1:]
        for i, (_, key) in enumerate(tail):
            bundles[key] = rest if i == len(tail) - 1 else rest[:1]
            rest = rest if i == len(tail) - 1 else rest[1:]

os.makedirs(os.path.join(here, "data"), exist_ok=True)
with open(os.path.join(here, "data", "story-reward-bundles.tsv"), "w", encoding="utf-8", newline="\n") as out:
    out.write("key\tentries (| separated)\n")
    for k, v in sorted(bundles.items()):
        out.write(k + "\t" + "|".join(v) + "\n")
print("bundles", len(bundles), "unmatched", len(unmatched))
for k in unmatched: print("  unmatched", k)
