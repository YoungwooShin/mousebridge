using System.ComponentModel;
using System.Runtime.InteropServices;
using MouseBridge.Core;
namespace MouseBridge.App;
internal static partial class NativeInput
{
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X,Y; }
    [StructLayout(LayoutKind.Sequential)] internal struct KeyboardHook { public uint Vk,Scan,Flags,Time;public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] internal struct MouseHook { public Point Point;public uint Data,Flags,Time;public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] internal struct MouseInput { public int Dx,Dy;public uint Data,Flags,Time;public UIntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] internal struct KeyInput { public ushort Vk,Scan;public uint Flags,Time;public UIntPtr Extra; }
    [StructLayout(LayoutKind.Explicit)] internal struct InputUnion { [FieldOffset(0)]public MouseInput Mouse;[FieldOffset(0)]public KeyInput Key; }
    [StructLayout(LayoutKind.Sequential)] internal struct Input { public uint Type;public InputUnion Union; }
    internal delegate IntPtr HookProc(int code,IntPtr w,IntPtr l);
    [DllImport("user32.dll",SetLastError=true)] internal static extern IntPtr SetWindowsHookEx(int type,HookProc proc,IntPtr module,uint thread);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] internal static extern IntPtr GetModuleHandle(string? module);
    [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(IntPtr hook);
    [DllImport("user32.dll")] internal static extern IntPtr CallNextHookEx(IntPtr hook,int code,IntPtr w,IntPtr l);
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point point);
    [DllImport("user32.dll")] internal static extern bool SetCursorPos(int x,int y);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern int GetSystemMetrics(int metric);
    [DllImport("user32.dll",SetLastError=true)] private static extern uint SendInput(uint n,[In]Input[] inputs,int size);
    [DllImport("user32.dll",SetLastError=true)] private static extern bool RegisterHotKey(IntPtr hwnd,int id,uint modifiers,uint key);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd,int id);
    internal static void ReserveShortcut(int id,SwitchShortcut shortcut)
    {
        shortcut.Validate();
        if(!RegisterHotKey(IntPtr.Zero,id,(uint)shortcut.Modifiers|0x4000,(uint)shortcut.Key))
            throw new Win32Exception(Marshal.GetLastWin32Error(),$"{shortcut.DisplayName} is unavailable or reserved by Windows/another app. Choose another shortcut.");
    }
    internal static void ReleaseShortcut(int id)=>UnregisterHotKey(IntPtr.Zero,id);
    internal static void ProbeShortcut(SwitchShortcut shortcut)
    {ReserveShortcut(0x4D44,shortcut);ReleaseShortcut(0x4D44);}
    internal static bool OtherKeysHeld(int switchKey=0x77)
    { for(int k=1;k<256;k++)if(k!=switchKey&&(GetAsyncKeyState(k)&0x8000)!=0)return true;return false; }
    internal static void Inject(InputAction a)
    {
        a=KeyEncoding.Normalize(a);InputWire.Validate(a);var input=new Input();
        if(a.Kind==InputActionKind.Key)
        {
            input.Type=1;input.Union.Key=new KeyInput{Vk=(ushort)(a.B==0?a.A:0),Scan=(ushort)a.B,
                Flags=(uint)((a.B==0?0:8)|((a.C&1)!=0?2:0)|((a.C&2)!=0?1:0))};
        }
        else
        {
            uint flags=0,data=0;int dx=0,dy=0;
            switch(a.Kind)
            {
                case InputActionKind.Move:flags=0x2001;dx=a.A;dy=a.B;break;
                case InputActionKind.Wheel:flags=a.B==0?0x800u:0x1000u;data=unchecked((uint)a.A);break;
                case InputActionKind.Button:
                    flags=a.A switch{1=>a.B==1?2u:4u,2=>a.B==1?8u:16u,3=>a.B==1?32u:64u,_=>a.B==1?128u:256u};
                    if(a.A>=4)data=(uint)(a.A-3);break;
            }
            input.Union.Mouse=new MouseInput{Dx=dx,Dy=dy,Flags=flags,Data=data};
        }
        if(SendInput(1,[input],Marshal.SizeOf<Input>())!=1)throw new Win32Exception(Marshal.GetLastWin32Error(),"SendInput failed. Elevated apps and secure desktops are not supported.");
    }
}
internal sealed class InputInjector
{
    private readonly HashSet<(int Vk,int Scan,int Extended)> keys=[];
    private readonly HashSet<int> buttons=[];
    public bool AnyHeld=>keys.Count>0||buttons.Count>0;
    public bool ControlDown=>keys.Any(k=>k.Vk is 0x11 or 0xA2 or 0xA3);
    public void Apply(InputAction a)
    {
        a=KeyEncoding.Normalize(a);NativeInput.Inject(a);
        if(a.Kind==InputActionKind.Key){var key=(a.A,a.B,a.C&2);if((a.C&1)==0)keys.Add(key);else keys.Remove(key);}
        if(a.Kind==InputActionKind.Button){if(a.B==1)buttons.Add(a.A);else buttons.Remove(a.A);}
    }
    public int ReleaseAll()
    {
        int failures=0;
        foreach(var k in keys.ToArray()){try{NativeInput.Inject(new(InputActionKind.Key,k.Vk,k.Scan,k.Extended|1));keys.Remove(k);}catch{failures++;}}
        foreach(int b in buttons.ToArray()){try{NativeInput.Inject(new(InputActionKind.Button,b,0));buttons.Remove(b);}catch{failures++;}}
        return failures;
    }
}
internal sealed class InputCapture : IDisposable
{
    private readonly NativeInput.HookProc keyProc,mouseProc;
    private IntPtr keyHook,mouseHook;
    private bool deferredV;
    private SwitchShortcut shortcut;
    private ShortcutGesture gesture;
    private readonly Action<Action> post;
    private readonly HashSet<int> physicalKeys=[];
    private readonly HashSet<int> physicalButtons=[];
    private int shortcutId,gestureVersion;
    private bool closed;
    public bool DeferPaste{get;set;}
    public event Action? CopyRequested;
    public event Action? PasteRequested;
    private NativeInput.Point anchor,saved;
    public bool Active{get;private set;}
    public event Action? Toggle;
    public event Action<int,int,int>? LocalMove;
    private NativeInput.Point previous;
    public event Action<InputAction>? Captured;
    public InputCapture(SwitchShortcut shortcut,Action<Action> post)
    {
        this.shortcut=shortcut;this.post=post;gesture=new(shortcut);keyProc=Key;mouseProc=Mouse;
        NativeInput.ReserveShortcut(0x4D42,shortcut);shortcutId=0x4D42;
        for(int key=8;key<256;key++)if(key is not (0x10 or 0x11 or 0x12)&&(NativeInput.GetAsyncKeyState(key)&0x8000)!=0)physicalKeys.Add(key);
        var module=NativeInput.GetModuleHandle(null);
        keyHook=NativeInput.SetWindowsHookEx(13,keyProc,module,0);
        mouseHook=NativeInput.SetWindowsHookEx(14,mouseProc,module,0);
        if(keyHook==IntPtr.Zero||mouseHook==IntPtr.Zero){int error=Marshal.GetLastWin32Error();Dispose();throw new Win32Exception(error,"Could not register input hooks");}
    }
    public void ChangeShortcut(SwitchShortcut value)
    {
        if(Active)throw new InvalidOperationException("Return to the main PC before changing the shortcut.");
        value.Validate();if(value==shortcut)return;
        int next=shortcutId==0x4D42?0x4D43:0x4D42;NativeInput.ReserveShortcut(next,value);
        NativeInput.ReleaseShortcut(shortcutId);shortcutId=next;shortcut=value;gesture=new(value);gestureVersion++;
    }
    public bool OtherKeysHeld()=>physicalKeys.Any(k=>k!=shortcut.Key)||
        (Active?physicalButtons.Count>0:new[]{1,2,4,5,6}.Any(k=>(NativeInput.GetAsyncKeyState(k)&0x8000)!=0));
    public void Start()
    {
        physicalButtons.Clear();
        if(!NativeInput.GetCursorPos(out saved))throw new Win32Exception("Could not read the cursor position");
        anchor=new(){X=NativeInput.GetSystemMetrics(76)+NativeInput.GetSystemMetrics(78)/2,Y=NativeInput.GetSystemMetrics(77)+NativeInput.GetSystemMetrics(79)/2};
        if(!NativeInput.SetCursorPos(anchor.X,anchor.Y)||!NativeInput.GetCursorPos(out anchor))throw new Win32Exception("Could not reposition the local cursor");Active=true;
    }
    public void Stop(){if(!Active)return;Active=false;previous=saved;NativeInput.SetCursorPos(saved.X,saved.Y);}
    private IntPtr Key(int code,IntPtr w,IntPtr l)
    {
        if(code>=0)
        {
            var k=Marshal.PtrToStructure<NativeInput.KeyboardHook>(l);
            if((k.Flags&0x10)==0)
            {
                bool up=(k.Flags&0x80)!=0;
                int key=(int)k.Vk;
                if(key==0x10)key=k.Scan==0x36?0xA1:0xA0;
                if(key==0x11)key=(k.Flags&1)!=0?0xA3:0xA2;
                if(key==0x12)key=(k.Flags&1)!=0?0xA5:0xA4;
                if(up)physicalKeys.Remove(key);else physicalKeys.Add(key);
                bool physicalCtrl=physicalKeys.Contains(0xA2)||physicalKeys.Contains(0xA3);
                bool physicalAlt=physicalKeys.Contains(0xA4)||physicalKeys.Contains(0xA5);
                bool physicalShift=physicalKeys.Contains(0xA0)||physicalKeys.Contains(0xA1);
                var decision=gesture.Process(key,up,physicalCtrl,physicalAlt,physicalShift,physicalKeys.Contains(0x5B)||physicalKeys.Contains(0x5C));
                if(decision.Toggle){int version=gestureVersion;post(()=>{if(!closed&&version==gestureVersion)Toggle?.Invoke();});}
                if(decision.Consume)return new(1);
                if(k.Vk==0x56&&up&&deferredV){deferredV=false;return new(1);}
                bool ctrl=(NativeInput.GetAsyncKeyState(0x11)&0x8000)!=0;
                if(!Active&&!up&&ctrl&&k.Vk is 0x43 or 0x2D)CopyRequested?.Invoke();
                if(!Active&&!up&&DeferPaste&&ctrl&&k.Vk==0x56&&
                    (NativeInput.GetAsyncKeyState(0x10)&0x8000)==0&&(NativeInput.GetAsyncKeyState(0x12)&0x8000)==0&&
                    (NativeInput.GetAsyncKeyState(0x5B)&0x8000)==0&&(NativeInput.GetAsyncKeyState(0x5C)&0x8000)==0)
                {deferredV=true;PasteRequested?.Invoke();return new(1);}
                if(Active){Captured?.Invoke(KeyEncoding.Normalize(new(InputActionKind.Key,(int)k.Vk,(int)k.Scan,(up?1:0)|((k.Flags&1)!=0?2:0))));return new(1);}
            }
        }return NativeInput.CallNextHookEx(keyHook,code,w,l);
    }
    private IntPtr Mouse(int code,IntPtr w,IntPtr l)
    {
        if(code>=0)
        {
            var m=Marshal.PtrToStructure<NativeInput.MouseHook>(l);
            if((m.Flags&1)==0)
            {
                int msg=w.ToInt32();int button=msg switch{0x201 or 0x202=>1,0x204 or 0x205=>2,0x207 or 0x208=>4,0x20B or 0x20C=>(m.Data>>16)==1?5:6,_=>0};
                if(button!=0){if(msg is 0x201 or 0x204 or 0x207 or 0x20B)physicalButtons.Add(button);else physicalButtons.Remove(button);}
                if(!Active)
                {
                    if(msg==0x200){int dx=m.Point.X-previous.X;previous=m.Point;LocalMove?.Invoke(m.Point.X,m.Point.Y,dx);}
                    return NativeInput.CallNextHookEx(mouseHook,code,w,l);
                }
                InputAction? a=msg switch
                {
                    0x200=>new(InputActionKind.Move,Math.Clamp(m.Point.X-anchor.X,-32767,32767),Math.Clamp(m.Point.Y-anchor.Y,-32767,32767)),
                    0x201=>new(InputActionKind.Button,1,1),0x202=>new(InputActionKind.Button,1,0),
                    0x204=>new(InputActionKind.Button,2,1),0x205=>new(InputActionKind.Button,2,0),
                    0x207=>new(InputActionKind.Button,3,1),0x208=>new(InputActionKind.Button,3,0),
                    0x20B=>new(InputActionKind.Button,3+(int)(m.Data>>16),1),0x20C=>new(InputActionKind.Button,3+(int)(m.Data>>16),0),
                    0x20A=>new(InputActionKind.Wheel,(short)(m.Data>>16),0),0x20E=>new(InputActionKind.Wheel,(short)(m.Data>>16),1),_=>null
                };
                if(a.HasValue)Captured?.Invoke(a.Value);return new(1);
            }
        }return NativeInput.CallNextHookEx(mouseHook,code,w,l);
    }
    public void Dispose(){closed=true;gestureVersion++;Stop();if(keyHook!=IntPtr.Zero)NativeInput.UnhookWindowsHookEx(keyHook);if(mouseHook!=IntPtr.Zero)NativeInput.UnhookWindowsHookEx(mouseHook);keyHook=mouseHook=IntPtr.Zero;if(shortcutId!=0)NativeInput.ReleaseShortcut(shortcutId);shortcutId=0;}
}
