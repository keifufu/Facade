
namespace Facade.Windows;

// HousingExterior.Unknown1 is the type, e.g. 1 for Roof, 2 for Walls, etc.

public partial class ConfigWindow(Configuration _configuration, IExteriorService _exteriorService, IDataManager _dataManager, IDalamudPluginInterface _pluginInterface, ITextureProvider _textureProvider) : Window("Facade##FacadeConfigWindow")
{
  private bool _addingFacade = false;
  private Facade? _editingFacade = null;
  private sbyte? _selectedPlot = null;
  private uint? _selectedExterior = null;
  private uint? _selectedStain = null;

  private readonly IFontHandle _uiFont = _pluginInterface.UiBuilder.FontAtlas.NewDelegateFontHandle(e =>
  {
    e.OnPreBuild(tk =>
    {
      float fontPx = UiBuilder.DefaultFontSizePx;
      SafeFontConfig safeFontConfig = new() { SizePx = fontPx };
      tk.AddDalamudAssetFont(Dalamud.DalamudAsset.NotoSansJpMedium, safeFontConfig);
      tk.AttachExtraGlyphsForDalamudLanguage(safeFontConfig);
    });
  });

  private float ScaledFloat(float value) => value * ImGuiHelpers.GlobalScale;
  private Vector2 ScaledVector2(float x, float? y = null) => new Vector2(x, y ?? x) * ImGuiHelpers.GlobalScale;

  public override void Draw()
  {
    using IDisposable _ = _uiFont.Push();

    Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.AlwaysAutoResize;
    SizeCondition = ImGuiCond.Always;
    Size = new(300, 300);

    if (DrawEditScreen()) return;

    bool buttonDisabled = _exteriorService.CurrentDivision == 0;
    using (ImRaii.PushStyle(ImGuiStyleVar.Alpha, ImGui.GetStyle().Alpha * 0.5f, buttonDisabled))
    {
      if (ImGui.Button("Add Facade", new(ImGui.GetContentRegionAvail().X, ScaledFloat(30))))
      {
        if (!buttonDisabled)
        {
          _selectedPlot = null;
          _selectedExterior = null;
          _selectedStain = null;
          _addingFacade = true;
        }
      }
    }

    if (buttonDisabled)
    {
      if (ImGui.IsItemHovered())
      {
        using (ImRaii.Tooltip())
        {
          ImGui.Text("Visit the Ward you want to modify an exterior in first.");
        }
      }
    }

    using (ImRaii.IEndObject child = ImRaii.Child("##facadeList"))
    {
      if (!child.Success) return;
      DrawFacadeList();
    }
  }

  private uint ABGRtoARGB(uint color) => ((color & 0xFF) << 16) | ((color >> 16) & 0xFF) | (color & 0xFF00) | 0xFF000000;

  private void DrawCenteredText(string text, bool horizontal = false, bool vertical = false)
  {
    float textHeight = ImGui.CalcTextSize(text).Y;
    if (vertical) ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (textHeight * 0.6f));

