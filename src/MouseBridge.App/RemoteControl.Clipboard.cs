using System.IO;
using System.Text;
using MouseBridge.Core;
namespace MouseBridge.App;
internal sealed partial class RemoteControl
{
    private readonly WindowsClipboard clipboard=new();
    private readonly ClipboardAssembler clipboardIncoming=new();
    private CancellationTokenSource? clipboardWork;
    private Guid clipboardStart,clipboardReturn,clipboardReturnOut;
    private uint expectedClipboardSequence;
    private uint? lastCopySequence;
    private long lastCopyTime,clipboardDeadline;
    private bool pastePending,pendingClipboardGrant;
    private IntPtr pasteWindow;
    private CancellationToken NewClipboardWork()
    {
        clipboardWork?.Cancel();clipboardWork?.Dispose();
        clipboardWork=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);clipboardWork.CancelAfter(8000);return clipboardWork.Token;
    }
    private void MarkCopy()
    {
        if(clipboardReturn!=Guid.Empty)
        {CancelClipboard();status($"Main PC active · {ShortcutName} to switch");log("Previous clipboard receive cancelled because a new local copy was requested");}
        lastCopySequence=WindowsClipboard.GetClipboardSequenceNumber();lastCopyTime=Environment.TickCount64;
    }
    private uint? RecentCopyBaseline()=>Environment.TickCount64-lastCopyTime<2000?lastCopySequence:null;
    private void MarkDeferredPaste()
    {if(clipboardReturn==Guid.Empty&&pending==Guid.Empty)return;pastePending=true;pasteWindow=NativeInput.GetForegroundWindow();}
    private void CancelClipboard()
    {
        clipboardWork?.Cancel();clipboardIncoming.Reset();clipboardStart=clipboardReturn=clipboardReturnOut=Guid.Empty;
        if(capture!=null)capture.DeferPaste=false;
        if(pastePending)log("Pending paste cancelled during clipboard transfer");pastePending=false;pendingClipboardGrant=false;
    }
    private void BeginStartClipboard(Guid id)
    {
        if(capture!=null)capture.DeferPaste=true;
        var token=NewClipboardWork();_=SendClipboardAsync(id,ClipboardFlow.ToSub,RecentCopyBaseline(),token);
    }
    private async Task SendClipboardAsync(Guid id,ClipboardFlow flow,uint? baseline,CancellationToken ct)
    {
        try
        {
            ClipboardSnapshot snapshot;
            using(var readDeadline=CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                readDeadline.CancelAfter(1400);
                try{snapshot=await clipboard.ReadAsync(baseline,readDeadline.Token);}
                catch(Exception ex) when(!ct.IsCancellationRequested)
                {log("Clipboard read skipped: "+ex.GetType().Name);snapshot=new(ClipboardPayloadKind.Unavailable);}
            }
            ct.ThrowIfCancellationRequested();
            if(disposed||(flow==ClipboardFlow.ToSub?pending!=id:clipboardReturnOut!=id))return;
            var messages=ClipboardWire.Encode(id,flow,snapshot);
            foreach(var message in messages){if(disposed||failed||ct.IsCancellationRequested)return;Queue(message);}
            log($"Clipboard TX {flow}: {snapshot.Kind}, {(snapshot.Text==null?0:Encoding.UTF8.GetByteCount(snapshot.Text))} bytes (content not logged)");
            if(flow==ClipboardFlow.ToMain)clipboardReturnOut=Guid.Empty;
        }
        catch(OperationCanceledException){}
        catch(Exception ex){if(!disposed)Fail(ex);}
    }
    private void BeginReturnClipboard()
    {
        Guid id=tx;if(id==Guid.Empty)return;
        // Preserve every already queued key-up and Ctrl+C before requesting the snapshot.
        if(actions.Count>0){var batch=actions.ToArray();actions.Clear();Queue(new(MessageKind.InputBatch,InputWire.Encode(id,batch)));sentActions+=batch.Length;}
        if(disposed||failed||tx!=id)return;
        tx=Guid.Empty;capture?.Stop();EdgeSenderStopped();clipboardReturn=id;clipboardDeadline=Environment.TickCount64+8000;
        expectedClipboardSequence=WindowsClipboard.GetClipboardSequenceNumber();clipboardIncoming.Reset();NewClipboardWork();
        if(capture!=null)capture.DeferPaste=true;
        Queue(new(MessageKind.ControlReturn,InputWire.Session(id)));
        status("Main PC active · receiving secondary clipboard");log($"{ShortcutName}: returned to main; requesting secondary clipboard");
    }
    private bool AcceptsClipboard(Guid id,ClipboardFlow flow)=>server
        ?flow==ClipboardFlow.ToSub&&clipboardStart==id&&rx==id&&AllowReceive
        :flow==ClipboardFlow.ToMain&&clipboardReturn==id;
    private void HandleClipboard(Message message)
    {
        var e=ClipboardWire.Envelope(message.Payload);
        if(!AcceptsClipboard(e.Session,e.Flow))return;
        if(server)lease.Touch(e.Session,Environment.TickCount64);
        switch(message.Kind)
        {
            case MessageKind.ClipboardBegin:clipboardIncoming.Begin(message.Payload);break;
            case MessageKind.ClipboardChunk:clipboardIncoming.Append(message.Payload);break;
            case MessageKind.ClipboardEnd:
                var snapshot=clipboardIncoming.Finish(message.Payload);
                if(snapshot!=null&&clipboardWork!=null)_=ApplyClipboardAsync(e.Session,e.Flow,snapshot,expectedClipboardSequence,clipboardWork.Token);
                break;
        }
    }
    private async Task ApplyClipboardAsync(Guid id,ClipboardFlow flow,ClipboardSnapshot snapshot,uint expected,CancellationToken ct)
    {
        try
        {
            var result=await clipboard.WriteAsync(snapshot,expected,ct);
            if(disposed||ct.IsCancellationRequested||!AcceptsClipboard(id,flow))return;
            log($"Clipboard RX {flow}: {snapshot.Kind}, {result.Status} (content not logged)");
            if(server)
            {
                if(!GrantReceiverControl(id))return;
                status("Receiving input from main PC");log("Control granted after clipboard processing");
            }
            else
            {
                clipboardReturn=Guid.Empty;if(capture!=null)capture.DeferPaste=false;
                status($"Main PC active · {ShortcutName} to switch");
                if(pastePending)
                {
                    pastePending=false;
                    if(result.Status==ClipboardWriteStatus.Applied&&WindowsClipboard.GetClipboardSequenceNumber()==result.Sequence&&pasteWindow!=IntPtr.Zero&&NativeInput.GetForegroundWindow()==pasteWindow)
                        ReplayPaste();
                    else log("Deferred paste cancelled: the target window or clipboard changed.");
                }
            }
        }
        catch(OperationCanceledException){}
        catch(Exception ex)
        {
            if(disposed)return;log("Clipboard apply failed: "+ex.GetType().Name);
            if(AcceptsClipboard(id,flow))
            {
                if(server)GrantReceiverControl(id);
                else {CancelClipboard();status($"Main PC active · {ShortcutName} to switch");}
            }
        }
    }
    private bool GrantReceiverControl(Guid id)
    {
        try
        {
            PlaceReceiverAtEdge();clipboardStart=Guid.Empty;lease.Touch(id,Environment.TickCount64);
            Queue(new(MessageKind.ControlGrant,InputWire.Session(id,1)));return !failed&&!disposed;
        }
        catch(Exception ex){Fail(ex);return false;}
    }
    private void ReplayPaste()
    {
        // Never release a physical Ctrl held by the user, or paste into a different window.
        if((NativeInput.GetAsyncKeyState(0x10)&0x8000)!=0||(NativeInput.GetAsyncKeyState(0x12)&0x8000)!=0||
            (NativeInput.GetAsyncKeyState(0x5B)&0x8000)!=0||(NativeInput.GetAsyncKeyState(0x5C)&0x8000)!=0)
        {log("Deferred paste cancelled: another modifier is held.");return;}
        bool ctrl=(NativeInput.GetAsyncKeyState(0x11)&0x8000)!=0;
        bool vDown=false;
        try
        {
            if(!ctrl)NativeInput.Inject(new(InputActionKind.Key,0xA2,0));
            NativeInput.Inject(new(InputActionKind.Key,0x56,0));vDown=true;
        }
        finally
        {
            try{if(vDown)NativeInput.Inject(new(InputActionKind.Key,0x56,0,1));}
            finally{if(!ctrl)NativeInput.Inject(new(InputActionKind.Key,0xA2,0,1));}
        }
        log("Deferred Ctrl+V processed after clipboard receive");
    }
    private void ActivateGrantedControl()
    {
        if(pending==Guid.Empty||!pendingClipboardGrant||capture==null)return;
        tx=pending;pending=Guid.Empty;pendingClipboardGrant=false;lastAck=Environment.TickCount64;lastPulse=0;
        capture.DeferPaste=false;capture.Start();EdgeActivated();
        status($"Controlling secondary · {ShortcutName} to return");log($"Secondary control started after clipboard processing. Return: {ShortcutName}");
        if(pastePending)
        {
            pastePending=false;
            InputAction[] paste=[new(InputActionKind.Key,0xA2,0x1D),new(InputActionKind.Key,0x56,0x2F),new(InputActionKind.Key,0x56,0x2F,1),new(InputActionKind.Key,0xA2,0x1D,1)];
            Queue(new(MessageKind.InputBatch,InputWire.Encode(tx,paste)));log("Deferred Ctrl+V sent to secondary after switching");
        }
    }
    private void ClipboardTick(long now)
    {
        if(pendingClipboardGrant&&pending!=Guid.Empty)
        {
            if(now-lastAck>2000){StopSender("No response while waiting to switch",true);return;}
            if(!(capture?.OtherKeysHeld()??NativeInput.OtherKeysHeld(shortcut.Key)))ActivateGrantedControl();
            else if(now-lastPulse>=500){lastPulse=now;Queue(new(MessageKind.Heartbeat,InputWire.Session(pending)));}
        }
        if(clipboardReturn!=Guid.Empty&&now>clipboardDeadline)
        {log("Secondary clipboard receive timed out; existing clipboard preserved");CancelClipboard();status($"Main PC active · {ShortcutName} to switch");}
        if(clipboardStart!=Guid.Empty&&now>clipboardDeadline)
        {log("Switch clipboard transfer timed out");StopReceiver("Switch clipboard transfer timed out",true);}
    }
}
