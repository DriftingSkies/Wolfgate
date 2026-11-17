## Games NPCs play for a stake (Content.Server/_WG/Minigames).

wg-minigame-name-orepiles = Ore Piles
wg-minigame-name-liarsdice = Liar's Dice
wg-minigame-name-lockbox = Lockbox
wg-minigame-name-higherlower = Higher or Lower
wg-minigame-name-memory = Memory
wg-minigame-name-riddles = Riddles

wg-minigame-difficulty-easy = easy table
wg-minigame-difficulty-normal = normal table
wg-minigame-difficulty-hard = high table

## Offer

wg-minigame-offer = {$game}, {$difficulty}. Stake {$stake}. A win pays {$prize}. Play?
wg-minigame-offer-ladder = {$game}, {$difficulty}. Stake {$stake}. A full run pays {$prize}; cash out sooner for less. Play?
wg-minigame-declined = Suit yourself.
wg-minigame-cooldown = You've had your win off me. Come back in {$minutes ->
    [one] a minute
   *[other] {$minutes} minutes
}.
wg-minigame-house-broke = I've paid out all I can for today. Try me tomorrow.
wg-minigame-begin = Let's play.
wg-minigame-reward-item = {$count ->
    [one] {$item}
   *[other] {$count}x {$item}
}

## Window

wg-minigame-window-title = Game
wg-minigame-window-header = {$game}, {$difficulty}
wg-minigame-window-stakes = Stake {$stake}. Prize {$prize}.
wg-minigame-window-won = You win.
wg-minigame-window-lost = You lose.
wg-minigame-window-thinking = They're thinking...
wg-minigame-line-start = Your move.

## Ore Piles

wg-minigame-ore-rules = Take 1 to 3 ore in turn. Whoever takes the last one loses.
wg-minigame-ore-take = Take {$count}
wg-minigame-ore-pile = {$pile} ore left
wg-minigame-ore-start = {$pile} ore on the table. You go first.
wg-minigame-ore-player-took = You took {$count}. {$pile} left.
wg-minigame-ore-npc-took = They took {$count}. {$pile} left.
wg-minigame-ore-player-last = You took the last ore.
wg-minigame-ore-npc-last = They had to take the last ore.

## Liar's Dice

wg-minigame-dice-rules = Bid how many dice on the table show a face, or call the last bid a lie.
wg-minigame-dice-bid = Bid
wg-minigame-dice-call = Liar!
wg-minigame-dice-pick = {$count} x {$face}
wg-minigame-dice-round = Round {$round} of {$rounds}
wg-minigame-dice-yours = Your dice: {$dice}
wg-minigame-dice-theirs = Their dice: {$count} under the cup
wg-minigame-dice-theirs-shown = Their dice last round: {$dice}
wg-minigame-dice-no-bid = No bid yet. You open.
wg-minigame-dice-your-bid = Your bid: {$count} x {$face}
wg-minigame-dice-their-bid = Their bid: {$count} x {$face}
wg-minigame-dice-start = Five dice each, no wilds. Win {$rounds} rounds in a row.
wg-minigame-dice-player-bid = You bid {$count} x {$face}.
wg-minigame-dice-npc-bid = They bid {$count} x {$face}.
wg-minigame-dice-player-called-wrong = You called. There were {$count} x {$face}. The bid stood.
wg-minigame-dice-npc-called-right = They called. There were only {$count} x {$face}.
wg-minigame-dice-player-called-right = You called. There were only {$count} x {$face}.
wg-minigame-dice-npc-called-wrong = They called. There were {$count} x {$face}. Your bid stood.
wg-minigame-dice-round-won = Round to you: there were {$count} x {$face}. {$won} of {$rounds}. New dice.

## Lockbox

wg-minigame-lock-rules = Guess the code. Hits: right digit, right place. Near: right digit, wrong place.
wg-minigame-lock-placeholder = Code
wg-minigame-lock-try = Try
wg-minigame-lock-tries = {$left ->
    [one] 1 try left
   *[other] {$left} tries left
}
wg-minigame-lock-code = The code was {$code}.
wg-minigame-lock-guess = {$code}: {$hits} hit, {$near} near
wg-minigame-lock-start = A {$digits}-digit code. You get {$tries} tries.
wg-minigame-lock-feedback = {$hits} hit, {$near} near. {$left} left.
wg-minigame-lock-open = It clicks open on try {$tries}.
wg-minigame-lock-jammed = It jams. The code was {$code}.

## Higher or Lower

