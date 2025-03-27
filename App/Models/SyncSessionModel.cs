using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Coder.Desktop.App.Converters;
using Coder.Desktop.MutagenSdk.Proto.Synchronization;
using Coder.Desktop.MutagenSdk.Proto.Synchronization.Core;
using Coder.Desktop.MutagenSdk.Proto.Url;

namespace Coder.Desktop.App.Models;

// This is a much slimmer enum than the original enum from Mutagen and only
// contains the overarching states that we care about from a code perspective.
// We still store the original state in the model for rendering purposes.
public enum SyncSessionStatusCategory
{
    Unknown,
    Paused,

    // Halted is a combination of Error and Paused. If the session
    // automatically pauses due to a safety check, we want to show it as an
    // error, but also show that it can be resumed.
    Halted,
    Error,

    // If there are any conflicts, the state will be set to Conflicts,
    // overriding Working and Ok.
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

public enum SyncSessionModelEntryKind
{
    Unknown,
    Directory,
    File,
    SymbolicLink,
    Untracked,
    Problematic,
    PhantomDirectory,
}

public sealed class SyncSessionModelEntry
{
    public readonly SyncSessionModelEntryKind Kind;

    // For Kind == Directory only.
    public readonly ReadOnlyDictionary<string, SyncSessionModelEntry> Contents;

    // For Kind == File only.
    public readonly string Digest = "";
    public readonly bool Executable;

    // For Kind = SymbolicLink only.
    public readonly string Target = "";

    // For Kind = Problematic only.
    public readonly string Problem = "";

    public SyncSessionModelEntry(Entry protoEntry)
    {
        Kind = protoEntry.Kind switch
        {
            EntryKind.Directory => SyncSessionModelEntryKind.Directory,
            EntryKind.File => SyncSessionModelEntryKind.File,
            EntryKind.SymbolicLink => SyncSessionModelEntryKind.SymbolicLink,
            EntryKind.Untracked => SyncSessionModelEntryKind.Untracked,
            EntryKind.Problematic => SyncSessionModelEntryKind.Problematic,
            EntryKind.PhantomDirectory => SyncSessionModelEntryKind.PhantomDirectory,
            _ => SyncSessionModelEntryKind.Unknown,
        };

        switch (Kind)
        {
            case SyncSessionModelEntryKind.Directory:
            {
                var contents = new Dictionary<string, SyncSessionModelEntry>();
                foreach (var (key, value) in protoEntry.Contents)
                    contents[key] = new SyncSessionModelEntry(value);
                Contents = new ReadOnlyDictionary<string, SyncSessionModelEntry>(contents);
                break;
            }
            case SyncSessionModelEntryKind.File:
                Digest = BitConverter.ToString(protoEntry.Digest.ToByteArray()).Replace("-", "").ToLower();
                Executable = protoEntry.Executable;
                break;
            case SyncSessionModelEntryKind.SymbolicLink:
                Target = protoEntry.Target;
                break;
            case SyncSessionModelEntryKind.Problematic:
                Problem = protoEntry.Problem;
                break;
        }
    }

    public new string ToString()
    {
        var str = Kind.ToString();
        switch (Kind)
        {
            case SyncSessionModelEntryKind.Directory:
                str += $" ({Contents.Count} entries)";
                break;
            case SyncSessionModelEntryKind.File:
                str += $" ({Digest}, executable: {Executable})";
                break;
            case SyncSessionModelEntryKind.SymbolicLink:
                str += $" (target: {Target})";
                break;
            case SyncSessionModelEntryKind.Problematic:
                str += $" ({Problem})";
                break;
        }

        return str;
    }
}

public sealed class SyncSessionModelConflictChange
{
    public readonly string Path; // relative to sync root

    // null means non-existent:
    public readonly SyncSessionModelEntry? Old;
    public readonly SyncSessionModelEntry? New;

    public SyncSessionModelConflictChange(Change protoChange)
    {
        Path = protoChange.Path;
        Old = protoChange.Old != null ? new SyncSessionModelEntry(protoChange.Old) : null;
        New = protoChange.New != null ? new SyncSessionModelEntry(protoChange.New) : null;
    }

