using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Subverted.App.Presentation;

namespace Subverted.App.Views;

/// <summary>
/// A diff line's text with its changed spans painted behind it. The text stays plain
/// <see cref="TextBlock.Text"/>, so reading, trimming and copying it work as on any text block.
/// </summary>
public sealed class IntralineTextBlock : TextBlock
{
    public static readonly StyledProperty<IReadOnlyList<ChangedSpan>> ChangesProperty =
        AvaloniaProperty.Register<IntralineTextBlock, IReadOnlyList<ChangedSpan>>(
            nameof(Changes),
            []
        );

    /// <summary>What a changed span is painted with; with none, nothing is marked.</summary>
    public static readonly StyledProperty<IBrush?> ChangeBrushProperty = AvaloniaProperty.Register<
        IntralineTextBlock,
        IBrush?
    >(nameof(ChangeBrush));

    static IntralineTextBlock()
    {
        AffectsRender<IntralineTextBlock>(ChangesProperty, ChangeBrushProperty);
    }

    public IReadOnlyList<ChangedSpan> Changes
    {
        get => GetValue(ChangesProperty);
        set => SetValue(ChangesProperty, value);
    }

    public IBrush? ChangeBrush
    {
        get => GetValue(ChangeBrushProperty);
        set => SetValue(ChangeBrushProperty, value);
    }

    protected override void RenderTextLayout(DrawingContext context, Point origin)
    {
        if (ChangeBrush is { } brush)
        {
            var offset = new Vector(origin.X, origin.Y);
            foreach (var span in Changes)
            {
                foreach (var bounds in TextLayout.HitTestTextRange(span.Start, span.Length))
                {
                    context.FillRectangle(brush, bounds.Translate(offset), 2);
                }
            }
        }

        base.RenderTextLayout(context, origin);
    }
}
