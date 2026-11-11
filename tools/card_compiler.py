"""TESL installed-card text compiler (offline tooling; no third-party dependencies).

Parses the exact installed English card text (original-card-metadata-audit.tsv; icons kept as [Attribute]) into a
structured effect spec consumed by OriginalSpecRules.cs. Installed text is authoritative. The audited fan reference is
NOT used to decide behaviour. Every parsed ability records the exact source clause so each compiled card stays
reviewable. Unparsed clauses are reported, never guessed.

Usage: python -I card_compiler.py <offline-engine dir> <supported rule-import-results.tsv> <out.json> <report.tsv>
"""
import csv, json, os, re, sys, collections

NUM = {'a': 1, 'an': 1, 'one': 1, 'two': 2, 'three': 3, 'four': 4, 'five': 5, 'six': 6, 'seven': 7, 'eight': 8, 'nine': 9, 'ten': 10}
KEYWORDS = {'guard': 'Taunt', 'ward': 'Ward', 'lethal': 'Deathtouch', 'drain': 'Drain', 'breakthrough': 'Breakthrough',
            'charge': 'Charge', 'regenerate': 'Regenerate', 'cover': 'Cover'}
TRIGGERS = [
    (r'^summon and last gasp:\s*', ['Summon', 'LastGasp']), (r'^summon and slay:\s*', ['Summon', 'Slay']),
    (r'^summon:\s*', ['Summon']), (r'^last gasp:\s*', ['LastGasp']), (r'^slay:\s*', ['Slay']), (r'^pilfer:\s*', ['Pilfer']),
    (r'^veteran:\s*', ['Veteran']), (r'^activate:\s*', ['Activate']), (r'^plot:\s*', ['Plot']), (r'^expertise:\s*', ['Expertise']),
    (r'^at the start of your turn,\s*', ['StartOfTurn']), (r'^at the end of your turn,\s*', ['EndOfTurn']),
    (r'^at the start of each turn,\s*', ['StartOfEachTurn']), (r'^at the end of each turn,\s*', ['EndOfEachTurn']),
    (r'^when you summon another creature,\s*', ['SummonOther']), (r'^after you play an action,\s*', ['AfterAction']),
    (r'^when you gain health,\s*', ['GainHealth']), (r'^when you draw a card,\s*', ['DrawCard']),
]


def num(token):
    token = token.lower()
    return NUM[token] if token in NUM else int(token)


def drop_display_blocks(t):
    """Remove [if:IsEntity, ...] (in-game live-value display) and [if:N>0, +a/+b] item-stat display blocks; other [if:...]
    blocks (level/state-dependent rules text) are kept so they stay unparsed rather than silently ignored."""
    out, i = [], 0
    while i < len(t):
        if t.startswith('[if:', i):
            depth, j = 0, i
            while j < len(t):
                if t[j] == '[': depth += 1
                elif t[j] == ']':
                    depth -= 1
                    if depth == 0: break
                j += 1
            block = t[i:j + 1]
            if block.startswith('[if:IsEntity'):
                # [if:IsEntity, <live/level text>, <static text>]: keep the static branch; level-dependent text (Shouts)
                # is kept as an explicit marker sentence so the card stays partial until Shout levels are emitted.
                parts, depth2, cur = [], 0, ''
                for ch in block[len('[if:IsEntity'):-1]:
                    if ch == ',' and depth2 == 0: parts.append(cur); cur = ''; continue
                    if ch in '[(': depth2 += 1
                    elif ch in '])': depth2 -= 1
                    cur += ch
                parts.append(cur)
                live = parts[1].strip() if len(parts) > 1 else ''
                static = ','.join(parts[2:]).strip() if len(parts) > 2 else ''
                if static:
                    out.append(static)
                    if live.startswith('[if:'): out.append(' Shout levels: ' + re.sub(r'\s+', ' ', live) + '.')
                i = j + 1; continue
            if re.match(r'^\[if:\d+>0, \+\[var:\d+,-?\d+\]/\+\[var:\d+,-?\d+\]\]$', block):
                i = j + 1; continue
        out.append(t[i]); i += 1
    return ''.join(out)


def attributes_of(row):
    attrs = {}
    for part in (row.get('attributes') or '').split(','):
        if ':' in part:
            k, v = part.split(':', 1); attrs[k.strip()] = v.strip()
    return attrs


