using System.Collections.Immutable;
using ChurchSlides.Domain.Model;
using ChurchSlides.Domain.Text;

namespace ChurchSlides.Domain.Resolution;

/// <summary>
/// Turns a Slide into a <see cref="RenderModel"/>: resolves the Template, binds text boxes, merges formatting,
/// resolves tokens and adds the copyright notice. Pure: same inputs, same output, never throws for bad content.
/// </summary>
public static class SlideResolver
{
    public static RenderModel Resolve(Slide slide, ResolutionContext context)
    {
        var resolution = TemplateResolver.Resolve(slide, context.Show, context.Templates);
        var template = resolution.Template;
        var problems = new List<RenderProblem>(resolution.Problems);
        var firstContentBox = TemplateBoxes.FirstContent(template);

        var boxes = ImmutableArray.CreateBuilder<RenderTextBox>();
        foreach (var textBox in slide.TextBoxes)
            boxes.Add(ResolveTextBox(textBox, template, firstContentBox, context, problems));

        if (ResolveCopyright(template, context, problems) is { } copyright)
            boxes.Add(copyright);

        return new RenderModel
        {
            TemplateId = template.Id,
            Background = slide.BackgroundOverride ?? template.Background,
            TextBoxes = boxes.ToImmutable(),
            Problems = [.. problems.Distinct()],
        };
    }

    private static RenderTextBox ResolveTextBox(
        SlideTextBox textBox, Template template, TemplateBox? firstContentBox, ResolutionContext context, List<RenderProblem> problems)
    {
        TemplateBox? box;
        if (textBox.TemplateBoxName is { } name)
        {
            box = TemplateBoxes.Find(template, name);
            if (box is null)
            {
                problems.Add(new TemplateBoxNotFound(name, textBox.Id)); // TPL-027
                box = firstContentBox;
            }
        }
        else
        {
            if (textBox.Geometry is null) problems.Add(new TextBoxUnplaced(textBox.Id));
            box = firstContentBox;
        }

        if (box is null) problems.Add(new NoUsableTemplateBox());
        if (context.Service is not null) ReportTimerTokensNotAlone(textBox, problems);

        var defaults = box?.Defaults ?? new TextDefaults();
        return NewBox(
            textBox.Geometry ?? box?.Geometry ?? TemplateBoxes.FullCanvas,
            defaults,
            textBox,
            ResolveParagraphs(textBox.Content.Paragraphs, defaults, context, problems));
    }

    /// <summary>TMR-009: a Timer token must be the only content of its Text Box (ignoring whitespace and empty paragraphs).</summary>
    private static void ReportTimerTokensNotAlone(SlideTextBox textBox, List<RenderProblem> problems)
    {
        var content = textBox.Content.Paragraphs
            .SelectMany(p => p.Inlines)
            .Where(i => i is not TextRun { Text: var text } || !string.IsNullOrWhiteSpace(text))
            .ToList();
        if (content is [TokenRun { Token: TimerToken }]) return;

        foreach (var name in content.Select(i => (i as TokenRun)?.Token).OfType<TimerToken>().Select(t => t.Name))
            problems.Add(new TimerTokenNotAlone(name, textBox.Id));
    }

    /// <summary>Builds a render box. A null source means the generated copyright box.</summary>
    private static RenderTextBox NewBox(
        RectRelative geometry, TextDefaults defaults, SlideTextBox? source, ImmutableArray<RenderParagraph> paragraphs) => new()
        {
            SourceId = source?.Id,
            IsCopyright = source is null,
            Geometry = geometry,
            HAlign = source?.HAlign ?? defaults.HAlign,
            VAlign = source?.VAlign ?? defaults.VAlign,
            LineSpacing = source?.LineSpacing ?? defaults.LineSpacing,
            Outline = defaults.Outline,
            Shadow = defaults.Shadow,
            Paragraphs = paragraphs,
        };

