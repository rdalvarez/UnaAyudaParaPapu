using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;

namespace PapaPersonas.App.Ui;

public static class StatusBrushes
{
    public static readonly Brush Success = new SolidColorBrush(Color.FromRgb(0x1F, 0x6F, 0x43));
    public static readonly Brush Error = new SolidColorBrush(Color.FromRgb(0xB8, 0x00, 0x00));
    public static readonly Brush Info = new SolidColorBrush(Color.FromRgb(0x1E, 0x40, 0x8A));
}
