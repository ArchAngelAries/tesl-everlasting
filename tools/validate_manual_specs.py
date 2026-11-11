"""Validate manual card-spec translations against tools/SPEC-FORMAT.md (offline, stdlib only).

Usage: python -I validate_manual_specs.py <worklist chunk.json> <translations.json>
translations.json: {"<id>": {"abilities": [...], "note": "..."}}; ids must come from the worklist chunk.
Prints one line per problem and exits non-zero when any card is invalid.
"""
import json, sys

TRIGGERS = {'Play', 'Summon', 'LastGasp', 'Slay', 'Pilfer', 'Veteran', 'Activate', 'Plot', 'Expertise', 'StartOfTurn', 'EndOfTurn',
            'StartOfEachTurn', 'EndOfEachTurn', 'SummonOther', 'AfterAction', 'Aura', 'Native',
            'CreatureSummoned', 'CreatureDies', 'CardDrawn', 'SelfDamaged', 'SelfAttacks', 'SelfWardBroken', 'SelfMoves', 'SelfEquipped',
            'RuneDestroyed', 'HealthGained', 'SelfConsumed', 'SelfConsumes', 'SelfDefends', 'AfterSelfAttacks', 'FriendlyCreatureEquipped', 'CreatureAttacks', 'SelfAttacksGuard', 'CreatureMoves', 'SelfDamagedByCreature', 'SelfDamagedSurvives', 'CreatureDamaged'}
EVENT_TRIGGERS = {'SummonOther', 'CreatureSummoned', 'CreatureDies', 'CardDrawn', 'RuneDestroyed', 'CreatureAttacks', 'CreatureMoves', 'CreatureDamaged'}
CARD_TYPES = {'Creature', 'Support', 'Item', 'Spell', 'Card', 'Avatar'}
CREATURE_ONLY = {'Summon', 'LastGasp', 'Slay', 'Pilfer', 'Veteran'}
KEYWORDS = {'Taunt', 'Ward', 'Deathtouch', 'Drain', 'Breakthrough', 'Charge', 'Regenerate', 'Cover'}
TYPES = set('''Creature Spell Item Support Shout Elixir Orc Nord Dark_Elf Wood_Elf High_Elf Khajiit Argonian Redguard Imperial Breton
Daedra Dwemer Animal Atronach Dragon Skeleton Spider Undead Vampire Werewolf Goblin Spriggan Beast Mummy Spirit Reachman Fish Kwama Blood_Magic
Falmer Troll Giant Netch Harpy Imp Wraith Factotum Fabricant Automaton Mammoth Reptile Insect Minotaur Gargoyle Centaur Recruit Wolf'''.split())
CARD_ACTIONS = {'destroy', 'shackle', 'silence', 'unsummon', 'battle', 'banish', 'move'}
CONDITIONS = {'maxMagicka', 'moreHealth', 'opponentDestroyedRune', 'otherCreatures', 'creatureEachLane', 'count', 'turn', 'health', 'equipped', 'selfKeyword', 'creaturesDiedThisTurn', 'friendlyDiedThisTurn', 'enemyDiedThisTurn', 'opponentMoreHand'}


def check_target(t, where, errs, card_only=False, player_only=False):
    if not isinstance(t, dict): errs.append(where + ': target missing'); return None
    kind = t.get('kind')
    if kind == 'player':
        if card_only: errs.append(where + ': card action with player target')
        if t.get('who') not in ('self', 'opponent', 'both', 'inciter'): errs.append(where + ': bad player who')
    elif kind in ('self', 'inciter', 'eventTarget'):
        if player_only: errs.append(where + ': player action with card target')
    elif kind == 'card':
        if player_only: errs.append(where + ': player action with card target')
        if t.get('pick') not in ('choose', 'random', 'all'): errs.append(where + ': bad pick')
        if t.get('side') not in ('enemy', 'friendly', 'any'): errs.append(where + ': bad side')
        if t.get('lane') not in (None, 'this', 'other'): errs.append(where + ': bad lane')
        if t.get('tribe') is not None and t['tribe'] not in TYPES: errs.append(where + ': unknown tribe ' + str(t['tribe']))
        p = t.get('power')
        if p is not None and (not isinstance(p, list) or len(p) != 2 or p[0] not in ('less', 'more') or not isinstance(p[1], int)): errs.append(where + ': bad power')
        if t.get('zone') not in (None, 'board', 'hand', 'deck', 'discard'): errs.append(where + ': bad zone')
        if t.get('cardType') not in (None,) and t.get('cardType') not in CARD_TYPES: errs.append(where + ': bad cardType')
        if t.get('keyword') is not None and t['keyword'] not in KEYWORDS: errs.append(where + ': bad target keyword')
        if 'cardId' in t and (not isinstance(t['cardId'], int) or t['cardId'] == 0): errs.append(where + ': invalid cardId')
        if 'costMin' in t and (not isinstance(t['costMin'], int) or t['costMin'] < 0): errs.append(where + ': invalid costMin')
        extra = set(t) - {'kind', 'pick', 'side', 'lane', 'other', 'avatar', 'wounded', 'tribe', 'power', 'zone', 'cardType', 'keyword', 'cardId', 'costMin'}
        if extra: errs.append(where + ': unknown target fields ' + str(sorted(extra)))
    else:
        errs.append(where + ': bad target kind')
    return kind


