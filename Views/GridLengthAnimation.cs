using System.Windows;
using System.Windows.Media.Animation;

namespace Shellf.Views;

/// <summary>
/// WPF cannot animate GridLength natively; this timeline interpolates pixel-valued
/// GridLengths (used for the sliding notes panel).
/// </summary>
public sealed class GridLengthAnimation : AnimationTimeline
{
    public static readonly DependencyProperty FromProperty = DependencyProperty.Register(
        nameof(From), typeof(GridLength), typeof(GridLengthAnimation), new PropertyMetadata(new GridLength(0)));

    public static readonly DependencyProperty ToProperty = DependencyProperty.Register(
        nameof(To), typeof(GridLength), typeof(GridLengthAnimation), new PropertyMetadata(new GridLength(0)));

    public GridLength From
    {
        get => (GridLength)GetValue(FromProperty);
        set => SetValue(FromProperty, value);
    }

    public GridLength To
    {
        get => (GridLength)GetValue(ToProperty);
        set => SetValue(ToProperty, value);
    }

    public IEasingFunction? Easing { get; set; }

    public override Type TargetPropertyType => typeof(GridLength);

    protected override Freezable CreateInstanceCore() => new GridLengthAnimation();

    public override object GetCurrentValue(
        object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock)
    {
        var progress = animationClock.CurrentProgress ?? 0.0;
        if (Easing is not null)
            progress = Easing.Ease(progress);

        var value = From.Value + (To.Value - From.Value) * progress;
        return new GridLength(Math.Max(0, value), GridUnitType.Pixel);
    }
}
