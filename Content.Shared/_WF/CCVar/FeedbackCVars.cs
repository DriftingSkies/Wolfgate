using Robust.Shared.Configuration;

namespace Content.Shared._WF.CCVar;

/// <summary>Settings for player bug reports and suggestions.</summary>
[CVarDefs]
public sealed class FeedbackCVars
{
    /// <summary>Whether players may send bug reports and suggestions.</summary>
    public static readonly CVarDef<bool> Enabled =
        CVarDef.Create("wf.feedback.enabled", true, CVar.SERVER | CVar.REPLICATED);

    /// <summary>Whether a report may carry a screenshot.</summary>
    public static readonly CVarDef<bool> Screenshots =
        CVarDef.Create("wf.feedback.screenshots", true, CVar.SERVER | CVar.REPLICATED);

    /// <summary>Seconds a player waits between two reports.</summary>
    public static readonly CVarDef<int> Cooldown =
        CVarDef.Create("wf.feedback.cooldown", 60, CVar.SERVERONLY);

    /// <summary>Most reports one player may send in a day. 0 for no limit.</summary>
    public static readonly CVarDef<int> DailyLimit =
        CVarDef.Create("wf.feedback.daily_limit", 20, CVar.SERVERONLY);

    /// <summary>Whether players are offered the end of shift survey on going into cryo and on leaving.</summary>
    public static readonly CVarDef<bool> Surveys =
        CVarDef.Create("wf.feedback.surveys", true, CVar.SERVER | CVar.REPLICATED);

    /// <summary>Set by the survey chip's Never show again button.</summary>
    public static readonly CVarDef<bool> SurveyNever =
        CVarDef.Create("wf.feedback.survey_never", false, CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>Whether online admins are told in chat when a report comes in.</summary>
    public static readonly CVarDef<bool> AdminNotice =
        CVarDef.Create("wf.feedback.admin_notice", true, CVar.SERVERONLY);
}
