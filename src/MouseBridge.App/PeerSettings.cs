using System.IO;
using System.Text.Json;
namespace MouseBridge.App;
internal static class PeerSettings
{
    private static string FilePath=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"MouseBridge","peer.json");
    public static string? Load(){try{return JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(FilePath))?.GetValueOrDefault("deviceId");}catch{return null;}}
    public static void Save(string? id)
    {try{Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);File.WriteAllText(FilePath,JsonSerializer.Serialize(new Dictionary<string,string>{{"deviceId",id??""}}));}catch{}}
}
