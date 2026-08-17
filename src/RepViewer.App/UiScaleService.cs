using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;

namespace RepViewer.App;

internal static class UiScaleService
{
    private static readonly ConditionalWeakTable<Window, ScaleState> States = new();
    public static double Scale { get; private set; } = 1;

    public static void SetScale(double scale)
    {
        Scale = Math.Clamp(scale, 1, 2);
        foreach (Window window in Application.Current.Windows) Apply(window);
    }

    public static void Apply(Window window)
    {
        if (window.Content is not FrameworkElement content) return;
        var state = States.GetValue(window, _ => new ScaleState(content.LayoutTransform));
        var ratio = Scale / state.AppliedScale;
        state.Transform.ScaleX = Scale;
        state.Transform.ScaleY = Scale;
        if (!ReferenceEquals(content.LayoutTransform, state.Transform)) content.LayoutTransform = state.Transform;
        ScaleDimension(window, ratio);
        state.AppliedScale = Scale;
    }

    private static void ScaleDimension(Window window, double ratio)
    {
        if (Math.Abs(ratio - 1) < 0.0001) return;
        if (!double.IsNaN(window.Width)) window.Width *= ratio;
        if (!double.IsNaN(window.Height)) window.Height *= ratio;
        if (window.MinWidth > 0) window.MinWidth *= ratio;
        if (window.MinHeight > 0) window.MinHeight *= ratio;
    }

    private sealed class ScaleState(Transform originalTransform)
    {
        public ScaleTransform Transform { get; } = originalTransform is ScaleTransform existing
            ? existing
            : new ScaleTransform(1, 1);
        public double AppliedScale { get; set; } = 1;
    }
}
