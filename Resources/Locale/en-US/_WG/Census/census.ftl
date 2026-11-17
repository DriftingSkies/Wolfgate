wg-census-button = Census
wg-census-button-count = Census ({ $count })
wg-census-window-title = Census
wg-census-list-empty = There are no censuses open right now.
wg-census-list-loading = Loading...
wg-census-closes-in = Closes in { $time }
wg-census-answered = Answered
wg-census-answer = Answer
wg-census-back = Back to the list
wg-census-required = [color=#e05050]*[/color] { $prompt }
wg-census-required-note = [color=#999999]Questions marked [color=#e05050]*[/color] need an answer.[/color]
wg-census-pick-one = Pick one
wg-census-pick-up-to = Pick up to { $max }
wg-census-pick-at-least = Pick at least { $min }
wg-census-pick-between = Pick { $min } to { $max }
wg-census-pick-exactly = Pick { $count }
wg-census-characters = { $count } / { $max }
wg-census-text-placeholder = Your answer
wg-census-submit = Send
wg-census-sending = Sending...
wg-census-sent = [color=#7fd17f]Thank you, your answers are in.[/color]

wg-census-time-days = { $count ->
    [one] 1 day
   *[other] { $count } days
}
wg-census-time-hours = { $count ->
    [one] 1 hour
   *[other] { $count } hours
}
wg-census-time-minutes = { $count ->
    [one] 1 minute
   *[other] { $count } minutes
}

wg-census-opened-chat = A census is open: { $title }. Answer it from the Escape menu > Census.

wg-census-error-disabled = [color=#e05050]Censuses are switched off on this server.[/color]
wg-census-error-not-open = [color=#e05050]This census has closed.[/color]
wg-census-error-answered = [color=#e05050]You have already answered this census.[/color]
wg-census-error-rate = [color=#e05050]Too many tries. Wait { $count } seconds and send again.[/color]
wg-census-error-required = [color=#e05050]"{ $question }" needs an answer.[/color]
wg-census-error-too-few = [color=#e05050]"{ $question }": pick at least { $count }.[/color]
wg-census-error-too-many = [color=#e05050]"{ $question }": pick at most { $count }.[/color]
wg-census-error-invalid = [color=#e05050]The server could not read your answers. Close the census and try again.[/color]
wg-census-error-failed = [color=#e05050]Your answers could not be stored. Try again in a moment.[/color]
