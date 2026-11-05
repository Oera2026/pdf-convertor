using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;

namespace PDFile.App;

public sealed partial class Icon : UserControl
{
    // WinUI 3 cannot parse path mini-language into PathGeometry.Figures, so icons are stored as strings
    // (see Icons.xaml) and converted to a Geometry here.
    public static readonly DependencyProperty DataProperty =
        DependencyProperty.Register(nameof(Data), typeof(string), typeof(Icon),
            new PropertyMetadata(null, (d, e) => ((Icon)d).Shape.Data = e.NewValue is string s
                ? (Geometry)XamlBindingHelper.ConvertValue(typeof(Geometry), s)
                : null));

    public Icon() => InitializeComponent();

    public string? Data
    {
        get => (string?)GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }
}
