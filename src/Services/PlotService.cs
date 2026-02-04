using System.Text.Json;
using System.Text.Json.Serialization;

namespace Facade.Services;

public interface IPlotService : IHostedService
{
  uint CurrentWorld { get; }
  District CurrentDistrict { get; }
  sbyte CurrentWard { get; }
  byte CurrentDivision { get; }
  int DivisionMin { get; }
  int DivisionMax { get; }

  sbyte GetCurrentPlot();

#if SAVE_MODE
  void SaveCorner();
  void SaveJson();
#endif
}

public class PlotService(ILogger _logger, IClientState _clientState, IDalamudPluginInterface _pluginInterface, IPlayerState _playerState, IObjectTable _objectTable) : IPlotService
{
  private Dictionary<(District District, byte Division), List<Plot>> _plots = [];

  public Task StartAsync(CancellationToken cancellationToken)
  {
    string plotsJsonPath = Path.Combine(_pluginInterface.AssemblyLocation.Directory?.FullName!, "plots.json");
    if (!File.Exists(plotsJsonPath)) throw new Exception("Failed to find plots.json");

    string json = File.ReadAllText(plotsJsonPath);
    JsonSerializerOptions options = new()
    {
      Converters = { new Vector2Converter() }
    };

    List<SimplePlot>? simplePlots = JsonSerializer.Deserialize<List<SimplePlot>>(json, options) ?? throw new Exception("Failed to deserialize plots.json");

    foreach (SimplePlot simplePlot in simplePlots)
    {
      (District District, byte Division) key = (simplePlot.District, simplePlot.Division);

      if (!_plots.ContainsKey(key))
      {
        _plots[key] = [];
      }

      _plots[key].AddRange(simplePlot.Plots);
    }
    _logger.ServiceLifecycle();
    return Task.CompletedTask;
  }

  public Task StopAsync(CancellationToken cancellationToken)
  {
    _logger.ServiceLifecycle();
    return Task.CompletedTask;
  }

  public uint CurrentWorld => _playerState.CurrentWorld.RowId;
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
  private unsafe HousingManager* _housingManager => HousingManager.Instance();
  public unsafe sbyte CurrentWard => _housingManager == null ? (sbyte)-1 : _housingManager->GetCurrentWard();
  public unsafe byte CurrentDivision => _housingManager == null ? (byte)0 : _housingManager->GetCurrentDivision();
  public int DivisionMin => CurrentDivision == 1 ? 0 : 30;
  public int DivisionMax => CurrentDivision == 1 ? 30 : 60;

  private Vector2 _lastPosition;
  private sbyte _lastPlot = -1;
  public unsafe sbyte GetCurrentPlot()
  {
#if SAVE_MODE
    return -1;
#pragma warning disable CS0162
#endif

    if (CurrentWard == -1) return -1;
    if (_objectTable.LocalPlayer == null) return -1;

    Vector2 position = new(_objectTable.LocalPlayer.Position.X, _objectTable.LocalPlayer.Position.Z);
    if (_lastPosition == position) return _lastPlot;
    _lastPosition = position;

    _plots.TryGetValue((CurrentDistrict, CurrentDivision), out List<Plot>? plots);
    if (plots == null) return -1;

    foreach (Plot plot in plots)
    {
      if (plot.IsInside(position, 7.5f))
      {
        _lastPlot = plot.PlotId;
        return _lastPlot;
      }
    }

    _lastPlot = _housingManager->GetCurrentPlot();
    return _lastPlot;
  }

#if SAVE_MODE
  public unsafe void SaveCorner()
  {
    sbyte plot = _housingManager->GetCurrentPlot();
    if (plot == -1)
    {
      _logger.Chat("You're not on a plot.");
      return;
    }

    if (_objectTable.LocalPlayer == null) return;
    Vector2 position = new(_objectTable.LocalPlayer.Position.X, _objectTable.LocalPlayer.Position.Z);

    _plots.TryGetValue((CurrentDistrict, CurrentDivision), out List<Plot>? plots);
    plots ??= [];

    Plot? p = plots.Find((p) => p.PlotId == plot);
    if (p != null) plots.Remove(p);
    p ??= new();

    p.PlotId = plot;

    if (p.C1.X == 0)
    {
      p.C1 = position;
      _logger.Chat("Saved corner 1 for plot " + (plot + 1));
    }
    else if (p.C2.X == 0)
    {
      p.C2 = position;
      _logger.Chat("Saved corner 2 for plot " + (plot + 1));
    }
    else if (p.C3.X == 0)
    {
      p.C3 = position;
      _logger.Chat("Saved corner 3 for plot " + (plot + 1));
    }
    else if (p.C4.X == 0)
    {
      p.C4 = position;
      _logger.Chat("Saved corner 4 for plot " + (plot + 1));
    }

    plots.Add(p);
    _plots.Remove((CurrentDistrict, CurrentDivision));
    _plots.Add((CurrentDistrict, CurrentDivision), plots);
  }

