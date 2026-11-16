using System.Buffers.Binary;
namespace MouseBridge.Core;
public readonly record struct ScreenRect(int Left,int Top,int Width,int Height)
{
    public int Right=>Left+Width-1;
    public int Bottom=>Top+Height-1;
    public bool Contains(int x,int y)=>x>=Left&&x<=Right&&y>=Top&&y<=Bottom;
}
public static class ScreenEdge
{
    public static bool InPortal(ScreenRect r,int y)=>r.Height>1&&(y-r.Top)/(double)(r.Height-1)>=0.1&&(y-r.Top)/(double)(r.Height-1)<0.9;
    public static int Ratio(ScreenRect r,int y)=>(int)Math.Clamp(Math.Round((y-r.Top)*10000.0/Math.Max(1,r.Height-1)),0,10000);
    public static int Y(ScreenRect r,int ratio)=>r.Top+(int)Math.Round(Math.Clamp(ratio,0,10000)*(r.Height-1)/10000.0);
    public static bool Hit(ScreenRect r,int x,int y,bool left,int dx)=>InPortal(r,y)&&(left?x<=r.Left&&dx<0:x>=r.Right&&dx>0);
    public static byte[] Encode(Guid session,bool left,int ratio)
    {
        if(ratio is <0 or >10000)throw new InvalidDataException("Invalid edge position");
        var b=new byte[21];InputWire.Session(session).CopyTo(b,0);b[16]=(byte)(left?1:0);BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(17),ratio);return b;
    }
    public static (Guid Session,bool Left,int Ratio) Decode(byte[] b)
    {
        if(b.Length!=21||b[16]>1)throw new InvalidDataException("Invalid edge frame");
        var id=InputWire.ReadSession(b[..16]);int ratio=BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(17));
        if(ratio is <0 or >10000)throw new InvalidDataException("Invalid edge position");return(id,b[16]==1,ratio);
    }
}
