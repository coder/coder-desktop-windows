namespace Coder.Desktop.App.Converters;

/// <summary>
/// Utility for human-readable byte size formatting.
/// </summary>
public static class FriendlyByteConverter
{
    private static readonly string[] Suffixes = ["B", "KB", "MB", "GB", "TB", "PB", "EB"];

    public static string FriendlyBytes(ulong bytes)
    {
        if (bytes == 0)
            return $"0 {Suffixes[0]}";

        var place = Convert.ToInt32(Math.Floor(Math.Log(bytes, 1024)));
        var num = Math.Round(bytes / Math.Pow(1024, place), 1);
        return $"{num} {Suffixes[place]}";
    }
}
