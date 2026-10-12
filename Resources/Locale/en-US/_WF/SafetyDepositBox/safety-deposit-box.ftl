# Safety Deposit Box Console UI
safety-deposit-console-title = Safety Deposit Box Console
safety-deposit-console-header = Safety Deposit Box Management
safety-deposit-console-purchase-section = Purchase New Box
safety-deposit-console-bank-payment-note = Cost will be deducted from your bank and savings accounts.
safety-deposit-console-purchase-small = Small (2x2) - ${$cost}
safety-deposit-console-purchase-medium = Medium (2x4) - ${$cost}
safety-deposit-console-purchase-large = Large (2x6) - ${$cost}
safety-deposit-console-deposit-section = Deposit Box
safety-deposit-console-box-in-slot = Box {$id} in slot
safety-deposit-console-no-box-in-slot = No box in slot
safety-deposit-console-deposit-button = Deposit Box
safety-deposit-console-withdraw-button = Withdraw
safety-deposit-console-reclaim-button = Reclaim
safety-deposit-console-owned-boxes = Your Safety Deposit Boxes:
safety-deposit-console-no-boxes = You don't own any safety deposit boxes yet.
safety-deposit-console-box-id = Box ID: {$id}... - {$status}
safety-deposit-console-box-id-short = Box ID: {$id}...
safety-deposit-console-box-unnamed = Box {$id}
safety-deposit-console-box-deposited = Stored
safety-deposit-console-box-not-deposited = Not Stored
safety-deposit-console-box-in-world = In World
safety-deposit-console-box-lost = Lost

# Safety Deposit Box Entity
safety-deposit-box-boxSlot = Box Slot
safety-deposit-console-boxSlot = Box Slot

# Console popups
safety-deposit-console-error-invalid-box-size = Error: Invalid box size.
safety-deposit-console-error-no-character-data = Error: Could not load character data.
safety-deposit-console-error-no-bank-account = Error: No bank account found.
safety-deposit-console-error-insufficient-funds = Insufficient funds. You need ${$cost}, but only have ${$bank} in bank and ${$savings} in savings.
safety-deposit-console-error-transaction-failed = Transaction failed.
safety-deposit-console-purchase-success = Safety deposit box purchased! Box ID: {$id}...
safety-deposit-console-error-no-box-in-slot = Please insert a safety deposit box.
safety-deposit-console-error-invalid-box = Invalid safety deposit box.
safety-deposit-console-error-not-owner = This box does not belong to you.
safety-deposit-console-error-no-storage = Error: Box has no storage.
safety-deposit-console-deposit-success = Safety deposit box contents saved. The box has been stored.
safety-deposit-console-error-box-not-found = Box not found.
safety-deposit-console-error-not-lost = This box is not lost and cannot be reclaimed.
safety-deposit-console-reclaim-success = Lost box reclaimed! A new empty box has been issued.
safety-deposit-console-error-already-withdrawn = Box already withdrawn in world.
safety-deposit-console-withdraw-success = Safety deposit box retrieved.
safety-deposit-console-withdraw-refused = Safety deposit box retrieved. {$count ->
    [one] One item can no longer be stored in it and was left at your feet.
   *[other] {$count} items can no longer be stored in it and were left at your feet.
}

# Examine text
safety-deposit-stored-examine = [color=gray]This item has a signature on it that indicates that it was at one point stowed in a deposit box.[/color]
safety-deposit-box-examine-id = [color=cyan]Box ID:[/color] {$id}...
safety-deposit-box-examine-owner = [color=yellow]Owner:[/color] {$owner}
