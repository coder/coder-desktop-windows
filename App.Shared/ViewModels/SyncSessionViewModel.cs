using System.Threading.Tasks;
using Coder.Desktop.App.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Coder.Desktop.App.ViewModels;

public partial class SyncSessionViewModel : ObservableObject
{
    public SyncSessionModel Model { get; }

    private FileSyncListViewModel Parent { get; }

    public string Icon => Model.Paused ? "\uE768" : "\uE769";

    // Tooltip text for views to bind to (replaces WinUI ToolTipService hacks).
    public string StatusToolTip => Model.StatusDetails;
    public string SizeToolTip => Model.SizeDetails;

    public SyncSessionViewModel(FileSyncListViewModel parent, SyncSessionModel model)
    {
        Parent = parent;
        Model = model;
    }

    [RelayCommand]
    public async Task PauseOrResumeSession()
    {
        await Parent.PauseOrResumeSession(Model.Identifier);
    }

    [RelayCommand]
    public async Task TerminateSession()
    {
        await Parent.TerminateSession(Model.Identifier);
    }
}
