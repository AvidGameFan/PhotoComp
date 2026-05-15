using CommunityToolkit.Mvvm.ComponentModel;

namespace PhotoComp.Models;

/// <summary>
/// Shared observable zoom/pan state passed to both image panel view-models.
/// When any property changes, both panels update their transforms simultaneously.
/// </summary>
public sealed partial class ZoomState : ObservableObject
{
    [ObservableProperty]
    private double _scale = 1.0;

    [ObservableProperty]
    private double _offsetX = 0.0;

    [ObservableProperty]
    private double _offsetY = 0.0;

    public void Reset()
    {
        Scale = 1.0;
        OffsetX = 0.0;
        OffsetY = 0.0;
    }
}
