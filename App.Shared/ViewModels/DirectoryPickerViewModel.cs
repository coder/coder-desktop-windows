using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Coder.Desktop.App.Services;
using Coder.Desktop.CoderSdk.Agent;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Coder.Desktop.App.ViewModels;

public class DirectoryPickerBreadcrumb
{
    // HACK: you cannot access the parent context when inside an ItemsRepeater.
    public required DirectoryPickerViewModel ViewModel;

    public required string Name { get; init; }

    public required IReadOnlyList<string> AbsolutePathSegments { get; init; }

    // HACK: we need to know which one is first so we don't prepend an arrow
    // icon. You can't get the index of the current ItemsRepeater item in XAML.
    public required bool IsFirst { get; init; }
}

public enum DirectoryPickerItemKind
{
    ParentDirectory, // aka. ".."
    Directory,
    File, // includes everything else
}

public class DirectoryPickerItem
{
    // HACK: you cannot access the parent context when inside an ItemsRepeater.
    public required DirectoryPickerViewModel ViewModel;

    public required DirectoryPickerItemKind Kind { get; init; }
    public required string Name { get; init; }
    public required IReadOnlyList<string> AbsolutePathSegments { get; init; }

    public bool Selectable => Kind is DirectoryPickerItemKind.ParentDirectory or DirectoryPickerItemKind.Directory;
}

public partial class DirectoryPickerViewModel : ObservableObject
{
    // PathSelected will be called ONCE when the user either cancels or selects
    // a directory. If the user cancelled, the path will be null.
    public event EventHandler<string?>? PathSelected;

    /// <summary>
    /// Raised when the view should close itself.
    /// </summary>
    public event EventHandler? CloseRequested;

    private const int RequestTimeoutMilliseconds = 15_000;

    private readonly IAgentApiClient _client;
    private readonly IDispatcher _dispatcher;
    private readonly IWindowService _windowService;

    public readonly string AgentFqdn;

