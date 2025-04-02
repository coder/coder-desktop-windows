using System.Threading.Tasks;
using Coder.Desktop.App.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Coder.Desktop.App.ViewModels;

public partial class SyncSessionViewModel : ObservableObject
{
    public SyncSessionModel Model { get; }

    private FileSyncListViewModel Parent { get; }

    public string Icon => Model.Paused ? "\uE768" : "\uE769";

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

    // Check the comments in FileSyncListMainPage.xaml to see why this tooltip
    // stuff is necessary.
    private void SetToolTip(FrameworkElement element, string text)
    {
        // Get current tooltip and compare the text. Setting the tooltip with
        // the same text causes it to dismiss itself.
        var currentToolTip = ToolTipService.GetToolTip(element) as ToolTip;
        if (currentToolTip?.Content as string == text) return;

        ToolTipService.SetToolTip(element, new ToolTip { Content = text });
    }

    public void OnStatusTextLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element) return;
        SetToolTip(element, Model.StatusDetails);
    }

    public void OnStatusTextDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        SetToolTip(sender, Model.StatusDetails);
    }

    public void OnSizeTextLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement element) return;
        SetToolTip(element, Model.SizeDetails);
    }

    public void OnSizeTextDataContextChanged(FrameworkElement sender, DataContextChangedEventArgs args)
    {
        SetToolTip(sender, Model.SizeDetails);
    }
}
