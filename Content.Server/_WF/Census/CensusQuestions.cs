using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Content.Shared._WF.Census;

namespace Content.Server._WF.Census;

/// <summary>
/// Reads, checks and writes census questions and answers as JSON. Questions are normalised: defaults filled in,
/// unknown keys dropped, text trimmed, so two that mean the same compare equal as text.
/// </summary>
public static class CensusQuestions
{
    /// <summary>The questions in a JSON array, or why they break the rules, in a sentence for staff.</summary>
    public static List<CensusQuestion>? Parse(JsonElement json, out string? problem)
    {
        problem = null;
        if (json.ValueKind != JsonValueKind.Array)
        {
            problem = "questions must be an array.";
            return null;
        }

        var count = json.GetArrayLength();
        if (count is < CensusRules.MinQuestions or > CensusRules.MaxQuestions)
        {
            problem = $"questions must have {CensusRules.MinQuestions} to {CensusRules.MaxQuestions} entries; it has {count}.";
            return null;
        }

        var questions = new List<CensusQuestion>();
        var ids = new HashSet<string>();
        var n = 0;
        foreach (var item in json.EnumerateArray())
        {
            n++;
            if (ParseOne(item, n, out problem) is not { } question)
                return null;

            if (!ids.Add(question.Id))
            {
                problem = $"Question {n}: the id \"{question.Id}\" is used twice.";
                return null;
            }

            questions.Add(question);
        }

        if (Encoding.UTF8.GetByteCount(Write(questions)) > CensusRules.MaxQuestionsBytes)
        {
            problem = $"questions is larger than {CensusRules.MaxQuestionsBytes / 1024} KiB.";
            return null;
        }

        return questions;
    }

