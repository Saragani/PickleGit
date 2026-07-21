using System.Windows;
using System.Windows.Media;

namespace PickleGit.Controls
{
    /// <summary>Attached property so ToolbarButton's single shared ControlTemplate can look up a
    /// per-style hover background. Without this, every style based on ToolbarButton (including
    /// AccentButton/DangerButton/SuccessButton) hovers to the same generic SurfaceAltBrush —
    /// fine for a plain transparent button, but on an already-colored button it replaces the
    /// color entirely, and in Light theme SurfaceAltBrush is pale enough that the button's own
    /// white text becomes white-on-near-white while hovering.</summary>
    public static class ButtonChrome
    {
        public static readonly DependencyProperty HoverBackgroundProperty =
            DependencyProperty.RegisterAttached(
                "HoverBackground", typeof(Brush), typeof(ButtonChrome), new PropertyMetadata(null));

        public static Brush GetHoverBackground(DependencyObject obj) => (Brush)obj.GetValue(HoverBackgroundProperty);
        public static void SetHoverBackground(DependencyObject obj, Brush value) => obj.SetValue(HoverBackgroundProperty, value);
    }
}
