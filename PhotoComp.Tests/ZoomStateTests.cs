using System.ComponentModel;
using PhotoComp.Models;

namespace PhotoComp.Tests;

public class ZoomStateTests
{
    [Fact]
    public void DefaultScale_IsOne()
    {
        var z = new ZoomState();
        Assert.Equal(1.0, z.Scale);
    }

    [Fact]
    public void DefaultOffsets_AreZero()
    {
        var z = new ZoomState();
        Assert.Equal(0.0, z.OffsetX);
        Assert.Equal(0.0, z.OffsetY);
    }

    [Fact]
    public void Reset_RestoresDefaults()
    {
        var z = new ZoomState { Scale = 3.5, OffsetX = 100, OffsetY = -50 };
        z.Reset();
        Assert.Equal(1.0, z.Scale);
        Assert.Equal(0.0, z.OffsetX);
        Assert.Equal(0.0, z.OffsetY);
    }

    [Fact]
    public void PropertyChanged_FiredOnScaleChange()
    {
        var z = new ZoomState();
        var fired = new List<string?>();
        z.PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        z.Scale = 2.0;

        Assert.Contains(nameof(ZoomState.Scale), fired);
    }

    [Fact]
    public void PropertyChanged_FiredOnReset()
    {
        var z = new ZoomState { Scale = 2.0, OffsetX = 10, OffsetY = 10 };
        var fired = new List<string?>();
        z.PropertyChanged += (_, e) => fired.Add(e.PropertyName);

        z.Reset();

        Assert.Contains(nameof(ZoomState.Scale), fired);
        Assert.Contains(nameof(ZoomState.OffsetX), fired);
        Assert.Contains(nameof(ZoomState.OffsetY), fired);
    }
}