NATIVE_KEYWORDS = {'guard': 'Taunt', 'charge': 'Charge', 'ward': 'Ward', 'lethal': 'Deathtouch', 'drain': 'Drain',
                   'breakthrough': 'Breakthrough', 'regenerate': 'Regenerate', 'prophecy': 'Prophecy', 'mobilize': 'Mobilize',
                   'cover': 'Cover'}
IMMUNITIES = {'immune to silence': 'ImmuneToSilence', 'immune to lethal': 'ImmuneToLethal', 'immune to shackle': 'ImmuneToFreeze',
              'permanently shackled': 'PermFreeze', 'immune to wounded enemy creatures': 'ImmuneToWounded'}


def native_clause(sentence, row, attrs):
    """True when the sentence is fully represented by the card's installed native attributes (no effect graph needed)."""
    low = sentence.lower().strip().rstrip('.').strip()
    if row['definition'] == 'Avatar' and low == 'when your avatar dies, you lose the match': return True
    if low in IMMUNITIES: return attrs.get(IMMUNITIES[low]) == 'True'
    if re.match(r"^your opponent can't target .+ with actions$", low): return attrs.get('ImmuneToEnemyActionTargeting') == 'True'
    m = re.match(r'^\[?\+(-?\d+)/\+(-?\d+)\]?$', low)
    if m and row['definition'] == 'Item':
        return attrs.get('Attack') == m.group(1) and attrs.get('MaxHealth') == m.group(2)
    words = [w.strip() for w in re.split(r',| and ', low) if w.strip()]
    if words and all(w in NATIVE_KEYWORDS and attrs.get(NATIVE_KEYWORDS[w]) == 'True' for w in words): return True
    m = re.match(r'^rally (\d+)$', low)
    if m: return attrs.get('Rally') == m.group(1)
    return False


def normalize(text):
    t = (text or '').replace('\\n', ' ').replace('&#44;', ',')
    t = re.sub(r'<br\s*/?>', ' ', t)
    t = re.sub(r'</?(b|color|nobr|i)[^>]*>', '', t)
    t = re.sub(r'<img=TextIcons/icon(\w+)/>', r'[\1]', t)
    t = t.replace(' ', ' ').replace('�', "'").replace('’', "'")
    t = drop_display_blocks(t)
    t = re.sub(r'^\[\+\[var:\d+,(-?\d+)\]/\+\[var:\d+,(-?\d+)\]\]', r'[+\1/+\2]', t)
    return re.sub(r'\s+', ' ', t).strip()


def split_sentences(text):
    parts = re.split(r'(?<=[.!])\s*(?=[A-Z+\[])', text)
    return [p.strip() for p in parts if p.strip()]


# ---------------------------------------------------------------- targets
TARGET_PATTERNS = [
    (r'^your opponent$', {'kind': 'player', 'who': 'opponent'}),
    (r'^each player$|^both players$', {'kind': 'player', 'who': 'both'}),
    (r'^a random enemy$', {'kind': 'card', 'pick': 'random', 'side': 'enemy', 'avatar': True}),
    (r'^a random enemy creature$', {'kind': 'card', 'pick': 'random', 'side': 'enemy'}),
    (r'^a random enemy creature in this lane$', {'kind': 'card', 'pick': 'random', 'side': 'enemy', 'lane': 'this'}),
    (r'^a random friendly creature$', {'kind': 'card', 'pick': 'random', 'side': 'friendly'}),
    (r'^another random friendly creature$', {'kind': 'card', 'pick': 'random', 'side': 'friendly', 'other': True}),
    (r'^a random creature$', {'kind': 'card', 'pick': 'random', 'side': 'any'}),
    (r'^all enemy creatures in this lane$', {'kind': 'card', 'pick': 'all', 'side': 'enemy', 'lane': 'this'}),
    (r'^all enemy creatures$', {'kind': 'card', 'pick': 'all', 'side': 'enemy'}),
    (r'^all other creatures in this lane$', {'kind': 'card', 'pick': 'all', 'side': 'any', 'lane': 'this', 'other': True}),
    (r'^all creatures in this lane$', {'kind': 'card', 'pick': 'all', 'side': 'any', 'lane': 'this'}),
    (r'^all other creatures$', {'kind': 'card', 'pick': 'all', 'side': 'any', 'other': True}),
    (r'^all creatures$', {'kind': 'card', 'pick': 'all', 'side': 'any'}),
    (r'^(?:all|each) other friendly creatures?$|^other friendly creatures$', {'kind': 'card', 'pick': 'all', 'side': 'friendly', 'other': True}),
    (r'^(?:all|each) friendly creatures?$|^friendly creatures$', {'kind': 'card', 'pick': 'all', 'side': 'friendly'}),
    (r'^(?:all|each) other friendly creatures? in this lane$|^other friendly creatures in this lane$', {'kind': 'card', 'pick': 'all', 'side': 'friendly', 'lane': 'this', 'other': True}),
    (r'^(?:all|each) friendly creatures? in this lane$|^friendly creatures in this lane$', {'kind': 'card', 'pick': 'all', 'side': 'friendly', 'lane': 'this'}),
    (r'^an enemy creature in this lane$', {'kind': 'card', 'pick': 'choose', 'side': 'enemy', 'lane': 'this'}),
    (r'^an enemy creature$', {'kind': 'card', 'pick': 'choose', 'side': 'enemy'}),
    (r'^another friendly creature$', {'kind': 'card', 'pick': 'choose', 'side': 'friendly', 'other': True}),
    (r'^a friendly creature$', {'kind': 'card', 'pick': 'choose', 'side': 'friendly'}),
    (r'^another creature$', {'kind': 'card', 'pick': 'choose', 'side': 'any', 'other': True}),
    (r'^a creature in this lane$', {'kind': 'card', 'pick': 'choose', 'side': 'any', 'lane': 'this'}),
    (r'^a creature$', {'kind': 'card', 'pick': 'choose', 'side': 'any'}),
    (r'^an enemy$|^a creature or player$', {'kind': 'card', 'pick': 'choose', 'side': 'any', 'avatar': True}),
    (r'^a wounded (enemy )?creature$', {'kind': 'card', 'pick': 'choose', 'side': 'any', 'wounded': True}),
    (r'^itself$', {'kind': 'self'}),
]


