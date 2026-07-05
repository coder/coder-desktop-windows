using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using SkiaSharp;
using Svg.Skia;

namespace Coder.Desktop.App.Controls;

public partial class AgentAppIcon : UserControl
{
    public static readonly StyledProperty<Uri?> IconUrlProperty =
        AvaloniaProperty.Register<AgentAppIcon, Uri?>(nameof(IconUrl));

    private static readonly HttpClient IconHttpClient = new();

    private CancellationTokenSource? _iconLoadCts;

    static AgentAppIcon()
    {
        IconUrlProperty.Changed.AddClassHandler<AgentAppIcon>((x, e) =>
        {
            x.LoadIcon(e.NewValue is Uri uri ? uri : null);
        });
    }

    public Uri? IconUrl
    {
        get => GetValue(IconUrlProperty);
        set => SetValue(IconUrlProperty, value);
    }

    public AgentAppIcon()
    {
        InitializeComponent();

        DetachedFromVisualTree += (_, _) =>
        {
            _iconLoadCts?.Cancel();
            _iconLoadCts?.Dispose();
            _iconLoadCts = null;
            SetImageSource(null);
        };

        LoadIcon(IconUrl);
    }

    private void LoadIcon(Uri? iconUrl)
    {
        _iconLoadCts?.Cancel();
        _iconLoadCts?.Dispose();
        _iconLoadCts = null;

        if (iconUrl is null ||
            (iconUrl.Scheme is not "http" and not "https"))
        {
            ShowFallback();
            return;
        }

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        _iconLoadCts = cts;

        _ = LoadIconCore(iconUrl, cts.Token);
    }

    private async Task LoadIconCore(Uri iconUrl, CancellationToken ct)
    {
        try
        {
            using var response = await IconHttpClient.GetAsync(iconUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var mediaType = response.Content.Headers.ContentType?.MediaType;
            var iconBytes = await response.Content.ReadAsByteArrayAsync(ct);

            if (ct.IsCancellationRequested)
                return;

            using var iconStream = new MemoryStream(iconBytes, writable: false);
            var bitmap = ShouldDecodeAsSvg(iconUrl, mediaType, iconBytes)
                ? DecodeSvgToBitmap(iconStream)
                : new Bitmap(iconStream);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (ct.IsCancellationRequested)
                {
                    bitmap.Dispose();
                    return;
                }

                SetImageSource(bitmap);
                IconImage.IsVisible = true;
                FallbackIcon.IsVisible = false;
            });
        }
        catch
        {
            if (!ct.IsCancellationRequested)
            {
                await Dispatcher.UIThread.InvokeAsync(ShowFallback);
            }
        }
    }

    private static bool ShouldDecodeAsSvg(Uri iconUrl, string? mediaType, byte[] iconBytes)
    {
        if (!string.IsNullOrWhiteSpace(mediaType) &&
            mediaType.Contains("svg", StringComparison.OrdinalIgnoreCase))
            return true;

        if (iconUrl.AbsolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
            return true;

        if (iconBytes.Length == 0)
            return false;

        var probeLen = Math.Min(iconBytes.Length, 256);
        var prefix = Encoding.UTF8.GetString(iconBytes, 0, probeLen).TrimStart('\uFEFF', ' ', '\t', '\r', '\n');
        return prefix.IndexOf("<svg", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static Bitmap DecodeSvgToBitmap(Stream svgStream)
    {
        using var svg = new SKSvg();
        var picture = svg.Load(svgStream);
        if (picture is null)
            throw new InvalidOperationException("Could not parse SVG icon");

        using var pngStream = new MemoryStream();
        if (!svg.Save(pngStream, SKColors.Transparent, SKEncodedImageFormat.Png, 100, 1f, 1f))
            throw new InvalidOperationException("Could not render SVG icon");

        pngStream.Position = 0;
        return new Bitmap(pngStream);
    }

    private void ShowFallback()
    {
        SetImageSource(null);
        IconImage.IsVisible = false;
        FallbackIcon.IsVisible = true;
    }

    private void SetImageSource(IImage? image)
    {
        if (ReferenceEquals(IconImage.Source, image))
            return;

        if (IconImage.Source is IDisposable disposable)
            disposable.Dispose();

        IconImage.Source = image;
    }
}
