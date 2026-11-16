using System.IO;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using MouseBridge.Core;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;
namespace MouseBridge.App;
public partial class MainWindow : Window
{
    private BluetoothLink? link;
    private volatile RemoteControl? remote;
    private bool serverRole;
    private bool closeRequested,closeReady,busy,pingBusy;
    private readonly SessionLogger logger=new();
    private readonly RttProbe rtt=new();
    private CancellationTokenSource session=new();
    private CancellationTokenSource? roleCancellation;
    private Task roleTask=Task.CompletedTask;
    private string? peerId=PeerSettings.Load();
    private bool waitingForSelection;
    private SwitchShortcut currentShortcut=ShortcutSettings.Load();
    public MainWindow()
    {
        InitializeComponent();
        Height=Math.Min(Height,Math.Max(MinHeight,SystemParameters.WorkArea.Height-24));
        Width=Math.Min(Width,Math.Max(MinWidth,SystemParameters.WorkArea.Width-24));
        InitializeEdgeControls();InitializeShortcutControls();SetRoleSelection(null);Log($"MouseBridge 0.3.0 · switch shortcut: {currentShortcut.DisplayName}");
        StateChanged+=(_,_)=>Log($"Window state: {WindowState} (input engine remains active)");
        Closing+=async(_,e)=>
        {
            if(closeReady)return;e.Cancel=true;if(closeRequested)return;closeRequested=true;
            await StopRole();await Reset();await logger.DisposeAsync();closeReady=true;Close();
        };
    }
    private void Log(string text)
    {
        string line=$"{DateTime.Now:HH:mm:ss.fff} {text}\n";logger.Write(line);
        void Append(){if(WindowState==WindowState.Minimized)return;if(LogBox.Text.Length>200000)LogBox.Clear();LogBox.AppendText(line);LogBox.ScrollToEnd();}
        if(Dispatcher.CheckAccess())Append();else _=Dispatcher.InvokeAsync(Append);
    }
    private async Task Reset()
    {
        var engine=remote;remote=null;engine?.Dispose();InputStatus.Text="Input sharing is ready";
        session.Cancel();var old=link;link=null;if(old!=null)await old.DisposeAsync();
        session.Dispose();session=new();Status.Text="Waiting for connection";
    }
    private async Task StopRole()
    {
        roleCancellation?.Cancel();try{await roleTask;}catch(OperationCanceledException){}
        roleCancellation?.Dispose();roleCancellation=null;
    }
    private async Task Run(Func<Task> action)
    {
        if(busy){Log("Another operation is in progress. Try again shortly.");return;}busy=true;
        try{await action();}catch(Exception e){Log($"Error 0x{e.HResult:X8}: {e.Message}");}finally{busy=false;}
    }
    private async void Main_Click(object sender,RoutedEventArgs e)=>await Run(()=>SelectRole(false));
    private async void Sub_Click(object sender,RoutedEventArgs e)=>await Run(()=>SelectRole(true));
    private async Task SelectRole(bool server)
    {
        await StopRole();await Reset();serverRole=server;waitingForSelection=false;DevicePanel.Visibility=Visibility.Collapsed;
        SetRoleSelection(server);
        Mode.Text=server?"Secondary PC · receive keyboard and mouse input":$"Main PC · {currentShortcut.DisplayName} to switch control";
        Log(server?"Secondary selected: advertising and input receive enabled":$"Main selected: automatic discovery, {currentShortcut.DisplayName} and keepalive enabled");
        roleCancellation=new();roleTask=RoleLoop(roleCancellation.Token);
    }
    private async Task RoleLoop(CancellationToken ct)
    {
        try
        {
            while(!ct.IsCancellationRequested)
            {
                try
                {
                    if(link?.Transport==null)
                    {
                        if(serverRole)
                        {if(link==null){await NewLink().ListenAsync(ct);Status.Text="Secondary ready · waiting for a main PC";Log("RFCOMM advertising started");}}
                        else if(!waitingForSelection)
                        {
                            if(string.IsNullOrEmpty(peerId))await DiscoverSub(ct);
                            if(!string.IsNullOrEmpty(peerId))
                            {Status.Text="Connecting to secondary";await NewLink().ConnectAsync(peerId,ct);PeerSettings.Save(peerId);}
                        }
                    }
                }
                catch(OperationCanceledException) when(ct.IsCancellationRequested){break;}
                catch(Exception ex){Log($"Waiting to reconnect: {ex.Message}");await Reset();}
                await Task.Delay(2000,ct);
            }
        }
        catch(OperationCanceledException) when(ct.IsCancellationRequested){}
    }
    private async Task DiscoverSub(CancellationToken ct)
    {
        Status.Text="Looking for secondary PCs among paired devices";
        var devices=await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(true)).AsTask(ct);
        var candidates=new List<DeviceInformation>();
        foreach(var d in devices)
        {
            try{if(await BluetoothLink.HasServiceAsync(d.Id,ct))candidates.Add(d);}
            catch(OperationCanceledException) when(ct.IsCancellationRequested){throw;}
            catch(Exception ex){Log($"Skipping device ({d.Name}): {ex.Message}");}
        }
        if(candidates.Count==1){peerId=candidates[0].Id;Log("Secondary found: "+candidates[0].Name);}
        else if(candidates.Count>1)
        {
            waitingForSelection=true;Devices.ItemsSource=candidates;Devices.SelectedIndex=0;DevicePanel.Visibility=Visibility.Visible;
            Status.Text="More than one secondary PC is available. Choose a device.";
        }
        else Status.Text="Waiting for secondary. Select Secondary PC on the other PC; pair in Windows settings if needed.";
    }
    private async void ChooseSub_Click(object sender,RoutedEventArgs e)
    {
        if(Devices.SelectedItem is not DeviceInformation d)return;
        await Run(async()=>{await StopRole();await Reset();peerId=d.Id;PeerSettings.Save(peerId);waitingForSelection=false;DevicePanel.Visibility=Visibility.Collapsed;roleCancellation=new();roleTask=RoleLoop(roleCancellation.Token);});
    }
    private async void SearchAgain_Click(object sender,RoutedEventArgs e)=>await Run(async()=>
    {peerId=null;PeerSettings.Save(null);await SelectRole(false);});
    private async void Stop_Click(object sender,RoutedEventArgs e)=>await Run(async()=>
    {await StopRole();await Reset();Mode.Text="Choose a role";SetRoleSelection(null);Log("Stopped by user; input hooks and remote permissions disabled");});
    private BluetoothLink NewLink()
    {
        var current=new BluetoothLink();var token=session.Token;bool isServer=serverRole;
        current.Connected+=()=>Dispatcher.InvokeAsync(async()=>
        {
            if(current!=link||token.IsCancellationRequested)return;
            try
            {
            var engine=await RemoteControl.CreateAsync(isServer,current.Transport!,token,Log,
                text=>{_=Dispatcher.InvokeAsync(()=>{if(current==link)InputStatus.Text=text;});},
                ex=>{_=Dispatcher.InvokeAsync(async()=>{if(current==link){Log("Input engine error: "+ex.Message);await Reset();}});});
            if(current!=link||token.IsCancellationRequested){engine.Dispose();return;}
                remote=engine;engine.ChangeEdges(currentEdges);engine.ChangeShortcut(currentShortcut);engine.ReceivePermissionChanged(isServer);engine.SetKeepAlive(true);engine.SetArmed(!isServer);
                Status.Text=isServer?"Secondary connected · ready to receive input":$"Main connected · {currentShortcut.DisplayName} is ready";Log("Connected · role settings applied");Log("Connection protection: "+current.SecurityStatus);
                _=Task.Run(()=>Receive(current,engine,token));
            }
            catch(OperationCanceledException) when(token.IsCancellationRequested){}
            catch(Exception ex){if(current==link){Log("Could not start input engine: "+ex.Message);await Reset();}}
        });
        current.Diagnostic+=text=>Log(text);
        current.Failed+=ex=>Log("Incoming connection error: "+ex.Message);
        link=current;return current;
    }
    private async Task Receive(BluetoothLink current,RemoteControl engine,CancellationToken ct)
    {
        try
        {
            while(!ct.IsCancellationRequested)
            {
                var m=await current.Transport!.ReceiveAsync(ct).ConfigureAwait(false);
                if(m==null){Log("The peer closed the connection.");break;}
                switch(m.Kind)
                {
                    case MessageKind.Ping:
                        if(m.Payload.Length!=8)throw new InvalidDataException("Invalid ping");
                        Log($"RX ping #{BitConverter.ToInt64(m.Payload)}");
                        await current.Transport.SendAsync(new(MessageKind.Pong,m.Payload),ct).ConfigureAwait(false);
                        Log($"TX pong #{BitConverter.ToInt64(m.Payload)} sent");break;
                    case MessageKind.Pong:
                        if(m.Payload.Length!=8)throw new InvalidDataException("Invalid pong");
                        long id=BitConverter.ToInt64(m.Payload);Log($"RX pong #{id} ({(rtt.Complete(id)?"matched":"late/unmatched")})");break;
                    case MessageKind.Text:Log("RX text: "+Encoding.UTF8.GetString(m.Payload));break;
                    case MessageKind.Binary:Log("RX binary: "+Convert.ToHexString(m.Payload));break;
                    default:await engine.HandleAsync(m).ConfigureAwait(false);break;
                }
            }
        }
        catch(OperationCanceledException){}
        catch(Exception ex){if(!ct.IsCancellationRequested)Log($"Receive error 0x{ex.HResult:X8}: {ex.Message}");}
        finally{await Dispatcher.InvokeAsync(async()=>{if(link==current)await Reset();}).Task.Unwrap();}
    }
    private void Settings_Click(object sender,RoutedEventArgs e)=>Process.Start(new ProcessStartInfo("ms-settings:bluetooth"){UseShellExecute=true});
    private async Task Send(Message m)
    {
        var transport=link?.Transport??throw new IOException("Connect to a peer first.");
        using var timeout=CancellationTokenSource.CreateLinkedTokenSource(session.Token);timeout.CancelAfter(10000);await transport.SendAsync(m,timeout.Token);
    }
    private async void Text_Click(object sender,RoutedEventArgs e)=>await Run(async()=>{await Send(new(MessageKind.Text,Encoding.UTF8.GetBytes(Input.Text)));Log("TX text");});
    private async void Binary_Click(object sender,RoutedEventArgs e)=>await Run(async()=>{await Send(new(MessageKind.Binary,[0,1,2,127,128,255]));Log("TX binary");});
    private async void Burst_Click(object sender,RoutedEventArgs e)=>await Run(async()=>{for(int i=0;i<100;i++)await Send(new(MessageKind.Text,Encoding.UTF8.GetBytes($"seq={i:D3} {Input.Text}")));Log("TX 100 messages");});
    private void Logs_Click(object sender,RoutedEventArgs e)
    {try{Directory.CreateDirectory(logger.DirectoryPath);Process.Start(new ProcessStartInfo("explorer.exe",logger.DirectoryPath){UseShellExecute=true});}catch(Exception ex){Log(ex.Message);}}
    private void SetRoleSelection(bool? secondary)
    {
        var selected=(Style)FindResource("PrimaryButton");var normal=(Style)FindResource(typeof(Button));
        MainRoleButton.ClearValue(Control.BackgroundProperty);SecondaryRoleButton.ClearValue(Control.BackgroundProperty);
        MainRoleButton.Style=secondary==false?selected:normal;SecondaryRoleButton.Style=secondary==true?selected:normal;
    }
    private void InitializeShortcutControls()
    {
        foreach(string key in Enumerable.Range(13,12).Select(n=>"F"+n).Concat(Enumerable.Range('A',26).Select(n=>((char)n).ToString())).Concat(Enumerable.Range(0,10).Select(n=>n.ToString())).Append("Space"))
            ShortcutKey.Items.Add(new ComboBoxItem{Content=key});
        ShortcutCtrl.IsChecked=currentShortcut.Ctrl;ShortcutAlt.IsChecked=currentShortcut.Alt;ShortcutShift.IsChecked=currentShortcut.Shift;
        ShortcutKey.SelectedItem=ShortcutKey.Items.Cast<ComboBoxItem>().FirstOrDefault(item=>string.Equals(item.Content?.ToString(),currentShortcut.KeyName,StringComparison.OrdinalIgnoreCase));
        ShortcutSummary.Text=currentShortcut.DisplayName;ShortcutFeedback.Text=$"Current: {currentShortcut.DisplayName}. Saved on this PC.";
    }
    private void ShortcutSave_Click(object sender,RoutedEventArgs e)
    {
        try
        {
            string key=(ShortcutKey.SelectedItem as ComboBoxItem)?.Content?.ToString()??"F8";
            var candidate=new SwitchShortcut(SwitchShortcut.ParseKey(key),ShortcutCtrl.IsChecked==true,ShortcutAlt.IsChecked==true,ShortcutShift.IsChecked==true);candidate.Validate();
            var engine=remote;if(engine!=null)engine.ChangeShortcut(candidate);else NativeInput.ProbeShortcut(candidate);
            try{ShortcutSettings.Save(candidate);}
            catch
            {
                if(engine!=null)engine.ChangeShortcut(currentShortcut);
                throw;
            }
            currentShortcut=candidate;ShortcutSummary.Text=candidate.DisplayName;
            if(roleCancellation!=null&&!serverRole)Mode.Text=$"Main PC · {candidate.DisplayName} to switch control";
            ShortcutFeedback.Text=$"Saved: {candidate.DisplayName}. Use it on the main PC.";
            ShortcutFeedback.Foreground=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(34,122,75));
            Log($"Shortcut saved: {candidate.DisplayName}. Press and release the complete shortcut to switch.");
        }
        catch(Exception ex)
        {
            ShortcutFeedback.Text=ex.Message;ShortcutFeedback.Foreground=new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(180,35,24));
            Log("Shortcut was not saved: "+ex.Message);
        }
    }
    private async void Ping_Click(object sender, RoutedEventArgs e)
    {
        if (pingBusy) { Log("Waiting for an RTT reply."); return; }
        if (busy) { Log("Wait for the current operation before measuring RTT."); return; }
        var current = link;
        if (current?.Transport == null) { Log("Connect before measuring RTT."); return; }
        var ct = session.Token;
        pingBusy = true; PingButton.IsEnabled = false;
        try
        {
            var result = await rtt.MeasureAsync(async (id, token) =>
            {
                Log($"TX ping #{id}");
                await current.Transport.SendAsync(new(MessageKind.Ping, BitConverter.GetBytes(id)), token);
                Log($"TX ping #{id} sent");
            }, TimeSpan.FromSeconds(5), ct);
            if (link == current)
                Log($"RTT: {result.RoundTripMilliseconds:F2} ms (send API completed in: {result.SendMilliseconds:F2} ms)");
        }
        catch (RttTimeoutException ex)
        {
            Log(ex.SendCompleted ? "RTT timeout (5s). Send completed; keeping the connection." : "RTT timeout (5s). Incomplete send; closing the connection.");
            if (!ex.SendCompleted && link == current) await Reset();
        }
        catch (OperationCanceledException) { Log("RTT cancelled (connection ended/changed)"); }
        catch (Exception ex)
        {
            Log($"RTT error 0x{ex.HResult:X8}: {ex.Message}");
            if (link == current) await Reset();
        }
        finally { pingBusy = false; PingButton.IsEnabled = true; }
    }
}