wg-minigame-cards-rules = Call the next card higher or lower. A tie loses. Cash out after any right call.
wg-minigame-cards-higher = Higher
wg-minigame-cards-lower = Lower
wg-minigame-cards-cash-out = Cash out
wg-minigame-cards-card = {$rank} of {$suit}
wg-minigame-cards-run = Run {$streak} of {$max}. Next right call pays {$next}x.
wg-minigame-cards-run-top = Run {$streak}. Top of the ladder.
wg-minigame-cards-start = Climb the ladder to {$top}x. Fresh deck every card.
wg-minigame-cards-right = Right. Run of {$streak}, worth {$multiple}x.
wg-minigame-cards-wrong = Wrong call.
wg-minigame-cards-tie = Same rank. A tie loses.
wg-minigame-cards-cashed = You cash out at {$multiple}x.
wg-minigame-cards-top = Top of the ladder: {$multiple}x.
wg-minigame-cards-rank-1 = Two
wg-minigame-cards-rank-2 = Three
wg-minigame-cards-rank-3 = Four
wg-minigame-cards-rank-4 = Five
wg-minigame-cards-rank-5 = Six
wg-minigame-cards-rank-6 = Seven
wg-minigame-cards-rank-7 = Eight
wg-minigame-cards-rank-8 = Nine
wg-minigame-cards-rank-9 = Ten
wg-minigame-cards-rank-10 = Jack
wg-minigame-cards-rank-11 = Queen
wg-minigame-cards-rank-12 = King
wg-minigame-cards-rank-13 = Ace
wg-minigame-cards-suit-0 = Hearts
wg-minigame-cards-suit-1 = Diamonds
wg-minigame-cards-suit-2 = Clubs
wg-minigame-cards-suit-3 = Spades

## Memory

wg-minigame-memory-rules = Watch the pads light up, then press them back in order.
wg-minigame-memory-progress = {$entered} of {$length} pressed. Reach {$target} to win.
wg-minigame-memory-start = Reach a sequence of {$length}.
wg-minigame-memory-next = Right. Now {$length} of {$target}.
wg-minigame-memory-wrong = Wrong pad. You got to {$reached}.
wg-minigame-memory-won = Every one right.

## Riddles

wg-minigame-riddle-number = Riddle {$number} of {$total}
wg-minigame-riddle-start = Answer {$count} riddles right in a row.
wg-minigame-riddle-right = Right. {$left} to go.
wg-minigame-riddle-wrong = Wrong.
wg-minigame-riddle-won = Every one right.

## Hosts

wg-minigame-prompt-rules = How does this work?

wg-minigame-dealer-greeting = Take a seat. Cards, dice or the lockbox?
wg-minigame-dealer-farewell = Come back when your luck's in.
wg-minigame-dealer-prompt-cards = Deal me Higher or Lower.
wg-minigame-dealer-response-cards = Fresh deck, every card.
wg-minigame-dealer-prompt-dice = Liar's Dice.
wg-minigame-dealer-response-dice = Shake the cup.
wg-minigame-dealer-prompt-lock = I'll crack your lockbox.
wg-minigame-dealer-response-lock = Four digits. Not many tries.
wg-minigame-dealer-response-rules = Put your stake on the counter or show me your ID. Win and I pay several times over. Win once and you wait a while before I play you again.

wg-minigame-sage-greeting = Ah, a visitor. Do you like puzzles?
wg-minigame-sage-farewell = Think on it.
wg-minigame-sage-prompt-riddles = Ask me your riddles.
wg-minigame-sage-response-riddles = Three in a row, and no guessing.
wg-minigame-sage-prompt-memory = The memory game.
wg-minigame-sage-response-memory = Watch closely.
wg-minigame-sage-prompt-ore = Ore piles, high stakes.
wg-minigame-sage-response-ore = Nobody beats me at this. You can try.
wg-minigame-sage-response-rules = A stake on the counter, or your ID. Win and I pay well. Lose and I keep it.

wg-minigame-gambler-greeting = Hey, friend. Got a few credits? Let's make it interesting.
wg-minigame-gambler-farewell = See you down the road.
wg-minigame-gambler-prompt-ore = Ore piles.
wg-minigame-gambler-response-ore = Last one loses. Easy, right?
wg-minigame-gambler-prompt-dice = Liar's Dice.
wg-minigame-gambler-response-dice = Don't lie to me now.
wg-minigame-gambler-prompt-cards = Higher or Lower.
wg-minigame-gambler-response-cards = Here's your first card.
wg-minigame-gambler-response-rules = Cash on the table or your ID. I pay three to one. Don't win too often.

wg-minigame-miner-greeting = Hm. Don't get many visitors this deep. You play?
wg-minigame-miner-farewell = Mind the ceiling.
wg-minigame-miner-prompt-ore-easy = A friendly game of ore piles.
wg-minigame-miner-response-ore-easy = I'll go easy. Gold and silver if you win.
wg-minigame-miner-prompt-dice = Liar's Dice for diamonds.
wg-minigame-miner-response-dice = Diamonds and uranium in the pot.
wg-minigame-miner-prompt-ore-hard = Ore piles. Your best game.
wg-minigame-miner-response-ore-hard = Beat me and the diamond drill is yours. You won't.
wg-minigame-miner-response-rules = Stake in cash, on the crate or from your account. I don't pay in money. I pay in ore, and the best drill you've ever held.
