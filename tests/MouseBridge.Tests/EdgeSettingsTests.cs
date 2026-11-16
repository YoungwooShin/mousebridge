using MouseBridge.App;
using System.Text.Json;
using Xunit;
namespace MouseBridge.Tests;
public class EdgeSettingsTests
{
    [Fact] public void DefaultsAndAppearanceRoundTrip()
    {
        var defaults=new EdgeSettings();defaults.Validate();Assert.True(defaults.Enabled);Assert.True(defaults.SecondaryLeft);
        var value=new EdgeSettings(false,true,false,"#aAbB09",0);value.Validate();
        Assert.Equal(value,JsonSerializer.Deserialize<EdgeSettings>(JsonSerializer.Serialize(value)));
        new EdgeSettings(Opacity:1).Validate();
    }
    [Theory][InlineData("red")][InlineData("#123")][InlineData("#12345678")][InlineData("#GG0000")][InlineData(null)]
    public void InvalidColorIsRejected(string? value)=>Assert.Throws<ArgumentException>(()=>new EdgeSettings(Color:value!).Validate());
    [Theory][InlineData(-0.1)][InlineData(1.1)][InlineData(double.NaN)][InlineData(double.PositiveInfinity)]
    public void InvalidOpacityIsRejected(double value)=>Assert.Throws<ArgumentException>(()=>new EdgeSettings(Opacity:value).Validate());
    [Fact] public void PreviousSettingsDefaultToFiftyPixelWidth()
    {
        var previous=JsonSerializer.Deserialize<EdgeSettings>("{\"SecondaryLeft\":false,\"Enabled\":true,\"ShowIndicator\":true,\"Color\":\"#4263D6\",\"Opacity\":0.35}")!;
        previous.Validate();Assert.Equal(50,previous.Width);Assert.False(previous.SecondaryLeft);
        Assert.Equal(50,new EdgeSettings().Width);
    }
    [Theory][InlineData(7)][InlineData(121)][InlineData(0)][InlineData(-1)]
    public void InvalidWidthIsRejected(int width)=>Assert.Throws<ArgumentException>(()=>new EdgeSettings(Width:width).Validate());
    [Theory][InlineData(8)][InlineData(120)]
    public void SupportedWidthsPersist(int width)
    {
        var settings=new EdgeSettings(Width:width);settings.Validate();
        Assert.Equal(settings,JsonSerializer.Deserialize<EdgeSettings>(JsonSerializer.Serialize(settings)));
    }
}
