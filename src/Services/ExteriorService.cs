namespace Facade.Services;

public interface IExteriorService : IHostedService
{
  uint CurrentWorld { get; }
  District CurrentDistrict { get; }
  sbyte CurrentWard { get; }
  byte CurrentDivision { get; }
  int DivisionMin { get; }
  int DivisionMax { get; }

  PlotSize? GetPlotSize(sbyte plot);
  IEnumerable<Facade> GetCurrentFacades();
  unsafe void UpdateExteriors(bool reset = false);
}

public class ExteriorService(ILogger _logger, Configuration _configuration, IFramework _framework, IDataManager _dataManager, IClientState _clientState, IPlayerState _playerState) : IExteriorService
{
  private readonly Dictionary<int, OutdoorPlotExteriorData> _originalExteriorData = [];
  private bool _wasNotLoaded = false;

  public uint CurrentWorld => _playerState.CurrentWorld.RowId;
  private unsafe HousingManager* _housingManager => HousingManager.Instance();
  private unsafe LayoutWorld* _layoutWorld => LayoutWorld.Instance();

  private District _lastDistrict = District.Invalid;
  public District CurrentDistrict
  {
    get
    {
      if (Enum.IsDefined(typeof(District), _clientState.TerritoryType))
      {
        return (District)_clientState.TerritoryType;
      }
      return District.Invalid;
    }
  }

  private sbyte _lastWard = -1;
  public unsafe sbyte CurrentWard => _housingManager == null ? (sbyte)-1 : _housingManager->GetCurrentWard();

  private byte _lastDivision = 0;
  public unsafe byte CurrentDivision => _housingManager == null ? (byte)0 : _housingManager->GetCurrentDivision();

  private sbyte _lastPlot = -1;
  private unsafe sbyte CurrentPlot => _housingManager == null ? (sbyte)-1 : _housingManager->GetCurrentPlot();

  public int DivisionMin => CurrentDivision == 1 ? 0 : 30;
  public int DivisionMax => CurrentDivision == 1 ? 30 : 60;

  public unsafe PlotSize? GetPlotSize(sbyte plot)
  {
    if (plot < DivisionMin | plot >= DivisionMax) return null;
    if (_layoutWorld == null) return null;
    LayoutManager* layoutManager = _layoutWorld->ActiveLayout;
    if (layoutManager->InitState != 7) return null;
    Span<OutdoorPlotExteriorData> plots = layoutManager->OutdoorExteriorData->Plots;
    if (plot >= plots.Length) return null;
    return plots[plot].Size;
  }

  public Task StartAsync(CancellationToken cancellationToken)
  {
    _framework.Update += OnFrameworkUpdate;

    _logger.ServiceLifecycle();
    return Task.CompletedTask;
  }

  public Task StopAsync(CancellationToken cancellationToken)
  {
    _framework.Update -= OnFrameworkUpdate;

    UpdateExteriors(true);

    _logger.ServiceLifecycle();
    return Task.CompletedTask;
  }

  // Is doing this on every frame fine? Yeah.
  private void OnFrameworkUpdate(IFramework _)
  {
    bool shouldUpdateExteriors = false;

    if (CurrentDistrict != _lastDistrict || CurrentWard != _lastWard || CurrentDivision != _lastDivision)
    {
      _lastDistrict = CurrentDistrict;
      _lastWard = CurrentWard;
      _lastDivision = CurrentDivision;

      _originalExteriorData.Clear();
      shouldUpdateExteriors = true;
    }

    if (CurrentPlot != _lastPlot)
    {
      _lastPlot = CurrentPlot;

      shouldUpdateExteriors = true;
    }

    if (_wasNotLoaded)
    {
      _wasNotLoaded = false;

      shouldUpdateExteriors = true;
    }

    if (shouldUpdateExteriors) UpdateExteriors();
  }

  public IEnumerable<Facade> GetCurrentFacades()
  {
    return _configuration.Facades.Where(facade => facade.World == CurrentWorld && facade.District == CurrentDistrict && facade.Ward == CurrentWard && facade.Plot >= DivisionMin && facade.Plot < DivisionMax);
  }

