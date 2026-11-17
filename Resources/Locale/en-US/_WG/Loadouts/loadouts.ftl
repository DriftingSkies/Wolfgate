wg-loadout-search-placeholder = Search all gear...
wg-loadout-search-clear = Clear
wg-loadout-search-empty = Nothing matches "{$query}".
wg-loadout-search-clear-link = Clear search
wg-loadout-search-hint = Matches from every slot, closest names first.

wg-loadout-name-label = Name for this role
wg-loadout-name-placeholder = Same as character
wg-loadout-name-placeholder-dataset = Random if empty

wg-loadout-kit-heading = Your kit
wg-loadout-category-clothing = Clothing
wg-loadout-category-headface = Head & face
wg-loadout-category-bags = Bags & ID
wg-loadout-category-weapons = Weapons
wg-loadout-category-tools = Tools & tech
wg-loadout-category-extras = Extras
wg-loadout-category-rank = Rank
wg-loadout-group-label = { CAPITALIZE($name) }
wg-loadout-entry-variant = {$variant} {$name}

wg-loadout-nothing = Nothing
wg-loadout-free = Free
wg-loadout-row-none = none
wg-loadout-row-more = {$name} +{$count}
wg-loadout-row-hits = { $count ->
    [one] 1 match
   *[other] {$count} matches
}
wg-loadout-chip-pick = Pick
wg-loadout-chip-fixed = Fixed
wg-loadout-chip-count = {$count}/{$max}
wg-loadout-chip-locked = Locked
wg-loadout-chip-unaffordable = Can't afford
wg-loadout-chip-stale = Outdated
wg-loadout-chip-stale-tooltip = A saved pick here is no longer offered to this character and is removed on save.

wg-loadout-limit-required = Pick 1 · required
wg-loadout-limit-optional = Pick 1 · optional
wg-loadout-limit-upto = Up to {$max} · {$count}/{$max} picked
wg-loadout-limit-atleast = Pick {$min} to {$max} · {$count}/{$max} picked
wg-loadout-limit-fixed = Standard issue
wg-loadout-group-count = { $count ->
    [one] 1 item
   *[other] {$count} items
}
wg-loadout-group-count-locked = { $count ->
    [one] 1 item
   *[other] {$count} items
}, {$locked} locked
wg-loadout-search-heading = Search
wg-loadout-search-count = { $count ->
    [one] 1 match
   *[other] {$count} matches
}

wg-loadout-hint-required = Required: pick another item to replace it.
wg-loadout-hint-fixed = Standard issue: this slot has only one option.
wg-loadout-hint-required-multi = Required: this slot needs at least {$min}.
wg-loadout-hint-full = Full {$max}/{$max}: remove a pick first.
wg-loadout-hint-all-locked = Locked: {$reason}
wg-loadout-locked-generic = You do not meet the requirements for this item.

wg-loadout-detail-none = Nothing picked in this slot.
wg-loadout-detail-remove-glyph = ×
wg-loadout-detail-remove = Remove {$name}
wg-loadout-detail-locked = Locked
wg-loadout-detail-unaffordable = Can't afford
wg-loadout-selected-heading = Selected
wg-loadout-dropped = You can't afford this, so it will not be issued.
wg-loadout-dropped-fallback = You can't afford this, so {$item} is issued instead.

wg-loadout-budget = Kit {$cost} / {$balance}
wg-loadout-budget-free = Kit: free
wg-loadout-budget-tooltip = Taken from your balance and savings every time you spawn. Priced gear is only issued to a saved character.
wg-loadout-attention-ok = Kit ready
wg-loadout-attention-pick = { $count ->
    [one] 1 slot needs a pick
   *[other] {$count} slots need a pick
}
wg-loadout-attention-unaffordable = { $count ->
    [one] 1 item can't be afforded
   *[other] {$count} items can't be afforded
}
wg-loadout-points = Points {$count}/{$max}
wg-loadout-no-options = This role has no loadout options.
