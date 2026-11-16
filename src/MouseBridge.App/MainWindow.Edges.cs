using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
namespace MouseBridge.App;
public partial class MainWindow
{
    private EdgeSettings currentEdges=EdgeSettings.Load();
    private bool layoutLeft;
    private Point dragStart;
    private void InitializeEdgeControls()
    {
        layoutLeft=currentEdges.SecondaryLeft;RefreshLayout();EdgeEnabled.IsChecked=currentEdges.Enabled;EdgeVisible.IsChecked=currentEdges.ShowIndicator;
        EdgeColor.Text=currentEdges.Color;EdgeOpacity.Value=currentEdges.Opacity*100;EdgeWidth.Value=currentEdges.Width;
        UpdateEdgePreview();
        EdgeFeedback.Text="Save arrangement on Main. Indicator appearance is saved separately on each PC.";
    }
    private void RefreshLayout(){Grid.SetColumn(LayoutSecondary,layoutLeft?0:2);Grid.SetColumn(LayoutMain,layoutLeft?2:0);UpdateEdgePreview();}
    private void Screen_MouseDown(object sender,MouseButtonEventArgs e)=>dragStart=e.GetPosition(ScreenLayout);
    private void Screen_MouseMove(object sender,MouseEventArgs e)
    {
        if(e.LeftButton!=MouseButtonState.Pressed)return;
        var point=e.GetPosition(ScreenLayout);
        if(Math.Abs(point.X-dragStart.X)<SystemParameters.MinimumHorizontalDragDistance&&Math.Abs(point.Y-dragStart.Y)<SystemParameters.MinimumVerticalDragDistance)return;
        if(sender is Border b)DragDrop.DoDragDrop(b,new DataObject("MouseBridge.Screen",b.Tag.ToString()),DragDropEffects.Move);
    }
    private void ScreenLayout_DragOver(object sender,DragEventArgs e){e.Effects=e.Data.GetDataPresent("MouseBridge.Screen")?DragDropEffects.Move:DragDropEffects.None;e.Handled=true;}
    private void ScreenLayout_Drop(object sender,DragEventArgs e)
    {
        if(!e.Data.GetDataPresent("MouseBridge.Screen"))return;
        bool dropLeft=e.GetPosition(ScreenLayout).X<ScreenLayout.ActualWidth/2;
        layoutLeft=(string)e.Data.GetData("MouseBridge.Screen")=="Secondary"?dropLeft:!dropLeft;
        RefreshLayout();EdgeFeedback.Text="Arrangement changed. Save screen settings to apply.";e.Handled=true;
    }
    private void EdgeAppearance_Changed(object sender,RoutedPropertyChangedEventArgs<double> e)=>UpdateEdgePreview();
    private void EdgeColor_TextChanged(object sender,TextChangedEventArgs e)=>UpdateEdgePreview();
    private void EdgeVisibility_Click(object sender,RoutedEventArgs e)=>UpdateEdgePreview();
    private void EdgePreset_Click(object sender,RoutedEventArgs e)
    {
        if(sender is Button b&&b.Tag is string color)EdgeColor.Text=color;
    }
    private void UpdateEdgePreview()
    {
        // Slider/TextChanged events also occur while InitializeComponent builds controls.
        if(EdgePreview==null||EdgeWidth==null||EdgeOpacity==null||EdgeColor==null||EdgeVisible==null||EdgeWidthValue==null||EdgeOpacityValue==null)return;
        EdgeWidthValue.Text=$"{EdgeWidth.Value:0} px";EdgeOpacityValue.Text=$"{EdgeOpacity.Value:0}%";
        try
        {
            var candidate=new EdgeSettings(Color:EdgeColor.Text.Trim(),Opacity:EdgeOpacity.Value/100,Width:(int)Math.Round(EdgeWidth.Value));candidate.Validate();
            var visual=EdgeIndicator.CreateVisual((Color)ColorConverter.ConvertFromString(candidate.Color),layoutLeft);
            EdgePreview.Child=visual;EdgePreview.Width=candidate.Width;EdgePreview.Opacity=candidate.Opacity;
            EdgePreview.HorizontalAlignment=layoutLeft?HorizontalAlignment.Left:HorizontalAlignment.Right;
            EdgePreview.Visibility=EdgeVisible.IsChecked==true?Visibility.Visible:Visibility.Hidden;
            if(EdgePreviewThis!=null&&EdgePreviewOther!=null){Grid.SetColumn(EdgePreviewThis,layoutLeft?2:0);Grid.SetColumn(EdgePreviewOther,layoutLeft?0:2);}
            EdgeColor.BorderBrush=(Brush)FindResource("MutedText");EdgeColor.ToolTip="Hex color in #RRGGBB format";
        }
        catch(ArgumentException)
        {
            EdgeColor.BorderBrush=new SolidColorBrush(Color.FromRgb(220,80,80));EdgeColor.ToolTip="Enter a color in #RRGGBB format.";
        }
    }
    [StructLayout(LayoutKind.Sequential)] private struct ColorChoice
    {
        public uint Size;public IntPtr Owner,Instance;public uint Result;public IntPtr CustomColors;
        public uint Flags;public IntPtr Data,Hook,Template;
    }
    [DllImport("comdlg32.dll",EntryPoint="ChooseColorW",CharSet=CharSet.Unicode)]
    private static extern bool ChooseColor(ref ColorChoice choice);
    private void EdgeColorChoose_Click(object sender,RoutedEventArgs e)
    {
        IntPtr custom=Marshal.AllocHGlobal(16*sizeof(int));
        try
        {
            var colors=new int[16];Array.Fill(colors,0xFFFFFF);
            string[] presets=["#4263D6","#14B8A6","#38BDF8","#A78BFA","#F59E0B","#F472B6"];
            for(int i=0;i<presets.Length;i++)colors[i]=(int)ColorRef((Color)ColorConverter.ConvertFromString(presets[i]));
            Marshal.Copy(colors,0,custom,colors.Length);
            Color initial=Color.FromRgb(66,99,214);
            try{var candidate=new EdgeSettings(Color:EdgeColor.Text.Trim());candidate.Validate();initial=(Color)ColorConverter.ConvertFromString(candidate.Color);}catch(ArgumentException){}
            var choice=new ColorChoice{Size=(uint)Marshal.SizeOf<ColorChoice>(),Owner=new WindowInteropHelper(this).Handle,Result=ColorRef(initial),CustomColors=custom,Flags=3};
            if(ChooseColor(ref choice))EdgeColor.Text=$"#{choice.Result&255:X2}{(choice.Result>>8)&255:X2}{(choice.Result>>16)&255:X2}";
        }
        finally{Marshal.FreeHGlobal(custom);}
    }
    private static uint ColorRef(Color color)=>(uint)(color.R|(color.G<<8)|(color.B<<16));
    private void EdgesSave_Click(object sender,RoutedEventArgs e)
    {
        try
        {
            var next=new EdgeSettings(layoutLeft,EdgeEnabled.IsChecked==true,EdgeVisible.IsChecked==true,EdgeColor.Text.Trim(),EdgeOpacity.Value/100,(int)Math.Round(EdgeWidth.Value));next.Validate();
            remote?.ChangeEdges(next);
            try{next.Save();}catch{remote?.ChangeEdges(currentEdges);throw;}
            currentEdges=next;EdgeFeedback.Text="Screen settings saved. Shortcut switching remains available.";
        }
        catch(Exception ex){EdgeFeedback.Text=ex.Message;}
    }
}
