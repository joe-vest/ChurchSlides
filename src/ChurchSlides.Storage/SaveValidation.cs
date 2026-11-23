using System.Collections.Immutable;
using ChurchSlides.Domain.Model;
using ChurchSlides.Domain.Resolution;

namespace ChurchSlides.Storage;

/// <summary>
/// The safety net before a save: the model has no compile-time "required", so an object built in code can have an empty Id
/// or a blank name. Each method returns a readable list of problems, empty when the object may be saved.
/// </summary>
public static class SaveValidation
{
    public static IReadOnlyList<string> Validate(Show show)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(show.Title)) problems.Add("Show: the title is empty.");
        CheckShowIds(show, "Show", problems);
        CheckShowActions(show, "Show", problems);
        return problems;
    }

    public static IReadOnlyList<string> Validate(Service service)
    {
        var problems = new List<string>();
        if (service.Id.IsEmpty) problems.Add("Service: the Id is empty.");
        if (string.IsNullOrWhiteSpace(service.Name)) problems.Add("Service: the name is empty.");
        CheckItems(service.Items, problems);
        CheckActions(service.Actions, ActionLevel.Service, "Service", problems);
        CheckNames(service.Variables.Select(v => v.Name), service.Timers.Select(t => t.Name), problems);
        return problems;
    }

    public static IReadOnlyList<string> Validate(ServiceTemplate template)
    {
        var problems = new List<string>();
        if (template.Id.IsEmpty) problems.Add("Service Template: the Id is empty.");
        if (string.IsNullOrWhiteSpace(template.Name)) problems.Add("Service Template: the name is empty.");
        CheckItems(template.Items, problems);
        CheckActions(template.Actions, ActionLevel.ServiceTemplate, "Service Template", problems);
        CheckNames(template.Variables.Select(v => v.Name), template.Timers.Select(t => t.Name), problems);
        return problems;
    }

    public static IReadOnlyList<string> Validate(Template template)
    {
        var problems = new List<string>();
        if (template.Id.IsEmpty) problems.Add("Template: the Id is empty.");
        if (string.IsNullOrWhiteSpace(template.Name)) problems.Add("Template: the name is empty.");
        return problems;
    }

    public static IReadOnlyList<string> Validate(CategoryList list)
    {
        var problems = new List<string>();
        for (var i = 0; i < list.Categories.Length; i++)
        {
            if (list.Categories[i].Id.IsEmpty) problems.Add($"Category {i + 1}: the Id is empty.");
            if (string.IsNullOrWhiteSpace(list.Categories[i].Name)) problems.Add($"Category {i + 1}: the name is empty.");
        }
        if (!BuiltInCategories.IsPresentIn(list)) problems.Add("Categories: the built-in Scripture Category can't be removed.");
        return problems;
    }

    public static IReadOnlyList<string> Validate(LibrarySettings settings) => [];

    public static IReadOnlyList<string> Validate(ServiceState state) =>
        state.ServiceId.IsEmpty ? ["Service state: the Service Id is empty."] : [];

    /// <summary>VAR-001, TMR-001: valid characters, and no two Variables (or two Timers) with the same name, ignoring case.</summary>
    private static void CheckNames(IEnumerable<string> variables, IEnumerable<string> timers, List<string> problems)
    {
        CheckNameSet(variables, "Variable", problems);
        CheckNameSet(timers, "Timer", problems);
    }

    private static void CheckNameSet(IEnumerable<string> names, string kind, List<string> problems)
    {
        var seen = new HashSet<string>(VariableNames.Comparer);
        foreach (var name in names)
        {
            if (!VariableNames.IsValid(name)) problems.Add($"{kind} \"{name}\": names can only use letters, digits and underscores.");
            else if (!seen.Add(name)) problems.Add($"{kind} \"{name}\": the name is used more than once.");
        }
    }

    /// <summary>ACT-010..016: an Action's trigger must be one that is allowed where the Action lives.</summary>
    private static void CheckActions(ImmutableArray<TriggeredAction> actions, ActionLevel level, string where, List<string> problems)
    {
        for (var i = 0; i < actions.Length; i++)
            if (!ActionRules.IsAllowed(actions[i].Trigger, level))
                problems.Add($"{where}: Action {i + 1} uses the trigger {actions[i].Trigger.GetType().Name}, which isn't allowed here.");
    }

    private static void CheckShowActions(Show show, string where, List<string> problems)
    {
        CheckActions(show.Actions, ActionLevel.Show, where, problems);
        for (var i = 0; i < show.Slides.Length; i++)
            CheckActions(show.Slides[i].Actions, ActionLevel.Slide, $"{where}, slide {i + 1}", problems);
    }

    /// <summary>Ids only. A nested Show (a Service-local copy, generated Scripture) takes its title from elsewhere, so a blank one isn't an error here.</summary>
    private static void CheckShowIds(Show show, string where, List<string> problems)
    {
        if (show.Id.IsEmpty) problems.Add($"{where}: the Id is empty.");
        for (var i = 0; i < show.Slides.Length; i++)
        {
            var slide = show.Slides[i];
            if (slide.Id.IsEmpty) problems.Add($"{where}: slide {i + 1} has an empty Id.");
            for (var b = 0; b < slide.TextBoxes.Length; b++)
                if (slide.TextBoxes[b].Id.IsEmpty)
                    problems.Add($"{where}: slide {i + 1}, text box {b + 1} has an empty Id.");
        }
    }

    private static void CheckItems(IReadOnlyList<ServiceItem> items, List<string> problems)
    {
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            var where = $"Item {i + 1}";
            if (item.Id.IsEmpty) problems.Add($"{where}: the Id is empty.");
            CheckActions(item.Actions, ActionLevel.ServiceItem, where, problems);

            switch (item)
            {
                case ShowCopyItem { LocalShow: null }:
                    problems.Add($"{where}: the local Show is missing.");
                    break;
                case ShowCopyItem copy:
                    CheckShowIds(copy.LocalShow, $"{where} (local Show)", problems);
                    CheckShowActions(copy.LocalShow, $"{where} (local Show)", problems);
                    break;
                case ScriptureItem { Generated: { } generated }:
                    CheckShowIds(generated, $"{where} (generated Show)", problems);
                    CheckShowActions(generated, $"{where} (generated Show)", problems);
                    break;
            }
        }
    }
}