  public void SaveJson()
  {
    string filePath = "/home/keifufu/plots.json";
    var simplePlots = _plots.Select(kvp => new
    {
      kvp.Key.District,
      kvp.Key.Division,
      Plots = kvp.Value
    });
    JsonSerializerOptions options = new()
    {
      Converters = { new Vector2Converter() }
    };
    string json = JsonSerializer.Serialize(simplePlots, options);
    File.WriteAllText(filePath, json);
    _logger.Chat($"Saved plots.json to '{filePath}'");
  }
#endif
}

public class SimplePlot
{
  public required District District { get; set; }
  public required byte Division { get; set; }
  public required List<Plot> Plots { get; set; }
}

public class Plot
{
  public sbyte PlotId { get; set; }
  public Vector2 C1 { get; set; }
  public Vector2 C2 { get; set; }
  public Vector2 C3 { get; set; }
  public Vector2 C4 { get; set; }

  public bool IsInside(Vector2 position, float padding)
  {
    Vector2[] corners = [C1, C2, C3, C4];

    Vector2 center = new(
        (corners[0].X + corners[1].X + corners[2].X + corners[3].X) / 4,
        (corners[0].Y + corners[1].Y + corners[2].Y + corners[3].Y) / 4
    );

    Vector2[] paddedCorners = new Vector2[4];

    for (int i = 0; i < 4; i++)
    {
      Vector2 dir = corners[i] - center;
      dir = Vector2.Normalize(dir);
      paddedCorners[i] = corners[i] + (dir * padding);
    }

    Vector2 edge1 = paddedCorners[1] - paddedCorners[0];
    Vector2 edge2 = paddedCorners[2] - paddedCorners[1];
    Vector2 edge3 = paddedCorners[3] - paddedCorners[2];
    Vector2 edge4 = paddedCorners[0] - paddedCorners[3];

    Vector2 pointVector1 = position - paddedCorners[0];
    Vector2 pointVector2 = position - paddedCorners[1];
    Vector2 pointVector3 = position - paddedCorners[2];
    Vector2 pointVector4 = position - paddedCorners[3];

    bool isOnLeftOfEdge1 = Vector2.Cross(edge1, pointVector1) >= 0;
    bool isOnLeftOfEdge2 = Vector2.Cross(edge2, pointVector2) >= 0;
    bool isOnLeftOfEdge3 = Vector2.Cross(edge3, pointVector3) >= 0;
    bool isOnLeftOfEdge4 = Vector2.Cross(edge4, pointVector4) >= 0;

    return isOnLeftOfEdge1 && isOnLeftOfEdge2 && isOnLeftOfEdge3 && isOnLeftOfEdge4;
  }
}

public class Vector2Converter : JsonConverter<Vector2>
{
  public override void Write(Utf8JsonWriter writer, Vector2 value, JsonSerializerOptions options)
  {
    writer.WriteStartObject();
    writer.WriteNumber("X", value.X);
    writer.WriteNumber("Y", value.Y);
    writer.WriteEndObject();
  }

  public override Vector2 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
  {
    float x = 0, y = 0;

    if (reader.TokenType == JsonTokenType.StartObject)
    {
      reader.Read();
      while (reader.TokenType == JsonTokenType.PropertyName)
      {
        string? propertyName = reader.GetString();
        reader.Read();

        switch (propertyName)
        {
          case "X":
            x = reader.GetSingle();
            break;
          case "Y":
            y = reader.GetSingle();
            break;
        }
        reader.Read();
      }
    }

    return new Vector2(x, y);
  }
}
