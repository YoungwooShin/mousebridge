using MouseBridge.Core;
using Xunit;
namespace MouseBridge.Tests;
public class ScreenEdgeTests
{
    private static readonly ScreenRect Screen=new(-1920,-100,1920,1001);
    [Theory]
    [InlineData(-100,false)] [InlineData(-1,false)] [InlineData(0,true)] [InlineData(400,true)] [InlineData(799,true)] [InlineData(800,false)] [InlineData(900,false)]
    public void OnlyMiddleEightyPercentIsPortal(int y,bool expected)=>Assert.Equal(expected,ScreenEdge.InPortal(Screen,y));
    [Fact] public void CrossingRequiresCorrectEdgeAndOutwardDirection()
    {
        Assert.True(ScreenEdge.Hit(Screen,-1920,400,true,-1));
        Assert.False(ScreenEdge.Hit(Screen,-1919,400,true,-1));
        Assert.False(ScreenEdge.Hit(Screen,-1920,400,true,1));
        Assert.False(ScreenEdge.Hit(Screen,-1920,400,true,0));
        Assert.True(ScreenEdge.Hit(Screen,-1,400,false,1));
        Assert.False(ScreenEdge.Hit(Screen,-2,400,false,1));
        Assert.False(ScreenEdge.Hit(Screen,-1,400,false,-1));
        Assert.False(ScreenEdge.Hit(Screen,-1920,-100,true,-1));
    }
    [Fact] public void RelativeHeightMapsAcrossDifferentResolutionAndNegativeOrigins()
    {
        var other=new ScreenRect(0,200,2560,2001);
        Assert.Equal(5000,ScreenEdge.Ratio(Screen,400));
        Assert.Equal(1200,ScreenEdge.Y(other,5000));
        Assert.Equal(0,ScreenEdge.Ratio(Screen,-500));
        Assert.Equal(10000,ScreenEdge.Ratio(Screen,2000));
        Assert.Equal(200,ScreenEdge.Y(other,0));Assert.Equal(2200,ScreenEdge.Y(other,10000));
        Assert.False(ScreenEdge.InPortal(new(0,0,1,1),0));
    }
    [Theory] [InlineData(true)] [InlineData(false)]
    public void EdgeMessagesPreserveSessionSideAndHeight(bool left)
    {
        var id=Guid.NewGuid();var bytes=ScreenEdge.Encode(id,left,4321);
        Assert.Equal((id,left,4321),ScreenEdge.Decode(bytes));
        foreach(var kind in new[]{MessageKind.EdgeEnter,MessageKind.EdgeReturn,MessageKind.EdgeConfigure})
            Assert.Equal(kind,Protocol.DecodeBody(Protocol.Encode(new(kind,bytes))[4..]).Kind);
    }
    [Fact] public void MalformedOrOutOfRangeMessagesAreRejected()
    {
        var bytes=ScreenEdge.Encode(Guid.NewGuid(),true,5000);
        Assert.Throws<InvalidDataException>(()=>ScreenEdge.Decode(bytes[..20]));
        bytes[16]=2;Assert.Throws<InvalidDataException>(()=>ScreenEdge.Decode(bytes));
        Assert.Throws<InvalidDataException>(()=>ScreenEdge.Encode(Guid.NewGuid(),false,10001));
        Assert.Throws<InvalidDataException>(()=>ScreenEdge.Encode(Guid.Empty,false,0));
        bytes=ScreenEdge.Encode(Guid.NewGuid(),true,5000);Array.Fill(bytes,(byte)255,17,4);
        Assert.Throws<InvalidDataException>(()=>ScreenEdge.Decode(bytes));
    }
}