    // The initial loading screen is differentiated from subsequent loading
    // screens because:
    // 1. We don't want to show a broken state while the page is loading.
    // 2. An error dialog allows the user to get to a broken state with no
    //    breadcrumbs, no items, etc. with no chance to reload.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLoadingScreen))]
    [NotifyPropertyChangedFor(nameof(ShowListScreen))]
    private bool _initialLoading = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowLoadingScreen))]
    [NotifyPropertyChangedFor(nameof(ShowErrorScreen))]
    [NotifyPropertyChangedFor(nameof(ShowListScreen))]
    private string? _initialLoadError = null;

    [ObservableProperty]
    private bool _navigatingLoading = false;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsSelectable))]
    private string _currentDirectory = "";

    [ObservableProperty]
    private IReadOnlyList<DirectoryPickerBreadcrumb> _breadcrumbs = [];

    [ObservableProperty]
    private IReadOnlyList<DirectoryPickerItem> _items = [];

    public bool ShowLoadingScreen => InitialLoadError == null && InitialLoading;
    public bool ShowErrorScreen => InitialLoadError != null;
    public bool ShowListScreen => InitialLoadError == null && !InitialLoading;

    // The "root" directory on Windows isn't a real thing, but in our model
    // it's a drive listing. We don't allow users to select the fake drive
    // listing directory.
    //
    // On Linux, this will never be empty since the highest you can go is "/".
    public bool IsSelectable => CurrentDirectory != "";

    public DirectoryPickerViewModel(IAgentApiClientFactory clientFactory, IDispatcher dispatcher,
        IWindowService windowService, string agentFqdn)
    {
        _client = clientFactory.Create(agentFqdn);
        _dispatcher = dispatcher;
        _windowService = windowService;
        AgentFqdn = agentFqdn;
    }

    public void Initialize()
    {
        if (!_dispatcher.CheckAccess())
            throw new InvalidOperationException("Initialize must be called from the UI thread");

        InitialLoading = true;
        InitialLoadError = null;

        // Initial load is in the home directory.
        _ = BackgroundLoad(ListDirectoryRelativity.Home, []).ContinueWith(ContinueInitialLoad);
    }

    [RelayCommand]
    private void RetryLoad()
    {
        InitialLoading = true;
        InitialLoadError = null;

        // Subsequent loads after the initial failure are always in the root
        // directory in case there's a permanent issue preventing listing the
        // home directory.
        _ = BackgroundLoad(ListDirectoryRelativity.Root, []).ContinueWith(ContinueInitialLoad);
    }

    private async Task<ListDirectoryResponse> BackgroundLoad(ListDirectoryRelativity relativity, List<string> path)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        return await _client.ListDirectory(new ListDirectoryRequest
        {
            Path = path,
            Relativity = relativity,
        }, cts.Token);
    }

    private void ContinueInitialLoad(Task<ListDirectoryResponse> task)
    {
        // Ensure we're on the UI thread.
        if (!_dispatcher.CheckAccess())
        {
            _dispatcher.Post(() => ContinueInitialLoad(task));
            return;
        }

        if (task.IsCompletedSuccessfully)
        {
            ProcessResponse(task.Result);
            return;
        }

        InitialLoadError = "Could not list home directory in workspace: ";
        if (task.IsCanceled)
            InitialLoadError += new TaskCanceledException();
        else if (task.IsFaulted)
            InitialLoadError += task.Exception;
        else
            InitialLoadError += "no successful result or error";

        InitialLoading = false;
    }

    [RelayCommand]
    public async Task ListPath(IReadOnlyList<string> path)
    {
        if (NavigatingLoading)
            return;

        NavigatingLoading = true;

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(RequestTimeoutMilliseconds));
        try
        {
            var res = await _client.ListDirectory(new ListDirectoryRequest
            {
                Path = path.ToList(),
                Relativity = ListDirectoryRelativity.Root,
            }, cts.Token);

            ProcessResponse(res);
        }
        catch (Exception e)
        {
            // Subsequent listing errors are just shown as dialog boxes.
            _windowService.ShowMessageWindow("Failed to list remote directory", e.ToString(), "Coder Connect");
        }
        finally
        {
            NavigatingLoading = false;
        }
    }

    [RelayCommand]
    public void Cancel()
    {
        PathSelected?.Invoke(this, null);
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    [RelayCommand]
    public void Select()
    {
        if (CurrentDirectory == "")
            return;

        PathSelected?.Invoke(this, CurrentDirectory);
        CloseRequested?.Invoke(this, EventArgs.Empty);
    }

    private void ProcessResponse(ListDirectoryResponse res)
    {
        InitialLoading = false;
        InitialLoadError = null;
        NavigatingLoading = false;

        var breadcrumbs = new List<DirectoryPickerBreadcrumb>(res.AbsolutePath.Count + 1)
        {
            new()
            {
                Name = "🖥️",
                AbsolutePathSegments = [],
                IsFirst = true,
                ViewModel = this,
            },
        };

        for (var i = 0; i < res.AbsolutePath.Count; i++)
            breadcrumbs.Add(new DirectoryPickerBreadcrumb
            {
                Name = res.AbsolutePath[i],
                AbsolutePathSegments = res.AbsolutePath[..(i + 1)],
                IsFirst = false,
                ViewModel = this,
            });

        var items = new List<DirectoryPickerItem>(res.Contents.Count + 1);
        if (res.AbsolutePath.Count != 0)
            items.Add(new DirectoryPickerItem
            {
                Kind = DirectoryPickerItemKind.ParentDirectory,
                Name = "..",
                AbsolutePathSegments = res.AbsolutePath[..^1],
                ViewModel = this,
            });

        foreach (var item in res.Contents)
        {
            if (item.Name.StartsWith("."))
                continue;

            items.Add(new DirectoryPickerItem
            {
                Kind = item.IsDir ? DirectoryPickerItemKind.Directory : DirectoryPickerItemKind.File,
                Name = item.Name,
                AbsolutePathSegments = res.AbsolutePath.Append(item.Name).ToList(),
                ViewModel = this,
            });
        }

        CurrentDirectory = res.AbsolutePathString;
        Breadcrumbs = breadcrumbs;
        Items = items;
    }
}
