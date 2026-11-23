using ChurchSlides.Domain.Model;

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
        return problems;
    }

    public static IReadOnlyList<string> Validate(Service service)
    {
        var problems = new List<string>();
        if (service.Id.IsEmpty) problems.Add("Service: the Id is empty.");
        if (string.IsNullOrWhiteSpace(service.Name)) problems.Add("Service: the name is empty.");
        CheckItems(service.Items, problems);
        return problems;
    }

    public static IReadOnlyList<string> Validate(ServiceTemplate template)
    {
        var problems = new List<string>();
        if (template.Id.IsEmpty) problems.Add("Service Template: the Id is empty.");
        if (string.IsNullOrWhiteSpace(template.Name)) problems.Add("Service Template: the name is empty.");
        CheckItems(template.Items, problems);
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
        return problems;
    }

    public static IReadOnlyList<string> Validate(LibrarySettings settings) => [];

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

            switch (item)
            {
                case ShowCopyItem { LocalShow: null }:
                    problems.Add($"{where}: the local Show is missing.");
                    break;
                case ShowCopyItem copy:
                    CheckShowIds(copy.LocalShow, $"{where} (local Show)", problems);
                    break;
                case ScriptureItem { Generated: { } generated }:
                    CheckShowIds(generated, $"{where} (generated Show)", problems);
                    break;
            }
        }
    }
}
