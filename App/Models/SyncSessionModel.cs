using System;
using Coder.Desktop.App.Converters;
using Coder.Desktop.MutagenSdk.Proto.Synchronization;
using Coder.Desktop.MutagenSdk.Proto.Url;

namespace Coder.Desktop.App.Models;

// This is a much slimmer enum than the original enum from Mutagen and only
// contains the overarching states that we care about from a code perspective.
// We still store the original state in the model for rendering purposes.
public enum SyncSessionStatusCategory
{
    Unknown,
    Paused,
    Error,
    Conflicts,
    Working,
    Ok,
}

public sealed class SyncSessionModelEndpointSize
{
    public ulong SizeBytes { get; init; }
    public ulong FileCount { get; init; }
    public ulong DirCount { get; init; }
    public ulong SymlinkCount { get; init; }

    public string Description(string linePrefix = "")
    {
        var str =
            $"{linePrefix}{FriendlyByteConverter.FriendlyBytes(SizeBytes)}\n" +
            $"{linePrefix}{FileCount:N0} files\n" +
            $"{linePrefix}{DirCount:N0} directories";
        if (SymlinkCount > 0) str += $"\n{linePrefix} {SymlinkCount:N0} symlinks";

        return str;
    }
}

public class SyncSessionModel
{
    public readonly string Identifier;
    public readonly string Name;

    public readonly string LocalPath = "Unknown";
    public readonly string RemoteName = "Unknown";
    public readonly string RemotePath = "Unknown";

    public readonly SyncSessionStatusCategory StatusCategory;
    public readonly string StatusString;
    public readonly string StatusDescription;

    public readonly SyncSessionModelEndpointSize LocalSize;
    public readonly SyncSessionModelEndpointSize RemoteSize;

    public readonly string[] Errors = [];

    public string StatusDetails
    {
        get
        {
            var str = $"{StatusString} ({StatusCategory})\n\n{StatusDescription}";
            foreach (var err in Errors) str += $"\n\n{err}";
            return str;
        }
    }

    public string SizeDetails
    {
        get
        {
            var str = "Local:\n" + LocalSize.Description("  ") + "\n\n" +
                      "Remote:\n" + RemoteSize.Description("  ");
            return str;
        }
    }

    // TODO: remove once we process sessions from the mutagen RPC
    public SyncSessionModel(string localPath, string remoteName, string remotePath,
        SyncSessionStatusCategory statusCategory,
        string statusString, string statusDescription, string[] errors)
    {
        Identifier = "TODO";
        Name = "TODO";

        LocalPath = localPath;
        RemoteName = remoteName;
        RemotePath = remotePath;
        StatusCategory = statusCategory;
        StatusString = statusString;
        StatusDescription = statusDescription;
        LocalSize = new SyncSessionModelEndpointSize
        {
            SizeBytes = (ulong)new Random().Next(0, 1000000000),
            FileCount = (ulong)new Random().Next(0, 10000),
            DirCount = (ulong)new Random().Next(0, 10000),
        };
        RemoteSize = new SyncSessionModelEndpointSize
        {
            SizeBytes = (ulong)new Random().Next(0, 1000000000),
            FileCount = (ulong)new Random().Next(0, 10000),
            DirCount = (ulong)new Random().Next(0, 10000),
        };

        Errors = errors;
    }

