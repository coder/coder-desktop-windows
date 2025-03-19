using System;
using Coder.Desktop.App.Converters;
using Coder.Desktop.MutagenSdk.Proto.Synchronization;
using Coder.Desktop.MutagenSdk.Proto.Url;

namespace Coder.Desktop.App.Models;

// This is a much slimmer enum than the original enum from Mutagen and only
// contains the overarching states that we care about from a code perspective.
// We still store the original state in the model for rendering purposes.
public enum MutagenSessionStatus
{
    Unknown,
    Paused,
    Error,
    NeedsAttention,
    Working,
    Ok,
}

public sealed class MutagenSessionModelEndpointSize
{
    public ulong SizeBytes { get; init; }
    public ulong FileCount { get; init; }
    public ulong DirCount { get; init; }
    public ulong SymlinkCount { get; init; }

    public string Description(string linePrefix)
    {
        var str =
            $"{linePrefix}{FriendlyByteConverter.FriendlyBytes(SizeBytes)}\n" +
            $"{linePrefix}{FileCount:N0} files\n" +
            $"{linePrefix}{DirCount:N0} directories";
        if (SymlinkCount > 0) str += $"\n{linePrefix} {SymlinkCount:N0} symlinks";

        return str;
    }

    public bool Equals(MutagenSessionModelEndpointSize other)
    {
        return SizeBytes == other.SizeBytes &&
               FileCount == other.FileCount &&
               DirCount == other.DirCount &&
               SymlinkCount == other.SymlinkCount;
    }
}

public class MutagenSessionModel
{
    public readonly string Identifier;
    public readonly string Name;

    public readonly string LocalPath = "Unknown";
    public readonly string RemoteName = "unknown";
    public readonly string RemotePath = "Unknown";

    public readonly MutagenSessionStatus Status;
    public readonly string StatusString;
    public readonly string StatusDescription;

    public readonly MutagenSessionModelEndpointSize MaxSize;
    public readonly MutagenSessionModelEndpointSize LocalSize;
    public readonly MutagenSessionModelEndpointSize RemoteSize;

    public readonly string[] Errors = [];

    public string StatusDetails
    {
        get
        {
            var str = $"{StatusString} ({Status})\n\n{StatusDescription}";
            foreach (var err in Errors) str += $"\n\n{err}";
            return str;
        }
    }

    public string SizeDetails
    {
        get
        {
            var str = "";
            if (!LocalSize.Equals(RemoteSize)) str = "Maximum:\n" + MaxSize.Description("  ") + "\n\n";

            str += "Local:\n" + LocalSize.Description("  ") + "\n\n" +
                   "Remote:\n" + RemoteSize.Description("  ");
            return str;
        }
    }

    // TODO: remove once we process sessions from the mutagen RPC
    public MutagenSessionModel(string localPath, string remoteName, string remotePath, MutagenSessionStatus status,
        string statusString, string statusDescription, string[] errors)
    {
        Identifier = "TODO";
        Name = "TODO";

        LocalPath = localPath;
        RemoteName = remoteName;
        RemotePath = remotePath;
        Status = status;
        StatusString = statusString;
        StatusDescription = statusDescription;
        LocalSize = new MutagenSessionModelEndpointSize
        {
            SizeBytes = (ulong)new Random().Next(0, 1000000000),
            FileCount = (ulong)new Random().Next(0, 10000),
            DirCount = (ulong)new Random().Next(0, 10000),
        };
        RemoteSize = new MutagenSessionModelEndpointSize
        {
            SizeBytes = (ulong)new Random().Next(0, 1000000000),
            FileCount = (ulong)new Random().Next(0, 10000),
            DirCount = (ulong)new Random().Next(0, 10000),
        };
        MaxSize = new MutagenSessionModelEndpointSize
        {
            SizeBytes = ulong.Max(LocalSize.SizeBytes, RemoteSize.SizeBytes),
            FileCount = ulong.Max(LocalSize.FileCount, RemoteSize.FileCount),
            DirCount = ulong.Max(LocalSize.DirCount, RemoteSize.DirCount),
            SymlinkCount = ulong.Max(LocalSize.SymlinkCount, RemoteSize.SymlinkCount),
        };

        Errors = errors;
    }