    /// <summary>CCLI-004/005: the notice appears on the first or last displayed slide, never for public domain.</summary>
    private static RenderTextBox? ResolveCopyright(Template template, ResolutionContext context, List<RenderProblem> problems)
    {
        var box = template.Boxes.FirstOrDefault(b => b.IsCopyrightBox);
        var show = context.Show;
        if (box is null || show.CopyrightPlacement == CopyrightPlacement.None || show.Metadata?.IsPublicDomain == true)
            return null;

        var onThisSlide = show.CopyrightPlacement == CopyrightPlacement.FirstSlide
            ? context.Position.IsFirst
            : context.Position.IsLast;
        var inlines = TokenMarkup.Parse(context.Library.CopyrightFormat);
        if (!onThisSlide || inlines.IsEmpty) return null;

        return NewBox(
            box.Geometry,
            box.Defaults,
            source: null,
            ResolveParagraphs([new Paragraph { Inlines = inlines }], box.Defaults, context, problems));
    }

    private static ImmutableArray<RenderParagraph> ResolveParagraphs(
        ImmutableArray<Paragraph> paragraphs, TextDefaults defaults, ResolutionContext context, List<RenderProblem> problems) =>
        [.. paragraphs.Select(p => new RenderParagraph
        {
            Runs = [.. p.Inlines.Select(inline => ResolveInline(inline, defaults, context, problems))],
        })];

    private static RenderRun ResolveInline(Inline inline, TextDefaults defaults, ResolutionContext context, List<RenderProblem> problems)
    {
        // TPL-024: inline formatting always wins; defaults only fill what the run leaves unset.
        var format = inline.Format;
        var style = new RunStyle
        {
            FontFamily = format?.FontFamily ?? defaults.FontFamily,
            FontSize = format?.FontSize ?? defaults.FontSizeRelative,
            ColorHex = format?.ColorHex ?? defaults.ColorHex,
            Bold = format?.Bold ?? defaults.Bold,
            Italic = format?.Italic ?? defaults.Italic,
        };

        if (inline is not TokenRun { Token: var token })
            return new RenderTextRun { Style = style, Text = ((TextRun)inline).Text };

        if (token is TimerToken timerToken && context.Service is { } timerService)
        {
            var timer = timerService.Timers.FirstOrDefault(t => VariableNames.Comparer.Equals(t.Name, timerToken.Name));
            if (timer is not null)
            {
                return new RenderTimerRun
                {
                    Style = style,
                    TimerName = timer.Name,
                    WarningThresholdSeconds = timer.WarningThresholdSeconds,
                    WarningColorHex = timer.WarningColorHex,
                };
            }
            problems.Add(new TimerUndefined(timerToken.Name));
            return new RenderTextRun { Style = style, Text = "" };
        }

        return new RenderTextRun { Style = style, Text = ResolveTokenText(token, context, problems) };
    }

    private static string ResolveTokenText(Token token, ResolutionContext context, List<RenderProblem> problems)
    {
        switch (token)
        {
            case ShowMetadataToken meta:
                var metadata = context.Show.Metadata;
                return meta.Field switch
                {
                    ShowMetadataField.Title => context.Show.Title,
                    ShowMetadataField.Authors => metadata?.Authors ?? "",
                    ShowMetadataField.Copyright => metadata?.Copyright ?? "",
                    _ => metadata?.CcliSongNumber ?? "",
                };

            case ChurchLicenseToken:
                return context.Library.CcliLicenseNumber ?? "";

            case VariableToken variable when context.Service is { } service:
                var found = service.Variables.FirstOrDefault(v => VariableNames.Comparer.Equals(v.Name, variable.Name));
                if (found is null) problems.Add(new VariableUndefined(variable.Name));
                else if (string.IsNullOrEmpty(found.Value)) problems.Add(new VariableEmpty(variable.Name));
                return found?.Value ?? "";

            default:
                // VAR-003: outside a Service, Variable and Timer tokens show as their markup.
                return token.ToMarkup();
        }
    }
}
