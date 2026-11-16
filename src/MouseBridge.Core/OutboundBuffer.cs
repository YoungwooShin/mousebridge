namespace MouseBridge.Core;
// Never discard keys/buttons. Merge ONLY consecutive unsent pure movement batches.
public sealed class OutboundBuffer(int capacity=64)
{
    private readonly object sync=new();
    private readonly LinkedList<Message> queue=[];
    private readonly SemaphoreSlim available=new(0);
    public long MergedMoves{get;private set;}
    public int Count{get{lock(sync)return queue.Count;}}
    public bool TryWrite(Message message)
    {
        lock(sync)
        {
            if(queue.Last!=null&&TryMerge(queue.Last.Value,message,out var merged))
            {queue.Last.Value=merged!;MergedMoves++;return true;}
            if(queue.Count>=capacity)return false;
            queue.AddLast(message);available.Release();return true;
        }
    }
    private static bool TryMerge(Message a,Message b,out Message? merged)
    {
        merged=null;if(a.Kind!=MessageKind.InputBatch||b.Kind!=MessageKind.InputBatch)return false;
        var x=InputWire.Decode(a.Payload);var y=InputWire.Decode(b.Payload);
        if(x.Session!=y.Session||x.Actions.Any(v=>v.Kind!=InputActionKind.Move)||y.Actions.Any(v=>v.Kind!=InputActionKind.Move))return false;
        long dx=0,dy=0;foreach(var v in x.Actions.Concat(y.Actions)){dx+=v.A;dy+=v.B;}
        if(Math.Abs(dx)>32767||Math.Abs(dy)>32767)return false;
        merged=new(MessageKind.InputBatch,InputWire.Encode(x.Session,[new(InputActionKind.Move,(int)dx,(int)dy)]));return true;
    }
    public async Task<Message> ReadAsync(CancellationToken ct)
    {
        while(true)
        {
            await available.WaitAsync(ct).ConfigureAwait(false);
            lock(sync){if(queue.First!=null){var m=queue.First.Value;queue.RemoveFirst();return m;}}
        }
    }
    public void Clear(){lock(sync){queue.Clear();while(available.Wait(0)){} }}
}