def check_pool(p, where, errs):
    if not isinstance(p, dict): errs.append(where + ': pool missing'); return
    for x in p.get('types') or []:
        if x not in TYPES: errs.append(where + ': unknown pool type ' + str(x))
    if not (p.get('types') or p.get('keyword') or p.get('costMin') is not None or p.get('costMax') is not None or p.get('cardId')): errs.append(where + ': empty pool')
    if 'cardId' in p and (not isinstance(p['cardId'], int) or p['cardId'] == 0): errs.append(where + ': invalid pool cardId')
    if p.get('keyword') is not None and p['keyword'] not in KEYWORDS: errs.append(where + ': bad pool keyword')


def check_effect(e, where, errs, definition, trigger):
    a = e.get('action')
    num = lambda k: isinstance(e.get(k), int)
    dynamic = 'valueFrom' in e
    if dynamic and (e['valueFrom'] not in ('consumedPower', 'consumedHealth', 'consumedStats', 'eventValue', 'turnDeaths', 'enemyRunesLost', 'selfPower') or a not in ('damage', 'heal', 'stats', 'draw') or 'amount' in e or 'per' in e):
        errs.append(where + ': invalid dynamic value expression')
    if e.get('valueFrom') == 'turnDeaths' and a not in ('damage', 'stats'):
        errs.append(where + ': turnDeaths supports damage or stats')
    if e.get('valueFrom') == 'enemyRunesLost' and a != 'stats': errs.append(where + ': enemyRunesLost supports stats')
    if e.get('valueFrom') == 'selfPower' and a not in ('damage', 'draw'): errs.append(where + ': selfPower supports damage or draw')
    if a == 'draw' and dynamic and e.get('valueFrom') != 'selfPower': errs.append(where + ': dynamic draw requires selfPower')
    if e.get('valueFrom') == 'eventValue' and trigger not in ('SelfDamaged', 'SelfDamagedByCreature', 'HealthGained'):
        errs.append(where + ': eventValue requires a damage/healing event')
    if e.get('valueFrom') == 'consumedStats' and (a != 'stats' or e.get('target') != {'kind':'self'} or e.get('attack') != 1 or e.get('health') != 1 or e.get('temporary') or e.get('condition')):
        errs.append(where + ': consumedStats requires permanent self +power/+health')
    if a == 'damage':
        if not dynamic and not num('amount'): errs.append(where + ': amount')
        check_target(e.get('target'), where, errs)
        if 'damageSource' in e and (e['damageSource'] != 'inciter' or trigger != 'CreatureAttacks' or e.get('target') != {'kind':'player','who':'opponent'}): errs.append(where + ': unsupported damage source')
        if 'per' in e: check_target(e['per'], where + '.per', errs, card_only=True)
    elif a == 'heal':
        if not dynamic and not num('amount'): errs.append(where + ': amount')
        k = check_target(e.get('target'), where, errs)
        if k == 'player' and e['target'].get('who') != 'self': errs.append(where + ': player heal must be self')
        if 'per' in e: check_target(e['per'], where + '.per', errs, card_only=True)
    elif a == 'healTo':
        if not num('amount') or e.get('amount', 0) < 0 or e.get('target') != {'kind':'player','who':'self'}: errs.append(where + ': healTo needs nonnegative amount and self player')
    elif a == 'breakRune':
        check_target(e.get('target'), where, errs, player_only=True)
    elif a == 'draw':
        if not dynamic and not num('amount'): errs.append(where + ': amount')
        check_target(e.get('target'), where, errs, player_only=True)
        if 'per' in e: check_target(e['per'], where + '.per', errs, card_only=True)
    elif a in ('magicka', 'maxMagicka'):
        if not num('amount'): errs.append(where + ': amount')
    elif a == 'stats':
        if not (num('attack') and num('health')): errs.append(where + ': attack/health')
        if dynamic and (e.get('attack', 0) < 0 or e.get('health', 0) < 0): errs.append(where + ': negative consumed stat multiplier')
        check_target(e.get('target'), where, errs, card_only=True)
        if 'per' in e:
            check_target(e['per'], where + '.per', errs, card_only=True)
            if e.get('attack', 0) < 0 or e.get('health', 0) < 0: errs.append(where + ': per with negative stats')
    elif a in ('keyword', 'loseKeyword'):
        if e.get('keyword') not in KEYWORDS: errs.append(where + ': keyword')
        check_target(e.get('target'), where, errs, card_only=True)
    elif a in CARD_ACTIONS or a in ('doubleStats', 'doublePower', 'powerFromHealth', 'stealEnemyKeywords', 'copyFriendlyKeywords'):
        check_target(e.get('target'), where, errs, card_only=True)
        if a == 'stealEnemyKeywords' and e.get('target') != {'kind':'self'}: errs.append(where + ': stealEnemyKeywords requires self')
        if a == 'copyFriendlyKeywords' and e.get('target') != {'kind':'self'}: errs.append(where + ': copyFriendlyKeywords requires self')
    elif a == 'transform':
        check_target(e.get('target'), where, errs, card_only=True)
        if not e.get('token'): errs.append(where + ': token')
    elif a == 'equipToken':
        check_target(e.get('target'), where, errs, card_only=True)
        if not e.get('token'): errs.append(where + ': token')
    elif a in ('handToken', 'summonToken'):
        if not e.get('token') or not num('count'): errs.append(where + ': token/count')
        if a == 'summonToken' and e.get('place') not in ('same', 'other', 'each'): errs.append(where + ': place')
        if e.get('fill') and definition != 'Creature': errs.append(where + ': fill needs a creature source')
    elif a in ('handRandom', 'summonRandom', 'tutor'):
        if not num('count'): errs.append(where + ': count')
        check_pool(e.get('pool'), where, errs)
        if (e.get('pool') or {}).get('cardId') and a != 'tutor': errs.append(where + ': pool cardId is supported only for tutor')
        if a == 'summonRandom' and (e.get('zone') not in ('collection', 'deck', 'discard', 'opponentDiscard') or e.get('place') not in ('same', 'each', 'random')): errs.append(where + ': zone/place')
        if a == 'summonRandom' and 'target' in e:
            check_target(e['target'], where, errs, player_only=True)
            if e.get('zone') != 'collection': errs.append(where + ': player target requires collection')
        if a == 'tutor' and e.get('zone') not in ('deck', 'discard'): errs.append(where + ': zone')
    elif a in ('randomKeyword', 'sacrifice', 'extraAttack'):
        check_target(e.get('target'), where, errs, card_only=True)
    elif a == 'cost':
        if not num('amount'): errs.append(where + ': amount')
        check_target(e.get('target'), where, errs, card_only=True)
    elif a == 'steal':
        check_target(e.get('target'), where, errs, card_only=True)
        if (e.get('target') or {}).get('pick') != 'choose': errs.append(where + ': steal needs a chosen target')
    elif a == 'drawCopy':
        check_target(e.get('target'), where, errs, card_only=True)
        if e.get('target') not in ({'kind':'card','pick':'random','side':'enemy','zone':'deck','cardType':'Card'}, {'kind':'card','pick':'random','side':'enemy','zone':'deck','cardType':'Creature'}, {'kind':'card','pick':'choose','side':'friendly','zone':'board','cardType':'Creature'}): errs.append(where + ': unsupported drawCopy target')
    elif a == 'shuffleToken':
        if not e.get('token') or not num('count'): errs.append(where + ': token/count')
    elif a == 'invade':
        if not num('count'): errs.append(where + ': count')
    else:
        errs.append(where + ': unknown action ' + str(a))
    c = e.get('condition')
    if c is not None and (not isinstance(c, dict) or c.get('type') not in CONDITIONS): errs.append(where + ': bad condition')
    if isinstance(c, dict) and c.get('type') == 'count':
        check_target(c.get('target'), where + '.condition', errs, card_only=True)
        if not isinstance(c.get('min', 1), int) or ('max' in c and not isinstance(c['max'], int)): errs.append(where + ': count min/max')
    if isinstance(c, dict) and c.get('type') == 'turn' and c.get('who') not in ('self', 'opponent'):
        errs.append(where + ': turn needs self/opponent')
    if isinstance(c, dict) and c.get('type') == 'selfKeyword' and c.get('keyword') not in KEYWORDS:
        errs.append(where + ': selfKeyword needs a known keyword')
    if isinstance(c, dict) and c.get('type') in ('health', 'maxMagicka', 'otherCreatures', 'creaturesDiedThisTurn', 'friendlyDiedThisTurn', 'enemyDiedThisTurn') and not isinstance(c.get('min'), int):
        errs.append(where + ': condition needs integer min')
    t = e.get('target') or {}
    if trigger == 'Aura':
        if a not in ('stats', 'keyword'): errs.append(where + ': aura needs stats/keyword')
        if t.get('kind') != 'self' and not (t.get('kind') == 'card' and t.get('pick') == 'all'):
            errs.append(where + ': aura needs self/all card target')
        if e.get('temporary') or ('per' in e and a != 'stats'): errs.append(where + ': temporary/non-stat per aura not implemented')
    if t.get('pick') == 'choose' and trigger in ('Aura', 'Native'): errs.append(where + ': no chosen target in Aura/Native')