TARGET_PATTERNS += [
    (r'^each enemy creature$', {'kind': 'card', 'pick': 'all', 'side': 'enemy'}),
    (r'^each enemy creature in this lane$', {'kind': 'card', 'pick': 'all', 'side': 'enemy', 'lane': 'this'}),
    (r'^each creature$', {'kind': 'card', 'pick': 'all', 'side': 'any'}),
    (r'^each creature in this lane$', {'kind': 'card', 'pick': 'all', 'side': 'any', 'lane': 'this'}),
    (r'^each other creature$', {'kind': 'card', 'pick': 'all', 'side': 'any', 'other': True}),
    (r'^(?:all|each) enem(?:y|ies)$', {'kind': 'card', 'pick': 'all', 'side': 'enemy', 'avatar': True}),
    (r'^everything$', {'kind': 'card', 'pick': 'all', 'side': 'any', 'avatar': True}),
    (r'^the enemy player$|^the opponent$', {'kind': 'player', 'who': 'opponent'}),
    (r'^you$|^yourself$', {'kind': 'player', 'who': 'self'}),
    (r'^a friendly creature in this lane$', {'kind': 'card', 'pick': 'choose', 'side': 'friendly', 'lane': 'this'}),
    (r'^another friendly creature in this lane$', {'kind': 'card', 'pick': 'choose', 'side': 'friendly', 'lane': 'this', 'other': True}),
    (r'^a random enemy creature in this lane$', {'kind': 'card', 'pick': 'random', 'side': 'enemy', 'lane': 'this'}),
    (r'^a random friendly creature in this lane$', {'kind': 'card', 'pick': 'random', 'side': 'friendly', 'lane': 'this'}),
    (r'^another random friendly creature in this lane$', {'kind': 'card', 'pick': 'random', 'side': 'friendly', 'lane': 'this', 'other': True}),
    (r'^a random other creature$|^another random creature$', {'kind': 'card', 'pick': 'random', 'side': 'any', 'other': True}),
]

