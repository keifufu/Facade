namespace Facade.Services;

public interface IExteriorService : IHostedService
{
  event EventHandler? OnDivisionChange;
  PlotSize? GetPlotSize(sbyte plot);
  short? GetPlotExterior(sbyte plot, ExteriorItemType type);
  IEnumerable<Facade> GetCurrentFacades();
  FestivalFacade? GetCurrentFestivalFacade();
  unsafe void UpdateFestival(bool reset = false);
  unsafe void UpdateExteriors(bool reset = false, List<sbyte>? currentPlots = null);
}

public class ExteriorService(ILogger _logger, Configuration _configuration, IPlotService _plotService, IFramework _framework) : IExteriorService
{
  private readonly Dictionary<int, OutdoorPlotExteriorData> _originalExteriorData = [];
  private readonly List<GameMain.Festival> _originalFestival = [];
  private bool _wasNotLoaded = false;

  public event EventHandler? OnDivisionChange;

  private unsafe HousingManager* _housingManager => HousingManager.Instance();
  private unsafe LayoutWorld* _layoutWorld => LayoutWorld.Instance();
  private sbyte _lastWard = -1;
  private byte _lastDivision = 0;
  private List<sbyte> _lastPlots = [];

  private District _lastDistrict = District.Invalid;

  public unsafe PlotSize? GetPlotSize(sbyte plot)
  {
    if (plot < _plotService.DivisionMin | plot >= _plotService.DivisionMax) return null;
    if (_layoutWorld == null || _layoutWorld->ActiveLayout == null || _layoutWorld->ActiveLayout->OutdoorExteriorData == null || _layoutWorld->ActiveLayout->InitState != 7) return null;
    Span<OutdoorPlotExteriorData> plots = _layoutWorld->ActiveLayout->OutdoorExteriorData->Plots;
    if (plot >= plots.Length) return null;
    return plots[plot].Size;
  }

  public unsafe short? GetPlotExterior(sbyte plot, ExteriorItemType type)
  {
    if (plot < _plotService.DivisionMin | plot >= _plotService.DivisionMax) return null;
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

  private void OnFrameworkUpdate(IFramework _)
  {
#if SAVE_MODE
    unsafe
    {
      if (_housingManager != null && _housingManager->OutdoorTerritory != null)
      {
        _housingManager->OutdoorTerritory->FurnitureStruct.FurnitureVector.Clear();
      }
    }
#endif

    bool shouldUpdateExteriors = false;

    if (_plotService.CurrentDistrict != _lastDistrict || _plotService.CurrentWard != _lastWard)
    {
      _originalFestival.Clear();
      UpdateFestival();
    }

    if (_plotService.CurrentDistrict != _lastDistrict || _plotService.CurrentWard != _lastWard || _plotService.CurrentDivision != _lastDivision)
    {
      _lastDistrict = _plotService.CurrentDistrict;
      _lastWard = _plotService.CurrentWard;
      _lastDivision = _plotService.CurrentDivision;

      _originalExteriorData.Clear();
      shouldUpdateExteriors = true;

      OnDivisionChange?.Invoke(this, new());
    }

    List<sbyte> currentPlots = _plotService.GetCurrentPlots();
    if (!currentPlots.SequenceEqual(_lastPlots))
    {
      _lastPlots = currentPlots;
      shouldUpdateExteriors = true;
    }

    if (_wasNotLoaded)
    {
      _wasNotLoaded = false;
      shouldUpdateExteriors = true;
    }

    if (shouldUpdateExteriors) UpdateExteriors(false, currentPlots);
  }

  public IEnumerable<Facade> GetCurrentFacades()
  {
#if SAVE_MODE
    List<Facade> facades = [];
    for (int i = _plotService.DivisionMin; i < _plotService.DivisionMax; i++)
    {
      facades.Add(new()
      {
        World = _plotService.CurrentWorld,
        District = _plotService.CurrentDistrict,
        IsUnitedExterior = false,
        PackedExteriorIds = UInt128.Parse("15976697433711664612988337205245378560"),
        PackedStainIds = 0,
        Ward = _plotService.CurrentWard,
        Plot = (sbyte)i,
      });
    }
    return facades;
#pragma warning disable CS0162
#endif

    return _configuration.Facades.Where(facade => facade.World == _plotService.CurrentWorld && facade.District == _plotService.CurrentDistrict && facade.Ward == _plotService.CurrentWard && facade.Plot >= _plotService.DivisionMin && facade.Plot < _plotService.DivisionMax);
  }

  public FestivalFacade? GetCurrentFestivalFacade()
  {
    return _configuration.FestivalFacades.FirstOrDefault(facade => facade.World == _plotService.CurrentWorld && facade.District == _plotService.CurrentDistrict && facade.Ward == _plotService.CurrentWard);
  }

  private unsafe void SetFestival(List<GameMain.Festival> festivals)
  {
    if (_plotService.CurrentDivision == 0) return;
    if (festivals.Count != 8) return;

    fixed (GameMain.Festival* festivalsArray = new GameMain.Festival[8])
    {
      for (int i = 0; i < 8; i++)
      {
        festivalsArray[i].Id = festivals[i].Id;
        festivalsArray[i].Phase = festivals[i].Phase;
      }

      if (_layoutWorld == null || _layoutWorld->ActiveLayout == null) return;
      _layoutWorld->ActiveLayout->SetActiveFestivals(festivalsArray);
    }
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
          _originalFestival.Add(festival);
        }
      }

      GameMain.Festival newFestival = new()
      {
        Id = festivalFacade.Id
      };
      GameMain.Festival empty = new();
      List<GameMain.Festival> currentFestivalIds = [newFestival, empty, empty, empty, empty, empty, empty, empty];
      SetFestival(currentFestivalIds);
    }
  }

  private unsafe void SetExterior(sbyte plot, OutdoorPlotExteriorData exteriorData)
  {
    if (_layoutWorld == null || _layoutWorld->ActiveLayout == null || _layoutWorld->ActiveLayout->OutdoorExteriorData == null) return;
    if (plot >= 60) return;
    if (_layoutWorld->ActiveLayout->OutdoorExteriorData->Plots[plot].Size != exteriorData.Size) return;
    _layoutWorld->ActiveLayout->HousingLayoutDataUpdatePending = true;
    for (int i = 0; i < 8; i++)
    {
      _layoutWorld->ActiveLayout->OutdoorExteriorData->Plots[plot].HousingExteriorIds[i] = exteriorData.HousingExteriorIds[i];
      _layoutWorld->ActiveLayout->OutdoorExteriorData->Plots[plot].StainIds[i] = exteriorData.StainIds[i];
    }
  }

  public unsafe void UpdateExteriors(bool reset = false, List<sbyte>? _currentPlots = null)
  {
    List<sbyte> currentPlots = _currentPlots ?? _plotService.GetCurrentPlots(reset);

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

      if (currentPlots.Contains(facade.Plot) || reset)
      {
        if (_originalExteriorData.TryGetValue(facade.Plot, out OutdoorPlotExteriorData originalExteriorData))
        {
          SetExterior(facade.Plot, originalExteriorData);
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

        SetExterior(facade.Plot, exteriorData);
      }
    }

    foreach (int plot in _originalExteriorData.Keys)
    {
      if (!handledPlots.Contains(plot))
      {
        if (_originalExteriorData.TryGetValue(plot, out OutdoorPlotExteriorData originalExteriorData))
        {
          SetExterior((sbyte)plot, originalExteriorData);
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
