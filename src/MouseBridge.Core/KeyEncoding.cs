namespace MouseBridge.Core;
public static class KeyEncoding
{
    public static InputAction Normalize(InputAction a)
    {
        if(a.Kind!=InputActionKind.Key)return a;
        // Shift has two scan codes, NOT an E0/extended distinction.
        if(a.A is 0x10 or 0xA0 or 0xA1)
        {
            bool right=a.A==0xA1||a.B==0x36;
            return new(InputActionKind.Key,right?0xA1:0xA0,right?0x36:0x2A,a.C&1);
        }
        return a;
    }
}