# Creature types (native CardSubtype names), singular and plural as printed on installed cards.
TRIBES = {}
for singular, plural, native in [
        ('orc', 'orcs', 'Orc'), ('skeleton', 'skeletons', 'Skeleton'), ('goblin', 'goblins', 'Goblin'),
        ('werewolf', 'werewolves', 'Werewolf'), ('animal', 'animals', 'Animal'), ('dwemer', 'dwemer', 'Dwemer'),
        ('daedra', 'daedra', 'Daedra'), ('nord', 'nords', 'Nord'), ('wood elf', 'wood elves', 'Wood_Elf'),
        ('dark elf', 'dark elves', 'Dark_Elf'), ('high elf', 'high elves', 'High_Elf'), ('khajiit', 'khajiit', 'Khajiit'),
        ('argonian', 'argonians', 'Argonian'), ('redguard', 'redguards', 'Redguard'), ('imperial', 'imperials', 'Imperial'),
        ('breton', 'bretons', 'Breton'), ('dragon', 'dragons', 'Dragon'), ('spider', 'spiders', 'Spider'),
        ('vampire', 'vampires', 'Vampire'), ('atronach', 'atronachs', 'Atronach'), ('wolf', 'wolves', 'Wolf'),
        ('recruit', 'recruits', 'Recruit'), ('spriggan', 'spriggans', 'Spriggan'), ('undead', 'undead', 'Undead'),
        ('beast', 'beasts', 'Beast'), ('mummy', 'mummies', 'Mummy'), ('spirit', 'spirits', 'Spirit'),
        ('reachman', 'reachmen', 'Reachman'), ('fish', 'fish', 'Fish'), ('kwama', 'kwama', 'Kwama'),
        ('falmer', 'falmer', 'Falmer'), ('troll', 'trolls', 'Troll'), ('giant', 'giants', 'Giant'),
        ('netch', 'netch', 'Netch'), ('harpy', 'harpies', 'Harpy'), ('imp', 'imps', 'Imp'), ('wraith', 'wraiths', 'Wraith'),
        ('factotum', 'factotums', 'Factotum'), ('fabricant', 'fabricants', 'Fabricant'), ('automaton', 'automatons', 'Automaton'),
        ('mammoth', 'mammoths', 'Mammoth'), ('reptile', 'reptiles', 'Reptile'), ('insect', 'insects', 'Insect'),
        ('minotaur', 'minotaurs', 'Minotaur'), ('gargoyle', 'gargoyles', 'Gargoyle'), ('centaur', 'centaurs', 'Centaur')]:
    TRIBES[singular] = native; TRIBES[plural] = native

CURRENT_IT = [None]


def parse_target(text):
    t = text.strip().lower().rstrip('.')
    if t == 'it':
        return dict(CURRENT_IT[0]) if CURRENT_IT[0] else None
    # Tribe groups: "friendly Orcs", "all other friendly Goblins", "other friendly Skeletons in this lane".
    m = re.match(r'^(?:all |each )?(other )?friendly (.+?)( in this lane)?$', t)
    if m and m.group(2) in TRIBES and m.group(2) not in ('creature', 'creatures'):
        return {'kind': 'card', 'pick': 'all', 'side': 'friendly', 'other': bool(m.group(1)), 'tribe': TRIBES[m.group(2)],
                'lane': 'this' if m.group(3) else None}
    m = re.match(r'^(?:all |each )?(enemy )?(.+?)( in this lane)?$', t)
    if m and m.group(2) in TRIBES and (t.startswith('all ') or t.startswith('each ')):
        return {'kind': 'card', 'pick': 'all', 'side': 'enemy' if m.group(1) else 'any', 'tribe': TRIBES[m.group(2)],
                'lane': 'this' if m.group(3) else None}
    m = re.match(r'^(another |an? )(random )?(enemy |friendly )?(.+)$', t)
    if m and m.group(4) in TRIBES:
        return {'kind': 'card', 'pick': 'random' if m.group(2) else 'choose', 'side': (m.group(3) or 'any ').strip(),
                'tribe': TRIBES[m.group(4)], 'other': m.group(1) == 'another '}
    m = re.match(r'^(an? (?:enemy |friendly )?creature) with (\d+) power or (less|more)$', t)
    if m:
        base = parse_target(m.group(1))
        if base:
            base = dict(base); base['power'] = (m.group(3), int(m.group(2))); return base
    m = re.match(r'^a wounded enemy creature$', t)
    if m:
        return {'kind': 'card', 'pick': 'choose', 'side': 'enemy', 'wounded': True}
    for pattern, spec in TARGET_PATTERNS:
        if re.match(pattern, t):
            return dict(spec)
    return None


