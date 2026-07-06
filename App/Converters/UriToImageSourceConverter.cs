using System;
using System.Linq;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Coder.Desktop.App.Converters;

/// <summary>
///     Converts an icon Uri (provided by UI-agnostic ViewModels) into a WinUI
///     ImageSource. SVG URLs get an SvgImageSource, everything else gets a
///     BitmapImage.
/// </summary>
public class UriToImageSourceConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, string language)
    {
        if (value is not Uri uri)
            return null;

        // TODO: this is definitely a hack, URLs should not need to end in .svg
        var ext = uri.AbsolutePath.Split('/').LastOrDefault()?.Split('.').LastOrDefault();
        if (ext is "svg")
            return new SvgImageSource(uri);

        return new BitmapImage(uri);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, string language)
    {
        throw new NotImplementedException();
    }
}
