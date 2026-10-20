using Microsoft.UI.Composition;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace PyTouchCode.App;

// Each window owns its controller. XAML supplies activation, high-contrast and
// system transparency policy through the default backdrop configuration.
sealed class AdjustableBackdrop(string material, double transparency) : SystemBackdrop
{
    MicaController? mica;
    DesktopAcrylicController? acrylic;
    SystemBackdropConfiguration? configuration;
    public string Material { get; } = material;
    double amount = transparency;
    public double Transparency
    {
        get => amount;
        set { amount = Math.Clamp(value, 0, 1); UpdateTint(); }
    }
    internal double? TintOpacity => mica?.TintOpacity ?? acrylic?.TintOpacity;

    protected override void OnTargetConnected(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        base.OnTargetConnected(target, xamlRoot);
        configuration = GetDefaultSystemBackdropConfiguration(target, xamlRoot);
        if (Material == "Acrylic")
        {
            acrylic = new DesktopAcrylicController();
            acrylic.SetSystemBackdropConfiguration(configuration);
            acrylic.AddSystemBackdropTarget(target);
        }
        else
        {
            mica = new MicaController { Kind = Material == "MicaAlt" ? MicaKind.BaseAlt : MicaKind.Base };
            mica.SetSystemBackdropConfiguration(configuration);
            mica.AddSystemBackdropTarget(target);
        }
        UpdateTint();
    }
    protected override void OnDefaultSystemBackdropConfigurationChanged(ICompositionSupportsSystemBackdrop target, XamlRoot xamlRoot)
    {
        base.OnDefaultSystemBackdropConfigurationChanged(target, xamlRoot);
        configuration = GetDefaultSystemBackdropConfiguration(target, xamlRoot);
        UpdateTint();
    }
    void UpdateTint()
    {
        bool dark = configuration?.Theme == SystemBackdropTheme.Dark;
        var color = dark ? Windows.UI.Color.FromArgb(255, 32, 32, 32) : Windows.UI.Color.FromArgb(255, 243, 243, 243);
        float opacity = (float)(1 - amount);
        if (mica is not null)
        {
            mica.TintColor = mica.FallbackColor = color;
            mica.TintOpacity = opacity; mica.LuminosityOpacity = opacity;
        }
        if (acrylic is not null)
        {
            acrylic.TintColor = acrylic.FallbackColor = color;
            acrylic.TintOpacity = opacity; acrylic.LuminosityOpacity = opacity;
        }
    }
    protected override void OnTargetDisconnected(ICompositionSupportsSystemBackdrop target)
    {
        mica?.RemoveSystemBackdropTarget(target); mica?.Dispose(); mica = null;
        acrylic?.RemoveSystemBackdropTarget(target); acrylic?.Dispose(); acrylic = null;
        configuration = null;
        base.OnTargetDisconnected(target);
    }
}
