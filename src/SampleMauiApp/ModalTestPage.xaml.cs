#if MACOS
using Microsoft.Maui.Platform.MacOS;
#endif

namespace SampleMauiApp;

public partial class ModalTestPage : ContentPage
{
    public ModalTestPage()
    {
        InitializeComponent();
#if MACOS
        MacOSPage.SetModalSheetSizesToContent(this, true);
#endif
    }

    private async void OnCloseModal(object? sender, EventArgs e)
    {
        await Navigation.PopModalAsync();
    }
}
