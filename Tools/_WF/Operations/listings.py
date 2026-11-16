"""Writes Resources/Prototypes/_WF/Operations/listings.yml from operations.yml: one uplink listing per operation,
costing its stake. Run from the repository root after changing an operation's stake, name or faction."""
import re

OPERATIONS = 'Resources/Prototypes/_WF/Operations/operations.yml'
LISTINGS = 'Resources/Prototypes/_WF/Operations/listings.yml'
FACTIONS = {
    'TSF': ('WFUplinkSecurityOperations', 'FederationMilitaryCredit', 'FactionStaticUplinkTsf'),
    'PDV': ('WFUplinkPdvOperations', 'Doubloon', 'FactionStaticUplinkPdv'),
}


def operations():
    text = open(OPERATIONS, encoding='utf-8').read().replace('\r\n', '\n')
    ops = {}
    for block in re.split(r'(?m)^- type: wfOperation\n', text)[1:]:
        fields = dict(re.findall(r'(?m)^  (abstract|id|parent|name|company|stake): (\S+)$', block))
        ops[fields['id']] = fields
    def get(op, key):
        while op is not None:
            if key in op:
                return op[key]
            op = ops.get(op.get('parent'))
        return None
    return [(op['id'], get(op, 'name'), get(op, 'company'), int(get(op, 'stake') or 0))
            for op in ops.values() if op.get('abstract') != 'true']


def listing(op_id, name, company, stake):
    category, currency, tag = FACTIONS[company]
    return f'''- type: listing
  id: WFUplink{op_id.removeprefix("WF")}
  name: {name}
  description: {name.replace("-name-", "-desc-")}
  icon:
    sprite: Objects/Devices/communication.rsi
    state: beacon
  cost:
    {currency}: {stake}
  categories:
  - {category}
  productEvent: !type:WFCallOperationEvent
    operation: {op_id}
  raiseProductEventOnUser: true
  disableRefund: true
  conditions:
  - !type:StoreWhitelistCondition
    whitelist:
      tags:
      - {tag}
  - !type:WFOperationReadyCondition
    operation: {op_id}
'''


def main():
    out = ['# Operation call-ins on the faction static uplinks, one per operation at its stake. Written by',
           '# Tools/_WF/Operations/listings.py from operations.yml; edit the operation and rerun it.', '']
    for company, (category, _, _) in FACTIONS.items():
        out += [f'- type: storeCategory\n  id: {category}\n  name: wf-store-category-operations\n  priority: 10\n']
    ops = sorted(operations(), key=lambda op: (list(FACTIONS).index(op[2]), op[3], op[0]))
    out += [listing(*op) for op in ops]
    open(LISTINGS, 'w', encoding='utf-8', newline='').write('\n'.join(out).replace('\n', '\r\n'))
    print(f'{len(ops)} listings')


main()