    public SyncSessionModel(State state)
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
            StatusCategory = SyncSessionStatusCategory.Paused;
            StatusString = "Paused";
            StatusDescription = "The session is paused.";
        }
        else
        {
            switch (state.Status)
            {
                case Status.Disconnected:
                    StatusCategory = SyncSessionStatusCategory.Error;
                    StatusString = "Disconnected";
                    StatusDescription =
                        "The session is unpaused but not currently connected or connecting to either endpoint.";
                    break;
                case Status.HaltedOnRootEmptied:
                    StatusCategory = SyncSessionStatusCategory.Error;
                    StatusString = "Halted on root emptied";
                    StatusDescription = "The session is halted due to the root emptying safety check.";
                    break;
                case Status.HaltedOnRootDeletion:
                    StatusCategory = SyncSessionStatusCategory.Error;
                    StatusString = "Halted on root deletion";
                    StatusDescription = "The session is halted due to the root deletion safety check.";
                    break;
                case Status.HaltedOnRootTypeChange:
                    StatusCategory = SyncSessionStatusCategory.Error;
                    StatusString = "Halted on root type change";
                    StatusDescription = "The session is halted due to the root type change safety check.";
                    break;
                case Status.ConnectingAlpha:
                    StatusCategory = SyncSessionStatusCategory.Working;
                    StatusString = "Connecting (alpha)";
                    StatusDescription = "The session is attempting to connect to the alpha endpoint.";
                    break;
                case Status.ConnectingBeta:
                    StatusCategory = SyncSessionStatusCategory.Working;
                    StatusString = "Connecting (beta)";
                    StatusDescription = "The session is attempting to connect to the beta endpoint.";
                    break;
                case Status.Watching:
                    StatusCategory = SyncSessionStatusCategory.Ok;
                    StatusString = "Watching";
                    StatusDescription = "The session is watching for filesystem changes.";
                    break;
                case Status.Scanning:
                    StatusCategory = SyncSessionStatusCategory.Working;
                    StatusString = "Scanning";
                    StatusDescription = "The session is scanning the filesystem on each endpoint.";
                    break;
                case Status.WaitingForRescan:
                    StatusCategory = SyncSessionStatusCategory.Working;
                    StatusString = "Waiting for rescan";
                    StatusDescription =
                        "The session is waiting to retry scanning after an error during the previous scanning operation.";
                    break;
                case Status.Reconciling:
                    StatusCategory = SyncSessionStatusCategory.Working;
                    StatusString = "Reconciling";
                    StatusDescription = "The session is performing reconciliation.";
                    break;
                case Status.StagingAlpha:
                    StatusCategory = SyncSessionStatusCategory.Working;
                    StatusString = "Staging (alpha)";
                    StatusDescription = "The session is staging files on alpha.";
                    break;
                case Status.StagingBeta:
                    StatusCategory = SyncSessionStatusCategory.Working;
                    StatusString = "Staging (beta)";
                    StatusDescription = "The session is staging files on beta.";
                    break;
                case Status.Transitioning:
                    StatusCategory = SyncSessionStatusCategory.Working;
                    StatusString = "Transitioning";
                    StatusDescription = "The session is performing transition operations on each endpoint.";
                    break;
                case Status.Saving:
                    StatusCategory = SyncSessionStatusCategory.Working;
                    StatusString = "Saving";
                    StatusDescription = "The session is recording synchronization history to disk.";
                    break;
                default:
                    StatusCategory = SyncSessionStatusCategory.Unknown;
                    StatusString = state.Status.ToString();
                    StatusDescription = "Unknown status message.";
                    break;
            }
        }

        // If there are any conflicts, set the status to Conflicts.
        if (state.Conflicts.Count > 0 && StatusCategory > SyncSessionStatusCategory.Conflicts)
        {
            StatusCategory = SyncSessionStatusCategory.Conflicts;
            StatusString = "Conflicts";
            StatusDescription = "The session has conflicts that need to be resolved.";
        }

        LocalSize = new SyncSessionModelEndpointSize
        {
            SizeBytes = state.AlphaState.TotalFileSize,
            FileCount = state.AlphaState.Files,
            DirCount = state.AlphaState.Directories,
            SymlinkCount = state.AlphaState.SymbolicLinks,
        };
        RemoteSize = new SyncSessionModelEndpointSize
        {
            SizeBytes = state.BetaState.TotalFileSize,
            FileCount = state.BetaState.Files,
            DirCount = state.BetaState.Directories,
            SymlinkCount = state.BetaState.SymbolicLinks,
        };

        // TODO: accumulate errors, there seems to be multiple fields they can
        //       come from
        if (!string.IsNullOrWhiteSpace(state.LastError)) Errors = [state.LastError];
    }
}
