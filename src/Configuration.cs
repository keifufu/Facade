using Dalamud.Configuration;

namespace Facade;

[Serializable]
// TerritoryType
public enum District : ushort
{
  Mist = 339,
  LavenderBeds = 340,
  TheGoblet = 341,
  Shirogane = 641,
  Empyreum = 979,
  Invalid = 0
}

[Serializable]
public class Facade
{
  public required uint World;
  public required District District;
  public required sbyte Ward;
  public required sbyte Plot;
  public required uint? HousingUnitedExterior;
  public required uint? Stain;
}

[Serializable]
public class Configuration : IPluginConfiguration
{
  public int Version { get; set; } = 0;

  public List<Facade> Facades { get; set; } = [];

  [NonSerialized]
  private IDalamudPluginInterface? PluginInterface;

  public void Initialize(IDalamudPluginInterface pluginInterface)
  {
    PluginInterface = pluginInterface;
  }

  public void Save()
  {
    Facades = Facades.OrderBy(f => f.World).ThenBy(f => f.District).ThenBy(f => f.Ward).ThenBy(f => f.Plot).ToList();
    PluginInterface!.SavePluginConfig(this);
  }
}
