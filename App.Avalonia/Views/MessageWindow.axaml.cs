using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Coder.Desktop.App.Views;

public partial class MessageWindow : Window
{
    public string MessageTitle { get; }
    public string MessageContent { get; }

    public MessageWindow()
    {
        InitializeComponent();

        MessageTitle = "Message";
        MessageContent = "";
        Title = "Coder Desktop";
        DataContext = this;
    }

    public MessageWindow(string messageTitle, string messageContent, string windowTitle = "Coder Desktop") : this()
    {
        MessageTitle = messageTitle;
        MessageContent = messageContent;
        Title = windowTitle;

        // Re-assign DataContext to ensure bindings pick up the new values.
        DataContext = this;
    }

    private void CloseClicked(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