# ---------------------------------------------------------------- effects
def parse_effect(s, ctx):
    """Return a list of effect dicts for one imperative clause, or None."""
    t = s.strip().rstrip('.').strip()
    low = t.lower()
    CURRENT_IT[0] = ctx.get('it')
    # Optional condition prefix.
    m = re.match(r'^if (.+?), (.+)$', t, re.I)
    if m:
        cond = parse_condition(m.group(1))
        rest = parse_effect(m.group(2), ctx)
        if cond and rest:
            for e in rest: e['condition'] = cond
            return rest
        return None
    # Compound "X and Y" / "X, then Y" where both halves parse.
    for sep in [', then ', ' then ', '. ', ' and ']:
        if sep in t:
            a, b = t.split(sep, 1)
            ea = parse_effect(a, ctx)
            if not ea: continue
            # "it" in the second clause refers to the first clause's target, not the source card.
            prev = next((e['target'] for e in ea if e.get('target') and e['target'].get('kind') == 'card'), None)
            eb = parse_effect(b, dict(ctx, it=prev))
            if eb: return ea + eb
    # "... this turn": the same effect as a temporary enchantment that expires at end of turn.
    if low.endswith(' this turn') and not low.endswith('magicka this turn'):
        effs = parse_effect(t[:-len(' this turn')], ctx)
        if effs and all(e['action'] in ('stats', 'keyword') for e in effs):
            for e in effs: e['temporary'] = True
            return effs
        return None
    m = re.match(r'^deal (\d+) damage to (.+)$', low)
    if m:
        tgt = parse_target(m.group(2))
        if tgt: return [{'action': 'damage', 'amount': int(m.group(1)), 'target': tgt}]
    # Implicit target: an unqualified "Deal N damage." may target any creature or player.
    m = re.match(r'^deal (\d+) damage$', low)
    if m:
        return [{'action': 'damage', 'amount': int(m.group(1)), 'target': {'kind': 'card', 'pick': 'choose', 'side': 'any', 'avatar': True}}]
    # Random pools. "Draw" always takes from your own deck unless a discard pile is named (TESL rules wording).
    m = re.match(r'^draw (a|an|two|three) random (.+?)(?: from your (deck|discard pile))?$', low)
    if m:
        pool = parse_pool(m.group(2))
        if pool is not None:
            return [{'action': 'tutor', 'count': num(m.group(1)), 'zone': 'discard' if m.group(3) == 'discard pile' else 'deck', 'pool': pool}]
    m = re.match(r'^summon (a|an|two|three) random (.+?)(?: from your (deck|discard pile))?( in each lane)?$', low)
    if m:
        pool = parse_pool(m.group(2), creature=True)
        if pool is not None:
            return [{'action': 'summonRandom', 'count': num(m.group(1)), 'pool': pool,
                     'zone': {'deck': 'deck', 'discard pile': 'discard'}.get(m.group(3), 'collection'), 'place': 'each' if m.group(4) else 'same'}]
    m = re.match(r'^put (a|an|two|three) random (.+?) (?:into|in) your hand$', low)
    if m:
        pool = parse_pool(m.group(2))
        if pool is not None: return [{'action': 'handRandom', 'count': num(m.group(1)), 'pool': pool}]
    if low in ('invade', 'invade twice'):
        return [{'action': 'invade', 'count': 2 if low.endswith('twice') else 1}]
    if low == 'move':
        return [{'action': 'move', 'target': {'kind': 'self'}}]
    m = re.match(r'^move (.+)$', low)
    if m:
        tgt = parse_target(m.group(1))
        if tgt and tgt.get('kind') == 'card': return [{'action': 'move', 'target': tgt}]
    m = re.match(r'^fill this lane with (?:(\d+)/(\d+) )?(.+?)$', low)
    if m:
        name = token_name(t, m.group(3))
        return [{'action': 'summonToken', 'count': 1, 'fill': True, 'token': singular_token(name),
                 'attack': int(m.group(1)) if m.group(1) else None, 'health': int(m.group(2)) if m.group(2) else None, 'place': 'same'}]
    m = re.match(r'^transform (.+?) into an? (?:(\d+)/(\d+) )?(.+?)$', low)
    if m:
        tgt = parse_target(m.group(1))
        if tgt and tgt.get('kind') in ('card', 'self'):
            return [{'action': 'transform', 'target': tgt, 'token': token_name(t, m.group(4)),
                     'attack': int(m.group(2)) if m.group(2) else None, 'health': int(m.group(3)) if m.group(3) else None}]
    # Scaling: "+1/+1 for each other friendly creature", "Give X +2/+0 for each Y", "Deal 1 damage to X for each Y".
    m = re.match(r'^(?:give (.+?) )?([+-]\d+)/([+-]\d+) for each (.+)$', low)
    if m:
        tgt = parse_target(m.group(1)) if m.group(1) else ({'kind': 'self'} if ctx.get('creature') else None)
        per = parse_target('each ' + m.group(4))
        if tgt and per and per.get('kind') == 'card':
            return [{'action': 'stats', 'attack': int(m.group(2)), 'health': int(m.group(3)), 'target': tgt, 'per': per}]
    m = re.match(r'^deal (\d+) damage to (.+?) for each (.+)$', low)
    if m:
        tgt, per = parse_target(m.group(2)), parse_target('each ' + m.group(3))
        if tgt and per and per.get('kind') == 'card':
            return [{'action': 'damage', 'amount': int(m.group(1)), 'target': tgt, 'per': per}]
    m = re.match(r'^deal (\d+) damage to your opponent and gain (\d+) health$', low)
    if m:
        return [{'action': 'damage', 'amount': int(m.group(1)), 'target': {'kind': 'player', 'who': 'opponent'}},
                {'action': 'heal', 'amount': int(m.group(2)), 'target': {'kind': 'player', 'who': 'self'}}]
    m = re.match(r'^(?:you )?gain (\d+) health$', low)
    if m: return [{'action': 'heal', 'amount': int(m.group(1)), 'target': {'kind': 'player', 'who': 'self'}}]
    m = re.match(r'^(?:you )?gain \+?(\d+) max magicka$', low)
    if m: return [{'action': 'maxMagicka', 'amount': int(m.group(1)), 'target': {'kind': 'player', 'who': 'self'}}]
    m = re.match(r'^(?:you )?gain (\d+) magicka this turn$', low)
    if m: return [{'action': 'magicka', 'amount': int(m.group(1)), 'target': {'kind': 'player', 'who': 'self'}}]
    m = re.match(r'^(?:you )?draw (a|an|one|two|three|four|\d+) cards?$', low)
    if m: return [{'action': 'draw', 'amount': num(m.group(1)), 'target': {'kind': 'player', 'who': 'self'}}]
    m = re.match(r'^each player draws (a|two|three|\d+) cards?$', low)
    if m: return [{'action': 'draw', 'amount': num(m.group(1)), 'target': {'kind': 'player', 'who': 'both'}}]
    m = re.match(r'^your opponent draws (a|two|\d+) cards?$', low)
    if m: return [{'action': 'draw', 'amount': num(m.group(1)), 'target': {'kind': 'player', 'who': 'opponent'}}]
    m = re.match(r'^give (.+?) ([+-]\d+)/([+-]\d+)(?: and (\w+))?$', low)
    if m:
        tgt = parse_target(m.group(1))
        kw = m.group(4)
        if tgt and (kw is None or kw in KEYWORDS):
            effs = [{'action': 'stats', 'attack': int(m.group(2)), 'health': int(m.group(3)), 'target': tgt}]
            if kw: effs.append({'action': 'keyword', 'keyword': KEYWORDS[kw], 'target': tgt})
            return effs
    m = re.match(r'^([+-]\d+)/([+-]\d+)(?: and (\w+))?$', low)
    if m and ctx.get('creature'):
        kw = m.group(3)
        if kw is None or kw in KEYWORDS:
            effs = [{'action': 'stats', 'attack': int(m.group(1)), 'health': int(m.group(2)), 'target': {'kind': 'self'}}]
            if kw: effs.append({'action': 'keyword', 'keyword': KEYWORDS[kw], 'target': {'kind': 'self'}})
            return effs
    m = re.match(r'^give (.+?) (guard|ward|lethal|drain|breakthrough|charge|regenerate|cover)$', low)
    if m:
        tgt = parse_target(m.group(1))
        if tgt: return [{'action': 'keyword', 'keyword': KEYWORDS[m.group(2)], 'target': tgt}]
    m = re.match(r'^give (.+?) an? (guard|ward|lethal|drain|breakthrough|charge|regenerate|cover)$', low)
    if m:
        tgt = parse_target(m.group(1))
        if tgt: return [{'action': 'keyword', 'keyword': KEYWORDS[m.group(2)], 'target': tgt}]
    for verb, action in [('destroy', 'destroy'), ('shackle', 'shackle'), ('silence', 'silence'), ('unsummon', 'unsummon'),
                         ('battle', 'battle'), ('banish', 'banish')]:
        m = re.match(r'^' + verb + r' (.+)$', low)
        if m:
            tgt = parse_target(m.group(1))
            if tgt: return [{'action': action, 'target': tgt}]
    m = re.match(r'^put (a|an|two|three) (.+?) into your hand$', low)
    if m and not m.group(2).startswith('random '):
        return [{'action': 'handToken', 'count': num(m.group(1)), 'token': token_name(t, m.group(2))}]
    m = re.match(r'^summon (a|an|two) (?:(\d+)/(\d+) )?(.+?)(?: with (\w+))?( in each lane| in the other lane| in this lane)?$', low)
    if m and not m.group(4).startswith('random') and 'from your' not in m.group(4) and 'copy' not in m.group(4):
        where = (m.group(6) or '').strip()
        place = {'in each lane': 'each', 'in the other lane': 'other', 'in this lane': 'same', '': 'same'}[where]
        return [{'action': 'summonToken', 'count': num(m.group(1)), 'token': token_name(t, m.group(4)),
                 'attack': int(m.group(2)) if m.group(2) else None, 'health': int(m.group(3)) if m.group(3) else None,
                 'place': place}]
    return None


