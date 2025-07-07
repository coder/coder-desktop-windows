using Coder.Desktop.App.Models;
using Microsoft.UI.Xaml.Controls;
using System.Collections.Generic;

namespace Coder.Desktop.App.Views.Pages
{
    /// <summary>
    /// An empty page that can be used on its own or navigated to within a Frame.
    /// </summary>
    public sealed partial class PortForwardCreation : Page
    {
        public PortForward PortForward { get; set; } = new();
        public List<string> Hosts { get; set; } = [];
        public PortForwardCreation()
        {
            InitializeComponent();
        }

        public PortForwardCreation(List<string> hosts) : this()
        {
            Hosts = hosts;
        }
    }
}
