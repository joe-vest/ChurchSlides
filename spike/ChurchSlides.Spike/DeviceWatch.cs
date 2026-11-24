using System.Runtime.InteropServices;
using System.Windows.Threading;
using NAudio.CoreAudioApi;

namespace ChurchSlides.Spike;

/// <summary>One audio-device notification, as Windows sent it.</summary>
public sealed record DeviceEvent(string Text, bool DefaultOutputChanged);

/// <summary>
/// Logs every default-device and device-state notification (C0.4 decision 6). Create it on the
/// UI thread: <see cref="Changed"/> is raised on that thread's dispatcher.
/// </summary>
public sealed class DeviceWatch : IDisposable
{
    private readonly MMDeviceEnumerator enumerator = new();
    private readonly Dispatcher dispatcher = Dispatcher.CurrentDispatcher;
    private readonly MMDeviceNotificationClient client;

    public DeviceWatch()
    {
        client = enumerator.CreateNotificationClient(true);
        client.DefaultDeviceChanged += (_, e) =>
            Raise($"Default {e.Flow} device for {e.Role} is now {NameOf(e.DeviceId)}", e.Flow == DataFlow.Render && e.Role == Role.Console);
        client.DeviceStateChanged += (_, e) => Raise($"Device {NameOf(e.DeviceId)} is now {e.NewState}", false);
        client.DeviceAdded += (_, e) => Raise($"Device added: {NameOf(e.DeviceId)}", false);
        client.DeviceRemoved += (_, e) => Raise($"Device removed: {NameOf(e.DeviceId)}", false);
    }

    /// <summary>On the UI thread.</summary>
    public event Action<DeviceEvent>? Changed;

    public string DefaultOutputName()
    {
        try
        {
            using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
            return device.FriendlyName;
        }
        catch (COMException)
        {
            return "none";
        }
    }

    public void Dispose()
    {
        client.Dispose();
        enumerator.Dispose();
    }

    private string NameOf(string? id)
    {
        if (id is null)
            return "none";
        try
        {
            using var device = enumerator.GetDevice(id);
            return device.FriendlyName;
        }
        catch (COMException)
        {
            return id;
        }
    }

    /// <summary>
    /// Posted to the dispatcher explicitly: the panel is built before Application.Run installs the
    /// synchronization context, so NAudio raises these on a thread-pool thread (seen in C0.4b).
    /// </summary>
    private void Raise(string text, bool defaultOutput) =>
        dispatcher.BeginInvoke(() => Changed?.Invoke(new DeviceEvent(text, defaultOutput)));
}
