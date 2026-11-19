"""Reports which recipe ingredients chop on the prep board (our chop recipes), slice by the game's own knife
(SliceableFood, which the board also does), or neither. Usage: python Tools/_WG/Cooking/chop_inventory.py"""
import re
from collections import defaultdict
from pathlib import Path

ROOT = Path(__file__).resolve().parents[3] / 'Resources' / 'Prototypes'
docs = {}
for f in ROOT.rglob('*.yml'):
    try:
        text = f.read_text(encoding='utf-8')
    except Exception:
        continue
    for block in re.split(r'\n(?=- type: )', '\n' + text):
        m = re.match(r'\s*- type: (\w+)', block)
        i = re.search(r'\n  id: (\S+)', block)
        if m and i:
            docs.setdefault(m.group(1), {})[i.group(1)] = block

entities = docs.get('entity', {})
def parent_chain(eid, seen=None):
    seen = seen or set()
    b = entities.get(eid)
    if not b or eid in seen:
        return []
    seen.add(eid)
    out = [b]
    p = re.search(r'\n  parent: (.+)', b)
    if p:
        for par in re.findall(r'[A-Za-z0-9_]+', p.group(1)):
            out += parent_chain(par, seen)
    return out
def has_comp(eid, comp):
    return any(re.search(rf'- type: {comp}\b', b) for b in parent_chain(eid))
def name(eid):
    for b in parent_chain(eid):
        n = re.search(r'\n  name: (.+)', b)
        if n:
            return n.group(1).strip()
    return eid

# our chop recipes
chops = {}
for cid, b in docs.get('wgChopRecipe', {}).items():
    ins = re.findall(r'inputs: \[ ([^\]]+) \]', b)
    tags = re.findall(r'tags: \[ ([^\]]+) \]', b)
    res = re.search(r'result: (\S+)', b).group(1)
    for x in (ins[0].split(', ') if ins else []):
        chops[x.strip()] = res
    for t in (tags[0].split(', ') if tags else []):
        chops['tag:' + t.strip()] = res

def entity_tags(eid):
    tags = set()
    for b in parent_chain(eid):
        m = re.search(r'- type: Tag\s*\n\s*tags:\s*(\[[^\]]*\]|(?:\n\s*- \S+)+)', b)
        if m:
            tags |= set(re.findall(r'[A-Za-z0-9_]+', m.group(1)))
    return tags

def chop_of(eid):
    if eid in chops:
        return chops[eid]
    for t in entity_tags(eid):
        if 'tag:' + t in chops:
            return chops['tag:' + t]
    return None

# every ingredient used, and where
uses = defaultdict(set)
for rid, b in docs.get('microwaveMealRecipe', {}).items():
    sol = re.search(r'\n  solids:\n((?:    .+\n?)+)', b)
    if sol:
        for x in re.findall(r'    (\S+):', sol.group(1)):
            uses[x].add('kitchen recipe')
for kind in ('wgPrepRecipe', 'wgStewRecipe', 'wgCookRecipe', 'wgProcessRecipe'):
    for rid, b in docs.get(kind, {}).items():
        for lst in re.findall(r'(?:inputs|ids): \[ ([^\]]+) \]', b):
            for x in lst.split(', '):
                uses[x.strip()].add({'wgPrepRecipe': 'prep board', 'wgStewRecipe': 'stew', 'wgCookRecipe': 'pan/oven', 'wgProcessRecipe': 'machine'}[kind])

chopped, sliced, neither, products = [], [], [], set(chops.values())
for eid, where in sorted(uses.items()):
    if eid not in entities:
        continue
    if chop_of(eid):
        chopped.append((eid, where))
    elif has_comp(eid, 'SliceableFood'):
        sliced.append((eid, where))
    else:
        neither.append((eid, where))

food_raw = [e for e, w in neither if any(k in e for k in ('Food', 'WGMeat', 'WGFish', 'Bushel')) and e not in products]
print(f'ingredients used by recipes: {len(uses)} ({len(chopped)} chop on the board, {len(sliced)} slice with upstream knife, {len(neither)} neither)')
print('\n== CHOP ON THE PREP BOARD ==')
for e, w in chopped:
    print(f'  {name(e)} [{e}] -> {name(chop_of(e))}   used by: {", ".join(sorted(w))}')
print('\n== SLICED BY UPSTREAM (knife in hand, not the board) ==')
for e, w in sliced[:60]:
    print(f'  {name(e)} [{e}]   used by: {", ".join(sorted(w))}')
print(f'  ... {len(sliced)} total')
print('\n== RAW FOOD USED BY RECIPES THAT NOTHING CUTS ==')
for e in food_raw:
    print(f'  {name(e)} [{e}]   used by: {", ".join(sorted(uses[e]))}')
