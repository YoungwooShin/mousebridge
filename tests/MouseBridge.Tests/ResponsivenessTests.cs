using MouseBridge.Core;
using Xunit;
namespace MouseBridge.Tests;
public class ResponsivenessTests
{
    [Theory]
    [InlineData(0xA1,0x36,2,0xA1,0x36,0)]
    [InlineData(0xA1,0x36,3,0xA1,0x36,1)]
    [InlineData(0x10,0x36,2,0xA1,0x36,0)]
    [InlineData(0x10,0x2A,2,0xA0,0x2A,0)]
    [InlineData(0xA3,0x1D,2,0xA3,0x1D,2)]
    public void ShiftUsesScanCodeNotExtendedFlag(int vk,int scan,int flags,int expectedVk,int expectedScan,int expectedFlags)
    {Assert.Equal(new InputAction(InputActionKind.Key,expectedVk,expectedScan,expectedFlags),KeyEncoding.Normalize(new InputAction(InputActionKind.Key,vk,scan,flags)));}
    private static Message Batch(Guid id,params InputAction[] a)=>new(MessageKind.InputBatch,InputWire.Encode(id,a));
    [Fact] public async Task QueuedMovementMergesWithoutLosingDistance()
    {
        var q=new OutboundBuffer(2);Guid id=Guid.NewGuid();
        for(int i=0;i<100;i++)Assert.True(q.TryWrite(Batch(id,new InputAction(InputActionKind.Move,1,-2))));
        Assert.Equal(1,q.Count);Assert.Equal(99,q.MergedMoves);
        var batch=InputWire.Decode((await q.ReadAsync(default)).Payload);
        Assert.Equal(new InputAction(InputActionKind.Move,100,-200),Assert.Single(batch.Actions));
    }
    [Fact] public async Task ButtonsAndKeysFormMergeBarriers()
    {
        var q=new OutboundBuffer();Guid id=Guid.NewGuid();
        InputAction[] actions=[new InputAction(InputActionKind.Move,1,2),new InputAction(InputActionKind.Button,1,1),new InputAction(InputActionKind.Move,3,4),new InputAction(InputActionKind.Key,65,30),new InputAction(InputActionKind.Button,1,0)];
        foreach(var a in actions)Assert.True(q.TryWrite(Batch(id,a)));
        foreach(var a in actions)Assert.Equal(a,Assert.Single(InputWire.Decode((await q.ReadAsync(default)).Payload).Actions));
    }
    [Fact] public void CapacityAndSessionBoundariesArePreserved()
    {
        var q=new OutboundBuffer(2);Assert.True(q.TryWrite(Batch(Guid.NewGuid(),new InputAction(InputActionKind.Move,1,1))));
        Assert.True(q.TryWrite(Batch(Guid.NewGuid(),new InputAction(InputActionKind.Move,1,1))));
        Assert.False(q.TryWrite(Batch(Guid.NewGuid(),new InputAction(InputActionKind.Key,65,30))));Assert.Equal(2,q.Count);
    }
    [Fact] public async Task ClearDiscardsOldSessionAndReadObservesCancellation()
    {
        var q=new OutboundBuffer();Guid id=Guid.NewGuid();q.TryWrite(Batch(id,new InputAction(InputActionKind.Move,1,1)));q.Clear();
        var expected=Batch(id,new InputAction(InputActionKind.Button,1,0));q.TryWrite(expected);Assert.Same(expected,await q.ReadAsync(default));
        using var ct=new CancellationTokenSource();ct.Cancel();await Assert.ThrowsAnyAsync<OperationCanceledException>(()=>q.ReadAsync(ct.Token));
    }
    [Fact] public void MergeNeverSilentlyClampsLargeMovement()
    {
        var q=new OutboundBuffer();Guid id=Guid.NewGuid();q.TryWrite(Batch(id,new InputAction(InputActionKind.Move,32767,0)));q.TryWrite(Batch(id,new InputAction(InputActionKind.Move,1,0)));Assert.Equal(2,q.Count);
    }
}
