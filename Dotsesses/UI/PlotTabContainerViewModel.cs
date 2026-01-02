using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Dotsesses.UI;

/// <summary>
/// ViewModel for the tabbed container holding violin and correlation plots.
/// </summary>
public partial class PlotTabContainerViewModel : ViewModelBase
{
    [ObservableProperty]
    private ViolinPlotViewModel _violinPlotViewModel;

    [ObservableProperty]
    private CorrelationPlotViewModel _correlationPlotViewModel;

    [ObservableProperty]
    private bool _isViolinSelected = true;

    [ObservableProperty]
    private bool _isCorrelationSelected = false;

    public PlotTabContainerViewModel(
        ViolinPlotViewModel violinPlotViewModel,
        CorrelationPlotViewModel correlationPlotViewModel)
    {
        _violinPlotViewModel = violinPlotViewModel;
        _correlationPlotViewModel = correlationPlotViewModel;
    }

    [RelayCommand]
    private void SelectViolin()
    {
        IsViolinSelected = true;
        IsCorrelationSelected = false;
    }

    [RelayCommand]
    private void SelectCorrelation()
    {
        IsViolinSelected = false;
        IsCorrelationSelected = true;
    }
}
