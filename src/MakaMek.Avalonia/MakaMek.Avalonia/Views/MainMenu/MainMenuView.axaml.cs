using System;
using Avalonia.Interactivity;
using Sanet.MakaMek.Presentation.ViewModels;
using Sanet.MVVM.Views.Avalonia;

namespace Sanet.MakaMek.Avalonia.Views.MainMenu;

public partial class MainMenuView : BaseView<MainMenuViewModel>
{
    public MainMenuView()
    {
        InitializeComponent();
#if DEBUG
        SpikeButton.IsVisible = true;
        if (OperatingSystem.IsBrowser() ||
            Environment.GetEnvironmentVariable("MAKAMEK_RECORD_SHEET_SPIKE") == "1")
            SpikeHost.IsVisible = true;
#endif
    }

    private void ShowRecordSheetSpike(object? sender, RoutedEventArgs e) => SpikeHost.IsVisible = true;

    private void HideRecordSheetSpike(object? sender, RoutedEventArgs e) => SpikeHost.IsVisible = false;
}
