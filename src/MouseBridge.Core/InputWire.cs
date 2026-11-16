using System.Buffers.Binary;
namespace MouseBridge.Core;
public enum InputActionKind : byte { Key=1, Move=2, Button=3, Wheel=4 }
public readonly record struct InputAction(InputActionKind Kind, int A, int B, int C=0);
public static class InputWire
{
    public const byte Version = 3;
    public const int MaxActions = 128;
    public static byte[] Session(Guid id, byte? value=null)
    {
        if (id == Guid.Empty) throw new InvalidDataException("Empty session");
        var b=new byte[value.HasValue?17:16]; id.TryWriteBytes(b);
        if(value.HasValue)b[16]=value.Value; return b;
    }
    public static Guid ReadSession(byte[] b, bool hasValue=false)
    {
        if(b.Length!=(hasValue?17:16))throw new InvalidDataException("Invalid session frame");
        var id=new Guid(b.AsSpan(0,16)); if(id==Guid.Empty)throw new InvalidDataException("Empty session");return id;
    }
    public static void Validate(InputAction a)
    {
        bool valid=a.Kind switch
        {
            InputActionKind.Key => a.A is >=1 and <=255 && a.B is >=0 and <=255 && a.C is >=0 and <=3,
            InputActionKind.Move => a.A is >=-32767 and <=32767 && a.B is >=-32767 and <=32767 && a.C==0,
            InputActionKind.Button => a.A is >=1 and <=5 && a.B is 0 or 1 && a.C==0,
            InputActionKind.Wheel => a.A is >=-32767 and <=32767 && a.B is 0 or 1 && a.C==0,
            _ => false
        };
        if(!valid)throw new InvalidDataException("Invalid input action");
    }
    public static byte[] Encode(Guid id, IReadOnlyList<InputAction> actions)
    {
        if(actions.Count<1||actions.Count>MaxActions)throw new InvalidDataException("Invalid action count");
        var b=new byte[18+13*actions.Count];Session(id).CopyTo(b,0);BinaryPrimitives.WriteUInt16LittleEndian(b.AsSpan(16),(ushort)actions.Count);
        for(int i=0;i<actions.Count;i++)
        {
            var a=actions[i];Validate(a);int o=18+13*i;b[o]=(byte)a.Kind;
            BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(o+1),a.A);BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(o+5),a.B);BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(o+9),a.C);
        }return b;
    }
    public static (Guid Session,InputAction[] Actions) Decode(byte[] b)
    {
        if(b.Length<18)throw new InvalidDataException("Truncated input batch");
        var id=ReadSession(b[..16]);int n=BinaryPrimitives.ReadUInt16LittleEndian(b.AsSpan(16));
        if(n<1||n>MaxActions||b.Length!=18+13*n)throw new InvalidDataException("Invalid input batch length");
        var actions=new InputAction[n];for(int i=0;i<n;i++)
        {
            int o=18+13*i;var a=new InputAction((InputActionKind)b[o],BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(o+1)),BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(o+5)),BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(o+9)));Validate(a);actions[i]=a;
        }return(id,actions);
    }
}
