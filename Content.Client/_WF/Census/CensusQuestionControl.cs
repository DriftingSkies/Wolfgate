using System.Linq;
using System.Numerics;
using Content.Client.Message;
using Content.Shared._WF.Census;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._WF.Census;

/// <summary>One census question: toggle buttons for single choice, checkboxes for multiple, a text box for text.</summary>
public sealed class CensusQuestionControl : BoxContainer
{
    private static readonly Color CounterColor = Color.FromHex("#999999");
    private static readonly Color CounterFullColor = Color.FromHex("#e5c07b");

    private readonly CensusQuestion _question;
    private readonly List<BaseButton> _options = new();
    private LineEdit? _line;
    private TextEdit? _text;
    private Label? _counter;

    public CensusQuestionControl(CensusQuestion question)
    {
        _question = question;
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        SeparationOverride = 4;

        var prompt = new RichTextLabel();
        var escaped = FormattedMessage.EscapeText(question.Prompt);
        prompt.SetMarkup(question.Required ? Loc.GetString("wf-census-required", ("prompt", escaped)) : escaped);
        AddChild(prompt);

        switch (question.Kind)
        {
            case CensusQuestionKind.Single:
                BuildSingle();
                break;
            case CensusQuestionKind.Multi:
                BuildMulti();
                break;
            default:
                BuildText();
                break;
        }
    }

    private void BuildSingle()
    {
        AddHint(Loc.GetString("wf-census-pick-one"));
        var group = new ButtonGroup();
        foreach (var option in _question.Options)
        {
            var button = new Button
            {
                Text = option,
                ToggleMode = true,
                Group = group,
                TextAlign = Label.AlignMode.Left,
                HorizontalExpand = true,
                ClipText = true,
            };
            _options.Add(button);
            AddChild(button);
        }
    }

    private void BuildMulti()
    {
        var least = CensusRules.LeastPicks(_question);
        var max = _question.Max;
        string hint;
        if (least == max)
            hint = Loc.GetString("wf-census-pick-exactly", ("count", max));
        else if (least == 0)
            hint = Loc.GetString("wf-census-pick-up-to", ("max", max));
        else if (max == _question.Options.Count)
            hint = Loc.GetString("wf-census-pick-at-least", ("min", least));
        else
            hint = Loc.GetString("wf-census-pick-between", ("min", least), ("max", max));
        AddHint(hint);

        foreach (var option in _question.Options)
        {
            var box = new CheckBox { Text = option };
            box.OnToggled += _ => LimitPicks();
            _options.Add(box);
            AddChild(box);
        }
    }

    // Once max are picked, the rest can't be until one is let go.
    private void LimitPicks()
    {
        var full = _options.Count(o => o.Pressed) >= _question.Max;
        foreach (var option in _options)
        {
            option.Disabled = full && !option.Pressed;
        }
    }

    private void BuildText()
    {
        var max = _question.MaxLength;
        if (_question.Multiline)
        {
            _text = new TextEdit { MinSize = new Vector2(0, 90), HorizontalExpand = true };
            _text.Placeholder = new Rope.Leaf(Loc.GetString("wf-census-text-placeholder"));
            _text.OnTextChanged += _ => UpdateCounter(_text.TextLength);
            AddChild(_text);
        }
        else
        {
            _line = new LineEdit
            {
                HorizontalExpand = true,
                PlaceHolder = Loc.GetString("wf-census-text-placeholder"),
                IsValid = text => text.Length <= max,
            };
            _line.OnTextChanged += args => UpdateCounter(args.Text.Length);
            AddChild(_line);
        }

        _counter = new Label { HorizontalAlignment = HAlignment.Right, FontColorOverride = CounterColor };
        AddChild(_counter);
        UpdateCounter(0);
    }

    private void UpdateCounter(int length)
    {
        if (_counter == null)
            return;

        _counter.Text = Loc.GetString("wf-census-characters", ("count", length), ("max", _question.MaxLength));
        _counter.FontColorOverride = length > _question.MaxLength ? Color.FromHex("#e05050")
            : length == _question.MaxLength ? CounterFullColor
            : CounterColor;
    }

    private void AddHint(string text)
    {
        AddChild(new Label { Text = text, StyleClasses = { "LabelSubText" } });
    }

    /// <summary>Adds this question's answer, or returns the markup of what is wrong with it.</summary>
    public string? Collect(CensusSubmitEvent answers)
    {
        var escaped = FormattedMessage.EscapeText(_question.Prompt);
        if (_question.Kind == CensusQuestionKind.Text)
        {
            var text = (_text != null ? Rope.Collapse(_text.TextRope) : _line?.Text ?? "").Trim();
            if (text.Length == 0)
                return _question.Required ? Loc.GetString("wf-census-error-required", ("question", escaped)) : null;

            if (text.Length > _question.MaxLength)
                text = text[.._question.MaxLength];
            answers.Texts[_question.Id] = text;
            return null;
        }

        var picks = new List<int>();
        for (var i = 0; i < _options.Count; i++)
        {
            if (_options[i].Pressed)
                picks.Add(i);
        }

        if (picks.Count == 0)
            return _question.Required ? Loc.GetString("wf-census-error-required", ("question", escaped)) : null;

        if (picks.Count < _question.Min)
            return Loc.GetString("wf-census-error-too-few", ("question", escaped), ("count", _question.Min));

        if (picks.Count > _question.Max)
            return Loc.GetString("wf-census-error-too-many", ("question", escaped), ("count", _question.Max));

        answers.Choices[_question.Id] = picks;
        return null;
    }
}
