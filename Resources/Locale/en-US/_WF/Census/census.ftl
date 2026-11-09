wf-census-button = Census
wf-census-button-count = Census ({ $count })
wf-census-window-title = Census
wf-census-list-empty = There are no censuses open right now.
wf-census-list-loading = Loading...
wf-census-closes-in = Closes in { $time }
wf-census-answered = Answered
wf-census-answer = Answer
wf-census-back = Back to the list
wf-census-required = [color=#e05050]*[/color] { $prompt }
wf-census-required-note = [color=#999999]Questions marked [color=#e05050]*[/color] need an answer.[/color]
wf-census-pick-one = Pick one
wf-census-pick-up-to = Pick up to { $max }
wf-census-pick-at-least = Pick at least { $min }
wf-census-pick-between = Pick { $min } to { $max }
wf-census-pick-exactly = Pick { $count }
wf-census-characters = { $count } / { $max }
wf-census-text-placeholder = Your answer
wf-census-submit = Send
wf-census-sending = Sending...
wf-census-sent = [color=#7fd17f]Thank you, your answers are in.[/color]

wf-census-time-days = { $count ->
    [one] 1 day
   *[other] { $count } days
}
wf-census-time-hours = { $count ->
    [one] 1 hour
   *[other] { $count } hours
}
wf-census-time-minutes = { $count ->
    [one] 1 minute
   *[other] { $count } minutes
}

wf-census-opened-chat = A census is open: { $title }. Answer it from the Escape menu > Census.

wf-census-error-disabled = [color=#e05050]Censuses are switched off on this server.[/color]
wf-census-error-not-open = [color=#e05050]This census has closed.[/color]
wf-census-error-answered = [color=#e05050]You have already answered this census.[/color]
wf-census-error-rate = [color=#e05050]Too many tries. Wait { $count } seconds and send again.[/color]
wf-census-error-required = [color=#e05050]"{ $question }" needs an answer.[/color]
wf-census-error-too-few = [color=#e05050]"{ $question }": pick at least { $count }.[/color]
wf-census-error-too-many = [color=#e05050]"{ $question }": pick at most { $count }.[/color]
wf-census-error-invalid = [color=#e05050]The server could not read your answers. Close the census and try again.[/color]
wf-census-error-failed = [color=#e05050]Your answers could not be stored. Try again in a moment.[/color]