CARD_KINDS = {'creature': 'Creature', 'action': 'Spell', 'item': 'Item', 'support': 'Support', 'shout': 'Shout', 'elixir': 'Elixir',
              'card': None}


def parse_pool(text, creature=False):
    """Random-pool filter: {'types': [native CardSubtype], 'costMin', 'costMax', 'keyword': native attribute}. None if unknown."""
    t = text.strip().lower()
    pool = {'types': [], 'costMin': None, 'costMax': None, 'keyword': None}
    m = re.match(r'^(\d+)-(\d+) cost (.+)$', t)
    if m: pool['costMin'], pool['costMax'], t = int(m.group(1)), int(m.group(2)), m.group(3)
    m = re.match(r'^(\d+)-cost (.+)$', t)
    if m: pool['costMin'] = pool['costMax'] = int(m.group(1)); t = m.group(2)
    m = re.match(r'^(.+?) (?:with cost|that costs) (\d+) or less$', t)
    if m: pool['costMax'], t = int(m.group(2)), m.group(1)
    m = re.match(r'^(.+?) (?:with cost|that costs) (\d+) or more$', t)
    if m: pool['costMin'], t = int(m.group(2)), m.group(1)
    for word in (t, t[:-1] if t.endswith('s') else None, t[:-2] if t.endswith('es') else None):
        if not word: continue
        if word in CARD_KINDS:
            if CARD_KINDS[word]: pool['types'].append(CARD_KINDS[word])
            break
        if word in TRIBES: pool['types'].append(TRIBES[word]); break
        if word in KEYWORDS: pool['keyword'] = KEYWORDS[word]; pool['types'].append('Creature'); break
    else:
        return None
    if creature and not pool['types']: pool['types'].append('Creature')
    return pool


