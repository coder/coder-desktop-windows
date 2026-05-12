using System.Diagnostics;
using System.Text.Json;

namespace Coder.Desktop.App.Services;

/// <summary>
/// Credential backend using libsecret's secret-tool CLI.
/// Works on GNOME, KDE, and other desktop environments with Secret Service D-Bus API.
/// </summary>
public class LinuxSecretServiceBackend : ICredentialBackend
{
    private const string SecretToolBinary = "secret-tool";
    private const string AttributeKey = "application";
    private const string AttributeValue = "coder-desktop";
    private const string Label = "Coder Desktop Credentials";

    public async Task<RawCredentials?> ReadCredentials(CancellationToken ct = default)
    {
        try
        {
            var result = await RunProcess(SecretToolBinary,
                ["lookup", AttributeKey, AttributeValue], null, ct);
            if (string.IsNullOrWhiteSpace(result))
                return null;
            return JsonSerializer.Deserialize<RawCredentials>(result);
        }
        catch
        {
            // secret-tool not found or returned error — no credentials stored
            return null;
        }
    }

    public async Task WriteCredentials(RawCredentials credentials, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(credentials);
        await RunProcess(SecretToolBinary,
            ["store", "--label=" + Label, AttributeKey, AttributeValue],
            json, ct);
    }

    public async Task DeleteCredentials(CancellationToken ct = default)
    {
        try
        {
            await RunProcess(SecretToolBinary,
                ["clear", AttributeKey, AttributeValue], null, ct);
        }
        catch
        {
            // Ignore errors — credential may not exist
        }
    }

    private static async Task<string> RunProcess(string fileName, string[] args, string? stdin,
        CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdin != null,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi) ??
            throw new InvalidOperationException($"Failed to start {fileName}");

        if (stdin != null)
        {
            await process.StandardInput.WriteAsync(stdin);
            process.StandardInput.Close();
        }

        var output = await process.StandardOutput.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        if (process.ExitCode != 0)
        {
            var error = await process.StandardError.ReadToEndAsync(ct);
            throw new InvalidOperationException(
                $"{fileName} exited with code {process.ExitCode}: {error}");
        }

        return output.Trim();
    }
}