def validate(card, entry):
    errs = []
    abilities = entry.get('abilities')
    if not isinstance(abilities, list) or not abilities: return ['no abilities']
    definition = card['definition']
    for i, ab in enumerate(abilities):
        trig = ab.get('trigger')
        if not isinstance(trig, list) or not trig or any(t not in TRIGGERS for t in trig): errs.append('ability %d: bad trigger %s' % (i, trig)); continue
        if not ab.get('source'): errs.append('ability %d: source sentence missing' % i)
        if 'activationCondition' in ab:
            c = ab['activationCondition']
            if trig != ['Activate'] or not isinstance(c, dict) or c.get('type') != 'enemyDiedThisTurn' or not isinstance(c.get('min'), int) or c['min'] < 1:
                errs.append('ability %d: invalid activation condition' % i)
        if 'postPlacement' in ab and (ab['postPlacement'] is not True or trig != ['Summon'] or definition != 'Creature'
                                      or not any((e.get('target') or {}).get('pick') == 'choose' for e in ab.get('effects', []))):
            errs.append('ability %d: postPlacement needs a creature Summon with a chosen target' % i)
        values = {e['valueFrom'] for e in ab.get('effects', []) if e.get('valueFrom', '').startswith('consumed')}
        if values and (ab.get('consume') is not True or len(values) != 1): errs.append('ability %d: consumed values require Consume and a single value kind' % i)
        if 'consumedStats' in values and len(ab.get('effects', [])) != 1: errs.append('ability %d: consumedStats requires one effect' % i)
        if 'consume' in ab:
            if ab['consume'] is not True or trig not in (['Summon'], ['Pilfer']) or definition not in ('Creature', 'Item'):
                errs.append('ability %d: consume needs creature/item Summon' % i)
            choices = [e['target'] for e in ab.get('effects', []) if (e.get('target') or {}).get('pick') == 'choose']
            if choices and any(t != choices[0] for t in choices):
                errs.append('ability %d: multiple Consume reward targets' % i)
        for t in trig:
            if t in CREATURE_ONLY and definition not in ('Creature', 'Item') and not (t == 'Summon' and definition == 'Support'): errs.append('ability %d: %s on %s' % (i, t, definition))
            if t == 'Activate' and definition != 'Support': errs.append('ability %d: Activate on %s' % (i, definition))
            if t == 'Play' and definition != 'Action': errs.append('ability %d: Play on %s' % (i, definition))
        if trig == ['Native'] and ab.get('effects'): errs.append('ability %d: Native must have no effects' % i)
        if 'event' in ab:
            if trig[0] not in EVENT_TRIGGERS: errs.append('ability %d: event filter only on %s' % (i, sorted(EVENT_TRIGGERS)))
            ev = dict(ab['event'], kind='card', pick='all') if isinstance(ab['event'], dict) else None
            if ev is None: errs.append('ability %d: event must be an object' % i)
            else:
                ev.setdefault('side', 'any'); check_target(ev, 'ability %d event' % i, errs, card_only=True)
                if ev.get('lane') == 'other': errs.append('ability %d: event other-lane filter not implemented' % i)
        for j, e in enumerate(ab.get('effects') or []):
            check_effect(e, 'ability %d effect %d' % (i, j), errs, definition, trig[0])
    if definition == 'Action' and not any('Play' in ab.get('trigger', []) for ab in abilities): errs.append('action without Play ability')
    return errs


def main():
    chunk = {str(c['id']): c for c in json.load(open(sys.argv[1], encoding='utf-8'))}
    out = json.load(open(sys.argv[2], encoding='utf-8'))
    bad = 0
    for cid, entry in out.items():
        if cid not in chunk: print(cid, 'not in worklist'); bad += 1; continue
        errs = validate(chunk[cid], entry)
        if errs: bad += 1; print(cid, chunk[cid]['name'], '|', '; '.join(errs))
    print('%d entries, %d invalid' % (len(out), bad))
    sys.exit(1 if bad else 0)


if __name__ == '__main__':
    main()