    /// <summary>Parses stored questions; null when the text is not valid questions.</summary>
    public static List<CensusQuestion>? ParseStored(string text)
    {
        try
        {
            using var doc = JsonDocument.Parse(text);
            return Parse(doc.RootElement, out _);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static CensusQuestion? ParseOne(JsonElement item, int n, out string? problem)
    {
        problem = null;
        if (item.ValueKind != JsonValueKind.Object)
        {
            problem = $"Question {n} must be an object.";
            return null;
        }

        var question = new CensusQuestion();
        if (!item.TryGetProperty("id", out var id) || id.ValueKind != JsonValueKind.String || !CensusRules.ValidId(id.GetString()!))
        {
            problem = $"Question {n}: id must be 1 to {CensusRules.MaxQuestionId} of a-z, 0-9 and _.";
            return null;
        }

        question.Id = id.GetString()!;
        var label = $"Question {n} ({question.Id})";

        var kind = item.TryGetProperty("kind", out var k) && k.ValueKind == JsonValueKind.String ? k.GetString() : null;
        switch (kind)
        {
            case "single":
                question.Kind = CensusQuestionKind.Single;
                break;
            case "multi":
                question.Kind = CensusQuestionKind.Multi;
                break;
            case "text":
                question.Kind = CensusQuestionKind.Text;
                break;
            default:
                problem = $"{label}: kind must be single, multi or text.";
                return null;
        }

        var prompt = item.TryGetProperty("prompt", out var p) && p.ValueKind == JsonValueKind.String ? p.GetString()!.Trim() : "";
        if (prompt.Length is 0 or > CensusRules.MaxPrompt)
        {
            problem = $"{label}: prompt must be 1 to {CensusRules.MaxPrompt} characters.";
            return null;
        }

        question.Prompt = prompt;

        if (!OptionalBool(item, "required", false, out question.Required))
        {
            problem = $"{label}: required must be true or false.";
            return null;
        }

        if (question.Kind == CensusQuestionKind.Text)
        {
            if (!OptionalInt(item, "maxLength", CensusRules.DefaultTextLength, out question.MaxLength)
                || question.MaxLength is < 1 or > CensusRules.MaxTextLength)
            {
                problem = $"{label}: maxLength must be a whole number from 1 to {CensusRules.MaxTextLength}.";
                return null;
            }

            if (!OptionalBool(item, "multiline", false, out question.Multiline))
            {
                problem = $"{label}: multiline must be true or false.";
                return null;
            }

            return question;
        }

        if (!item.TryGetProperty("options", out var options) || options.ValueKind != JsonValueKind.Array)
        {
            problem = $"{label}: options must be a list of {CensusRules.MinOptions} to {CensusRules.MaxOptions} choices.";
            return null;
        }

        foreach (var option in options.EnumerateArray())
        {
            var text = option.ValueKind == JsonValueKind.String ? option.GetString()!.Trim() : "";
            if (text.Length is 0 or > CensusRules.MaxOption)
            {
                problem = $"{label}: every option must be 1 to {CensusRules.MaxOption} characters.";
                return null;
            }

            if (question.Options.Contains(text))
            {
                problem = $"{label}: the option \"{text}\" is listed twice.";
                return null;
            }

            question.Options.Add(text);
        }

        if (question.Options.Count is < CensusRules.MinOptions or > CensusRules.MaxOptions)
        {
            problem = $"{label}: options must have {CensusRules.MinOptions} to {CensusRules.MaxOptions} choices.";
            return null;
        }

        if (question.Kind == CensusQuestionKind.Single)
        {
            question.Max = 1;
            question.Min = question.Required ? 1 : 0;
            return question;
        }

        if (!OptionalInt(item, "min", question.Required ? 1 : 0, out question.Min)
            || !OptionalInt(item, "max", question.Options.Count, out question.Max))
        {
            problem = $"{label}: min and max must be whole numbers.";
            return null;
        }

        if (question.Min < 0 || question.Min > question.Max || question.Max > question.Options.Count || question.Max < 1)
        {
            problem = $"{label}: min and max must keep 0 <= min <= max <= {question.Options.Count} (the number of options), and max at least 1.";
            return null;
        }

        return question;
    }

    private static bool OptionalBool(JsonElement item, string name, bool fallback, out bool value)
    {
        value = fallback;
        if (!item.TryGetProperty(name, out var prop) || prop.ValueKind == JsonValueKind.Null)
            return true;

        if (prop.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            return false;

        value = prop.GetBoolean();
        return true;
    }

    private static bool OptionalInt(JsonElement item, string name, int fallback, out int value)
    {
        value = fallback;
        if (!item.TryGetProperty(name, out var prop) || prop.ValueKind == JsonValueKind.Null)
            return true;

        return prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out value);
    }

    /// <summary>The questions as normalised JSON, every default written out.</summary>
    public static string Write(List<CensusQuestion> questions)
    {
        return ToJson(questions).ToJsonString();
    }

    public static JsonArray ToJson(List<CensusQuestion> questions)
    {
        var array = new JsonArray();
        foreach (var q in questions)
        {
            var obj = new JsonObject
            {
                ["id"] = q.Id,
                ["kind"] = q.Kind.ToString().ToLowerInvariant(),
                ["prompt"] = q.Prompt,
            };

            if (q.Kind == CensusQuestionKind.Text)
            {
                obj["required"] = q.Required;
                obj["maxLength"] = q.MaxLength;
                obj["multiline"] = q.Multiline;
            }
            else
            {
                obj["options"] = new JsonArray(q.Options.Select(o => (JsonNode?) JsonValue.Create(o)).ToArray());
                obj["required"] = q.Required;
                if (q.Kind == CensusQuestionKind.Multi)
                {
                    obj["min"] = q.Min;
                    obj["max"] = q.Max;
                }
            }

            array.Add(obj);
        }

        return array;
    }

    /// <summary>
    /// Checks a player's answers against the questions and writes them as the stored JSON, or names what is
    /// wrong: a Fluent id, the prompt of the question at fault and a count.
    /// </summary>
    public static string? Answer(List<CensusQuestion> questions, CensusSubmitEvent ev,
        out string? error, out string question, out int count)
    {
        error = null;
        question = string.Empty;
        count = 0;

        var known = questions.Select(q => q.Id).ToHashSet();
        if (ev.Choices.Count + ev.Texts.Count > questions.Count
            || ev.Choices.Keys.Concat(ev.Texts.Keys).Any(id => !known.Contains(id)))
        {
            error = "wf-census-error-invalid";
            return null;
        }

        var answers = new JsonObject();
        foreach (var q in questions)
        {
            question = q.Prompt;
            if (q.Kind == CensusQuestionKind.Text)
            {
                if (ev.Choices.ContainsKey(q.Id))
                {
                    error = "wf-census-error-invalid";
                    return null;
                }

                var text = Clean(ev.Texts.GetValueOrDefault(q.Id), q.MaxLength, q.Multiline);
                if (text.Length > 0)
                    answers[q.Id] = text;
                else if (q.Required)
                {
                    error = "wf-census-error-required";
                    return null;
                }

                continue;
            }

            if (ev.Texts.ContainsKey(q.Id))
            {
                error = "wf-census-error-invalid";
                return null;
            }

            var picks = ev.Choices.GetValueOrDefault(q.Id);
            if (picks == null || picks.Count == 0)
            {
                if (q.Required)
                {
                    error = "wf-census-error-required";
                    return null;
                }

                continue;
            }

            if (picks.Any(i => i < 0 || i >= q.Options.Count) || picks.Distinct().Count() != picks.Count)
            {
                error = "wf-census-error-invalid";
                return null;
            }

            if (q.Kind == CensusQuestionKind.Single && picks.Count != 1)
            {
                error = "wf-census-error-invalid";
                return null;
            }

            if (q.Kind == CensusQuestionKind.Multi)
            {
                if (picks.Count < q.Min)
                {
                    error = "wf-census-error-too-few";
                    count = q.Min;
                    return null;
                }

                if (picks.Count > q.Max)
                {
                    error = "wf-census-error-too-many";
                    count = q.Max;
                    return null;
                }
            }

            answers[q.Id] = new JsonArray(picks.OrderBy(i => i).Select(i => (JsonNode?) JsonValue.Create(i)).ToArray());
        }

        question = string.Empty;
        return answers.ToJsonString();
    }

    /// <summary>
    /// Text fit to store: trimmed, cut to length, without control characters (bar newlines in a multi-line answer)
    /// or lone surrogates, which PostgreSQL refuses.
    /// </summary>
    public static string Clean(string? text, int max, bool multiline)
    {
        if (string.IsNullOrEmpty(text))
            return string.Empty;

        var builder = new StringBuilder(Math.Min(text.Length, max));
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                    builder.Append(c).Append(text[++i]);
                continue;
            }

            if (char.IsLowSurrogate(c) || c == '\r')
                continue;

            if (c is '\n' or '\t')
                builder.Append(multiline && c == '\n' ? c : ' ');
            else if (!char.IsControl(c))
                builder.Append(c);
        }

        var clean = builder.ToString().Trim();
        if (clean.Length <= max)
            return clean;

        // Cut without splitting a surrogate pair.
        var cut = char.IsHighSurrogate(clean[max - 1]) ? max - 1 : max;
        return clean[..cut].TrimEnd();
    }
}