def singular_token(name):
    """'Skeletons' -> 'Skeleton' for lane-fill text; the emitter still requires a unique installed card by that name."""
    return name[:-1] if name.endswith('s') and not name.endswith('ss') else name


def token_name(original, lowered):
    """Recover the original capitalisation of a token name from the source sentence."""
    i = original.lower().find(lowered)
    name = original[i:i + len(lowered)] if i >= 0 else lowered
    return re.sub(r'^\d+/\d+\s+', '', name).strip()


def parse_condition(text):
    low = text.lower().strip()
    m = re.match(r'^you have (\d+) or more max magicka$', low)
    if m: return {'type': 'maxMagicka', 'min': int(m.group(1))}
    m = re.match(r'^you have more health than your opponent$', low)
    if m: return {'type': 'moreHealth'}
    m = re.match(r'^your opponent has a destroyed rune$', low)
    if m: return {'type': 'opponentDestroyedRune'}
    m = re.match(r'^you have (?:two|2) other (\[\w+\] )?creatures$', low)
    if m: return {'type': 'otherCreatures', 'min': 2, 'attribute': (m.group(1) or '').strip(' []') or None}
    m = re.match(r'^you have another creature in each lane$', low)
    if m: return {'type': 'creatureEachLane'}
    return None


# ---------------------------------------------------------------- card
def compile_card(row):
    text = normalize(row['description'])
    ctx = {'creature': row['definition'] == 'Creature', 'definition': row['definition']}
    abilities, unparsed = [], []
    attrs = attributes_of(row)
    # Item stat display prefix "[+a/+b]" precedes the first sentence without punctuation.
    body = text
    m = re.match(r'^\[\+(-?\d+)/\+(-?\d+)\]\s*', text)
    if m and row['definition'] == 'Item' and attrs.get('Attack') == m.group(1) and attrs.get('MaxHealth') == m.group(2):
        abilities.append({'trigger': ['Native'], 'source': m.group(0).strip(), 'effects': []}); body = text[m.end():]
    for sentence in split_sentences(body):
        s = sentence.strip()
        low = s.lower()
        if native_clause(s, row, attrs):
            abilities.append({'trigger': ['Native'], 'source': sentence, 'effects': []}); continue
        trig = None
        for pattern, names in TRIGGERS:
            m = re.match(pattern, low)
            if m:
                trig = names; s = s[m.end():]; break
        aura = re.match(r'^(other )?friendly creatures( in this lane)? have ([+-]\d+)/([+-]\d+)\.?$', s, re.I)
        if trig is None and aura:
            abilities.append({'trigger': ['Aura'], 'source': sentence, 'effects': [{'action': 'stats', 'attack': int(aura.group(3)),
                'health': int(aura.group(4)), 'target': {'kind': 'card', 'pick': 'all', 'side': 'friendly',
                'lane': 'this' if aura.group(2) else None, 'other': bool(aura.group(1))}}]})
            continue
        if trig is None:
            if row['definition'] == 'Action':
                trig = ['Play']
            else:
                unparsed.append(sentence); continue
        effects = parse_effect(s, ctx)
        if effects is None:
            unparsed.append(sentence); continue
        abilities.append({'trigger': trig, 'source': sentence, 'effects': effects})
    return {'text': text, 'abilities': abilities, 'unparsed': unparsed}