    public MutagenSessionModel(State state)
    {
        Identifier = state.Session.Identifier;
        Name = state.Session.Name;

        // If the protocol isn't what we expect for alpha or beta, show
        // "unknown".
        if (state.Session.Alpha.Protocol == Protocol.Local && !string.IsNullOrWhiteSpace(state.Session.Alpha.Path))
            LocalPath = state.Session.Alpha.Path;
        if (state.Session.Beta.Protocol == Protocol.Ssh)
        {
            if (string.IsNullOrWhiteSpace(state.Session.Beta.Host))
            {
                var name = state.Session.Beta.Host;
                // TODO: this will need to be compatible with custom hostname
                //       suffixes
                if (name.EndsWith(".coder")) name = name[..^6];
                RemoteName = name;
            }

            if (string.IsNullOrWhiteSpace(state.Session.Beta.Path)) RemotePath = state.Session.Beta.Path;
        }

        if (state.Session.Paused)
        {
            // Disregard any status if it's paused.
            Status = MutagenSessionStatus.Paused;
            StatusString = "Paused";
            StatusDescription = "The session is paused.";
        }
        else
        {
            Status = MutagenSessionModelUtils.StatusFromProtoStatus(state.Status);
            StatusString = MutagenSessionModelUtils.ProtoStatusToDisplayString(state.Status);
            StatusDescription = MutagenSessionModelUtils.ProtoStatusToDescription(state.Status);
        }

        // If there are any conflicts, set the status to NeedsAttention.
        if (state.Conflicts.Count > 0 && Status > MutagenSessionStatus.NeedsAttention)
        {
            Status = MutagenSessionStatus.NeedsAttention;
            StatusString = "Conflicts";
            StatusDescription = "The session has conflicts that need to be resolved.";
        }

        LocalSize = new MutagenSessionModelEndpointSize
        {
            SizeBytes = state.AlphaState.TotalFileSize,
            FileCount = state.AlphaState.Files,
            DirCount = state.AlphaState.Directories,
            SymlinkCount = state.AlphaState.SymbolicLinks,
        };
        RemoteSize = new MutagenSessionModelEndpointSize
        {
            SizeBytes = state.BetaState.TotalFileSize,
            FileCount = state.BetaState.Files,
            DirCount = state.BetaState.Directories,
            SymlinkCount = state.BetaState.SymbolicLinks,
        };
        MaxSize = new MutagenSessionModelEndpointSize
        {
            SizeBytes = ulong.Max(LocalSize.SizeBytes, RemoteSize.SizeBytes),
            FileCount = ulong.Max(LocalSize.FileCount, RemoteSize.FileCount),
            DirCount = ulong.Max(LocalSize.DirCount, RemoteSize.DirCount),
            SymlinkCount = ulong.Max(LocalSize.SymlinkCount, RemoteSize.SymlinkCount),
        };

        // TODO: accumulate errors, there seems to be multiple fields they can
        //       come from
        if (!string.IsNullOrWhiteSpace(state.LastError)) Errors = [state.LastError];
    }
}

public static class MutagenSessionModelUtils
{
    public static MutagenSessionStatus StatusFromProtoStatus(Status protoStatus)
    {
        switch (protoStatus)
        {
            case Status.Disconnected:
            case Status.HaltedOnRootEmptied:
            case Status.HaltedOnRootDeletion:
            case Status.HaltedOnRootTypeChange:
            case Status.WaitingForRescan:
                return MutagenSessionStatus.Error;
            case Status.ConnectingAlpha:
            case Status.ConnectingBeta:
            case Status.Scanning:
            case Status.Reconciling:
            case Status.StagingAlpha:
            case Status.StagingBeta:
            case Status.Transitioning:
            case Status.Saving:
                return MutagenSessionStatus.Working;
            case Status.Watching:
                return MutagenSessionStatus.Ok;
            default:
                return MutagenSessionStatus.Unknown;
        }
    }

    public static string ProtoStatusToDisplayString(Status protoStatus)
    {
        switch (protoStatus)
        {
            case Status.Disconnected:
                return "Disconnected";
            case Status.HaltedOnRootEmptied:
                return "Halted on root emptied";
            case Status.HaltedOnRootDeletion:
                return "Halted on root deletion";
            case Status.HaltedOnRootTypeChange:
                return "Halted on root type change";
            case Status.ConnectingAlpha:
                // This string was changed from "alpha" to "local".
                return "Connecting (local)";
            case Status.ConnectingBeta:
                // This string was changed from "beta" to "remote".
                return "Connecting (remote)";
            case Status.Watching:
                return "Watching";
            case Status.Scanning:
                return "Scanning";
            case Status.WaitingForRescan:
                return "Waiting for rescan";
            case Status.Reconciling:
                return "Reconciling";
            case Status.StagingAlpha:
                // This string was changed from "alpha" to "local".
                return "Staging (local)";
            case Status.StagingBeta:
                // This string was changed from "beta" to "remote".
                return "Staging (remote)";
            case Status.Transitioning:
                return "Transitioning";
            case Status.Saving:
                return "Saving";
            default:
                return protoStatus.ToString();
        }
    }

    public static string ProtoStatusToDescription(Status protoStatus)
    {
        // These descriptions were mostly taken from the protobuf.
        switch (protoStatus)
        {
            case Status.Disconnected:
                return "The session is unpaused but not currently connected or connecting to either endpoint.";
            case Status.HaltedOnRootEmptied:
                return "The session is halted due to the root emptying safety check.";
            case Status.HaltedOnRootDeletion:
                return "The session is halted due to the root deletion safety check.";
            case Status.HaltedOnRootTypeChange:
                return "The session is halted due to the root type change safety check.";
            case Status.ConnectingAlpha:
                // This string was changed from "alpha" to "local".
                return "The session is attempting to connect to the local endpoint.";
            case Status.ConnectingBeta:
                // This string was changed from "beta" to "remote".
                return "The session is attempting to connect to the remote endpoint.";
            case Status.Watching:
                return "The session is watching for filesystem changes.";
            case Status.Scanning:
                return "The session is scanning the filesystem on each endpoint.";
            case Status.WaitingForRescan:
                return
                    "The session is waiting to retry scanning after an error during the previous scanning operation.";
            case Status.Reconciling:
                return "The session is performing reconciliation.";
            case Status.StagingAlpha:
                // This string was changed from "on alpha" to "locally".
                return "The session is staging files locally.";
            case Status.StagingBeta:
                // This string was changed from "beta" to "the remote".
                return "The session is staging files on the remote.";
            case Status.Transitioning:
                return "The session is performing transition operations on each endpoint.";
            case Status.Saving:
                return "The session is recording synchronization history to disk.";
            default:
                return "Unknown status message.";
        }
    }
}