    public new string ToString()
    {
        const string nonExistent = "<non-existent>";
        var oldStr = Old != null ? Old.ToString() : nonExistent;
        var newStr = New != null ? New.ToString() : nonExistent;
        return $"{Path} ({oldStr} -> {newStr})";
    }
}

public sealed class SyncSessionModelConflict
{
    public readonly string Root; // relative to sync root
    public readonly List<SyncSessionModelConflictChange> AlphaChanges;
    public readonly List<SyncSessionModelConflictChange> BetaChanges;

    public SyncSessionModelConflict(Conflict protoConflict)
    {
        Root = protoConflict.Root;
        AlphaChanges = protoConflict.AlphaChanges.Select(change => new SyncSessionModelConflictChange(change)).ToList();
        BetaChanges = protoConflict.BetaChanges.Select(change => new SyncSessionModelConflictChange(change)).ToList();
    }

    private string? FriendlyProblem()
    {
        // If the change is <non-existent> -> !<non-existent>.
        if (AlphaChanges.Count == 1 && BetaChanges.Count == 1 &&
            AlphaChanges[0].Old == null &&
            BetaChanges[0].Old == null &&
            AlphaChanges[0].New != null &&
            BetaChanges[0].New != null)
            return
                "An entry was created on both endpoints and they do not match. You can resolve this conflict by deleting one of the entries on either side.";

        return null;
    }

    public string Description()
    {
        // This formatting is very similar to Mutagen.
        var str = $"Conflict at path '{Root}':";
        foreach (var change in AlphaChanges)
            str += $"\n  (alpha) {change.ToString()}";
        foreach (var change in AlphaChanges)
            str += $"\n  (beta)  {change.ToString()}";
        if (FriendlyProblem() is { } friendlyProblem)
            str += $"\n\n  {friendlyProblem}";

        return str;
    }
}

public class SyncSessionModel
{
    public readonly string Identifier;
    public readonly string Name;

    public readonly string AlphaName;
    public readonly string AlphaPath;
    public readonly string BetaName;
    public readonly string BetaPath;

    public readonly SyncSessionStatusCategory StatusCategory;
    public readonly string StatusString;
    public readonly string StatusDescription;

    public readonly SyncSessionModelEndpointSize AlphaSize;
    public readonly SyncSessionModelEndpointSize BetaSize;

    public readonly IReadOnlyList<SyncSessionModelConflict> Conflicts;
    public ulong OmittedConflicts;
    public readonly IReadOnlyList<string> Errors;

    // If Paused is true, the session can be resumed. If false, the session can
    // be paused.
    public bool Paused => StatusCategory is SyncSessionStatusCategory.Paused or SyncSessionStatusCategory.Halted;

    public string StatusDetails
    {
        get
        {
            var str = $"{StatusString} ({StatusCategory})\n\n{StatusDescription}";
            foreach (var err in Errors) str += $"\n\nError: {err}";
            foreach (var conflict in Conflicts) str += $"\n\n{conflict.Description()}";
            if (OmittedConflicts > 0) str += $"\n\n{OmittedConflicts:N0} conflicts omitted";
            return str;
        }
    }

    public string SizeDetails
    {
        get
        {
            var str = "Alpha:\n" + AlphaSize.Description("  ") + "\n\n" +
                      "Remote:\n" + BetaSize.Description("  ");
            return str;
        }
    }

