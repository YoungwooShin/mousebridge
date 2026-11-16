using MouseBridge.Core;
using Xunit;
namespace MouseBridge.Tests;
public class ShortcutTests
{
    [Fact] public void DefaultF8FiresOnceOnRelease()
    {
        var g=new ShortcutGesture(SwitchShortcut.Default);
        Assert.Equal(new ShortcutDecision(true),g.Process(0x77,false,false,false,false));
        Assert.Equal(new ShortcutDecision(true),g.Process(0x77,false,false,false,false));
        Assert.Equal(new ShortcutDecision(true,true),g.Process(0x77,true,false,false,false));
        Assert.Equal(new ShortcutDecision(false),g.Process(0x77,true,false,false,false));
    }
    [Fact] public void ModifiedShortcutWaitsForModifierKeyUpAndDoesNotConsumeIt()
    {
        var shortcut=new SwitchShortcut(0x78,Ctrl:true,Shift:true);var g=new ShortcutGesture(shortcut);
        g.Process(0xA2,false,true,false,false);g.Process(0xA0,false,true,false,true);
        Assert.Equal(new ShortcutDecision(true),g.Process(0x78,false,true,false,true));
        Assert.Equal(new ShortcutDecision(true),g.Process(0x78,true,true,false,true));
        Assert.Equal(new ShortcutDecision(false),g.Process(0xA0,true,true,false,false));
        Assert.Equal(new ShortcutDecision(false,true),g.Process(0xA2,true,false,false,false));
        Assert.False(g.Process(0xA2,true,false,false,false).Toggle);
    }
    [Fact] public void OtherModifierCombinationsAndOldKeyPassThrough()
    {
        var g=new ShortcutGesture(new(0x78));
        Assert.False(g.Process(0x77,false,false,false,false).Consume);
        Assert.False(g.Process(0x78,false,true,false,false).Consume);
        Assert.False(g.Process(0x78,true,true,false,false).Toggle);
        Assert.True(g.Process(0x78,false,false,false,false).Consume);
        Assert.True(g.Process(0x78,true,false,false,false).Toggle);
    }
    [Fact] public void UnrelatedTypingCancelsDelayedToggle()
    {
        var g=new ShortcutGesture(new(0x77,Ctrl:true));g.Process(0x77,false,true,false,false);g.Process(0x77,true,true,false,false);
        Assert.False(g.Process(0x43,false,true,false,false).Consume);Assert.False(g.Process(0xA2,true,false,false,false).Toggle);
    }
    [Theory] [InlineData("f8",0x77)] [InlineData("F24",0x87)] [InlineData("Space",0x20)] [InlineData("a",0x41)] [InlineData("9",0x39)]
    public void KeyNamesParse(string name,int key)=>Assert.Equal(key,SwitchShortcut.ParseKey(name));
    [Theory] [InlineData(0x41,false,false,false)] [InlineData(0x41,false,false,true)] [InlineData(0x41,true,true,false)] [InlineData(0x73,false,true,false)] [InlineData(0x10,true,false,false)] [InlineData(0x43,true,false,false)] [InlineData(0x56,true,false,false)] [InlineData(0x58,true,false,false)]
    public void UnsafeTypingOrSystemBindingsAreRejected(int key,bool ctrl,bool alt,bool shift)=>Assert.Throws<ArgumentException>(()=>new SwitchShortcut(key,ctrl,alt,shift).Validate());
    [Fact] public void WindowsModifierDoesNotStealSystemShortcuts()
    {
        var g=new ShortcutGesture(SwitchShortcut.Default);
        Assert.False(g.Process(0x77,false,false,false,false,true).Consume);
        Assert.False(g.Process(0x77,true,false,false,false,true).Toggle);
    }
    [Fact] public void SavedShortcutRoundTripsAndRejectsInvalidSettings()
    {
        var expected=new SwitchShortcut(0x79,Ctrl:true,Shift:true);Assert.Equal("Ctrl+Shift+F10",expected.DisplayName);
        string json=expected.ToJson();Assert.Equal(expected,SwitchShortcut.FromJson(json));Assert.DoesNotContain("DisplayName",json);
        Assert.Throws<ArgumentException>(()=>SwitchShortcut.FromJson("{\"Key\":0}"));
        Assert.Throws<ArgumentException>(()=>SwitchShortcut.ParseKey("F25"));
    }
}