def main():
    root, supported_path, out_path, report_path = sys.argv[1:5]
    # Superseded bridge rows describe a rule that was *not* installed. In particular,
    # a later spec row for the same ID must remain eligible on the next compilation.
    # Staged rules are intentionally eligible for replacement by complete specs.
    imports = list(csv.DictReader(open(supported_path, encoding='utf-8'), delimiter='\t'))
    supported = set(r['card_id'] for r in imports if r['result'] not in ('staged', 'superseded', 'reconstructed-spec-compiled'))
    staged = set(r['card_id'] for r in imports if r['result'] == 'staged')
    rows = list(csv.DictReader(open(root + '/exports/original-card-metadata-audit.tsv', encoding='utf-8'), delimiter='\t'))
    # Reviewed per-card translations (tools/manual-specs.json): {"<id>": {"name", "raw", "abilities": [...], "note"}}. Used only
    # when the stored raw text equals the installed description exactly, so a text change re-opens the card for review.
    manual_path = root + '/tools/manual-specs.json'
    manual = json.load(open(manual_path, encoding='utf-8')) if os.path.exists(manual_path) else {}
    out, stats = [], collections.Counter()
    for row in rows:
        if row['id'] in supported: continue
        c = compile_card(row)
        status = 'compiled' if c['abilities'] and not c['unparsed'] else 'partial' if c['abilities'] else 'unparsed'
        if not c['text']: status = 'notext'
        entry = manual.get(row['id'])
        # A reviewed exact-text translation also overrides a permissive automatic parse
        # that reported compiled while swallowing a later clause into a token name.
        if entry is not None:
            if entry.get('raw') == row['description']:
                c = dict(c, abilities=entry['abilities'], unparsed=[], manual=entry.get('note', ''))
                status = 'compiled'
            else:
                stats['manual-stale'] += 1
        # A native-only empty graph would displace the existing staged graph.
        if status == 'notext' and row['id'] in staged: continue
        stats[status] += 1
        out.append({'id': int(row['id']), 'name': row['name'], 'definition': row['definition'], 'completion': row['completion'], 'raw': row['description'],
                    'source': row['source'], 'status': status, **c})
    json.dump(out, open(out_path, 'w', encoding='utf-8'), indent=1, ensure_ascii=False)
    with open(report_path, 'w', encoding='utf-8') as f:
        f.write('id\tname\tdefinition\tstatus\tunparsed\n')
        for c in out: f.write('%d\t%s\t%s\t%s\t%s\n' % (c['id'], c['name'], c['definition'], c['status'], ' || '.join(c['unparsed'])))
    print(dict(stats))
    actions = collections.Counter(e['action'] for c in out if c['status'] == 'compiled' for a in c['abilities'] for e in a['effects'])
    triggers = collections.Counter(tuple(a['trigger']) for c in out if c['status'] == 'compiled' for a in c['abilities'])
    print('actions', actions.most_common()); print('triggers', triggers.most_common())


if __name__ == '__main__':
    main()
