using System.Buffers.Binary;
using System.Text;
namespace MouseBridge.Core;
public enum ClipboardFlow:byte { ToSub=1, ToMain=2 }
public enum ClipboardPayloadKind:byte { NoText=0, Text=1, Unavailable=2 }
public sealed record ClipboardSnapshot(ClipboardPayloadKind Kind,string? Text=null);
public static class ClipboardWire
{
    public const int MaxBytes=256*1024, ChunkBytes=8192;
    internal static readonly UTF8Encoding Utf8=new(false,true);
    public static (Guid Session,ClipboardFlow Flow) Envelope(byte[] b)
    {
        if(b.Length<17)throw new InvalidDataException("Truncated clipboard envelope");
        Guid id=InputWire.ReadSession(b[..16]);var flow=(ClipboardFlow)b[16];
        if(!Enum.IsDefined(flow))throw new InvalidDataException("Invalid clipboard direction");return(id,flow);
    }
    public static byte[] Envelope(Guid id,ClipboardFlow flow)
    {
        if(!Enum.IsDefined(flow))throw new InvalidDataException("Invalid clipboard direction");
        return InputWire.Session(id,(byte)flow);
    }
    public static IReadOnlyList<Message> Encode(Guid id,ClipboardFlow flow,ClipboardSnapshot snapshot)
    {
        if(!Enum.IsDefined(snapshot.Kind)||(snapshot.Kind==ClipboardPayloadKind.Text)!=(snapshot.Text!=null))throw new InvalidDataException("Invalid clipboard snapshot");
        if(snapshot.Text!=null&&Utf8.GetByteCount(snapshot.Text)>MaxBytes)throw new InvalidDataException("Clipboard text exceeds limit");
        byte[] bytes=snapshot.Text==null?[]:Utf8.GetBytes(snapshot.Text);
        if(bytes.Length>MaxBytes)throw new InvalidDataException("Clipboard text exceeds limit");
        byte[] prefix=Envelope(id,flow),begin=new byte[22];prefix.CopyTo(begin,0);begin[17]=(byte)snapshot.Kind;BinaryPrimitives.WriteInt32LittleEndian(begin.AsSpan(18),bytes.Length);
        var result=new List<Message>{new(MessageKind.ClipboardBegin,begin)};
        for(int offset=0;offset<bytes.Length;offset+=ChunkBytes)
        {
            int n=Math.Min(ChunkBytes,bytes.Length-offset);var chunk=new byte[21+n];prefix.CopyTo(chunk,0);BinaryPrimitives.WriteInt32LittleEndian(chunk.AsSpan(17),offset);bytes.AsSpan(offset,n).CopyTo(chunk.AsSpan(21));result.Add(new(MessageKind.ClipboardChunk,chunk));
        }
        result.Add(new(MessageKind.ClipboardEnd,prefix));return result;
    }
}
public sealed class ClipboardAssembler
{
    private Guid session;private ClipboardFlow flow;private ClipboardPayloadKind kind;
    private byte[] data=[];private int received;
    public bool Matches(byte[] b)
    {var e=ClipboardWire.Envelope(b);return session!=Guid.Empty&&e.Session==session&&e.Flow==flow;}
    public void Begin(byte[] b)
    {
        var e=ClipboardWire.Envelope(b);if(b.Length!=22)throw new InvalidDataException("Invalid clipboard begin");
        var k=(ClipboardPayloadKind)b[17];int n=BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(18));
        if(!Enum.IsDefined(k)||n<0||n>ClipboardWire.MaxBytes||(k!=ClipboardPayloadKind.Text&&n!=0))throw new InvalidDataException("Invalid clipboard size/type");
        session=e.Session;flow=e.Flow;kind=k;data=new byte[n];received=0;
    }
    public bool Append(byte[] b)
    {
        if(!Matches(b))return false;
        if(b.Length<=21||b.Length>21+ClipboardWire.ChunkBytes)throw new InvalidDataException("Invalid clipboard chunk");
        int offset=BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(17)),n=b.Length-21;
        if(offset!=received||n>data.Length-received)throw new InvalidDataException("Clipboard offset/length mismatch");
        b.AsSpan(21).CopyTo(data.AsSpan(received));received+=n;return true;
    }
    public ClipboardSnapshot? Finish(byte[] b)
    {
        if(!Matches(b))return null;
        if(b.Length!=17||received!=data.Length)throw new InvalidDataException("Incomplete clipboard transfer");
        var snapshot=new ClipboardSnapshot(kind,kind==ClipboardPayloadKind.Text?ClipboardWire.Utf8.GetString(data):null);Reset();return snapshot;
    }
    public void Reset(){session=Guid.Empty;data=[];received=0;}
}