  public unsafe void UpdateExteriors(bool reset = false)
  {
    if (_layoutWorld == null || _layoutWorld->ActiveLayout == null || _layoutWorld->ActiveLayout->OutdoorExteriorData == null) return;
    Span<OutdoorPlotExteriorData> exteriorPlots = _layoutWorld->ActiveLayout->OutdoorExteriorData->Plots;
    List<int> handledPlots = [];

    IEnumerable<Facade> facades = GetCurrentFacades();
    foreach (Facade facade in facades)
    {
      if (facade.Plot >= exteriorPlots.Length) return;
      OutdoorPlotExteriorData exteriorData = exteriorPlots[facade.Plot];
      byte plotSize = (byte)exteriorData.Size;
      handledPlots.Add(facade.Plot);

      if (CurrentPlot == facade.Plot || reset)
      {
        if (_originalExteriorData.TryGetValue(facade.Plot, out OutdoorPlotExteriorData originalExteriorData))
        {
          _layoutWorld->ActiveLayout->SetOutdoorPlotExterior(facade.Plot, &originalExteriorData);
        }
      }
      else
      {
        if (exteriorData.HousingExteriorIds[0] == -1)
        {
          _wasNotLoaded = true;
          return;
        }

        if (!_originalExteriorData.ContainsKey(facade.Plot))
        {
          _originalExteriorData.Add(facade.Plot, exteriorData);
        }

        if (facade.HousingUnitedExterior != null && _dataManager.GetExcelSheet<HousingUnitedExterior>().TryGetRow(facade.HousingUnitedExterior ?? 0, out HousingUnitedExterior exterior))
        {
          exteriorData.HousingExteriorIds[0] = (short)exterior.Roof.RowId;
          exteriorData.HousingExteriorIds[1] = (short)exterior.Walls.RowId;
          exteriorData.HousingExteriorIds[2] = (short)exterior.Windows.RowId;
          exteriorData.HousingExteriorIds[3] = (short)exterior.Door.RowId;
          exteriorData.HousingExteriorIds[4] = (short)exterior.OptionalRoof.RowId;
          exteriorData.HousingExteriorIds[5] = (short)exterior.OptionalWall.RowId;
          exteriorData.HousingExteriorIds[6] = (short)exterior.OptionalSignboard.RowId;
          exteriorData.HousingExteriorIds[7] = (short)exterior.Fence.RowId;
        }
        else
        {
          if (_originalExteriorData.TryGetValue(facade.Plot, out OutdoorPlotExteriorData originalExteriorData))
          {
            exteriorData.HousingExteriorIds[0] = originalExteriorData.HousingExteriorIds[0];
            exteriorData.HousingExteriorIds[1] = originalExteriorData.HousingExteriorIds[1];
            exteriorData.HousingExteriorIds[2] = originalExteriorData.HousingExteriorIds[2];
            exteriorData.HousingExteriorIds[3] = originalExteriorData.HousingExteriorIds[3];
            exteriorData.HousingExteriorIds[4] = originalExteriorData.HousingExteriorIds[4];
            exteriorData.HousingExteriorIds[5] = originalExteriorData.HousingExteriorIds[5];
            exteriorData.HousingExteriorIds[6] = originalExteriorData.HousingExteriorIds[6];
            exteriorData.HousingExteriorIds[7] = originalExteriorData.HousingExteriorIds[7];
          }
        }

        if (facade.Stain != null)
        {
          exteriorData.StainIds[0] = (sbyte)facade.Stain;
          exteriorData.StainIds[1] = (sbyte)facade.Stain;
          exteriorData.StainIds[2] = (sbyte)facade.Stain;
          exteriorData.StainIds[3] = (sbyte)facade.Stain;
          exteriorData.StainIds[4] = (sbyte)facade.Stain;
          exteriorData.StainIds[5] = (sbyte)facade.Stain;
          exteriorData.StainIds[6] = (sbyte)facade.Stain;
          exteriorData.StainIds[7] = (sbyte)facade.Stain;
        }
        else
        {
          if (_originalExteriorData.TryGetValue(facade.Plot, out OutdoorPlotExteriorData originalExteriorData))
          {
            exteriorData.StainIds[0] = originalExteriorData.StainIds[0];
            exteriorData.StainIds[1] = originalExteriorData.StainIds[1];
            exteriorData.StainIds[2] = originalExteriorData.StainIds[2];
            exteriorData.StainIds[3] = originalExteriorData.StainIds[3];
            exteriorData.StainIds[4] = originalExteriorData.StainIds[4];
            exteriorData.StainIds[5] = originalExteriorData.StainIds[5];
            exteriorData.StainIds[6] = originalExteriorData.StainIds[6];
            exteriorData.StainIds[7] = originalExteriorData.StainIds[7];
          }
        }

        _layoutWorld->ActiveLayout->SetOutdoorPlotExterior(facade.Plot, &exteriorData);
      }
    }

    foreach (int plot in _originalExteriorData.Keys)
    {
      if (!handledPlots.Contains(plot))
      {
        if (_originalExteriorData.TryGetValue(plot, out OutdoorPlotExteriorData originalExteriorData))
        {
          _layoutWorld->ActiveLayout->SetOutdoorPlotExterior(plot, &originalExteriorData);
          _originalExteriorData.Remove(plot);
        }
      }
    }
  }
}
