using System.IO;

using System.Windows.Threading;
using MouseBridge.Core;
namespace MouseBridge.App;
internal sealed partial class RemoteControl : IDisposable
{
    private readonly bool server;
    private readonly Action<string> log,status;
    private readonly Action<Exception> fail;
    private readonly OutboundBuffer outgoing=new(64);
    private readonly CancellationTokenSource lifetime;
    private readonly System.Threading.Timer timer;
    private int tickScheduled;
    private long lastTick;
    private readonly InputInjector injector=new();
    private readonly List<InputAction> actions=[];
    private readonly Dispatcher dispatcher;
    private InputCapture? capture;
    private SwitchShortcut shortcut=ShortcutSettings.Load();
    private string ShortcutName=>shortcut.DisplayName;
    public void ChangeShortcut(SwitchShortcut value)
    {
        if(disposed)throw new ObjectDisposedException(nameof(RemoteControl));
        if(!dispatcher.CheckAccess()){dispatcher.Invoke(()=>ChangeShortcut(value));return;}
        value.Validate();
        if(tx!=Guid.Empty||pending!=Guid.Empty||clipboardReturn!=Guid.Empty)throw new InvalidOperationException("Return to the main PC and wait for clipboard transfer before changing the shortcut.");
        if(capture!=null)capture.ChangeShortcut(value);else if(!server)NativeInput.ProbeShortcut(value);
        shortcut=value;
        status(server?(rx==Guid.Empty?"Secondary ready to receive input":"Receiving input from main PC"):$"Main PC · {ShortcutName} to switch control");
    }
    private Guid tx,pending;
    private readonly ControlLease lease=new();
    private Guid rx=>lease.Session;
    private long lastAck,lastPulse,requested,lastSummary;
    private int sentActions,receivedActions;
    private bool disposed,failed;
    public bool AllowReceive{get;set;}
    public bool KeepAlive{get;set;}
    private readonly Guid pulseId=Guid.NewGuid();
    public static Task<RemoteControl> CreateAsync(bool server,IMessageTransport transport,CancellationToken ct,Action<string> log,Action<string> status,Action<Exception> fail)
    {
        var ready=new TaskCompletionSource<RemoteControl>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread=new Thread(()=>
        {
            try
            {
                ct.ThrowIfCancellationRequested();
                var dispatcher=Dispatcher.CurrentDispatcher;
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
                var engine=new RemoteControl(server,transport,ct,log,status,fail);
                ready.SetResult(engine);Dispatcher.Run();
            }
            catch(Exception e){ready.TrySetException(e);}
        }){IsBackground=true,Name="MouseBridge Input Engine"};
        thread.SetApartmentState(ApartmentState.STA);thread.Start();return ready.Task;
    }
    private RemoteControl(bool server,IMessageTransport transport,CancellationToken ct,Action<string> log,Action<string> status,Action<Exception> fail)
    {
        this.server=server;this.log=log;this.status=status;this.fail=fail;dispatcher=Dispatcher.CurrentDispatcher;
        lifetime=CancellationTokenSource.CreateLinkedTokenSource(ct);
        lastTick=Environment.TickCount64;
        timer=new System.Threading.Timer(_=>
        {
            if(Interlocked.Exchange(ref tickScheduled,1)!=0)return;
            try{_=dispatcher.InvokeAsync(()=>{try{Tick();}catch(Exception e){Fail(e);}finally{Volatile.Write(ref tickScheduled,0);}},DispatcherPriority.Send);}
            catch{Volatile.Write(ref tickScheduled,0);}
        },null,8,8);
        _=Task.Run(async()=>
        {
            try
            {
                while(!lifetime.IsCancellationRequested)
                {
                    var message=await outgoing.ReadAsync(lifetime.Token).ConfigureAwait(false);
                    using var timeout=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);timeout.CancelAfter(1500);
                    await transport.SendAsync(message,timeout.Token).ConfigureAwait(false);
                }
            }
            catch(Exception e) when(!lifetime.IsCancellationRequested){_=dispatcher.InvokeAsync(()=>Fail(e));}
            catch(Exception) when(lifetime.IsCancellationRequested){}
        });
        status(server?(rx==Guid.Empty?"Secondary ready to receive input":"Receiving input from main PC"):$"Main PC · {ShortcutName} to switch control");
        log("Independent input engine started · 8ms target batching");
    }
    private void Queue(Message message)
    {if(!disposed&&!outgoing.TryWrite(message))Fail(new IOException("Input queue is full. Returning control to this PC."));}
    private void Fail(Exception e)
    {if(disposed||failed)return;failed=true;StopSender("Communication error",false);StopReceiver("Communication error",false);fail(e);}
    public void SetArmed(bool armed)
    {
        if(!dispatcher.CheckAccess()){dispatcher.Invoke(()=>SetArmed(armed));return;}
        if(server&&armed)throw new IOException("Input capture is available only on the main PC.");
        if(!armed){StopSender("Switch shortcut disabled",true);capture?.Dispose();capture=null;return;}
        if(capture!=null)return;
        capture=new InputCapture(shortcut,action=>{_=dispatcher.InvokeAsync(action);});
        capture.Toggle+=()=>{try{Toggle();}catch(Exception e){Fail(e);}};
        capture.LocalMove+=(x,y,dx)=>{try{LocalEdgeMove(x,y,dx);}catch(Exception ex){_=dispatcher.InvokeAsync(()=>Fail(ex));}};
        capture.CopyRequested+=MarkCopy;
        capture.PasteRequested+=MarkDeferredPaste;
        capture.Captured+=a=>{try{Capture(a);}catch(Exception e){Fail(e);}};
        status($"Main PC active · {ShortcutName} to switch");log($"Switch shortcut enabled: {ShortcutName}. The matching shortcut is reserved locally.");
    }
    private void Toggle()
    {
        if(tx!=Guid.Empty){BeginReturnClipboard();return;}
        if(pending!=Guid.Empty){StopSender("Switch cancelled",true);return;}
        if(clipboardReturn!=Guid.Empty){log("Wait for clipboard receive to finish before switching again.");return;}
        if((capture?.OtherKeysHeld()??NativeInput.OtherKeysHeld(shortcut.Key))){log($"Release other keys/buttons, then press {ShortcutName}.");return;}
        pending=Guid.NewGuid();requested=Environment.TickCount64;
        Queue(new(MessageKind.ControlStart,InputWire.Session(pending,InputWire.Version)));
        QueueEdgeStart();
        if(!disposed&&!failed)BeginStartClipboard(pending);
        status($"Waiting for secondary · {ShortcutName} to cancel");log("Secondary input control requested");
    }
    private void Capture(InputAction a)
    {
        if(tx==Guid.Empty)return;
        if(a.Kind==InputActionKind.Move&&a.A==0&&a.B==0)return;
        if(a.Kind==InputActionKind.Move&&actions.Count>0&&actions[^1].Kind==InputActionKind.Move)
        {var old=actions[^1];int dx=old.A+a.A,dy=old.B+a.B;if(Math.Abs(dx)<=32767&&Math.Abs(dy)<=32767){actions[^1]=new(InputActionKind.Move,dx,dy);return;}}
        if(actions.Count>=InputWire.MaxActions){StopSender("Input buffer is full",true);return;}actions.Add(a);
    }
    private void Tick()
    {
        if(disposed)return;
        long now=Environment.TickCount64;
        if(now-lastTick>500)log($"Input engine delay: {now-lastTick}ms");lastTick=now;
        ClipboardTick(now);
        if(server&&rx!=Guid.Empty)PollReceiverEdge();
        if(pending!=Guid.Empty&&now-requested>8000)StopSender("Control request timed out",true);
        if(tx!=Guid.Empty)
        {
            if(now-lastAck>2000){StopSender("No control response for 2 seconds",true);return;}
            if(actions.Count>0){var batch=actions.ToArray();actions.Clear();Queue(new(MessageKind.InputBatch,InputWire.Encode(tx,batch)));sentActions+=batch.Length;}
            if(disposed||failed||tx==Guid.Empty)return;
            if(now-lastPulse>=500){lastPulse=now;Queue(new(MessageKind.Heartbeat,InputWire.Session(tx)));}
        }
        else if(!server&&KeepAlive&&now-lastPulse>=1000)
        {lastPulse=now;Queue(new(MessageKind.LinkPulse,InputWire.Session(pulseId)));}
        if(rx!=Guid.Empty&&(!AllowReceive||(clipboardStart==Guid.Empty&&lease.Expired(now))))StopReceiver(AllowReceive?"No input/heartbeat for 2 seconds":"Input permission disabled",true);
        if(now-lastSummary>=5000&&(sentActions>0||receivedActions>0))
        {lastSummary=now;log($"Input totals: TX={sentActions}, RX={receivedActions}, queued={outgoing.Count}, merged movements={outgoing.MergedMoves} (content not logged)");sentActions=receivedActions=0;}
    }
    public Task HandleAsync(Message m)
    {
        if(disposed)return Task.CompletedTask;
        return dispatcher.InvokeAsync(()=>Handle(m),DispatcherPriority.Send).Task;
    }
    private void Handle(Message m)
    {
        if(disposed)return;
        switch(m.Kind)
        {
            case MessageKind.ControlStart:
            {
                Guid id=InputWire.ReadSession(m.Payload,true);
                bool accepted=lease.TryOpen(id,m.Payload[16],server&&AllowReceive,Environment.TickCount64);
                if(accepted)
                {
                    edgeIndicator.Hide();receiverEdge=false;returnRequested=false;lastReceiverDx=0;
                    CancelClipboard();NewClipboardWork();clipboardStart=id;clipboardDeadline=Environment.TickCount64+8000;
                    expectedClipboardSequence=WindowsClipboard.GetClipboardSequenceNumber();lastCopySequence=null;
                    status("Secondary · receiving switch clipboard");log("Input session accepted; waiting for clipboard before granting control");
                }
                else {log("Input request rejected: check role, protocol version and active session");Queue(new(MessageKind.ControlGrant,InputWire.Session(id,0)));}break;
            }
            case MessageKind.ControlGrant:
            {
                Guid id=InputWire.ReadSession(m.Payload,true);if(m.Payload[16]>1)throw new InvalidDataException("Invalid grant");
                if(server)throw new InvalidDataException("Unexpected control grant");
                if(id!=pending){if(m.Payload[16]==1)Queue(new(MessageKind.ControlStop,InputWire.Session(id)));break;}
                if(m.Payload[16]==0){StopSender("Secondary input is unavailable or the protocol version differs.",false);break;}
                if(capture==null){StopSender("Input hooks are no longer active.",true);break;}
                pendingClipboardGrant=true;lastAck=Environment.TickCount64;lastPulse=0;
                if((capture?.OtherKeysHeld()??NativeInput.OtherKeysHeld(shortcut.Key)))
                {
                    if(!pastePending)StopSender("Release other keys/buttons before switching.",true);
                    else status("Switch ready · release Ctrl+V to paste on the secondary PC");
                    break;
                }
                ActivateGrantedControl();break;
            }
            case MessageKind.InputBatch:
            {
                var batch=InputWire.Decode(m.Payload);
                if(!server||!AllowReceive||batch.Session!=rx)break;
                if(!lease.Touch(batch.Session,Environment.TickCount64))break;
                foreach(var a in batch.Actions)
                {
                    if(a.Kind==InputActionKind.Key&&(a.C&1)==0&&a.A is 0x43 or 0x2D&&injector.ControlDown)MarkCopy();
                    injector.Apply(a);CheckReceiverEdge(a);
                }receivedActions+=batch.Actions.Length;break;
            }
            case MessageKind.EdgeConfigure:
            case MessageKind.EdgeEnter:
            case MessageKind.EdgeReturn:HandleEdge(m);break;
            case MessageKind.ClipboardBegin:
            case MessageKind.ClipboardChunk:
            case MessageKind.ClipboardEnd:HandleClipboard(m);break;
            case MessageKind.ControlReturn:
            {
                Guid id=InputWire.ReadSession(m.Payload);
                if(!server||id!=rx)break;
                uint? baseline=RecentCopyBaseline();StopReceiver("Returned to main",false);
                clipboardReturnOut=id;var token=NewClipboardWork();_=SendClipboardAsync(id,ClipboardFlow.ToMain,baseline,token);break;
            }
            case MessageKind.ControlStop:
            {
                Guid id=InputWire.ReadSession(m.Payload);
                if(rx==id)StopReceiver("Peer ended the control session",false);
                if(tx==id||pending==id||clipboardReturn==id)StopSender("Peer ended the control session",false);break;
            }
            case MessageKind.Heartbeat:
            {
                Guid id=InputWire.ReadSession(m.Payload);
                if(server&&AllowReceive&&id==rx){lease.Touch(id,Environment.TickCount64);Queue(new(MessageKind.HeartbeatAck,InputWire.Session(id)));}break;
            }
            case MessageKind.HeartbeatAck:
                var ack=InputWire.ReadSession(m.Payload);if(ack==tx||(pendingClipboardGrant&&ack==pending))lastAck=Environment.TickCount64;break;
            case MessageKind.LinkPulse:
                Queue(new(MessageKind.LinkPulseAck,InputWire.Session(InputWire.ReadSession(m.Payload))));break;
            case MessageKind.LinkPulseAck:InputWire.ReadSession(m.Payload);break;
        }
    }
    public void StopSender(string reason,bool notify)
    {
        EdgeSenderStopped();
        Guid id=tx!=Guid.Empty?tx:pending!=Guid.Empty?pending:clipboardReturn;tx=pending=Guid.Empty;actions.Clear();capture?.Stop();CancelClipboard();
        if(id!=Guid.Empty)
        {
            outgoing.Clear();
            if(notify)Queue(new(MessageKind.ControlStop,InputWire.Session(id)));
            log("Main PC restored: "+reason);status($"Main PC active · {ShortcutName} to switch");
        }
    }
    private void StopReceiver(string reason,bool notify)
    {
        EdgeReceiverStopped(reason=="Returned to main");
        Guid id=lease.Revoke();CancelClipboard();int releaseFailures=injector.ReleaseAll();
        if(releaseFailures>0)log($"Failed to release {releaseFailures} input states on secondary. Check key states in a normal-privilege app.");
        if(id!=Guid.Empty){if(notify)Queue(new(MessageKind.ControlStop,InputWire.Session(id)));log("Secondary input released: "+reason);status("Secondary · input session ended");}
    }
    public void SetKeepAlive(bool enabled)
    {if(!dispatcher.CheckAccess()){dispatcher.Invoke(()=>SetKeepAlive(enabled));return;}KeepAlive=enabled;}
    public void ReceivePermissionChanged(bool allowed)
    {if(!dispatcher.CheckAccess()){dispatcher.Invoke(()=>ReceivePermissionChanged(allowed));return;}AllowReceive=allowed;if(!allowed)StopReceiver("Input permission disabled",true);}
    public void Dispose()
    {
        if(disposed)return;
        if(!dispatcher.CheckAccess()){if(!dispatcher.HasShutdownStarted)dispatcher.Invoke(Dispose);return;}
        StopSender("Connection ended",false);StopReceiver("Connection ended",false);disposed=true;
        edgeIndicator.Dispose();timer.Dispose();capture?.Dispose();capture=null;lifetime.Cancel();clipboard.Dispose();outgoing.Clear();dispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
    }
}
