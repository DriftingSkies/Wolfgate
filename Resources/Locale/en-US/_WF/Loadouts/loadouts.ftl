wf-loadout-search-placeholder = Search all gear...
wf-loadout-search-clear = Clear
wf-loadout-search-empty = Nothing matches "{$query}".
wf-loadout-search-clear-link = Clear search
wf-loadout-search-hint = Matches from every slot, closest names first.

wf-loadout-name-label = Name for this role
wf-loadout-name-placeholder = Same as character
wf-loadout-name-placeholder-dataset = Random if empty

wf-loadout-kit-heading = Your kit
wf-loadout-category-clothing = Clothing
wf-loadout-category-headface = Head & face
wf-loadout-category-bags = Bags & ID
wf-loadout-category-weapons = Weapons
wf-loadout-category-tools = Tools & tech
wf-loadout-category-extras = Extras
wf-loadout-category-rank = Rank
wf-loadout-group-label = { CAPITALIZE($name) }
wf-loadout-entry-variant = {$variant} {$name}

wf-loadout-nothing = Nothing
wf-loadout-free = Free
wf-loadout-row-none = none
wf-loadout-row-more = {$name} +{$count}
wf-loadout-row-hits = { $count ->
    [one] 1 match
   *[other] {$count} matches
}
wf-loadout-chip-pick = Pick
wf-loadout-chip-fixed = Fixed
wf-loadout-chip-count = {$count}/{$max}
wf-loadout-chip-locked = Locked
wf-loadout-chip-unaffordable = Can't afford
wf-loadout-chip-stale = Outdated
wf-loadout-chip-stale-tooltip = A saved pick here is no longer offered to this character and is removed on save.

wf-loadout-limit-required = Pick 1 · required
wf-loadout-limit-optional = Pick 1 · optional
wf-loadout-limit-upto = Up to {$max} · {$count}/{$max} picked
wf-loadout-limit-atleast = Pick {$min} to {$max} · {$count}/{$max} picked
wf-loadout-limit-fixed = Standard issue
wf-loadout-group-count = { $count ->
    [one] 1 item
   *[other] {$count} items
}
wf-loadout-group-count-locked = { $count ->
    [one] 1 item
   *[other] {$count} items
}, {$locked} locked
wf-loadout-search-heading = Search
wf-loadout-search-count = { $count ->
    [one] 1 match
   *[other] {$count} matches
}

wf-loadout-hint-required = Required: pick another item to replace it.
wf-loadout-hint-fixed = Standard issue: this slot has only one option.
wf-loadout-hint-required-multi = Required: this slot needs at least {$min}.
wf-loadout-hint-full = Full {$max}/{$max}: remove a pick first.
wf-loadout-hint-all-locked = Locked: {$reason}
wf-loadout-locked-generic = You do not meet the requirements for this item.

wf-loadout-detail-selected = Selected
wf-loadout-detail-none = Nothing picked in this slot.
wf-loadout-detail-remove-glyph = ×
wf-loadout-detail-remove = Remove {$name}
wf-loadout-detail-idle = Hover an item for details.
wf-loadout-detail-locked = Locked
wf-loadout-detail-unaffordable = Can't afford
wf-loadout-dropped = You can't afford this, so it will not be issued.
wf-loadout-dropped-fallback = You can't afford this, so {$item} is issued instead.

wf-loadout-budget = Kit {$cost} / {$balance}
wf-loadout-budget-free = Kit: free
wf-loadout-budget-tooltip = Taken from your balance and savings every time you spawn. Priced gear is only issued to a saved character.
wf-loadout-attention-ok = Kit ready
wf-loadout-attention-pick = { $count ->
    [one] 1 slot needs a pick
   *[other] {$count} slots need a pick
}
wf-loadout-attention-unaffordable = { $count ->
    [one] 1 item can't be afforded
   *[other] {$count} items can't be afforded
}
wf-loadout-points = Points {$count}/{$max}
wf-loadout-no-options = This role has no loadout options.
