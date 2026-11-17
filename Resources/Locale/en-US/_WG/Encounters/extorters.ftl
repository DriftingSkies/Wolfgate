wg-encounter-name-extorter-light = Reaver toll boat {$designation}
wg-encounter-name-extorter-medium = Reaver toll cutter {$designation}
wg-encounter-name-extorter-heavy = Reaver toll gunship {$designation}

wg-bounty-title-extorter-light = Toll boat
wg-bounty-title-extorter-medium = Toll cutter
wg-bounty-title-extorter-heavy = Toll gunship

wg-bounty-desc-extorter-light = {$ship} is a small Reaver boat with a green crew. It stops ships for a toll and boards those that don't pay. Every toll paid is added here. Paid when its crew is dead or the ship is gone.
wg-bounty-desc-extorter-medium = {$ship} is an armed Reaver cutter with a steady crew. It stops ships for a toll and boards those that don't pay. Every toll paid is added here. Paid when its crew is dead or the ship is gone.
wg-bounty-desc-extorter-heavy = {$ship} is a Reaver gunship with a veteran crew. It stops ships for a toll and boards those that don't pay. Every toll paid is added here. Paid when its crew is dead or the ship is gone.

# Said down the hail channel once the ship answers. $ship is the ship being stopped.
wg-encounter-extort-demand-1 = {$ship}, this is a Reaver toll. {$toll} { $toll ->
        [one] thaler
       *[other] thalers
    } and you fly on.
wg-encounter-extort-demand-2 = {$ship}. You are in our space. Pay {$toll} { $toll ->
        [one] thaler
       *[other] thalers
    } or we board you.
wg-encounter-extort-demand-3 = Heave to, {$ship}. The toll is {$toll} { $toll ->
        [one] thaler
       *[other] thalers
    }. Pay it or we come aboard.
wg-encounter-extort-option-pay = Pay {$toll} { $toll ->
        [one] thaler
       *[other] thalers
    }
wg-encounter-extort-option-refuse = Refuse

wg-encounter-extort-paid-1 = Paid. You are clear. Fly on.
wg-encounter-extort-paid-2 = Good. We are done with you.
wg-encounter-extort-paid-3 = Smart. Stay out of our way.

wg-encounter-extort-short-1 = You don't have {$toll} { $toll ->
        [one] thaler
       *[other] thalers
    }. Then we take it out of your hold.
wg-encounter-extort-short-2 = Your account is short. We are coming aboard.
wg-encounter-extort-short-3 = No money, no pass. Stand by to be boarded.

wg-encounter-extort-refused-1 = Wrong answer. Stand by to be boarded.
wg-encounter-extort-refused-2 = Then we take it ourselves. We are coming aboard.
wg-encounter-extort-refused-3 = Your choice. Boarding now.

wg-encounter-extort-timeout-1 = Out of time. We are coming aboard.
wg-encounter-extort-timeout-2 = Too slow. Stand by to be boarded.
wg-encounter-extort-timeout-3 = No answer is a no. Boarding now.

# Said on the common radio channel when a hail cannot ring aboard a ship it is alongside: no powered console, running
# dark, or its line is busy.
wg-encounter-extort-dead-1 = {$ship}, your line is dead. Answer our hail or we board you.
wg-encounter-extort-dead-2 = {$ship}, we can't raise you. Pick up or be boarded.
wg-encounter-extort-dead-3 = {$ship}, hiding from a hail won't save you. Answer it.

# Said on the common radio channel after the last hail goes unanswered, as there is no channel open.
wg-encounter-extort-silent-1 = {$ship}, three hails and no answer. We are coming aboard.
wg-encounter-extort-silent-2 = {$ship}, you had your chance to talk. Stand by to be boarded.
wg-encounter-extort-silent-3 = Nobody home on {$ship}? We will see for ourselves. Boarding.

# The admin's encounter window and wg_encounter list.
wg-encounter-extort-status-roam = roaming
wg-encounter-extort-status-close = closing on {$ship}
wg-encounter-extort-status-hail = hailing {$ship} (attempt {$attempt})
wg-encounter-extort-status-demand = waiting for an answer from {$ship}
wg-encounter-extort-status-board = boarding {$ship}
wg-encounter-extort-status-nobody = no ship
wg-encounter-admin-state-detail = {$state}, {$detail}
wg-encounter-admin-state-pool = {$state}, pool {$pool}
