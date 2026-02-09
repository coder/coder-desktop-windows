using System;
using System.Collections.Generic;
using System.Diagnostics;
using Coder.Desktop.App.Converters;
using Coder.Desktop.Vpn.Proto;

namespace Coder.Desktop.App.Models;

public enum RpcLifecycle
{
    Disconnected,
    Connecting,
    Connected,
}

public enum VpnLifecycle
{
    Unknown,
    Stopped,
    Starting,
    Started,
    Stopping,
}

public enum VpnStartupStage
{
    Unknown,
    Initializing,
    Downloading,
    Finalizing,
}

public class VpnDownloadProgress
{
    public ulong BytesWritten { get; set; } = 0;
    public ulong? BytesTotal { get; set; } = null; // null means unknown total size

    public double Progress
    {
        get
        {
            if (BytesTotal is > 0)
            {
                return (double)BytesWritten / BytesTotal.Value;
            }
            return 0.0;
        }
    }

    public override string ToString()
    {
        // TODO: it would be nice if the two suffixes could match
        var s = FriendlyByteConverter.FriendlyBytes(BytesWritten);
        if (BytesTotal != null)
            s += $" of {FriendlyByteConverter.FriendlyBytes(BytesTotal.Value)}";
        else
            s += " of unknown";
        if (BytesTotal != null)
            s += $" ({Progress:0%})";
        return s;
    }

    public VpnDownloadProgress Clone()
    {
        return new VpnDownloadProgress
        {
            BytesWritten = BytesWritten,
            BytesTotal = BytesTotal,
        };
    }

    public static VpnDownloadProgress FromProto(StartProgressDownloadProgress proto)
    {
        return new VpnDownloadProgress
        {
            BytesWritten = proto.BytesWritten,
            BytesTotal = proto.HasBytesTotal ? proto.BytesTotal : null,
        };
    }
}

public class VpnStartupProgress
{
    public const string DefaultStartProgressMessage = "Starting Coder Connect...";

    // Scale the download progress to an overall progress value between these
    // numbers.
    private const double DownloadProgressMin = 0.05;
    private const double DownloadProgressMax = 0.80;

    public VpnStartupStage Stage { get; init; } = VpnStartupStage.Unknown;
    public VpnDownloadProgress? DownloadProgress { get; init; } = null;

    // 0.0 to 1.0
    public double Progress
    {
        get
        {
            switch (Stage)
            {
                case VpnStartupStage.Unknown:
                case VpnStartupStage.Initializing:
                    return 0.0;
                case VpnStartupStage.Downloading:
                    var progress = DownloadProgress?.Progress ?? 0.0;
                    return DownloadProgressMin + (DownloadProgressMax - DownloadProgressMin) * progress;
                case VpnStartupStage.Finalizing:
                    return DownloadProgressMax;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }

    public override string ToString()
    {
        switch (Stage)
        {
            case VpnStartupStage.Unknown:
            case VpnStartupStage.Initializing:
                return DefaultStartProgressMessage;
            case VpnStartupStage.Downloading:
                var s = "Downloading Coder Connect binary...";
                if (DownloadProgress is not null)
                {
                    s += "\n" + DownloadProgress;
                }

                return s;
            case VpnStartupStage.Finalizing:
                return "Finalizing Coder Connect startup...";
            default:
                throw new ArgumentOutOfRangeException();
        }
    }

    public VpnStartupProgress Clone()
    {
        return new VpnStartupProgress
        {
            Stage = Stage,
            DownloadProgress = DownloadProgress?.Clone(),
        };
    }

    public static VpnStartupProgress FromProto(StartProgress proto)
    {
        return new VpnStartupProgress
        {
            Stage = proto.Stage switch
            {
                StartProgressStage.Initializing => VpnStartupStage.Initializing,
                StartProgressStage.Downloading => VpnStartupStage.Downloading,
                StartProgressStage.Finalizing => VpnStartupStage.Finalizing,
                _ => VpnStartupStage.Unknown,
            },
            DownloadProgress = proto.Stage is StartProgressStage.Downloading ?
                VpnDownloadProgress.FromProto(proto.DownloadProgress) :
                null,
        };
    }
}

public class RpcModel
{
    public RpcLifecycle RpcLifecycle { get; set; } = RpcLifecycle.Disconnected;

    private VpnLifecycle _vpnLifecycle;
    public VpnLifecycle VpnLifecycle
    {
        get => _vpnLifecycle;
        set
        {
            if (_vpnLifecycle != value && value == VpnLifecycle.Starting)
                // Reset the startup progress when the VPN lifecycle changes to
                // Starting.
                _vpnStartupProgress = null;
            _vpnLifecycle = value;
        }
    }

    // Nullable because it is only set when the VpnLifecycle is Starting
    private VpnStartupProgress? _vpnStartupProgress;
    public VpnStartupProgress? VpnStartupProgress
    {
        get => VpnLifecycle is VpnLifecycle.Starting ? _vpnStartupProgress ?? new VpnStartupProgress() : null;
        set => _vpnStartupProgress = value;
    }

    public IReadOnlyList<Workspace> Workspaces { get; set; } = [];

    public IReadOnlyList<Agent> Agents { get; set; } = [];

    public RpcModel Clone()
    {
        return new RpcModel
        {
            RpcLifecycle = RpcLifecycle,
            VpnLifecycle = VpnLifecycle,
            VpnStartupProgress = VpnStartupProgress?.Clone(),
            Workspaces = Workspaces,
            Agents = Agents,
        };
    }
}
