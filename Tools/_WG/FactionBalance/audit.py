import os,re,sys,collections
import yaml

class Loader(yaml.SafeLoader):
    pass

def tagged(loader,suffix,node):
    # SS14 tags (!type:Foo) are kept as a __tag__ key on mappings and dropped elsewhere.
    if isinstance(node,yaml.MappingNode):
        d=loader.construct_mapping(node,deep=True); d['__tag__']=suffix; return d
    if isinstance(node,yaml.SequenceNode):
        return loader.construct_sequence(node,deep=True)
    return loader.construct_scalar(node)
Loader.add_multi_constructor('!',tagged)

root=sys.argv[1]
protos=os.path.join(root,'Resources','Prototypes')
docs=[]
for d,_,fs in os.walk(protos):
    for f in fs:
        if f.endswith('.yml'):
            path=os.path.join(d,f)
            text=open(path,encoding='utf-8',errors='ignore').read()
            try:
                data=yaml.load(text,Loader=Loader)
            except yaml.YAMLError as e:
                rel=os.path.relpath(path,root)
                # A file that feeds the counts must parse; anything else only loses display names.
                if re.search(r'(?m)^- type: (listing|latheRecipe|latheRecipePack)\s*$',text):
                    sys.exit(f'audit.py: cannot parse {rel}: {e}')
                print(f'audit.py: skipped {rel} (no listings or recipes): {e.problem}',file=sys.stderr)
                continue
            if isinstance(data,list):
                docs+=[x for x in data if isinstance(x,dict)]
names={}
recipes={}
packs={}
listings=[]

def whitelist_tags(conditions):
    tags=[]
    for c in conditions or []:
        if isinstance(c,dict) and isinstance(c.get('whitelist'),dict):
            tags+=c['whitelist'].get('tags') or []
    return tags

for b in docs:
    kind=b.get('type'); i=b.get('id')
    if not i: continue
    if kind=='entity':
        if b.get('name'): names[i]=str(b['name']).strip()
    elif kind=='latheRecipe':
        if b.get('result'): recipes[i]=b['result']
    elif kind=='latheRecipePack':
        packs[i]=list(b.get('recipes') or [])
    elif kind=='listing':
        cost=b.get('cost') or {}
        cur=next((c for c in ('FederationMilitaryCredit','Doubloon') if c in cost),None)
        if not b.get('productEntity') or not cur: continue
        conds=b.get('conditions') or []
        cats=[c for c in b.get('categories') or [] if str(c).startswith(('Uplink','WGUplink'))]
        holder=any(isinstance(c,dict) and c.get('__tag__')=='type:WGKitHolderCondition' for c in conds)
        listings.append((i,b['productEntity'],whitelist_tags(conds),cats[0] if cats else '?',int(cost[cur]),cur,holder))
def side(tags,cur,cat='?'):
    if cur=='FederationMilitaryCredit' and any(t in ('SecurityUplink','FactionStaticUplinkTsf') for t in tags): return 'TSF'
    if cur=='Doubloon' and any(t in ('PirateUplink','FactionStaticUplinkPdv') for t in tags): return 'PDV'
    # A Doubloon listing with no whitelist is the pirate uplink's own.
    if cur=='Doubloon' and (not tags or str(cat).startswith('UplinkPirate')): return 'PDV'
out=['# Phase 0 audit: TSF and PDV gear and where it is sold','',
'Generated from the prototypes by `Tools/_WG/FactionBalance/audit.py`; rerun it after changing listings or lathe packs.',
'The specialist kits are in `Resources/Prototypes/_WG/SpecialistKits/kits.yml`.','']
# The tabs each faction's uplink shows, from its store preset.
presets=open(os.path.join(protos,'_NF','Store','presets.yml'),encoding='utf-8').read()
def tabs(preset):
    block=presets.split(f'id: {preset}',1)[1].split('currencyWhitelist',1)[0]
    return set(re.findall(r'(?m)^    - (\S+)',block))
shown={'TSF':tabs('StorePresetSecurityUplink'),'PDV':tabs('StorePresetPirateUplink')}
out+=["Counts are what a player sees in the uplink's tabs, against the 80-100 cap. Operations (their own tab, one per",
      'operation) and the kit replacement crates (shown only to a player who drew that kit) are listed apart.','']
for fac in ('TSF','PDV'):
    mine=[l for l in listings if side(l[2],l[5],l[3])==fac and l[3] in shown[fac]]
    rows=[l for l in mine if not l[3].endswith('Operations') and not l[6]]
    kits=[l for l in mine if l[6]]
    out+=[f'## {fac} uplink ({len(rows)} listings; {len(kits)} kit replacements)','','| Category | Item | Cost |','|---|---|---|']
    for l in sorted(rows,key=lambda l:(l[3],l[4])):
        out.append(f'| {l[3].replace("WGUplink","").replace("Uplink","")} | {names.get(l[1],l[1])} (`{l[1]}`) | {l[4]} |')
    out.append('')
for fac,keys in (('TSF',('tsf',)),('PDV',('pdv','pirate','rogue'))):
    ps=[p for p in packs if any(k in p.lower() for k in keys)]
    items=sorted({recipes.get(r,r) for p in ps for r in packs[p]})
    out+=[f'## {fac} lathe packs ({len(items)} recipes)','',', '.join(f'{names.get(x,x)}' for x in items),'']
out+=['## Gaps','',
'- The TSF has no flamethrower on its uplink or lathes, and no specialist kit carries one.',
'- Most TSF guns are lathe-built; the uplink now sells the main lathe guns. Both uplinks stop at the entry hardsuits (TSF M82c and M82b, PDV CV-32 and SCAF); the rest are lathe-only.',
'- The PDV buys finished gear and its composite on the uplink; the TSF now buys plastitanium there at the same rate.',
'- Both uplinks sell the same Supplies tab at the same prices.','']
open(sys.argv[2],'w',encoding='utf-8',newline='\n').write('\n'.join(out))
print({fac: sum(1 for l in listings if side(l[2],l[5],l[3])==fac and l[3] in shown[fac] and not l[3].endswith('Operations') and not l[6]) for fac in ('TSF','PDV')})
