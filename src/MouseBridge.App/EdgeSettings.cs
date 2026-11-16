using System.IO;
using System.Text.Json;
namespace MouseBridge.App;
internal sealed record EdgeSettings(bool SecondaryLeft=true,bool Enabled=true,bool ShowIndicator=true,string Color="#4263D6",double Opacity=0.35,int Width=50)
{
    private static string FilePath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MouseBridge","edges.json");
    public void Validate()
    {
        if(Width is <8 or >120)throw new ArgumentException("Indicator width must be between 8 and 120 pixels.");
        if(!double.IsFinite(Opacity)||Opacity<0||Opacity>1)throw new ArgumentException("Opacity must be between 0 and 100 percent.");
        if(string.IsNullOrEmpty(Color)||Color.Length!=7||Color[0]!='#'||!uint.TryParse(Color[1..],System.Globalization.NumberStyles.HexNumber,null,out _))throw new ArgumentException("Use a color in #RRGGBB format.");
    }
    public static EdgeSettings Load(){try{var s=JsonSerializer.Deserialize<EdgeSettings>(File.ReadAllText(FilePath))??new();s.Validate();return s;}catch{return new();}}
    public void Save(){Validate();Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);File.WriteAllText(FilePath+".tmp",JsonSerializer.Serialize(this));File.Move(FilePath+".tmp",FilePath,true);}
}
