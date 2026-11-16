using System.Text.Json;
using System.Text.Json.Serialization;
namespace MouseBridge.Core;
public sealed record SwitchShortcut(int Key=0x77,bool Ctrl=false,bool Alt=false,bool Shift=false)
{
    public static SwitchShortcut Default=>new();
    [JsonIgnore] public int Modifiers=>(Alt?1:0)|(Ctrl?2:0)|(Shift?4:0);
    [JsonIgnore] public string KeyName=>Key is >=0x70 and <=0x87?"F"+(Key-0x6F):Key==0x20?"Space":((char)Key).ToString();
    [JsonIgnore] public string DisplayName=>string.Join("+",new[]{Ctrl?"Ctrl":null,Alt?"Alt":null,Shift?"Shift":null,KeyName}.Where(s=>s!=null));
    public void Validate()
    {
        bool function=Key is >=0x70 and <=0x87;
        if(!function&&Key!=0x20&&!(Key is >=0x41 and <=0x5A)&&!(Key is >=0x30 and <=0x39))throw new ArgumentException("Choose F1–F24, a letter, a digit, or Space.");
        if(!function&&!Ctrl&&!Alt)throw new ArgumentException("Letters, digits and Space need Ctrl or Alt so normal typing stays available.");
        if(Ctrl&&!Alt&&!Shift&&Key is 0x43 or 0x56 or 0x58)throw new ArgumentException("Ctrl+C, Ctrl+V and Ctrl+X are kept for clipboard actions. Choose another shortcut.");
        if(!function&&Ctrl&&Alt)throw new ArgumentException("Ctrl+Alt can overlap AltGr typing. Choose a function key or another combination.");
        if(Alt&&Key==0x73)throw new ArgumentException("Alt+F4 is reserved for closing windows. Choose another shortcut.");
    }
    public static int ParseKey(string name)
    {
        name=name.Trim().ToUpperInvariant();
        if(name=="SPACE")return 0x20;
        if(name.StartsWith('F')&&int.TryParse(name.AsSpan(1),out int n)&&n is >=1 and <=24)return 0x6F+n;
        if(name.Length==1&&(name[0] is >= 'A' and <= 'Z' or >= '0' and <= '9'))return name[0];
        throw new ArgumentException("Choose a valid shortcut key.");
    }
    public string ToJson(){Validate();return JsonSerializer.Serialize(this);}
    public static SwitchShortcut FromJson(string json)
    {var value=JsonSerializer.Deserialize<SwitchShortcut>(json)??throw new ArgumentException("Invalid shortcut settings.");value.Validate();return value;}
}
public readonly record struct ShortcutDecision(bool Consume,bool Toggle=false);
// The trigger key is reserved only for the configured exact modifier combination.
// Toggle after release of ALL modifiers so neither PC inherits a held modifier.
public sealed class ShortcutGesture(SwitchShortcut shortcut)
{
    private bool triggerDown,waitingRelease;
    public ShortcutDecision Process(int key,bool up,bool ctrl,bool alt,bool shift,bool win=false)
    {
        if(waitingRelease&&!up&&!IsModifier(key)&&key!=shortcut.Key)waitingRelease=false;
        if(key==shortcut.Key)
        {
            if(!up&&(triggerDown||(ctrl==shortcut.Ctrl&&alt==shortcut.Alt&&shift==shortcut.Shift&&!win)))
            {triggerDown=true;return new(true);}
            if(up&&triggerDown)
            {
                triggerDown=false;waitingRelease=true;
                if(!ctrl&&!alt&&!shift&&!win){waitingRelease=false;return new(true,true);}return new(true);
            }
        }
        if(waitingRelease&&up&&!ctrl&&!alt&&!shift&&!win){waitingRelease=false;return new(false,true);}
        return new(false);
    }
    private static bool IsModifier(int key)=>key is 0x10 or 0x11 or 0x12 or >=0xA0 and <=0xA5;
}
