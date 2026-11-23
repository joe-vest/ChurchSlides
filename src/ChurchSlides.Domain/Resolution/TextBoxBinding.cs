using ChurchSlides.Domain.Model;
using ChurchSlides.Domain.Text;

namespace ChurchSlides.Domain.Resolution;

/// <summary>Detach and reattach text boxes (TPL-023) and create new slides (TPL-026).</summary>
public static class TextBoxBinding
{
    /// <summary>Gives the text box its own copy of its current position, so it doesn't move when detached.</summary>
    public static SlideTextBox Detach(SlideTextBox textBox, Template template)
    {
        if (textBox.IsDetached) return textBox;

        var box = BoxFor(textBox, template);
        return textBox with
        {
            TemplateBoxName = textBox.TemplateBoxName ?? box?.Name,
            Geometry = box?.Geometry ?? TemplateBoxes.FullCanvas,
        };
    }

    /// <summary>Discards the text box's own geometry and binds it to its Template Box again.</summary>
    public static SlideTextBox Reattach(SlideTextBox textBox, Template template)
    {
        var box = BoxFor(textBox, template);
        return textBox with { TemplateBoxName = box?.Name ?? textBox.TemplateBoxName, Geometry = null };
    }

    private static TemplateBox? BoxFor(SlideTextBox textBox, Template template) =>
        (textBox.TemplateBoxName is { } name ? TemplateBoxes.Find(template, name) : null) ?? TemplateBoxes.FirstContent(template);
}

public static class SlideFactory
{
    /// <summary>TPL-026: one empty bound text box for each Template Box except the copyright box.</summary>
    public static Slide CreateBlank(Template template, string label = "") => new()
    {
        Id = Id<Slide>.New(),
        Label = label,
        TextBoxes =
        [
            .. template.Boxes.Where(b => !b.IsCopyrightBox).Select(b => new SlideTextBox
            {
                Id = Id<SlideTextBox>.New(),
                TemplateBoxName = b.Name,
                Content = RichText.Empty,
            }),
        ],
    };
}
