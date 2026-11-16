using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;
using System.Windows.Threading;
using MouseBridge.Core;
namespace MouseBridge.App;
internal enum ClipboardWriteStatus { Applied, ChangedLocally, NoText, Busy }
internal sealed record ClipboardWriteResult(ClipboardWriteStatus Status,uint Sequence=0);
// A dedicated STA prevents delayed clipboard owners from blocking hooks or heartbeats.
internal sealed class WindowsClipboard:IDisposable
{
    private readonly TaskCompletionSource<Dispatcher> ready=new(TaskCreationOptions.RunContinuationsAsynchronously);
    private IntPtr owner;
    private bool disposed;
    [DllImport("user32.dll")] internal static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll")] private static extern bool OpenClipboard(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool CloseClipboard();
    [DllImport("user32.dll")] private static extern bool IsClipboardFormatAvailable(uint format);
    [DllImport("user32.dll")] private static extern IntPtr GetClipboardData(uint format);
    [DllImport("user32.dll")] private static extern bool EmptyClipboard();
    [DllImport("user32.dll")] private static extern IntPtr SetClipboardData(uint format,IntPtr memory);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalAlloc(uint flags,UIntPtr bytes);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalLock(IntPtr memory);
    [DllImport("kernel32.dll")] private static extern bool GlobalUnlock(IntPtr memory);
    [DllImport("kernel32.dll")] private static extern UIntPtr GlobalSize(IntPtr memory);
    [DllImport("kernel32.dll")] private static extern IntPtr GlobalFree(IntPtr memory);
    public WindowsClipboard()
    {
        var thread=new Thread(()=>
        {
            try
            {
                var dispatcher=Dispatcher.CurrentDispatcher;SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(dispatcher));
                using var window=new HwndSource(new HwndSourceParameters("MouseBridge Clipboard"){ParentWindow=new IntPtr(-3),WindowStyle=0,Width=0,Height=0});
                owner=window.Handle;ready.SetResult(dispatcher);Dispatcher.Run();
            }
            catch(Exception e){ready.TrySetException(e);}
        }){IsBackground=true,Name="MouseBridge Clipboard"};thread.SetApartmentState(ApartmentState.STA);thread.Start();
    }
    public async Task<ClipboardSnapshot> ReadAsync(uint? copyBaseline,CancellationToken ct)
    {
        var d=await ready.Task.WaitAsync(ct).ConfigureAwait(false);
        var op=d.InvokeAsync(async()=>
        {
            // Ctrl+C may still be in the target application's input queue.
            if(copyBaseline.HasValue)
            {for(int i=0;i<20&&GetClipboardSequenceNumber()==copyBaseline.Value;i++)await Task.Delay(20,ct);}
            else await Task.Delay(60,ct);
            for(int attempt=0;attempt<20;attempt++)
            {
                ct.ThrowIfCancellationRequested();
                if(!OpenClipboard(owner)){await Task.Delay(15,ct);continue;}
                try
                {
                    if(!IsClipboardFormatAvailable(13))return new ClipboardSnapshot(ClipboardPayloadKind.NoText);
                    IntPtr handle=GetClipboardData(13);ct.ThrowIfCancellationRequested();if(handle==IntPtr.Zero)continue;
                    ulong size=GlobalSize(handle).ToUInt64();if(size>2UL*(ClipboardWire.MaxBytes+1))return new ClipboardSnapshot(ClipboardPayloadKind.Unavailable);
                    IntPtr pointer=GlobalLock(handle);if(pointer==IntPtr.Zero)continue;
                    try
                    {
                        string text=Marshal.PtrToStringUni(pointer,(int)(size/2))??"";int zero=text.IndexOf('\0');if(zero>=0)text=text[..zero];
                        if(new UTF8Encoding(false,true).GetByteCount(text)>ClipboardWire.MaxBytes)return new ClipboardSnapshot(ClipboardPayloadKind.Unavailable);
                        return new ClipboardSnapshot(ClipboardPayloadKind.Text,text);
                    }
                    finally{GlobalUnlock(handle);}
                }
                finally{CloseClipboard();}
            }
            return new ClipboardSnapshot(ClipboardPayloadKind.Unavailable);
        });
        return await op.Task.Unwrap().WaitAsync(ct).ConfigureAwait(false);
    }
    public async Task<ClipboardWriteResult> WriteAsync(ClipboardSnapshot snapshot,uint expected,CancellationToken ct)
    {
        if(snapshot.Kind!=ClipboardPayloadKind.Text)return new(ClipboardWriteStatus.NoText);
        var d=await ready.Task.WaitAsync(ct).ConfigureAwait(false);
        var op=d.InvokeAsync(async()=>
        {
            byte[] bytes=Encoding.Unicode.GetBytes(snapshot.Text!+'\0');
            for(int attempt=0;attempt<20;attempt++)
            {
                ct.ThrowIfCancellationRequested();
                if(!OpenClipboard(owner)){await Task.Delay(15,ct);continue;}
                IntPtr memory=IntPtr.Zero;
                try
                {
                    ct.ThrowIfCancellationRequested();if(GetClipboardSequenceNumber()!=expected)return new ClipboardWriteResult(ClipboardWriteStatus.ChangedLocally);
                    memory=GlobalAlloc(0x42,(UIntPtr)bytes.Length);if(memory==IntPtr.Zero)return new ClipboardWriteResult(ClipboardWriteStatus.Busy);
                    IntPtr p=GlobalLock(memory);if(p==IntPtr.Zero)return new ClipboardWriteResult(ClipboardWriteStatus.Busy);
                    try{Marshal.Copy(bytes,0,p,bytes.Length);}finally{GlobalUnlock(memory);}
                    ct.ThrowIfCancellationRequested();
                    if(!EmptyClipboard()||SetClipboardData(13,memory)==IntPtr.Zero)return new ClipboardWriteResult(ClipboardWriteStatus.Busy);
                    memory=IntPtr.Zero;return new ClipboardWriteResult(ClipboardWriteStatus.Applied,GetClipboardSequenceNumber());
                }
                finally{if(memory!=IntPtr.Zero)GlobalFree(memory);CloseClipboard();}
            }
            return new ClipboardWriteResult(ClipboardWriteStatus.Busy);
        });
        return await op.Task.Unwrap().WaitAsync(ct).ConfigureAwait(false);
    }
    public void Dispose()
    {
        if(disposed)return;disposed=true;
        _=ready.Task.ContinueWith(t=>{if(t.IsCompletedSuccessfully)t.Result.BeginInvokeShutdown(DispatcherPriority.Background);},TaskScheduler.Default);
    }
}