    float textWidth = ImGui.CalcTextSize(text).X;
    float textX = (ImGui.GetContentRegionAvail().X - textWidth) * 0.5f;
    if (horizontal) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + textX);

    ImGui.Text(text);
  }

  private void DrawColorCircle(uint? stain, uint unscaledSize, uint unscaledPaddingTopLeft = 0)
  {
    uint stainColor = 0x000000FF;
    if (stain != null && _dataManager.GetExcelSheet<Stain>().TryGetRow((uint)stain, out Stain stainRow))
      stainColor = ABGRtoARGB(stainRow.Color);
    Vector2 pos = ImGui.GetCursorScreenPos() + ScaledVector2(unscaledPaddingTopLeft);
    ImGui.GetWindowDrawList().AddRectFilled(pos - ScaledVector2(1), pos + ScaledVector2(unscaledSize + 1), 0xFFFFFFFF, ScaledFloat((unscaledSize + 2) / 2));
    ImGui.GetWindowDrawList().AddRectFilled(pos, pos + ScaledVector2(unscaledSize), stainColor, ScaledFloat(unscaledSize / 2));
  }

  private void DrawFacadeList()
  {
    IEnumerable<Facade> facades = _exteriorService.GetCurrentFacades();

    ImGui.Dummy(ScaledVector2(1));

    if (facades.Count() == 0)
    {
      ImGui.Dummy(ScaledVector2(8));
      DrawCenteredText("There are no Facades in your current division.", true, false);
    }
    else
    {
      using (ImRaii.PushColor(ImGuiCol.TableBorderStrong, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]))
      using (ImRaii.PushColor(ImGuiCol.TableBorderLight, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]))
      using (ImRaii.IEndObject table = ImRaii.Table(string.Empty, 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
      {
        if (!table) return;

        ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, ScaledFloat(40));
        ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, ScaledFloat(40));
        ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, ScaledFloat(40));

        foreach (Facade facade in facades)
        {
          ImGui.TableNextRow();
          ImGui.TableNextColumn();
          ISharedImmediateTexture? icon = GetExteriorIcon(facade.Plot, facade.HousingUnitedExterior);
          if (icon == null) continue;
          ImGui.Image(icon.GetWrapOrEmpty().Handle, ScaledVector2(40));

          ImGui.TableNextColumn();
          DrawColorCircle(facade.Stain, 28, 6);

          ImGui.TableNextColumn();
          DrawCenteredText($"Plot {facade.Plot + 1}", true, true);

          ImGui.TableNextColumn();
          if (ImGuiComponents.IconButton($"##Edit{facade.Plot}", FontAwesomeIcon.Edit, new(40)))
          {
            _editingFacade = facade;
            _selectedPlot = (sbyte)(_editingFacade.Plot + 1);
            _selectedExterior = _editingFacade.HousingUnitedExterior;
            _selectedStain = _editingFacade.Stain;
          }
        }
      }
    }

    int otherFacadeCount = _configuration.Facades.Count - facades.Count();
    string pluralText = otherFacadeCount == 1 ? "" : "s";
    DrawCenteredText($"{otherFacadeCount} more Facade{pluralText} in other divisions.", true, false);
    if (ImGui.IsItemHovered())
    {
      using (ImRaii.Tooltip())
      {
        ImGui.Text("You are only being shown facades from the current division.");
      }
    }
  }

  private ISharedImmediateTexture? GetExteriorIcon(sbyte plot, uint? exterior)
  {
    PlotSize? plotSize = _exteriorService.GetPlotSize(plot);
    uint defaultIconId = 60751 + (uint)(plotSize ?? 0);
    uint iconId = exterior == null ? defaultIconId : (uint)exterior - 276880;
    if (!_textureProvider.TryGetFromGameIcon(new GameIconLookup(iconId), out ISharedImmediateTexture? icon)) return null;
    return icon;
  }

  private bool DrawEditScreen()
  {
    if (_editingFacade == null && !_addingFacade) return false;

    if (_addingFacade) DrawCenteredText("Adding Facade", true, false);
    if (_editingFacade != null) DrawCenteredText($"Editing Facade (Plot {_selectedPlot})", true, false);

    ImGui.Dummy(ScaledVector2(8));

    if (_addingFacade) DrawPlotDropdown();
    DrawExteriorDropdown();
    DrawStainDropdown();

    ImGui.Dummy(ScaledVector2(8));

    if (_addingFacade)
    {
      using (ImRaii.Disabled(_selectedPlot == null))
      {
        if (ImGui.Button("Confirm", new(ImGui.GetContentRegionAvail().X / 2, ScaledFloat(30))))
        {
          _configuration.Facades.Add(new Facade
          {
            World = _exteriorService.CurrentWorld,
            District = _exteriorService.CurrentDistrict,
            Ward = _exteriorService.CurrentWard,
            Plot = (sbyte)(_selectedPlot! - 1),
            HousingUnitedExterior = _selectedExterior,
            Stain = _selectedStain,
          });
          _configuration.Save();
          _exteriorService.UpdateExteriors();
          _addingFacade = false;
        }
      }

      ImGui.SameLine();
      if (ImGui.Button("Cancel", new(ImGui.GetContentRegionAvail().X, ScaledFloat(30))))
      {
        _addingFacade = false;
      }
    }

    if (_editingFacade != null)
    {
      using (ImRaii.Disabled(_selectedPlot == null))
      {
        if (ImGui.Button("Save", new(ImGui.GetContentRegionAvail().X / 2, ScaledFloat(30))))
        {
          _editingFacade.Plot = (sbyte)(_selectedPlot! - 1);
          _editingFacade.HousingUnitedExterior = _selectedExterior;
          _editingFacade.Stain = _selectedStain;
          _configuration.Save();
          _exteriorService.UpdateExteriors();
          _editingFacade = null;
        }
      }

      ImGui.SameLine();
      if (ImGui.Button("Cancel", new(ImGui.GetContentRegionAvail().X, ScaledFloat(30))))
      {
        _editingFacade = null;
      }

      if (ImGui.Button("Delete", new(ImGui.GetContentRegionAvail().X, ScaledFloat(30))))
      {
        _configuration.Facades.Remove(_editingFacade!);
        _configuration.Save();
        _exteriorService.UpdateExteriors();
        _editingFacade = null;
      }
    }

    ImGui.Dummy(ScaledVector2(8));

    using (ImRaii.Disabled())
    {
      ImGui.TextWrapped("Exterior selection is currently limited to certain united exteriors, this might change in the future if there is demand for more in-depth customization.");
    }

    return true;
  }

  private void DrawPlotDropdown()
  {
    IEnumerable<Facade> facades = _exteriorService.GetCurrentFacades();
    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
    using (ImRaii.IEndObject dropdown = ImRaii.Combo("###PlotSelect", _selectedPlot == null ? "Select Plot" : _selectedPlot.ToString()))
    {
      if (!dropdown.Success) return;
      for (int i = _exteriorService.DivisionMin + 1; i < _exteriorService.DivisionMax + 1; i++)
      {
        if (facades.Any(f => f.Plot == i - 1)) continue;
        if (ImGui.Selectable(i.ToString(), _selectedPlot == i))
        {
          _selectedPlot = (sbyte)i;
        }
      }
    }
  }

  private void DrawExteriorDropdown()
  {
    PlotSize? plotSize = _exteriorService.GetPlotSize((sbyte)((_selectedPlot ?? 0) - 1));
    using (ImRaii.Disabled(_selectedPlot == null || plotSize == null))
    {
      ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
      using (ImRaii.IEndObject dropdown = ImRaii.Combo("###ExteriorSelect", _selectedExterior == null ? "Existing exterior" : _selectedExterior.ToString()))
      {
        if (!dropdown.Success) return;

        ISharedImmediateTexture? icon2 = GetExteriorIcon((sbyte)((_selectedPlot ?? 0) - 1), null);
        if (icon2 == null) return;
        ImGui.Image(icon2.GetWrapOrEmpty().Handle, ScaledVector2(16));
        ImGui.SameLine();
        if (ImGui.Selectable("Existing exterior", _selectedExterior == null))
        {
          _selectedExterior = null;
        }

        foreach (HousingUnitedExterior exterior in _dataManager.GetExcelSheet<HousingUnitedExterior>().Where(e => e.PlotSize == (byte)plotSize!))
        {
          ISharedImmediateTexture? icon = GetExteriorIcon((sbyte)_selectedPlot!, exterior.RowId);
          if (icon == null) continue;
          ImGui.Image(icon.GetWrapOrEmpty().Handle, ScaledVector2(16));
          ImGui.SameLine();
          if (ImGui.Selectable($"Unknown Name (I was lazy)##{exterior.RowId}", _selectedExterior == exterior.RowId))
          {
            _selectedExterior = exterior.RowId;
          }
        }
      }
    }
  }

  private void DrawStainDropdown()
  {
    using (ImRaii.Disabled(_selectedPlot == null))
    {
      Stain? selectedStain = _dataManager.GetExcelSheet<Stain>().GetRowOrDefault(_selectedStain ?? 0);
      string selectedStainString = _selectedStain == null || selectedStain == null || !selectedStain.HasValue ? "Existing color" : selectedStain.Value.Name.ToString();
      ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
      using (ImRaii.IEndObject dropdown = ImRaii.Combo("###StainSelect", selectedStainString))
      {
        if (!dropdown.Success) return;

        if (ImGui.Selectable("Existing color", _selectedStain == null))
        {
          _selectedStain = null;
        }

        foreach (Stain stain in _dataManager.GetExcelSheet<Stain>())
        {
          if (stain.RowId == 0 || stain.Name.IsEmpty) continue;

          DrawColorCircle(stain.RowId, 16);
          ImGui.Dummy(ScaledVector2(16));
          ImGui.SameLine();

          if (ImGui.Selectable(stain.Name.ToString(), _selectedStain == stain.RowId))
          {
            _selectedStain = stain.RowId;
          }
        }
      }
    }
  }
}
