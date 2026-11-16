import os,re,sys,collections
root=sys.argv[1]
protos=os.path.join(root,'Resources','Prototypes')
docs=[]
for d,_,fs in os.walk(protos):
    for f in fs:
        if f.endswith('.yml'):
            t=open(os.path.join(d,f),encoding='utf-8',errors='ignore').read()
            for b in re.split(r'(?m)^- type: ',t)[1:]:
                docs.append((os.path.relpath(os.path.join(d,f),root),b))
names={}
recipes={}
packs={}
listings=[]
for path,b in docs:
    kind=b.split('\n',1)[0].strip()
    m=re.search(r'(?m)^  id: (\S+)',b)
    if not m: continue
    i=m.group(1)
    if kind=='entity':
        n=re.search(r'(?m)^  name: (.+)$',b)
        if n: names[i]=n.group(1).strip().strip('"')
    elif kind=='latheRecipe':
        r=re.search(r'(?m)^  result: (\S+)',b)
        if r: recipes[i]=r.group(1)
    elif kind=='latheRecipePack':
        packs[i]=re.findall(r'(?m)^  - (\S+)',b)
    elif kind=='listing':
        if re.search(r'(?m)^\s*#',b) and False: pass
        e=re.search(r'(?m)^  productEntity: (\S+)',b)
        tags=re.findall(r'(?m)^      - (\S+)',b)
        cats=re.findall(r'(?m)^  - (Uplink\S+|WFUplink\S+)',b)
        holder='WFKitHolderCondition' in b
        cost=re.search(r'(?m)^    (FederationMilitaryCredit|Doubloon): (\d+)',b)
        if e and cost:
            listings.append((i,e.group(1),tags,cats[0] if cats else '?',int(cost.group(2)),cost.group(1),holder))
def side(tags,cur):
    if cur=='FederationMilitaryCredit' and any(t in ('SecurityUplink','FactionStaticUplinkTsf') for t in tags): return 'TSF'
    if cur=='Doubloon' and any(t in ('PirateUplink','FactionStaticUplinkPdv') for t in tags): return 'PDV'
out=['# Phase 0 audit: TSF and PDV gear and where it is sold','',
'Generated from the prototypes by `Tools/_WF/FactionBalance/audit.py`; rerun it after changing listings or lathe packs.',
'The specialist kits are in `Resources/Prototypes/_WF/SpecialistKits/kits.yml`.','']
# The tabs each faction's uplink shows, from its store preset.
presets=open(os.path.join(protos,'_NF','Store','presets.yml'),encoding='utf-8').read()
def tabs(preset):
    block=presets.split(f'id: {preset}',1)[1].split('currencyWhitelist',1)[0]
    return set(re.findall(r'(?m)^    - (\S+)',block))
shown={'TSF':tabs('StorePresetSecurityUplink'),'PDV':tabs('StorePresetPirateUplink')}
out+=["Counts are what a player sees in the uplink's tabs, against the 80-100 cap. Operations (their own tab, one per",
      'operation) and the kit replacement crates (shown only to a player who drew that kit) are listed apart.','']
for fac in ('TSF','PDV'):
    mine=[l for l in listings if side(l[2],l[5])==fac and l[3] in shown[fac]]
    rows=[l for l in mine if not l[3].endswith('Operations') and not l[6]]
    kits=[l for l in mine if l[6]]
    out+=[f'## {fac} uplink ({len(rows)} listings; {len(kits)} kit replacements)','','| Category | Item | Cost |','|---|---|---|']
    for l in sorted(rows,key=lambda l:(l[3],l[4])):
        out.append(f'| {l[3].replace("WFUplink","").replace("Uplink","")} | {names.get(l[1],l[1])} (`{l[1]}`) | {l[4]} |')
    out.append('')
for fac,keys in (('TSF',('tsf',)),('PDV',('pdv','pirate','rogue'))):
    ps=[p for p in packs if any(k in p.lower() for k in keys)]
    items=sorted({recipes.get(r,r) for p in ps for r in packs[p]})
    out+=[f'## {fac} lathe packs ({len(items)} recipes)','',', '.join(f'{names.get(x,x)}' for x in items),'']
out+=['## Gaps','',
'- The TSF has no flamethrower on its uplink or lathes; the Pyrotechnician kit uses incendiary shells and grenades instead.',
'- Most TSF guns are lathe-built; the uplink now sells the main lathe guns and all four TSF hardsuits (M82c, M82b, M86, M92-X).',
'- The PDV buys finished gear and its composite on the uplink; the TSF now buys plastitanium there at the same rate.',
'- Both uplinks sell the same Supplies tab at the same prices.','']
open(sys.argv[2],'w',encoding='utf-8',newline='\n').write('\n'.join(out))
print({fac: sum(1 for l in listings if side(l[2],l[5])==fac and l[3] in shown[fac] and not l[3].endswith('Operations') and not l[6]) for fac in ('TSF','PDV')})
