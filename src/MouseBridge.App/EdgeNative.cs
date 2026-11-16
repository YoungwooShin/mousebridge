using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Controls;
using System.Windows.Media;
using MouseBridge.Core;
namespace MouseBridge.App;
internal static partial class NativeInput
{
    [StructLayout(LayoutKind.Sequential)] internal struct Rect{public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Sequential)] internal struct MonitorInfo{public int Size;public Rect Monitor,Work;public uint Flags;}
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(Point point,uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
    internal static ScreenRect ScreenAt(int x,int y)
    {
        var info=new MonitorInfo{Size=Marshal.SizeOf<MonitorInfo>()};
        if(!GetMonitorInfo(MonitorFromPoint(new(){X=x,Y=y},2),ref info))throw new InvalidOperationException("Cannot read monitor bounds.");
        return new(info.Monitor.Left,info.Monitor.Top,info.Monitor.Right-info.Monitor.Left,info.Monitor.Bottom-info.Monitor.Top);
    }
    private delegate bool MonitorCallback(IntPtr monitor,IntPtr dc,ref Rect rect,IntPtr data);
    [DllImport("user32.dll")] private static extern bool EnumDisplayMonitors(IntPtr dc,IntPtr clip,MonitorCallback callback,IntPtr data);
    internal static ScreenRect ScreenForEntry(bool left)
    {
        var screens=new List<ScreenRect>();
        MonitorCallback callback=(IntPtr monitor,IntPtr dc,ref Rect rect,IntPtr data)=>{screens.Add(new(rect.Left,rect.Top,rect.Right-rect.Left,rect.Bottom-rect.Top));return true;};
        if(!EnumDisplayMonitors(IntPtr.Zero,IntPtr.Zero,callback,IntPtr.Zero)||screens.Count==0)throw new InvalidOperationException("Cannot enumerate monitors.");
        return left?screens.OrderBy(r=>r.Left).ThenBy(r=>r.Top).First():screens.OrderByDescending(r=>r.Right).ThenBy(r=>r.Top).First();
    }
    internal static bool OutsideDesktop(ScreenRect r,int y,bool left)=>MonitorFromPoint(new(){X=left?r.Left-1:r.Right+1,Y=y},0)==IntPtr.Zero;
    [DllImport("user32.dll",EntryPoint="GetWindowLongW")] internal static extern int GetWindowLong(IntPtr hwnd,int index);
    [DllImport("user32.dll",EntryPoint="SetWindowLongW")] internal static extern int SetWindowLong(IntPtr hwnd,int index,int value);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr hwnd,IntPtr after,int x,int y,int width,int height,uint flags);
}
internal sealed class EdgeIndicator:IDisposable
{
    private Window? window;
    // Fade towards the screen interior; a fine rail keeps the actual boundary readable.
    // Shared by the live settings preview and the physical-pixel overlay window.
    internal static FrameworkElement CreateVisual(Color color,bool left)
    {
        var glow=new LinearGradientBrush
        {
            StartPoint=new System.Windows.Point(left?0:1,0.5),EndPoint=new System.Windows.Point(left?1:0,0.5),
            GradientStops=new GradientStopCollection
            {
                new(Color.FromArgb(210,color.R,color.G,color.B),0),
                new(Color.FromArgb(108,color.R,color.G,color.B),0.28),
                new(Color.FromArgb(30,color.R,color.G,color.B),0.68),
                new(Color.FromArgb(0,color.R,color.G,color.B),1)
            }
        };
        var grid=new Grid{Background=glow,IsHitTestVisible=false};
        grid.Children.Add(new Border{Width=3,CornerRadius=new CornerRadius(1.5),Background=new SolidColorBrush(color),HorizontalAlignment=left?HorizontalAlignment.Left:HorizontalAlignment.Right});
        grid.OpacityMask=new LinearGradientBrush(new GradientStopCollection
        {
            new(Colors.Transparent,0),new(Colors.White,0.035),new(Colors.White,0.965),new(Colors.Transparent,1)
        },new System.Windows.Point(0.5,0),new System.Windows.Point(0.5,1));
        return grid;
    }
    public void Show(ScreenRect screen,bool left,EdgeSettings settings)
    {
        Hide();if(!settings.ShowIndicator||settings.Opacity<=0)return;
        settings.Validate();
        var color=(Color)ColorConverter.ConvertFromString(settings.Color);
        window=new Window{WindowStyle=WindowStyle.None,ResizeMode=ResizeMode.NoResize,AllowsTransparency=true,Background=Brushes.Transparent,Content=CreateVisual(color,left),Opacity=settings.Opacity,ShowActivated=false,ShowInTaskbar=false,Topmost=true,Focusable=false,IsHitTestVisible=false,Width=settings.Width,Height=100};
        var hwnd=new WindowInteropHelper(window).EnsureHandle();
        NativeInput.SetWindowLong(hwnd,-20,NativeInput.GetWindowLong(hwnd,-20)|0x20|0x08000000|0x80);
        window.Show();
        int top=screen.Top+(int)Math.Ceiling(screen.Height*0.1),bottom=screen.Top+(int)Math.Ceiling(screen.Height*0.9);
        NativeInput.SetWindowPos(hwnd,new IntPtr(-1),left?screen.Left:screen.Right-settings.Width+1,top,settings.Width,bottom-top,0x10|0x40);
    }
    public void Hide(){window?.Close();window=null;}
    public void Dispose()=>Hide();
}
