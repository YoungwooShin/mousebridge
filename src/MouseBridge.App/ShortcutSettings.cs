using System.IO;
using MouseBridge.Core;
namespace MouseBridge.App;
internal static class ShortcutSettings
{
    private static string FilePath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MouseBridge","shortcut.json");
    public static SwitchShortcut Load(){try{return SwitchShortcut.FromJson(File.ReadAllText(FilePath));}catch{return SwitchShortcut.Default;}}
    public static void Save(SwitchShortcut value)
    {
        string json=value.ToJson();Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        string temp=FilePath+"."+Guid.NewGuid().ToString("N")+".tmp";
        try{File.WriteAllText(temp,json);File.Move(temp,FilePath,true);}finally{if(File.Exists(temp))File.Delete(temp);}
    }
}
