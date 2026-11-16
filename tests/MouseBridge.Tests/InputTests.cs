using MouseBridge.Core;
using Xunit;
namespace MouseBridge.Tests;
public class InputTests
{
    [Fact] public void NativeInputLayoutMatchesWindowsAbi()
    {
        Assert.Equal(IntPtr.Size==8?40:28,System.Runtime.InteropServices.Marshal.SizeOf<MouseBridge.App.NativeInput.Input>());
        Assert.Equal(IntPtr.Size==8?8:4,(int)System.Runtime.InteropServices.Marshal.OffsetOf<MouseBridge.App.NativeInput.Input>("Union"));
    }

    [Fact] public void InputBatchRoundTrip()
    {
        Guid id=Guid.NewGuid();InputAction[] a=[new(InputActionKind.Key,65,30),new(InputActionKind.Key,65,30,1),new(InputActionKind.Move,-45,120),new(InputActionKind.Button,5,1),new(InputActionKind.Button,5,0),new(InputActionKind.Wheel,-120,1)];
        var b=InputWire.Decode(InputWire.Encode(id,a));Assert.Equal(id,b.Session);Assert.Equal(a,b.Actions);
    }
    [Fact] public void BatchRejectsTruncationAndTrailingBytes()
    {
        var b=InputWire.Encode(Guid.NewGuid(),[new(InputActionKind.Move,1,2)]);
        Assert.Throws<InvalidDataException>(()=>InputWire.Decode(b[..^1]));
        Assert.Throws<InvalidDataException>(()=>InputWire.Decode([..b,0]));
    }
    [Fact] public void RejectsEmptyAndOversizedBatch()
    {
        Assert.Throws<InvalidDataException>(()=>InputWire.Encode(Guid.NewGuid(),[]));
        Assert.Throws<InvalidDataException>(()=>InputWire.Encode(Guid.NewGuid(),Enumerable.Repeat(new InputAction(InputActionKind.Move,1,2),129).ToArray()));
    }
    [Theory]
    [InlineData(1,0,1,0)] [InlineData(1,65,256,0)] [InlineData(1,65,30,4)]
    [InlineData(2,40000,0,0)] [InlineData(3,6,1,0)] [InlineData(3,1,2,0)]
    [InlineData(4,120,2,0)] [InlineData(99,0,0,0)]
    public void InvalidActionsRejected(byte k,int a,int b,int c)=>Assert.Throws<InvalidDataException>(()=>InputWire.Validate(new((InputActionKind)k,a,b,c)));
    [Fact] public void MaximumBatchAndSessionRoundTrip()
    {
        Guid id=Guid.NewGuid();var b=InputWire.Encode(id,Enumerable.Repeat(new InputAction(InputActionKind.Move,1,2),128).ToArray());
        Assert.Equal(128,InputWire.Decode(b).Actions.Length);
        Assert.Equal(id,InputWire.ReadSession(InputWire.Session(id,InputWire.Version),true));
        Assert.Throws<InvalidDataException>(()=>InputWire.ReadSession(new byte[16]));
        Assert.Throws<InvalidDataException>(()=>InputWire.ReadSession(new byte[17]));
    }
    [Fact] public void LeaseRequiresConsentVersionAndSessionIdentity()
    {
        var lease=new ControlLease();Guid id=Guid.NewGuid();
        Assert.False(lease.TryOpen(id,InputWire.Version,false,100));Assert.False(lease.TryOpen(id,99,true,100));
        Assert.True(lease.TryOpen(id,InputWire.Version,true,100));Assert.False(lease.TryOpen(Guid.NewGuid(),InputWire.Version,true,100));
        Assert.False(lease.Touch(Guid.NewGuid(),200));Assert.True(lease.Touch(id,200));
        Assert.False(lease.Expired(2200));Assert.True(lease.Expired(2201));Assert.Equal(id,lease.Revoke());Assert.False(lease.Touch(id,2300));
        Assert.True(lease.TryOpen(Guid.NewGuid(),InputWire.Version,true,2400));
    }
}
