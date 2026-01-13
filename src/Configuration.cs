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
  public required bool IsUnitedExterior;
  public required UInt128 PackedExteriorIds;
  public required UInt128 PackedStainIds;
}

[Serializable]
public class FestivalFacade
{
  public required uint World;
  public required District District;
  public required sbyte Ward;
  public required ushort Id;
}

[Serializable]
public class Preset
{
  public required string Name;
  public required UInt128 PackedExteriorIds;
  public required UInt128 PackedStainIds;
  public required PlotSize PlotSize;
}

[Serializable]
public class Configuration : IPluginConfiguration
{
  public int Version { get; set; } = 0;

  public List<Facade> Facades { get; set; } = [];
  public List<FestivalFacade> FestivalFacades { get; set; } = [];
  public List<Preset> Presets { get; set; } = [];

  [NonSerialized]
  private IDalamudPluginInterface? PluginInterface;

  public void Initialize(IDalamudPluginInterface pluginInterface)
  {
    PluginInterface = pluginInterface;
  }

  public void Save()
  {
    Facades = Facades.OrderBy(f => f.World).ThenBy(f => f.District).ThenBy(f => f.Ward).ThenBy(f => f.Plot).ToList();
    Presets = Presets.OrderBy(f => f.PlotSize).ToList();
    PluginInterface!.SavePluginConfig(this);
  }
}

public static class UInt128Extensions
{
  public static UInt128 Pack(ushort? u1, ushort? u2, ushort? u3, ushort? u4, ushort? u5, ushort? u6, ushort? u7, ushort? u8)
  {
    UInt128 packed = 0;
    packed |= (UInt128)(u1 ?? ushort.MaxValue) << (16 * 0);
    packed |= (UInt128)(u2 ?? ushort.MaxValue) << (16 * 1);
    packed |= (UInt128)(u3 ?? ushort.MaxValue) << (16 * 2);
    packed |= (UInt128)(u4 ?? ushort.MaxValue) << (16 * 3);
    packed |= (UInt128)(u5 ?? ushort.MaxValue) << (16 * 4);
    packed |= (UInt128)(u6 ?? ushort.MaxValue) << (16 * 5);
    packed |= (UInt128)(u7 ?? ushort.MaxValue) << (16 * 6);
    packed |= (UInt128)(u8 ?? ushort.MaxValue) << (16 * 7);
    return packed;
  }

  public static (ushort? u1, ushort? u2, ushort? u3, ushort? u4, ushort? u5, ushort? u6, ushort? u7, ushort? u8) Unpack(this UInt128 value)
  {
    ushort? u1 = ((value >> (16 * 0)) & 0xFFFF) == ushort.MaxValue ? null : (ushort)((value >> (16 * 0)) & 0xFFFF);
    ushort? u2 = ((value >> (16 * 1)) & 0xFFFF) == ushort.MaxValue ? null : (ushort)((value >> (16 * 1)) & 0xFFFF);
    ushort? u3 = ((value >> (16 * 2)) & 0xFFFF) == ushort.MaxValue ? null : (ushort)((value >> (16 * 2)) & 0xFFFF);
    ushort? u4 = ((value >> (16 * 3)) & 0xFFFF) == ushort.MaxValue ? null : (ushort)((value >> (16 * 3)) & 0xFFFF);
    ushort? u5 = ((value >> (16 * 4)) & 0xFFFF) == ushort.MaxValue ? null : (ushort)((value >> (16 * 4)) & 0xFFFF);
    ushort? u6 = ((value >> (16 * 5)) & 0xFFFF) == ushort.MaxValue ? null : (ushort)((value >> (16 * 5)) & 0xFFFF);
    ushort? u7 = ((value >> (16 * 6)) & 0xFFFF) == ushort.MaxValue ? null : (ushort)((value >> (16 * 6)) & 0xFFFF);
    ushort? u8 = ((value >> (16 * 7)) & 0xFFFF) == ushort.MaxValue ? null : (ushort)((value >> (16 * 7)) & 0xFFFF);
    return (u1, u2, u3, u4, u5, u6, u7, u8);
  }
}
