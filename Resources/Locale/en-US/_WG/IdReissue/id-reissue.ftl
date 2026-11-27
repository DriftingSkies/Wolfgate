wg-id-reissue-prompt = I lost my ID.
wg-id-reissue-response = Happens more than you'd think. Let me pull up your file.
wg-id-reissue-quote = I can print you a new card for { $fee } and void the old one. { $near ->
    [0] No ship papers need moving.
    [one] I'll move the papers for the one ship you have parked here.
   *[other] I'll move the papers for the { $near } ships you have parked here, one card each.
}{ $distant ->
    [0] {""}
    [one] {" "}One of your ships is too far out; her papers stay on the old card until you bring her in.
   *[other] {" "}{ $distant } of your ships are too far out; their papers stay on the old card until you bring them in.
} Shall I?
wg-id-reissue-cash-only = I can't draw on your account, so it has to be cash on the counter.
wg-id-reissue-done = There you are. The old card's void now. { $moved ->
    [0] {""}
    [one] Your ship's papers are on the new one.
   *[other] Your ships' papers are on the new cards.
}
wg-id-reissue-carrying = You've got your own card right there. I only reprint lost ones.
wg-id-reissue-no-record = I've no record of you to print from.
wg-id-reissue-cooldown = I printed you a card not long ago. Come back in { $minutes ->
    [one] a minute
   *[other] { $minutes } minutes
}.
wg-id-reissue-void-name = voided ID card ({ $name })
wg-id-reissue-door-hint = Your ship doesn't know you without your ID. Lost it? The shipyard dealer at Caelestinus Central can reissue it.
