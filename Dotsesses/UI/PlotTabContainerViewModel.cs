using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Dotsesses.UI;

/// <summary>
/// ViewModel for the tabbed container holding the plot views (violin,
/// correlation, significance).
/// </summary>
public partial class PlotTabContainerViewModel : ViewModelBase
{
    [ObservableProperty]
    private ViolinPlotViewModel _violinPlotViewModel;

    [ObservableProperty]
    private CorrelationPlotViewModel _correlationPlotViewModel;

    [ObservableProperty]
    private SignificancePlotViewModel _significancePlotViewModel;

    [ObservableProperty]
    private bool _isViolinSelected = true;

    [ObservableProperty]
    private bool _isCorrelationSelected = false;

    [ObservableProperty]
    private bool _isSignificanceSelected = false;

    public PlotTabContainerViewModel(
        ViolinPlotViewModel violinPlotViewModel,
        CorrelationPlotViewModel correlationPlotViewModel,
        SignificancePlotViewModel significancePlotViewModel)
    {
        _violinPlotViewModel = violinPlotViewModel;
        _correlationPlotViewModel = correlationPlotViewModel;
        _significancePlotViewModel = significancePlotViewModel;
    }

    [RelayCommand]
    private void SelectViolin()
    {
        IsViolinSelected = true;
        IsCorrelationSelected = false;
        IsSignificanceSelected = false;
    }

    [RelayCommand]
    private void SelectCorrelation()
    {
        IsViolinSelected = false;
        IsCorrelationSelected = true;
        IsSignificanceSelected = false;
    }

    [RelayCommand]
    private void SelectSignificance()
    {
        IsViolinSelected = false;
        IsCorrelationSelected = false;
        IsSignificanceSelected = true;
    }
}
