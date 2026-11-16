using System.IO;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;
using MouseBridge.Core;
namespace MouseBridge.App;
public sealed class BluetoothLink : IAsyncDisposable
{
    public static readonly RfcommServiceId ServiceId = RfcommServiceId.FromUuid(new Guid("9d96d1bd-2786-4b5d-97e7-cf84394843af"));
    private readonly object sync = new();
    private bool disposed;
    private bool secureListenerBound;
    private StreamSocket? socket;
    private StreamSocketListener? listener;
    private RfcommServiceProvider? provider;
    private BluetoothDevice? device;
    private RfcommDeviceService? service;
    public IMessageTransport? Transport { get; private set; }
    public string SecurityStatus { get; private set; } = "Disconnected";
    public event Action? Connected;
    public event Action<Exception>? Failed;
    public event Action<string>? Diagnostic;
    private const SocketProtectionLevel RequiredProtection = SocketProtectionLevel.BluetoothEncryptionWithAuthentication;
    private void Attach(StreamSocket s, string role, bool acceptedByAuthenticatedListener = false)
    {
        var actual = s.Information.ProtectionLevel;
        Diagnostic?.Invoke($"{role}: ActualProtection={actual} ({(int)actual}); RequiredProtection={RequiredProtection} ({(int)RequiredProtection})");
        if (!RfcommAdmissionPolicy.CanAttach(acceptedByAuthenticatedListener,
            actual == RequiredProtection, actual == SocketProtectionLevel.PlainSocket))
        {
            s.Dispose();
            throw new IOException($"{role}: Authenticated Bluetooth protection check failed. ActualProtection={actual} ({(int)actual}), RequiredProtection={RequiredProtection} ({(int)RequiredProtection}). This report alone does not establish the link encryption state. Check diagnostics.");
        }
        SecurityStatus = actual == RequiredProtection
            ? "Authenticated Bluetooth protection reported"
            : "Authenticated listener policy applied (accepted socket reports PlainSocket)";
        if (actual == SocketProtectionLevel.PlainSocket)
            Diagnostic?.Invoke("Secondary: accepting the socket under the authenticated listener policy; accepted-socket reports are not independent link measurements.");
        socket = s;
        Transport = new RfcommTransport(s);
        Connected?.Invoke();
    }
    public async Task ListenAsync(CancellationToken ct = default)
    {
        provider = await RfcommServiceProvider.CreateAsync(ServiceId).AsTask(ct);
        listener = new StreamSocketListener();
        listener.ConnectionReceived += (_, args) =>
        {
            try { lock (sync) { if (disposed || socket != null) { args.Socket.Dispose(); return; } Attach(args.Socket, "B/server", secureListenerBound); } }
            catch (Exception e) { Failed?.Invoke(e); }
        };
        Diagnostic?.Invoke($"B/server: Bind requested={RequiredProtection} ({(int)RequiredProtection})");
        await listener.BindServiceNameAsync(provider.ServiceId.AsString(), RequiredProtection).AsTask(ct);
        lock (sync) { secureListenerBound = true; }
        Diagnostic?.Invoke("B/server: secure bind completed; advertising starts");
        provider.StartAdvertising(listener, true);
    }
    public async Task ConnectAsync(string id, CancellationToken ct = default)
    {
        device = await BluetoothDevice.FromIdAsync(id).AsTask(ct) ?? throw new IOException("Device unavailable / access denied");
        if (!device.DeviceInformation.Pairing.IsPaired) throw new IOException("Pair the PCs in Windows Bluetooth settings first.");
        var result = await device.GetRfcommServicesForIdAsync(ServiceId, BluetoothCacheMode.Uncached).AsTask(ct);
        if (result.Error != BluetoothError.Success || result.Services.Count == 0) throw new IOException($"Service discovery: {result.Error}; services={result.Services.Count}. Check that the secondary PC is running.");
        service = result.Services[0];
        Diagnostic?.Invoke($"A/client: Paired={device.DeviceInformation.Pairing.IsPaired}; ServiceProtection={service.ProtectionLevel} ({(int)service.ProtectionLevel}); ServiceMaxProtection={service.MaxProtectionLevel} ({(int)service.MaxProtectionLevel})");
        Diagnostic?.Invoke($"A/client: Connect requested={RequiredProtection} ({(int)RequiredProtection})");
        var s = new StreamSocket();
        try { await s.ConnectAsync(service.ConnectionHostName, service.ConnectionServiceName, RequiredProtection).AsTask(ct); ct.ThrowIfCancellationRequested(); Attach(s, "A/client"); }
        catch { s.Dispose(); throw; }
    }
    public static async Task<bool> HasServiceAsync(string id, CancellationToken ct)
    {
        using var device = await BluetoothDevice.FromIdAsync(id).AsTask(ct);
        if (device == null) return false;
        var result = await device.GetRfcommServicesForIdAsync(ServiceId, BluetoothCacheMode.Uncached).AsTask(ct);
        bool found = result.Error == BluetoothError.Success && result.Services.Count > 0;
        foreach (var service in result.Services) service.Dispose();
        return found;
    }
    public async ValueTask DisposeAsync()
    {
        lock (sync) { disposed = true; secureListenerBound = false; }
        try { provider?.StopAdvertising(); } catch { }
        listener?.Dispose(); socket?.Dispose();
        if (Transport != null) { try { await Transport.DisposeAsync(); } catch { } }
        service?.Dispose(); device?.Dispose();
    }
}
