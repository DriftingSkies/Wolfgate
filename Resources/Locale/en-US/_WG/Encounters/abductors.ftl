wg-encounter-name-abductor-light = Reaver snatch ship
wg-encounter-name-abductor-medium = Reaver slaver

wg-bounty-title-abductor-light = Reaver snatch ship
wg-bounty-desc-abductor-light = The crew of {$ship}, wanted for abduction. Paid when they are dead or the ship is gone.
wg-bounty-title-abductor-medium = Reaver slaver
wg-bounty-desc-abductor-medium = The crew of {$ship}, wanted for abduction. Paid when they are dead or the ship is gone.

# Read out to the whole sector when someone is taken.
wg-encounter-abduct-sender = Sector Traffic Control
wg-encounter-abduct-taken = {$name} has been abducted by {$ship}. The Reavers want {$ransom} {$ransom ->
        [one] thaler
       *[other] thalers
    }. A warrant is out on the ship.

# Told to the prisoner.
wg-encounter-abduct-prisoner-told = You are held for ransom. Right-click the chair to pay {$ransom} {$ransom ->
        [one] thaler
       *[other] thalers
    }.
wg-encounter-abduct-prisoner-short = You can't cover the {$ransom} {$ransom ->
        [one] thaler
       *[other] thalers
    }.
wg-encounter-abduct-released-paid = Paid. The Reavers put you back aboard.
wg-encounter-abduct-released-free = The Reavers are done waiting and send you back.
wg-encounter-abduct-verb-pay = Pay ransom ({$ransom} {$ransom ->
        [one] thaler
       *[other] thalers
    })
wg-encounter-abduct-handover-told = Your ransom is paid. The doors are open. Someone is coming to dock and collect you.

# On the mass scanner, for a ship holding someone.
wg-encounter-abduct-iff-label = HOSTAGE ABOARD - {$name}

# Said down the hail to whoever answers aboard the prisoner's ship.
wg-encounter-abduct-demand = We have {$name}. {$ransom} {$ransom ->
        [one] thaler
       *[other] thalers
    } and they come home in one piece.
wg-encounter-abduct-handover = Paid. Dock with us and collect {$name}. Nobody aboard will touch you. You have {$minutes} minutes.
wg-encounter-abduct-short = You don't have {$ransom} {$ransom ->
        [one] thaler
       *[other] thalers
    }. Call back when you do.
wg-encounter-abduct-refused = Your choice. They stay here.
wg-encounter-abduct-option-pay = Pay {$ransom} {$ransom ->
        [one] thaler
       *[other] thalers
    }
wg-encounter-abduct-option-refuse = Refuse

# The admin's encounter window and wg_encounter list.
wg-encounter-abduct-status-close = closing on {$ship}
wg-encounter-abduct-status-dock = docking with {$ship}
wg-encounter-abduct-status-grab = taking someone off {$ship}
wg-encounter-abduct-status-ransom = holding {$name} for ransom
wg-encounter-abduct-status-handover = paid, waiting for the prisoner to be collected
wg-encounter-admin-order-abduct-unreachable = That ship is berthed at a station or hidden.
wg-encounter-admin-order-abduct-nobody = There is nobody aboard that ship to take.