    public SyncSessionModel(State state)
    {
        Identifier = state.Session.Identifier;
        Name = state.Session.Name;

        (AlphaName, AlphaPath) = NameAndPathFromUrl(state.Session.Alpha);
        (BetaName, BetaPath) = NameAndPathFromUrl(state.Session.Beta);

        switch (state.Status)
        {
            case Status.Disconnected:
                StatusCategory = SyncSessionStatusCategory.Error;
                StatusString = "Disconnected";
                StatusDescription =
                    "The session is unpaused but not currently connected or connecting to either endpoint.";
                break;
            case Status.HaltedOnRootEmptied:
                StatusCategory = SyncSessionStatusCategory.Halted;
                StatusString = "Halted on root emptied";
                StatusDescription = "The session is halted due to the root emptying safety check.";
                break;
            case Status.HaltedOnRootDeletion:
                StatusCategory = SyncSessionStatusCategory.Halted;
                StatusString = "Halted on root deletion";
                StatusDescription = "The session is halted due to the root deletion safety check.";
                break;
            case Status.HaltedOnRootTypeChange:
                StatusCategory = SyncSessionStatusCategory.Halted;
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

        // If the session is paused, override all other statuses except Halted.
        if (state.Session.Paused && StatusCategory is not SyncSessionStatusCategory.Halted)
        {
            StatusCategory = SyncSessionStatusCategory.Paused;
            StatusString = "Paused";
            StatusDescription = "The session is paused.";
        }

        // If there are any conflicts, override Working and Ok.
        if (state.Conflicts.Count > 0 && StatusCategory > SyncSessionStatusCategory.Conflicts)
        {
            StatusCategory = SyncSessionStatusCategory.Conflicts;
            StatusString = "Conflicts";
            StatusDescription = "The session has conflicts that need to be resolved.";
        }

        Conflicts = state.Conflicts.Select(c => new SyncSessionModelConflict(c)).ToList();
        OmittedConflicts = state.ExcludedConflicts;

        AlphaSize = new SyncSessionModelEndpointSize
        {
            SizeBytes = state.AlphaState.TotalFileSize,
            FileCount = state.AlphaState.Files,
            DirCount = state.AlphaState.Directories,
            SymlinkCount = state.AlphaState.SymbolicLinks,
        };
        BetaSize = new SyncSessionModelEndpointSize
        {
            SizeBytes = state.BetaState.TotalFileSize,
            FileCount = state.BetaState.Files,
            DirCount = state.BetaState.Directories,
            SymlinkCount = state.BetaState.SymbolicLinks,
        };

        List<string> errors = [];
        if (!string.IsNullOrWhiteSpace(state.LastError)) errors.Add($"Last error:\n  {state.LastError}");
        // TODO: scan problems + transition problems + omissions should probably be fields
        foreach (var scanProblem in state.AlphaState.ScanProblems) errors.Add($"Alpha scan problem: {scanProblem}");
        if (state.AlphaState.ExcludedScanProblems > 0)
            errors.Add($"Alpha scan problems omitted: {state.AlphaState.ExcludedScanProblems}");
        foreach (var scanProblem in state.AlphaState.ScanProblems) errors.Add($"Beta scan problem: {scanProblem}");
        if (state.BetaState.ExcludedScanProblems > 0)
            errors.Add($"Beta scan problems omitted: {state.BetaState.ExcludedScanProblems}");
        foreach (var transitionProblem in state.AlphaState.TransitionProblems)
            errors.Add($"Alpha transition problem: {transitionProblem}");
        if (state.AlphaState.ExcludedTransitionProblems > 0)
            errors.Add($"Alpha transition problems omitted: {state.AlphaState.ExcludedTransitionProblems}");
        foreach (var transitionProblem in state.AlphaState.TransitionProblems)
            errors.Add($"Beta transition problem: {transitionProblem}");
        if (state.BetaState.ExcludedTransitionProblems > 0)
            errors.Add($"Beta transition problems omitted: {state.BetaState.ExcludedTransitionProblems}");
        Errors = errors;
    }

    private static (string, string) NameAndPathFromUrl(URL url)
    {
        var name = "Local";
        var path = !string.IsNullOrWhiteSpace(url.Path) ? url.Path : "Unknown";

        if (url.Protocol is not Protocol.Local)
            name = !string.IsNullOrWhiteSpace(url.Host) ? url.Host : "Unknown";
        if (string.IsNullOrWhiteSpace(url.Host)) name = url.Host;

        return (name, path);
    }
}
