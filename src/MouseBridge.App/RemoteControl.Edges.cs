using MouseBridge.Core;
namespace MouseBridge.App;
internal sealed partial class RemoteControl
{
    private EdgeSettings edges=EdgeSettings.Load();
    private readonly EdgeIndicator edgeIndicator=new();
    private ScreenRect sourceScreen,receiverScreen;
    private bool edgeStarting,edgeSession,receiverEdge,receiverLeft,receiverPlace,returnRequested;
    private int entryRatio;
    private long edgeCooldown,returnRequestTime,lastReceiverMove;
    private int lastReceiverDx;
    public void ChangeEdges(EdgeSettings settings)
    {
        if(!dispatcher.CheckAccess()){dispatcher.Invoke(()=>ChangeEdges(settings));return;}
        settings.Validate();
        if(tx!=Guid.Empty||pending!=Guid.Empty||rx!=Guid.Empty||clipboardReturn!=Guid.Empty)throw new InvalidOperationException("Return to the main PC before changing screen settings.");
        edges=settings;edgeCooldown=Environment.TickCount64+750;
    }
    private void LocalEdgeMove(int x,int y,int dx)
    {
        if(server||!edges.Enabled||tx!=Guid.Empty||pending!=Guid.Empty||clipboardReturn!=Guid.Empty||Environment.TickCount64<edgeCooldown||capture==null||capture.OtherKeysHeld())return;
        var screen=NativeInput.ScreenAt(x,y);
        if(!ScreenEdge.Hit(screen,x,y,edges.SecondaryLeft,dx)||!NativeInput.OutsideDesktop(screen,y,edges.SecondaryLeft))return;
        sourceScreen=screen;entryRatio=ScreenEdge.Ratio(screen,y);
        edgeStarting=true;
        // Leave the low-level hook before clipboard/control work or moving windows.
        _=dispatcher.InvokeAsync(()=>{try{if(edgeStarting&&!disposed&&capture!=null&&!capture.OtherKeysHeld()&&StillAtSourceEdge())Toggle();}catch(Exception ex){Fail(ex);}finally{edgeStarting=false;}});
        edgeCooldown=Environment.TickCount64+750;
    }
    private bool StillAtSourceEdge()
    {
        return NativeInput.GetCursorPos(out var point)&&ScreenEdge.InPortal(sourceScreen,point.Y)&&
            (edges.SecondaryLeft?point.X<=sourceScreen.Left:point.X>=sourceScreen.Right);
    }
    private void QueueEdgeStart()
    {
        edgeSession=edges.Enabled;
        if(!edgeSession)return;
        if(!edgeStarting)
        {
            NativeInput.GetCursorPos(out var point);sourceScreen=NativeInput.ScreenForEntry(edges.SecondaryLeft);
            entryRatio=ScreenEdge.Ratio(sourceScreen,point.Y);
        }
        Queue(new(edgeStarting?MessageKind.EdgeEnter:MessageKind.EdgeConfigure,ScreenEdge.Encode(pending,edges.SecondaryLeft,entryRatio)));
        log($"Screen session: secondary {(edges.SecondaryLeft?"left":"right")}, entry={(edgeStarting?"edge":"shortcut")}, source={sourceScreen.Width}x{sourceScreen.Height}");
    }
    private void PlaceReceiverAtEdge()
    {
        if(!receiverEdge||!receiverPlace)return;
        int x=receiverLeft?receiverScreen.Left+3:receiverScreen.Right-3;
        int y=ScreenEdge.Y(receiverScreen,entryRatio);
        if(!NativeInput.SetCursorPos(x,y))throw new InvalidOperationException("Cannot position the secondary cursor.");
        edgeCooldown=Environment.TickCount64+600;returnRequested=false;
    }
    private void CheckReceiverEdge(InputAction action)
    {
        if(action.Kind!=InputActionKind.Move)return;
        lastReceiverDx=action.A;lastReceiverMove=Environment.TickCount64;
        PollReceiverEdge();
    }
    private void PollReceiverEdge()
    {
        if(!receiverEdge||clipboardStart!=Guid.Empty||lastReceiverDx==0||injector.AnyHeld||Environment.TickCount64<edgeCooldown)return;
        if(Environment.TickCount64-lastReceiverMove>150)return;
        if(returnRequested&&Environment.TickCount64-returnRequestTime<500)return;
        if(!NativeInput.GetCursorPos(out var point))return;
        var screen=NativeInput.ScreenAt(point.X,point.Y);
        if(!ScreenEdge.Hit(screen,point.X,point.Y,receiverLeft,lastReceiverDx)||!NativeInput.OutsideDesktop(screen,point.Y,receiverLeft))return;
        int ratio=ScreenEdge.Ratio(screen,point.Y);
        returnRequested=true;returnRequestTime=Environment.TickCount64;receiverScreen=screen;
        Queue(new(MessageKind.EdgeReturn,ScreenEdge.Encode(rx,receiverLeft,ratio)));
    }
    private void HandleEdge(Message m)
    {
        var e=ScreenEdge.Decode(m.Payload);
        if(m.Kind is MessageKind.EdgeEnter or MessageKind.EdgeConfigure)
        {
            if(!server||rx!=e.Session||clipboardStart!=e.Session)return;
            receiverEdge=true;receiverPlace=m.Kind==MessageKind.EdgeEnter;receiverLeft=!e.Left;entryRatio=e.Ratio;returnRequested=false;
            receiverScreen=NativeInput.ScreenForEntry(receiverLeft);
        }
        else if(!server&&tx==e.Session&&edgeSession&&e.Left!=edges.SecondaryLeft)
        {
            if(capture==null||capture.OtherKeysHeld())return;
            log("Edge return accepted; restoring main cursor at matching height");
            BeginReturnClipboard();
            int x=edges.SecondaryLeft?sourceScreen.Left+3:sourceScreen.Right-3;
            int y=ScreenEdge.Y(sourceScreen,e.Ratio);
            NativeInput.SetCursorPos(x,y);edgeCooldown=Environment.TickCount64+600;
        }
    }
    private void EdgeActivated()
    {if(edgeSession)edgeIndicator.Show(sourceScreen,edges.SecondaryLeft,edges);}
    private void EdgeSenderStopped(){edgeIndicator.Hide();edgeSession=false;edgeStarting=false;edgeCooldown=Environment.TickCount64+600;}
    private void EdgeReceiverStopped(bool returned)
    {
        if(receiverEdge&&returned)edgeIndicator.Show(receiverScreen,receiverLeft,edges);
        else edgeIndicator.Hide();
        receiverEdge=false;returnRequested=false;lastReceiverDx=0;
    }
}
