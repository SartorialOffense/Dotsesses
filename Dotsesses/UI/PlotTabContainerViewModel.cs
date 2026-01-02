using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Dotsesses.UI;

/// <summary>
/// ViewModel for the tabbed container holding all plot types.
/// </summary>
public partial class PlotTabContainerViewModel : ViewModelBase
{
    [ObservableProperty]
    private ViolinPlotViewModel _violinPlotViewModel;

    [ObservableProperty]
    private CorrelationPlotViewModel _correlationPlotViewModel;

    [ObservableProperty]
    private PcaPlotViewModel _pcaPlotViewModel;

    [ObservableProperty]
    private UmapPlotViewModel _umapPlotViewModel;

    [ObservableProperty]
    private TsnePlotViewModel _tsnePlotViewModel;

    [ObservableProperty]
    private bool _isViolinSelected = true;

    [ObservableProperty]
    private bool _isCorrelationSelected = false;

    [ObservableProperty]
    private bool _isPcaSelected = false;

    [ObservableProperty]
    private bool _isUmapSelected = false;

    [ObservableProperty]
    private bool _isTsneSelected = false;

    public PlotTabContainerViewModel(
        ViolinPlotViewModel violinPlotViewModel,
        CorrelationPlotViewModel correlationPlotViewModel,
        PcaPlotViewModel pcaPlotViewModel,
        UmapPlotViewModel umapPlotViewModel,
        TsnePlotViewModel tsnePlotViewModel)
    {
        _violinPlotViewModel = violinPlotViewModel;
        _correlationPlotViewModel = correlationPlotViewModel;
        _pcaPlotViewModel = pcaPlotViewModel;
        _umapPlotViewModel = umapPlotViewModel;
        _tsnePlotViewModel = tsnePlotViewModel;
    }

    private void ClearSelections()
    {
        IsViolinSelected = false;
        IsCorrelationSelected = false;
        IsPcaSelected = false;
        IsUmapSelected = false;
        IsTsneSelected = false;
    }

    [RelayCommand]
    private void SelectViolin()
    {
        ClearSelections();
        IsViolinSelected = true;
    }

    [RelayCommand]
    private void SelectCorrelation()
    {
        ClearSelections();
        IsCorrelationSelected = true;
    }

    [RelayCommand]
    private void SelectPca()
    {
        ClearSelections();
        IsPcaSelected = true;
    }

    [RelayCommand]
    private void SelectUmap()
    {
        ClearSelections();
        IsUmapSelected = true;
    }

    [RelayCommand]
    private void SelectTsne()
    {
        ClearSelections();
        IsTsneSelected = true;
    }

    /// <summary>
    /// Selects a specific tab by index (0=Distribution, 1=Correlation, 2=PCA, 3=UMAP, 4=t-SNE).
    /// </summary>
    public void SelectTabByIndex(int index)
    {
        ClearSelections();
        switch (index)
        {
            case 0: IsViolinSelected = true; break;
            case 1: IsCorrelationSelected = true; break;
            case 2: IsPcaSelected = true; break;
            case 3: IsUmapSelected = true; break;
            case 4: IsTsneSelected = true; break;
            default: IsViolinSelected = true; break;
        }
    }

    /// <summary>
    /// Gets the currently selected tab index.
    /// </summary>
    public int GetSelectedTabIndex()
    {
        if (IsViolinSelected) return 0;
        if (IsCorrelationSelected) return 1;
        if (IsPcaSelected) return 2;
        if (IsUmapSelected) return 3;
        if (IsTsneSelected) return 4;
        return 0;
    }
}
