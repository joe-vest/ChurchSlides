using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using ChurchSlides.Domain.Text;

namespace ChurchSlides.Spike;

/// <summary>The model as JSON, for the live view and for comparing models (records holding arrays don't compare by value).</summary>
public static class ModelJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() },
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
    };

    public static string Of(RichText text) => JsonSerializer.Serialize(text, Options);

    public static RichText? Parse(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<RichText>(json, Options);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

/// <summary>
/// The Rich text tab (S3): the RichTextBox editor and the FormattedText renderer side by side at
/// the same size over a background, an overlay flick, the live model as JSON, and a few
/// formatting commands. The automatic run (RichTextTests) drives this editor.
/// </summary>
public sealed class RichTextPanel : DockPanel
{
    /// <summary>The editor canvas, 16:10 like the church's 1440×900 output.</summary>
    public const double CanvasWidth = 560;

    public const double CanvasHeight = 350;

    private static readonly string[] FontChoices = ["Segoe UI", "Georgia", "Times New Roman", "Arial", "Consolas"];
    private static readonly string[] ColorChoices = ["#FFFFFF", "#FFD54F", "#90CAF9", "#EF9A9A"];

    private readonly ComboBox sampleBox = new() { Width = 300, ItemsSource = RichTextSamples.All };
    private readonly RadioButton containerChips = new() { Content = "Container", GroupName = "Chips", Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly RadioButton runChips = new() { Content = "Tagged run", GroupName = "Chips", IsChecked = true, Margin = new Thickness(8, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly ToggleButton flick = new() { Content = "Flick: renderer in the editor's place", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(16, 0, 0, 0) };
    private readonly CheckBox showValues = new() { Content = "Renderer shows token values", Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
    private readonly Grid editorCanvas;
    private readonly Grid rendererCanvas;
    private readonly Border editorBox;
    private readonly Border rendererBox;
    private readonly Image overlayImage = new() { IsHitTestVisible = false, Visibility = Visibility.Collapsed, Stretch = Stretch.None };
    private readonly Image rendererImage = new() { Stretch = Stretch.None };
    private readonly TextBox jsonBox = new()
    {
        IsReadOnly = true,
        FontFamily = new FontFamily("Consolas"),
        Height = 220,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
    };

    private readonly ComboBox pasteModeBox = new()
    {
        Width = 250,
        Margin = new Thickness(8, 0, 0, 0),
        ItemsSource = Enum.GetValues<PasteMode>().Select(PasteFilter.Label).ToList(),
        SelectedIndex = 0,
        ToolTip = "What a paste from another program keeps",
    };

    private readonly DispatcherTimer pause;
    private readonly SnapshotUndo undo = new();
    private BoxStyle style = RichTextSamples.All[0].Style;

    public RichTextPanel()
    {
        Margin = new Thickness(8);
        Editor = new RichTextBox
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            CaretBrush = Brushes.White,
            AcceptsReturn = true,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, // Hidden, not Disabled: ExtentHeight is then the text height
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            IsUndoEnabled = false, // D-2: the app's snapshot stack instead
            AllowDrop = false, // drag and drop would bypass the paste filter
        };
        TokenChips.Guard(Editor);
        Editor.CommandBindings.Add(new CommandBinding(ApplicationCommands.Undo, (_, _) => Undo(), (_, e) => e.CanExecute = true));
        Editor.CommandBindings.Add(new CommandBinding(ApplicationCommands.Redo, (_, _) => Redo(), (_, e) => e.CanExecute = true));
        DataObject.AddCopyingHandler(Editor, OnCopying);
        DataObject.AddPastingHandler(Editor, OnPasting);
        TextOptions.SetTextRenderingMode(Editor, TextRenderingMode.Grayscale);
        TextOptions.SetTextFormattingMode(Editor, TextFormattingMode.Ideal);

        (editorCanvas, editorBox) = NewCanvas(new Grid { Children = { Editor, overlayImage } });
        (rendererCanvas, rendererBox) = NewCanvas(rendererImage);

        pause = new DispatcherTimer(TimeSpan.FromMilliseconds(300), DispatcherPriority.Background, (_, _) => OnPause(), Dispatcher);
        pause.Stop();
        Editor.TextChanged += (_, _) =>
        {
            AlignVertically();
            pause.Stop();
            pause.Start();
        };

        var top = Row(new TextBlock { Text = "Sample:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) }, sampleBox,
            new TextBlock { Text = "Token chips:", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(16, 0, 0, 0) }, containerChips, runChips);
        RunButton = new Button { Content = "Run rich text test", Padding = new Thickness(10, 4, 10, 4), Margin = new Thickness(16, 0, 0, 0) };
        top.Children.Add(RunButton);

        var tools = Row(
            Command("Bold", () => ToggleBold()),
            Command("Italic", () => ToggleItalic()));
        foreach (var hex in ColorChoices)
        {
            var swatch = new Button { Focusable = false, Width = 22, Height = 22, Margin = new Thickness(4, 0, 0, 0), Background = FlowConverter.BrushOf(hex), ToolTip = hex };
            swatch.Click += (_, _) => ApplyToSelection(TextElement.ForegroundProperty, FlowConverter.BrushOf(hex));
            tools.Children.Add(swatch);
        }
        var fontBox = new ComboBox { ItemsSource = FontChoices, Width = 130, Margin = new Thickness(8, 0, 0, 0), ToolTip = "Font" };
        fontBox.SelectionChanged += (_, _) =>
        {
            if (fontBox.SelectedItem is string family)
                ApplyToSelection(TextElement.FontFamilyProperty, new FontFamily(family));
        };
        tools.Children.Add(fontBox);
        tools.Children.Add(Command("Size −", () => StepSize(-0.005)));
        tools.Children.Add(Command("Size +", () => StepSize(0.005)));
        tools.Children.Add(Command("Clear formatting", () => ClearFormatting()));
        var insert = new Button { Content = "Insert token ▾", Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(8, 0, 0, 0), Focusable = false };
        var menu = new ContextMenu { PlacementTarget = insert };
        foreach (var (label, markup) in TokenChips.Menu)
        {
            var item = new MenuItem { Header = label };
            item.Click += (_, _) => InsertToken(RichTextSamples.Tok(markup).Token);
            menu.Items.Add(item);
        }
        insert.Click += (_, _) => menu.IsOpen = true;
        tools.Children.Add(insert);
        tools.Children.Add(flick);
        tools.Children.Add(showValues);
        tools.Margin = new Thickness(0, 6, 0, 6);

        PasteNote = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0), TextWrapping = TextWrapping.Wrap };
        var pasteRow = Row(new TextBlock { Text = "Paste from other programs:", VerticalAlignment = VerticalAlignment.Center }, pasteModeBox, PasteNote);
        pasteRow.Margin = new Thickness(0, 0, 0, 6);

        var canvases = new Grid { Margin = new Thickness(0, 4, 0, 0) };
        canvases.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        canvases.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        canvases.Children.Add(Labelled("Editor (RichTextBox)", editorCanvas));
        var right = Labelled("Renderer (FormattedText)", rendererCanvas);
        right.Margin = new Thickness(12, 0, 0, 0);
        Grid.SetColumn(right, 1);
        canvases.Children.Add(right);

        ResultText = new TextBlock { FontFamily = new FontFamily("Consolas"), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        ExtraPanel = new StackPanel();

        var body = new StackPanel();
        body.Children.Add(top);
        body.Children.Add(tools);
        body.Children.Add(pasteRow);
        body.Children.Add(canvases);
        body.Children.Add(new TextBlock { Text = "Current model (saved form, updated on typing pauses):", Margin = new Thickness(0, 8, 0, 2) });
        body.Children.Add(jsonBox);
        body.Children.Add(ResultText);
        body.Children.Add(ExtraPanel);
        Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });

        sampleBox.SelectionChanged += (_, _) => LoadSelectedSample();
        containerChips.Checked += (_, _) => LoadSelectedSample();
        runChips.Checked += (_, _) => LoadSelectedSample();
        flick.Checked += (_, _) => SetFlick(true);
        flick.Unchecked += (_, _) => SetFlick(false);
        showValues.Checked += (_, _) => RefreshRenderer();
        showValues.Unchecked += (_, _) => RefreshRenderer();
        sampleBox.SelectedIndex = 0;
    }

    public RichTextBox Editor { get; }
    public Button RunButton { get; }
    public TextBlock ResultText { get; }

    /// <summary>What the last paste kept and dropped.</summary>
    private TextBlock PasteNote { get; }

    public PasteMode PasteMode
    {
        get => (PasteMode)pasteModeBox.SelectedIndex;
    }

    /// <summary>Room below the results for the checklist (C0.3b).</summary>
    public StackPanel ExtraPanel { get; }

    public ChipDesign Chips
    {
        get => containerChips.IsChecked == true ? ChipDesign.Container : ChipDesign.TaggedRun;
        set => (value == ChipDesign.Container ? containerChips : runChips).IsChecked = true;
    }

    public Size BoxSize => new(Math.Round(style.Geometry.Width * CanvasWidth), Math.Round(style.Geometry.Height * CanvasHeight));

    public RichText Model => FlowConverter.ToModel(Editor.Document, CanvasHeight);

    /// <summary>Sets a church background behind both canvases, or a plain dark one.</summary>
    public void SetBackground(string? imagePath)
    {
        Brush brush = new SolidColorBrush(Color.FromRgb(0x1B, 0x2A, 0x41));
        if (imagePath is not null)
        {
            try
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.UriSource = new Uri(imagePath);
                image.DecodePixelHeight = (int)CanvasHeight;
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.EndInit();
                image.Freeze();
                brush = new ImageBrush(image) { Stretch = Stretch.UniformToFill };
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException)
            {
                // keep the plain background
            }
        }
        brush.Freeze();
        editorCanvas.Background = rendererCanvas.Background = brush;
    }

    /// <summary>Loads a document into the editor with the box's style, and redraws the renderer side.</summary>
    public void Load(RichText text, BoxStyle boxStyle)
    {
        style = boxStyle;
        foreach (var box in new[] { editorBox, rendererBox })
        {
            box.Width = BoxSize.Width;
            box.Height = BoxSize.Height;
            box.Margin = new Thickness(Math.Round(style.Geometry.X * CanvasWidth), Math.Round(style.Geometry.Y * CanvasHeight), 0, 0);
        }
        Editor.Document = FlowConverter.ToDocument(text, style, CanvasHeight, Chips);
        AlignVertically();
        undo.Reset(Model);
        OnPause();
    }

    // ---- Undo (D-2), copy, paste and tokens: model edits reloaded into the editor ----

    /// <summary>The selection as plain-text offsets, widened to whole tokens.</summary>
    public (int Start, int End) SelectionOffsets()
    {
        var (start, end) = TokenChips.ExpandToTokens(Editor.Selection);
        return (EditorText.Offset(Editor.Document, start), EditorText.Offset(Editor.Document, end));
    }

    /// <summary>Loads a model in place of the current one (same box) and selects by offset.</summary>
    public void Restore(RichText text, int selectionStart, int selectionEnd)
    {
        pause.Stop();
        Editor.Document = FlowConverter.ToDocument(text, style, CanvasHeight, Chips);
        AlignVertically();
        Editor.Selection.Select(EditorText.PointerAt(Editor.Document, selectionStart), EditorText.PointerAt(Editor.Document, selectionEnd));
        OnPause();
        Editor.Focus();
    }

    /// <summary>Replaces the selection with a fragment and puts the caret after it.</summary>
    public void ReplaceSelection(RichText fragment)
    {
        FlushPause();
        var (start, end) = SelectionOffsets();
        var text = ModelEdit.Splice(Model, start, end, fragment);
        var caret = start + ModelEdit.Length(fragment);
        Restore(text, caret, caret);
    }

    /// <summary>Inserts a token at the selection. A Timer token only goes in an empty box (TMR-009).</summary>
    public bool InsertToken(Token token)
    {
        if (token is TimerToken && !TokenChips.TimerAllowedIn(Model))
        {
            PasteNote.Text = "A Timer token goes in an empty Text Box on its own (TMR-009).";
            return false;
        }
        ReplaceSelection(new RichText { Paragraphs = [new ChurchSlides.Domain.Text.Paragraph { Inlines = [new TokenRun { Token = token }] }] });
        return true;
    }

    /// <summary>Takes the pending typing-pause snapshot now.</summary>
    public void FlushPause()
    {
        if (pause.IsEnabled)
            OnPause();
    }

    public void Undo()
    {
        FlushPause();
        if (undo.Undo() is { } snapshot)
            Restore(snapshot.Text, snapshot.SelectionStart, snapshot.SelectionEnd);
    }

    public void Redo()
    {
        FlushPause();
        if (undo.Redo() is { } snapshot)
            Restore(snapshot.Text, snapshot.SelectionStart, snapshot.SelectionEnd);
    }

    /// <summary>Our copies carry the model fragment, so a paste here restores tokens and formatting exactly.</summary>
    private void OnCopying(object sender, DataObjectCopyingEventArgs e)
    {
        if (e.IsDragDrop)
            return;
        var (start, end) = SelectionOffsets();
        var fragment = ModelEdit.Slice(Model, start, end);
        e.DataObject.SetData(PasteFilter.ModelFormat, ModelJson.Of(fragment));
        e.DataObject.SetData(DataFormats.UnicodeText, fragment.ToPlainText().Replace("\n", Environment.NewLine, StringComparison.Ordinal));
    }

    /// <summary>Every paste goes through the filter (C0.3 decision 3) instead of the control's own paste.</summary>
    private void OnPasting(object sender, DataObjectPastingEventArgs e)
    {
        e.CancelCommand();
        var result = PasteFilter.Read(e.DataObject, PasteMode, style, CanvasHeight);
        PasteNote.Text = $"Last paste: {result.Note}.";
        // Not now: the control is inside its own change block and won't let the document be replaced.
        Dispatcher.BeginInvoke(() => ReplaceSelection(result.Fragment));
    }

    /// <summary>
    /// The editor can't align text vertically itself, so the document's top padding does it, from
    /// the same rule the renderer uses (SLD-023).
    /// </summary>
    public void AlignVertically()
    {
        var doc = Editor.Document;
        var current = doc.PagePadding.Top;
        Editor.UpdateLayout();
        var textHeight = Editor.ExtentHeight - current;
        var wanted = Math.Round(RichTextRenderer.VerticalOffset(style.Defaults.VAlign, BoxSize.Height, textHeight), 2);
        if (Math.Abs(wanted - current) > 0.01)
            doc.PagePadding = new Thickness(0, wanted, 0, 0);
    }

    /// <summary>The renderer's drawing of a model at box size, on transparent.</summary>
    public BitmapSource RenderBox(RichText text, Func<Token, string> tokenText, out TextLayout layout)
    {
        layout = RichTextRenderer.Layout(text, style, BoxSize, CanvasHeight, tokenText);
        var visual = EditorText.NewVisual();
        using (var dc = visual.RenderOpen())
            RichTextRenderer.Draw(dc, layout, default);
        return EditorText.Rasterize(visual, (int)BoxSize.Width, (int)BoxSize.Height);
    }

    /// <summary>The editor's drawing at box size, on transparent.</summary>
    public BitmapSource RasterizeEditor()
    {
        Editor.UpdateLayout();
        return EditorText.Rasterize(Editor, (int)BoxSize.Width, (int)BoxSize.Height);
    }

    // ---- Formatting commands (the paths a user takes) ----

    /// <summary>Applies a property to the selection, widened to whole tokens.</summary>
    public void ApplyToSelection(DependencyProperty property, object value)
    {
        var (start, end) = TokenChips.ExpandToTokens(Editor.Selection);
        Editor.Selection.Select(start, end);
        Editor.Selection.ApplyPropertyValue(property, value);

        // The control leaves container chips out of range formatting, so set them directly.
        foreach (var chip in FlowConverter.AllParagraphs(Editor.Document.Blocks)
                     .SelectMany(p => EditorText.Leaves(p.Inlines)).OfType<InlineUIContainer>()
                     .Where(c => start.CompareTo(c.ElementStart) <= 0 && end.CompareTo(c.ElementEnd) >= 0))
            chip.SetValue(property, value);
        Editor.Focus();
    }

    /// <summary>
    /// Removes the five formatting properties from the selection (TPL-025). Not ClearAllProperties:
    /// that also clears the chip shading, after which the control merges a tagged-run token into
    /// the text next to it and the token is lost.
    /// </summary>
    public void ClearFormatting()
    {
        var (start, end) = TokenChips.ExpandToTokens(Editor.Selection);
        Editor.Selection.Select(start, end);
        var range = new TextRange(start, end);
        var doc = Editor.Document;
        foreach (var property in TokenChips.FormattingProperties)
        {
            // Splits runs at the range's edges, so the clearing below stays inside the range.
            range.ApplyPropertyValue(property, doc.GetValue(property));
        }
        foreach (var paragraph in FlowConverter.AllParagraphs(doc.Blocks))
        {
            var whole = range.Start.CompareTo(paragraph.ContentStart) <= 0 && range.End.CompareTo(paragraph.ContentEnd) >= 0;
            foreach (var element in Elements(paragraph, whole))
            {
                if (whole || (range.Start.CompareTo(element.ContentStart) <= 0 && range.End.CompareTo(element.ContentEnd) >= 0))
                {
                    foreach (var property in TokenChips.FormattingProperties)
                        element.ClearValue(property);
                }
            }
        }
        Editor.Focus();
    }

    /// <summary>The paragraph (when wholly selected) and every inline in it, Spans included.</summary>
    private static IEnumerable<TextElement> Elements(System.Windows.Documents.Paragraph paragraph, bool includeParagraph)
    {
        if (includeParagraph)
            yield return paragraph;
        var stack = new Stack<System.Windows.Documents.Inline>(paragraph.Inlines.Reverse());
        while (stack.Count > 0)
        {
            var inline = stack.Pop();
            yield return inline;
            if (inline is Span span)
            {
                foreach (var child in span.Inlines.Reverse())
                    stack.Push(child);
            }
        }
    }

    private void ToggleBold()
    {
        var isBold = Editor.Selection.GetPropertyValue(TextElement.FontWeightProperty) is FontWeight w && w == FontWeights.Bold;
        ApplyToSelection(TextElement.FontWeightProperty, isBold ? FontWeights.Normal : FontWeights.Bold);
    }

    private void ToggleItalic()
    {
        var isItalic = Editor.Selection.GetPropertyValue(TextElement.FontStyleProperty) is FontStyle s && s == FontStyles.Italic;
        ApplyToSelection(TextElement.FontStyleProperty, isItalic ? FontStyles.Normal : FontStyles.Italic);
    }

    /// <summary>Size in steps of the canvas height (SLD-005). Mixed sizes step from the default.</summary>
    private void StepSize(double step)
    {
        var current = Editor.Selection.GetPropertyValue(TextElement.FontSizeProperty) is double size
            ? size / CanvasHeight
            : style.Defaults.FontSizeRelative;
        var next = Math.Clamp(Math.Round(current + step, 3), 0.01, 0.3);
        ApplyToSelection(TextElement.FontSizeProperty, next * CanvasHeight);
    }

    // ---- Views ----

    private void LoadSelectedSample()
    {
        if (sampleBox.SelectedItem is RichTextSample sample)
            Load(sample.Text, sample.Style);
    }

    private void OnPause()
    {
        pause.Stop();
        var model = Model;
        jsonBox.Text = ModelJson.Of(model);
        RefreshRenderer(model);
        var (start, end) = (EditorText.Offset(Editor.Document, Editor.Selection.Start), EditorText.Offset(Editor.Document, Editor.Selection.End));
        undo.Record(model, start, end);
    }

    private void RefreshRenderer() => RefreshRenderer(Model);

    private void RefreshRenderer(RichText model)
    {
        var bitmap = RenderBox(model, showValues.IsChecked == true ? RichTextRenderer.SampleValue : RichTextRenderer.Markup, out var layout);
        rendererImage.Source = bitmap;
        overlayImage.Source = bitmap;
        var flag = layout.Overflows ? Brushes.OrangeRed : new SolidColorBrush(Color.FromArgb(90, 255, 255, 255));
        editorBox.BorderBrush = rendererBox.BorderBrush = flag; // SLD-028: overflow shows on the box
        editorBox.ToolTip = rendererBox.ToolTip = layout.Overflows ? "Text overflows this Text Box" : null;
    }

    private void SetFlick(bool rendererShown)
    {
        overlayImage.Visibility = rendererShown ? Visibility.Visible : Visibility.Collapsed;
        Editor.Opacity = rendererShown ? 0 : 1;
    }

    private static (Grid Canvas, Border Box) NewCanvas(UIElement content)
    {
        // The box border sits outside the box (negative margin) so it never shifts the text.
        var box = new Border { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Child = content };
        var outline = new Rectangle { StrokeThickness = 1, StrokeDashArray = [4, 3], IsHitTestVisible = false, Margin = new Thickness(-1) };
        outline.SetBinding(Shape.StrokeProperty, new System.Windows.Data.Binding(nameof(Border.BorderBrush)) { Source = box });
        var canvas = new Grid { Width = CanvasWidth, Height = CanvasHeight, ClipToBounds = true };
        canvas.Children.Add(box);
        var frame = new Border { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Child = outline, IsHitTestVisible = false };
        frame.SetBinding(MarginProperty, new System.Windows.Data.Binding(nameof(Margin)) { Source = box });
        frame.SetBinding(WidthProperty, new System.Windows.Data.Binding(nameof(Width)) { Source = box });
        frame.SetBinding(HeightProperty, new System.Windows.Data.Binding(nameof(Height)) { Source = box });
        canvas.Children.Add(frame);
        return (canvas, box);
    }

    private static StackPanel Labelled(string label, UIElement content)
    {
        var panel = new StackPanel();
        panel.Children.Add(new TextBlock { Text = label, Margin = new Thickness(0, 0, 0, 2) });
        panel.Children.Add(content);
        return panel;
    }

    private static StackPanel Row(params UIElement[] children)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal };
        foreach (var child in children)
            row.Children.Add(child);
        return row;
    }

    private static Button Command(string label, Action action)
    {
        var button = new Button { Content = label, Padding = new Thickness(8, 2, 8, 2), Margin = new Thickness(4, 0, 0, 0), Focusable = false };
        button.Click += (_, _) => action();
        return button;
    }
}
