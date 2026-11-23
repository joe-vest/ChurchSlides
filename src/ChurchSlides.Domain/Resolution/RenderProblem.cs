using ChurchSlides.Domain.Model;

namespace ChurchSlides.Domain.Resolution;

/// <summary>
/// Something wrong that resolution worked around instead of failing (DEC-012: never block).
/// The pre-service check (CHK-001) reports these.
/// </summary>
public abstract record RenderProblem;

/// <summary>A Template ID is set (slide, Show, Category or Library) but that Template no longer exists (TPL-002).</summary>
public sealed record TemplateMissing(Id<Template> TemplateId) : RenderProblem;

/// <summary>No Template could be found at any level, so the built-in plain Template was used.</summary>
public sealed record NoTemplateAvailable : RenderProblem;

/// <summary>A text box names a Template Box the resolved Template doesn't have (TPL-027).</summary>
public sealed record TemplateBoxNotFound(string BoxName, Id<SlideTextBox> TextBoxId) : RenderProblem;

/// <summary>A text box has neither a Template Box name nor its own geometry.</summary>
public sealed record TextBoxUnplaced(Id<SlideTextBox> TextBoxId) : RenderProblem;

/// <summary>The resolved Template has no non-copyright box to fall back on.</summary>
public sealed record NoUsableTemplateBox : RenderProblem;

/// <summary>A Variable token resolves to empty text (VAR-005).</summary>
public sealed record VariableEmpty(string Name) : RenderProblem;

/// <summary>A Variable token names a Variable the Service doesn't define (VAR-005).</summary>
public sealed record VariableUndefined(string Name) : RenderProblem;

/// <summary>A Timer token names a Timer the Service doesn't define.</summary>
public sealed record TimerUndefined(string Name) : RenderProblem;
