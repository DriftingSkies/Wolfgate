"""Writes Resources/Prototypes/_WG/Operations/listings.yml from operations.yml: one uplink listing per operation,
costing its stake. Run from the repository root after changing an operation's stake, name or faction."""
import re

import yaml

OPERATIONS = 'Resources/Prototypes/_WG/Operations/operations.yml'
LISTINGS = 'Resources/Prototypes/_WG/Operations/listings.yml'
FACTIONS = {
    'TSF': ('WGUplinkSecurityOperations', 'FederationMilitaryCredit', 'FactionStaticUplinkTsf'),
    'PDV': ('WGUplinkPdvOperations', 'Doubloon', 'FactionStaticUplinkPdv'),
}


def operations():
    text = open(OPERATIONS, encoding='utf-8').read().replace('\r\n', '\n')
    ops = {}
    for block in re.split(r'(?m)^- type: wgOperation\n', text)[1:]:
        # Parsed as YAML, so a trailing comment never hides a field.
        data = yaml.safe_load('- type: wgOperation\n' + block)[0]
        fields = {k: data[k] for k in ('abstract', 'id', 'parent', 'name', 'company', 'stake') if k in data}
        ops[fields['id']] = fields
    def get(op, key):
        while op is not None:
            if key in op:
                return op[key]
            op = ops.get(op.get('parent'))
        return None
    def stake(op):
        value = get(op, 'stake')
        if value is None:
            raise SystemExit(f"{op['id']} has no stake, on it or a parent")
        return int(value)
    return [(op['id'], get(op, 'name'), get(op, 'company'), stake(op))
            for op in ops.values() if op.get('abstract') is not True]


def listing(op_id, name, company, stake):
    category, currency, tag = FACTIONS[company]
    return f'''- type: listing
  id: WGUplink{op_id.removeprefix("WG")}
  name: {name}
  description: {name.replace("-name-", "-desc-")}
  icon:
    sprite: Objects/Devices/communication.rsi
    state: beacon
  cost:
    {currency}: {stake}
  categories:
  - {category}
  productEvent: !type:WGCallOperationEvent
    operation: {op_id}
  raiseProductEventOnUser: true
  disableRefund: true
  conditions:
  - !type:StoreWhitelistCondition
    whitelist:
      tags:
      - {tag}
  - !type:WGOperationReadyCondition
    operation: {op_id}
'''


def main():
    out = ['# Operation call-ins on the faction static uplinks, one per operation at its stake. Written by',
           '# Tools/_WG/Operations/listings.py from operations.yml; edit the operation and rerun it.', '']
    for company, (category, _, _) in FACTIONS.items():
        out += [f'- type: storeCategory\n  id: {category}\n  name: wg-store-category-operations\n  priority: 10\n']
    ops = sorted(operations(), key=lambda op: (list(FACTIONS).index(op[2]), op[3], op[0]))
    out += [listing(*op) for op in ops]
    open(LISTINGS, 'w', encoding='utf-8', newline='').write('\n'.join(out).replace('\n', '\r\n'))
    print(f'{len(ops)} listings')


main()
