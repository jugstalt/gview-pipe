using MudBlazor;

namespace gView.Pipe.Portal.Components;

static public class PortalTheme
{
    static public MudTheme Default { get; } = new()
    {
        PaletteLight = new PaletteLight
        {
            Primary = "#3d6fd6",
            Secondary = "#7a5af5",
            AppbarBackground = "#ffffff",
            AppbarText = "#1f2937",
            Background = "#f6f7f9",
            Surface = "#ffffff"
        },
        PaletteDark = new PaletteDark
        {
            Primary = "#6c95ec",
            Secondary = "#9d86f8",
            AppbarBackground = "#1b1d22",
            Background = "#131417",
            Surface = "#1b1d22"
        }
    };
}
