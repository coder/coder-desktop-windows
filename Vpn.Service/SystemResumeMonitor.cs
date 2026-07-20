using Microsoft.Win32;

namespace Coder.Desktop.Vpn.Service;

public interface ISystemResumeMonitor : IDisposable
{
    /// <summary>
    ///     Raised when the system resumes from sleep.
    /// </summary>
    event EventHandler? Resumed;
}

public class SystemResumeMonitor : ISystemResumeMonitor
{
    public event EventHandler? Resumed;

    public SystemResumeMonitor()
    {
        SystemEvents.PowerModeChanged += OnPowerModeChanged;
    }

    public void Dispose()
    {
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        GC.SuppressFinalize(this);
    }

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode == PowerModes.Resume) Resumed?.Invoke(this, EventArgs.Empty);
    }
}
