using Content.Shared._WF.Feedback;

namespace Content.Client._WF.Feedback;

/// <summary>Carries the feedback window's traffic: reports out, and answers, the player's list and notices back.</summary>
public sealed class FeedbackSystem : EntitySystem
{
    /// <summary>Raised when the server has stored or refused a report.</summary>
    public event Action<FeedbackResultEvent>? Answered;

    /// <summary>Raised with the player's own reports, after <see cref="RequestHistory"/>.</summary>
    public event Action<FeedbackHistoryEvent>? History;

    /// <summary>Raised when staff have moved some of the player's reports on.</summary>
    public event Action<FeedbackNoticeEvent>? Notice;

    /// <summary>Raised when the server offers the end of shift survey.</summary>
    public event Action<FeedbackSurveyPromptEvent>? SurveyPrompt;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<FeedbackResultEvent>(ev => Answered?.Invoke(ev));
        SubscribeNetworkEvent<FeedbackHistoryEvent>(ev => History?.Invoke(ev));
        SubscribeNetworkEvent<FeedbackNoticeEvent>(ev => Notice?.Invoke(ev));
        SubscribeNetworkEvent<FeedbackSurveyPromptEvent>(ev => SurveyPrompt?.Invoke(ev));
    }

    public void Submit(FeedbackSubmitEvent report)
    {
        RaiseNetworkEvent(report);
    }

    public void RequestHistory()
    {
        RaiseNetworkEvent(new FeedbackHistoryRequestEvent());
    }

    /// <summary>Tells the server the player has seen the notice for these reports.</summary>
    public void MarkSeen(List<int> ids)
    {
        RaiseNetworkEvent(new FeedbackNoticeSeenEvent { Ids = ids });
    }
}
