using System.Collections.Generic;
using Coder.Desktop.App.ViewModels;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;

namespace Coder.Desktop.App.Views.Pages;

public sealed partial class TrayWindowMainPage : Page
{
    public TrayWindowViewModel ViewModel { get; }

    public TrayWindowMainPage(TrayWindowViewModel viewModel)
    {
        InitializeComponent();
        ViewModel = viewModel;
        ViewModel.Initialize(DispatcherQueue);
    }

    // HACK: using XAML to populate the text Runs results in an additional
    // whitespace Run being inserted between the ViewableHostname and the
    // ViewableHostnameSuffix. You might think, "OK let's populate the entire
    // TextBlock content from code then!", but this results in the ItemsRepeater
    // corrupting it and firing events off to the wrong AgentModel.
    //
    // This is the best solution I came up with that worked.
    public void AgentHostnameText_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not TextBlock textBlock) return;

        var nonWhitespaceRuns = new List<Run>();
        foreach (var inline in textBlock.Inlines)
            if (inline is Run run && run.Text != " ")
                nonWhitespaceRuns.Add(run);

        textBlock.Inlines.Clear();
        foreach (var run in nonWhitespaceRuns) textBlock.Inlines.Add(run);
    }
}
