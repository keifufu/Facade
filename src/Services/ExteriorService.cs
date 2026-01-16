namespace Facade.Services;

public interface IExteriorService : IHostedService
{
  uint CurrentWorld { get; }
  District CurrentDistrict { get; }
  sbyte CurrentWard { get; }
  byte CurrentDivision { get; }
  int DivisionMin { get; }
  int DivisionMax { get; }

  event EventHandler? OnDivisionChange;

  PlotSize? GetPlotSize(sbyte plot);
  short? GetPlotExterior(sbyte plot, ExteriorItemType type);
  IEnumerable<Facade> GetCurrentFacades();
  FestivalFacade? GetCurrentFestivalFacade();
  unsafe void UpdateFestival(bool reset = false);
  unsafe void UpdateExteriors(bool reset = false);
}

public class ExteriorService(ILogger _logger, Configuration _configuration, IFramework _framework, IClientState _clientState, IPlayerState _playerState) : IExteriorService
{
  private readonly Dictionary<int, OutdoorPlotExteriorData> _originalExteriorData = [];
  private readonly List<ushort> _originalFestival = [];
  private bool _wasNotLoaded = false;

  public event EventHandler? OnDivisionChange;

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
    if (_layoutWorld == null || _layoutWorld->ActiveLayout == null || _layoutWorld->ActiveLayout->OutdoorExteriorData == null || _layoutWorld->ActiveLayout->InitState != 7) return null;
    Span<OutdoorPlotExteriorData> plots = _layoutWorld->ActiveLayout->OutdoorExteriorData->Plots;
    if (plot >= plots.Length) return null;
    return plots[plot].Size;
  }

  public unsafe short? GetPlotExterior(sbyte plot, ExteriorItemType type)
  {
    if (plot < DivisionMin | plot >= DivisionMax) return null;
    if (_layoutWorld == null || _layoutWorld->ActiveLayout == null || _layoutWorld->ActiveLayout->OutdoorExteriorData == null || _layoutWorld->ActiveLayout->InitState != 7) return null;
    Span<OutdoorPlotExteriorData> plots = _layoutWorld->ActiveLayout->OutdoorExteriorData->Plots;
    if (plot >= plots.Length) return null;
    if (type > ExteriorItemType.Fence) return null;
    return plots[plot].HousingExteriorIds[(int)type];
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
    UpdateFestival(true);

    _logger.ServiceLifecycle();
    return Task.CompletedTask;
  }

  // Is doing this on every frame fine? Yeah.
  private void OnFrameworkUpdate(IFramework _)
  {
    bool shouldUpdateExteriors = false;

    if (CurrentDistrict != _lastDistrict || CurrentWard != _lastWard)
    {
      _originalFestival.Clear();
      UpdateFestival();
    }

    if (CurrentDistrict != _lastDistrict || CurrentWard != _lastWard || CurrentDivision != _lastDivision)
    {
      _lastDistrict = CurrentDistrict;
      _lastWard = CurrentWard;
      _lastDivision = CurrentDivision;

      _originalExteriorData.Clear();
      shouldUpdateExteriors = true;

      OnDivisionChange?.Invoke(this, new());
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

  public FestivalFacade? GetCurrentFestivalFacade()
  {
    return _configuration.FestivalFacades.FirstOrDefault(facade => facade.World == CurrentWorld && facade.District == CurrentDistrict && facade.Ward == CurrentWard);
  }

  private unsafe void SetFestival(List<ushort> festivalIds)
  {
    if (festivalIds.Count != 8)
    {
      _logger.Error($"wrong list length: {festivalIds.Count}");
      return;
    }

    ushort* festivalArray = stackalloc ushort[] { 0, 0, 0, 0, 0, 0, 0, 0 };
    for (int i = 0; i < 8; i++)
    {
      festivalArray[i] = festivalIds[i];
    }

    if (_layoutWorld == null || _layoutWorld->ActiveLayout == null) return;
    _layoutWorld->ActiveLayout->SetActiveFestivals((GameMain.Festival*)festivalArray);
  }

  public unsafe void UpdateFestival(bool reset = false)
  {
    if (_layoutWorld == null || _layoutWorld->ActiveLayout == null) return;
    FestivalFacade? festivalFacade = GetCurrentFestivalFacade();

    if (_originalFestival.Count == 8 && (festivalFacade == null || reset))
    {
      SetFestival(_originalFestival);
      _originalFestival.Clear();
    }
    else if (festivalFacade != null && !reset)
    {
      if (_originalFestival.Count == 0)
      {
        foreach (GameMain.Festival festival in _layoutWorld->ActiveLayout->ActiveFestivals)
        {
          _originalFestival.Add(festival.Id);
        }
      }

      List<ushort> currentFestivalIds = [festivalFacade.Id, 0, 0, 0, 0, 0, 0, 0];
      SetFestival(currentFestivalIds);
    }
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

        void SetType<T>(Span<T> obj, ExteriorItemType type, ushort? value, Func<OutdoorPlotExteriorData, int, T> getValue)
        {
          if (value != null)
          {
            if (typeof(T) == typeof(short)) obj[(int)type] = (T)(object)(short)value;
            else if (typeof(T) == typeof(sbyte)) obj[(int)type] = (T)(object)(sbyte)value;
          }
          else if (_originalExteriorData.TryGetValue(facade.Plot, out OutdoorPlotExteriorData originalExteriorData))
          {
            obj[(int)type] = getValue(originalExteriorData, (int)type);
          }
        }

        (ushort? roof, ushort? walls, ushort? windows, ushort? door, ushort? optionalRoof, ushort? optionalWall, ushort? optionalSignboard, ushort? fence) = facade.PackedExteriorIds.Unpack();

        SetType(exteriorData.HousingExteriorIds, ExteriorItemType.Roof, roof, (data, index) => data.HousingExteriorIds[index]);
        SetType(exteriorData.HousingExteriorIds, ExteriorItemType.Walls, walls, (data, index) => data.HousingExteriorIds[index]);
        SetType(exteriorData.HousingExteriorIds, ExteriorItemType.Windows, windows, (data, index) => data.HousingExteriorIds[index]);
        SetType(exteriorData.HousingExteriorIds, ExteriorItemType.Door, door, (data, index) => data.HousingExteriorIds[index]);
        SetType(exteriorData.HousingExteriorIds, ExteriorItemType.OptionalRoof, optionalRoof, (data, index) => data.HousingExteriorIds[index]);
        SetType(exteriorData.HousingExteriorIds, ExteriorItemType.OptionalWall, optionalWall, (data, index) => data.HousingExteriorIds[index]);
        SetType(exteriorData.HousingExteriorIds, ExteriorItemType.OptionalSignboard, optionalSignboard, (data, index) => data.HousingExteriorIds[index]);
        SetType(exteriorData.HousingExteriorIds, ExteriorItemType.Fence, fence, (data, index) => data.HousingExteriorIds[index]);

        (ushort? roofStain, ushort? wallsStain, ushort? windowsStain, ushort? doorStain, ushort? optionalRoofStain, ushort? optionalWallStain, ushort? optionalSignboardStain, ushort? fenceStain) = facade.PackedStainIds.Unpack();

        SetType(exteriorData.StainIds, ExteriorItemType.Roof, roofStain, (data, index) => data.StainIds[index]);
        SetType(exteriorData.StainIds, ExteriorItemType.Walls, wallsStain, (data, index) => data.StainIds[index]);
        SetType(exteriorData.StainIds, ExteriorItemType.Windows, windowsStain, (data, index) => data.StainIds[index]);
        SetType(exteriorData.StainIds, ExteriorItemType.Door, doorStain, (data, index) => data.StainIds[index]);
        SetType(exteriorData.StainIds, ExteriorItemType.OptionalRoof, optionalRoofStain, (data, index) => data.StainIds[index]);
        SetType(exteriorData.StainIds, ExteriorItemType.OptionalWall, optionalWallStain, (data, index) => data.StainIds[index]);
        SetType(exteriorData.StainIds, ExteriorItemType.OptionalSignboard, optionalSignboardStain, (data, index) => data.StainIds[index]);
        SetType(exteriorData.StainIds, ExteriorItemType.Fence, fenceStain, (data, index) => data.StainIds[index]);

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

public enum ExteriorItemType : ushort
{
  Roof = 0,
  Walls = 1,
  Windows = 2,
  Door = 3,
  OptionalRoof = 4,
  OptionalWall = 5,
  OptionalSignboard = 6,
  Fence = 7,
  UnitedExterior = 8,
  UnitedExteriorPreset = 9
}
