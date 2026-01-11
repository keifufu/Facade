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
  public required Int128 PackedExteriorIds;
  public required Int128 PackedStainIds;

  public static Int128 Pack(uint? u1, uint? u2, uint? u3, uint? u4, uint? u5, uint? u6, uint? u7, uint? u8)
  {
    Int128 packed = 0;
    packed |= (Int128)(u1 ?? 0) << (16 * 0);
    packed |= (Int128)(u2 ?? 0) << (16 * 1);
    packed |= (Int128)(u3 ?? 0) << (16 * 2);
    packed |= (Int128)(u4 ?? 0) << (16 * 3);
    packed |= (Int128)(u5 ?? 0) << (16 * 4);
    packed |= (Int128)(u6 ?? 0) << (16 * 5);
    packed |= (Int128)(u7 ?? 0) << (16 * 6);
    packed |= (Int128)(u8 ?? 0) << (16 * 7);
    return packed;
  }

  public static (uint? u1, uint? u2, uint? u3, uint? u4, uint? u5, uint? u6, uint? u7, uint? u8) Unpack(Int128 packed)
  {
    uint? u1 = ((packed >> (16 * 0)) & 0x1FFF) == 0 ? null : (uint)(packed >> (16 * 0)) & 0x1FFF;
    uint? u2 = ((packed >> (16 * 1)) & 0x1FFF) == 0 ? null : (uint)(packed >> (16 * 1)) & 0x1FFF;
    uint? u3 = ((packed >> (16 * 2)) & 0x1FFF) == 0 ? null : (uint)(packed >> (16 * 2)) & 0x1FFF;
    uint? u4 = ((packed >> (16 * 3)) & 0x1FFF) == 0 ? null : (uint)(packed >> (16 * 3)) & 0x1FFF;
    uint? u5 = ((packed >> (16 * 4)) & 0x1FFF) == 0 ? null : (uint)(packed >> (16 * 4)) & 0x1FFF;
    uint? u6 = ((packed >> (16 * 5)) & 0x1FFF) == 0 ? null : (uint)(packed >> (16 * 5)) & 0x1FFF;
    uint? u7 = ((packed >> (16 * 6)) & 0x1FFF) == 0 ? null : (uint)(packed >> (16 * 6)) & 0x1FFF;
    uint? u8 = ((packed >> (16 * 7)) & 0x1FFF) == 0 ? null : (uint)(packed >> (16 * 7)) & 0x1FFF;
    return (u1, u2, u3, u4, u5, u6, u7, u8);
  }
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
public class Configuration : IPluginConfiguration
{
  public int Version { get; set; } = 0;

  public List<Facade> Facades { get; set; } = [];
  public List<FestivalFacade> FestivalFacades { get; set; } = [];

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
