namespace Facade.Windows;

public class ConfigWindow(ILogger _logger, Configuration _configuration, IExteriorService _exteriorService, IDataManager _dataManager, IDalamudPluginInterface _pluginInterface, ITextureProvider _textureProvider) : Window("Facade##FacadeConfigWindow"), IHostedService
{
  private bool _addingFacade = false;
  private Facade? _editingFacade = null;
  private sbyte? _selectedPlot = null;
  private Int128 _selectedExterior = Facade.Pack(null, null, null, null, null, null, null, null);
  private Int128 _selectedStain = Facade.Pack(null, null, null, null, null, null, null, null);
  private bool _unitedExteriorSelection = true;
  private bool _festivalView = false;
  public bool FacadeLocationOverlayOpen = false;

  // Took the most recent events as the older ones don't have models for newer districts.
  private readonly List<Festival> _festivals = [
    new Festival { Id = ushort.MaxValue, Name = "No override" },
    new Festival { Id = 00, Name = "None" },
    new Festival { Id = 129, Name = "Heavensturn" },
    new Festival { Id = 154, Name = "Valentione's Day" },
    new Festival { Id = 155, Name = "Little Ladies' Day" },
    new Festival { Id = 156, Name = "Hatching Tide" },
    new Festival { Id = 161, Name = "Make It Rain" },
    new Festival { Id = 159, Name = "Moonfire Faire" },
    new Festival { Id = 151, Name = "The Rising" },
    new Festival { Id = 164, Name = "All Saint's Wake" },
    new Festival { Id = 165, Name = "Starlight Celebration" },
    // Limited to Mist, Goblet, Lavender Beds
    new Festival { Id = 43, Name = "Starlight Celebration (No Snow)" },
  ];

  private readonly List<ExteriorItem> _cachedExteriorItems = [];
  private List<ExteriorItem> ExteriorItems
  {
    get
    {
      if (_cachedExteriorItems.Count > 0) return _cachedExteriorItems;
      IEnumerable<Item> items = _dataManager.GetExcelSheet<Item>().Where(item => item.AdditionalData.RowId != 0 && item.ItemSearchCategory.RowId == 65);
      foreach (Item item in items)
      {
        if (_dataManager.GetExcelSheet<HousingExterior>().TryGetRow(item.AdditionalData.RowId, out HousingExterior housingExterior))
        {
          _cachedExteriorItems.Add(new()
          {
            Size = housingExterior.HousingSize == 254 ? null : (PlotSize)housingExterior.HousingSize,
            Type = (ExteriorItemType)housingExterior.Unknown1 - 1,
            Id = housingExterior.RowId,
            Name = item.Name.ToString(),
            Icon = item.Icon,
            UnitedExteriorId = null
          });
        }
        else if (_dataManager.GetExcelSheet<HousingUnitedExterior>().TryGetRow(item.AdditionalData.RowId, out HousingUnitedExterior housingUnitedExterior))
        {
          Int128 packed = Facade.Pack(
            housingUnitedExterior.Roof.RowId,
            housingUnitedExterior.Walls.RowId,
            housingUnitedExterior.Windows.RowId,
            housingUnitedExterior.Door.RowId,
            housingUnitedExterior.OptionalRoof.RowId,
            housingUnitedExterior.OptionalWall.RowId,
            housingUnitedExterior.OptionalSignboard.RowId,
            housingUnitedExterior.Fence.RowId
          );

          _cachedExteriorItems.Add(new()
          {
            Size = (PlotSize)housingUnitedExterior.PlotSize,
            Type = ExteriorItemType.UnitedExterior,
            Id = packed,
            Name = item.Name.ToString(),
            Icon = item.Icon,
            UnitedExteriorId = housingUnitedExterior.RowId
          });
        }
      }

      return _cachedExteriorItems;
    }
  }

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

  public Task StartAsync(CancellationToken cancellationToken)
  {
    _exteriorService.OnDivisionChange += OnDivisionChange;

    Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.AlwaysAutoResize;
    SizeCondition = ImGuiCond.Always;

    SizeConstraints = new()
    {
      MinimumSize = new(300),
      MaximumSize = new(300, (_addingFacade || _editingFacade != null) && !_unitedExteriorSelection ? 999 : 300)
    };

    _logger.ServiceLifecycle();
    return Task.CompletedTask;
  }

  public Task StopAsync(CancellationToken cancellationToken)
  {
    _exteriorService.OnDivisionChange -= OnDivisionChange;

    _logger.ServiceLifecycle();
    return Task.CompletedTask;
  }

  private void OnDivisionChange(object? sender, object _)
  {
    _editingFacade = null;
    _addingFacade = false;
  }

  private float ScaledFloat(float value) => value * ImGuiHelpers.GlobalScale;
  private Vector2 ScaledVector2(float x, float? y = null) => new Vector2(x, y ?? x) * ImGuiHelpers.GlobalScale;

  public override void Draw()
  {
    using IDisposable _ = _uiFont.Push();
    if (DrawEditScreen()) return;

    bool buttonsDisabled = _exteriorService.CurrentDivision == 0;
    bool addButtonDisabled = buttonsDisabled || _festivalView;
    bool addButtonHovered = false;
    using (ImRaii.PushStyle(ImGuiStyleVar.Alpha, ImGui.GetStyle().Alpha * 0.5f, buttonsDisabled))
    {
      using (ImRaii.PushStyle(ImGuiStyleVar.Alpha, ImGui.GetStyle().Alpha * 0.5f, addButtonDisabled))
      {
        if (ImGui.Button("Add Facade", new(ImGui.GetContentRegionAvail().X - ScaledFloat(44), ScaledFloat(35))))
        {
          if (!buttonsDisabled && !addButtonDisabled)
          {
            _selectedPlot = null;
            _selectedExterior = Facade.Pack(null, null, null, null, null, null, null, null);
            _selectedStain = Facade.Pack(null, null, null, null, null, null, null, null);
            _unitedExteriorSelection = true;
            _addingFacade = true;
          }
        }
        addButtonHovered = ImGui.IsItemHovered();
      }
      ImGui.SameLine();
      if (ImGuiComponents.IconButton($"##SwitchButton", _festivalView ? FontAwesomeIcon.Snowflake : FontAwesomeIcon.HouseChimney, new(35)))
      {
        if (!buttonsDisabled)
        {
          _festivalView = !_festivalView;
        }
      }

      if (ImGui.IsItemHovered() && !buttonsDisabled)
      {
        using (ImRaii.Tooltip())
        {
          ImGui.Text(_festivalView ? "Switch to Exterior Facades" : "Switch to Festival Facades");
        }
      }
    }

    if (buttonsDisabled)
    {
      if (addButtonHovered || ImGui.IsItemHovered())
      {
        using (ImRaii.Tooltip())
        {
          ImGui.Text("Visit the ward you want to modify an exterior in first.");
        }
      }
    }

    using (ImRaii.IEndObject child = ImRaii.Child("##facadeList"))
    {
      if (!child.Success) return;
      if (_festivalView) DrawFestivalFacadeList();
      else DrawFacadeList();
    }

    DrawFacadeLocationOverlay();
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

  private string DrawColorCircle(uint? stain, uint unscaledSize, uint unscaledPaddingTopLeft = 0)
  {
    string stainName = "Existing Color";
    uint stainColor = ColorHelpers.RgbaVector4ToUint(ImGui.GetStyle().Colors[(int)ImGuiCol.WindowBg]);
    if (stain != null && _dataManager.GetExcelSheet<Stain>().TryGetRow((uint)stain, out Stain stainRow))
    {
      stainName = stainRow.Name.ToString();
      stainColor = ABGRtoARGB(stainRow.Color);
    }
    Vector2 pos = ImGui.GetCursorScreenPos() + ScaledVector2(unscaledPaddingTopLeft);
    ImGui.GetWindowDrawList().AddRectFilled(pos - ScaledVector2(1), pos + ScaledVector2(unscaledSize + 1), 0xFFFFFFFF, ScaledFloat((unscaledSize + 2) / 2));
    ImGui.GetWindowDrawList().AddRectFilled(pos, pos + ScaledVector2(unscaledSize), stainColor, ScaledFloat(unscaledSize / 2));
    return stainName;
  }

  private void DrawFestivalFacadeList()
  {
    using (ImRaii.Disabled(_exteriorService.CurrentDivision == 0))
    {
      ImGui.Dummy(ScaledVector2(4));

      FestivalFacade? currentFestivalFacade = _exteriorService.GetCurrentFestivalFacade();
      ushort currentFestivalId = currentFestivalFacade == null ? ushort.MaxValue : currentFestivalFacade.Id;
      Festival currentFestival = _festivals.Find(festival => festival.Id == currentFestivalId);

      ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
      using (ImRaii.IEndObject combo = ImRaii.Combo("###festivalCombo", currentFestival.Name))
      {
        if (combo.Success)
        {
          foreach (Festival festival in _festivals)
          {
            if (festival.Id == 43 && _exteriorService.CurrentDistrict != District.Mist && _exteriorService.CurrentDistrict != District.TheGoblet && _exteriorService.CurrentDistrict != District.LavenderBeds) continue;
            if (ImGui.Selectable(festival.Name, currentFestivalId == festival.Id))
            {
              if (currentFestivalFacade != null && festival.Id == ushort.MaxValue)
              {
                _configuration.FestivalFacades.Remove(currentFestivalFacade);
              }
              else if (currentFestivalFacade != null && festival.Id != ushort.MaxValue)
              {
                currentFestivalFacade.Id = festival.Id;
              }
              else if (currentFestivalFacade == null)
              {
                _configuration.FestivalFacades.Add(new FestivalFacade()
                {
                  World = _exteriorService.CurrentWorld,
                  District = _exteriorService.CurrentDistrict,
                  Ward = _exteriorService.CurrentWard,
                  Id = festival.Id,
                });
              }

              _configuration.Save();
              _exteriorService.UpdateFestival();
            }
          }
        }
      }

      int otherFacadeCount = _configuration.FestivalFacades.Count - (currentFestivalFacade == null ? 0 : 1);
      string pluralText = otherFacadeCount == 1 ? "" : "s";
      DrawCenteredText($"{otherFacadeCount} other Festival Facade{pluralText} in other wards.", true, false);
      if (ImGui.IsItemHovered())
      {
        using (ImRaii.Tooltip())
        {
          ImGui.Text("You can only change the Festival Facade of the current ward.\n(Click to see the location of other Festival Facades.)");
        }
      }
      if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
      {
        FacadeLocationOverlayOpen = true;
      }
    }

    using (ImRaii.Disabled())
    {
      ImGui.Dummy(ScaledVector2(8));
      ImGui.TextWrapped("Switching from an override back to 'No override' might not re-apply the original festival. Re-visit the current ward to fix this.");
    }
  }

  private void DrawFacadeList()
  {
    IEnumerable<Facade> facades = _exteriorService.GetCurrentFacades();

    ImGui.Dummy(ScaledVector2(1));

    if (facades.Count() == 0)
    {
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
          (ISharedImmediateTexture? icon, string iconDescription) = GetExteriorIcon(facade.Plot, facade);
          if (icon == null) continue;
          ImGui.Image(icon.GetWrapOrEmpty().Handle, ScaledVector2(40));

          if (ImGui.IsItemHovered())
          {
            using (ImRaii.Tooltip())
            {
              ImGui.Text(iconDescription);
            }
          }

          ImGui.TableNextColumn();
          string stainName = DrawColorCircle(Facade.Unpack(facade.PackedStainIds).u1, 28, 6);
          ImGui.Dummy(ScaledVector2(40));

          if (ImGui.IsItemHovered())
          {
            using (ImRaii.Tooltip())
            {
              ImGui.Text(stainName);
            }
          }

          ImGui.TableNextColumn();
          DrawCenteredText($"Plot {facade.Plot + 1}", true, true);

          ImGui.TableNextColumn();
          if (ImGuiComponents.IconButton($"##Edit{facade.Plot}", FontAwesomeIcon.Edit, new(40)))
          {
            _editingFacade = facade;
            _unitedExteriorSelection = _editingFacade.IsUnitedExterior;
            _selectedPlot = (sbyte)(_editingFacade.Plot + 1);
            _selectedExterior = _editingFacade.PackedExteriorIds;
            _selectedStain = _editingFacade.PackedStainIds;
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
        ImGui.Text("You are only being shown Facades from the current division.\n(Click to see the location of other Facades.)");
      }
    }
    if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
    {
      FacadeLocationOverlayOpen = true;
    }
  }

  private void DrawFacadeLocationOverlay()
  {
    if (!FacadeLocationOverlayOpen) return;

    ImGui.SetCursorPos(new(0, 0));

    Vector2 windowSize = ImGui.GetContentRegionAvail();
    Vector4 overlayColor = new(0f, 0f, 0f, 0.75f);
    Vector4 childColor = ImGui.GetStyle().Colors[(int)ImGuiCol.WindowBg];

    using (ImRaii.PushColor(ImGuiCol.ChildBg, overlayColor))
    {
      using (ImRaii.IEndObject overlay = ImRaii.Child("##facadeLocationOverlay", windowSize, false))
      {
        if (!overlay.Success) return;

        Vector2 childSize = ScaledVector2(230, 230);
        childSize.Y -= windowSize.Y * 0.04f;
        Vector2 childPos = (windowSize - childSize) / 2.0f;
        childPos.Y += windowSize.Y * 0.04f;
        ImGui.SetCursorPos(new(childPos.X, childPos.Y));

        using (ImRaii.PushColor(ImGuiCol.ChildBg, childColor))
        {
          using (ImRaii.IEndObject child = ImRaii.Child("##facadeLocation", childSize, false, ImGuiWindowFlags.AlwaysUseWindowPadding))
          {
            if (!child.Success) return;

            using (ImRaii.PushColor(ImGuiCol.TableBorderStrong, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]))
            using (ImRaii.PushColor(ImGuiCol.TableBorderLight, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]))
            using (ImRaii.IEndObject table = ImRaii.Table(string.Empty, _festivalView ? 3 : 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
            {
              if (!table) return;

              ImGui.TableSetupColumn("World", ImGuiTableColumnFlags.WidthStretch);
              ImGui.TableSetupColumn("District", ImGuiTableColumnFlags.WidthStretch);
              ImGui.TableSetupColumn("Ward", ImGuiTableColumnFlags.WidthFixed, ScaledFloat(30));
              if (!_festivalView) ImGui.TableSetupColumn("Total", ImGuiTableColumnFlags.WidthFixed, ScaledFloat(30));
              ImGui.TableHeadersRow();

              if (_festivalView)
              {
                IEnumerable<FestivalFacade> festivalFacades = _configuration.FestivalFacades.Where(f => !(f.Ward == _exteriorService.CurrentWard && f.District == _exteriorService.CurrentDistrict && f.Ward == _exteriorService.CurrentWard)).ToList();

                foreach (FestivalFacade festivalFacade in festivalFacades)
                {
                  if (!_dataManager.GetExcelSheet<World>().TryGetRow(festivalFacade.World, out World world)) continue;

                  ImGui.TableNextRow();
                  ImGui.TableNextColumn();
                  ImGui.Text(world.Name.ToString());

                  ImGui.TableNextColumn();
                  ImGui.Text(festivalFacade.District.ToString());

                  ImGui.TableNextColumn();
                  ImGui.Text(festivalFacade.Ward.ToString());
                }
              }
              else
              {
                var facades = _configuration.Facades
                  .Where(f => !(f.Ward == _exteriorService.CurrentWard && f.District == _exteriorService.CurrentDistrict && f.Ward == _exteriorService.CurrentWard))
                  .GroupBy(f => new { f.World, f.District, f.Ward })
                  .Select(g => new { Facade = g.First(), Count = g.Count() }).ToList();

                foreach (var group in facades)
                {
                  Facade facade = group.Facade;
                  if (!_dataManager.GetExcelSheet<World>().TryGetRow(facade.World, out World world)) continue;

                  ImGui.TableNextRow();
                  ImGui.TableNextColumn();
                  ImGui.Text(world.Name.ToString());

                  ImGui.TableNextColumn();
                  ImGui.Text(facade.District.ToString());

                  ImGui.TableNextColumn();
                  ImGui.Text(facade.Ward.ToString());

                  ImGui.TableNextColumn();
                  ImGui.Text(group.Count.ToString());
                }
              }
            }
          }
        }
      }

      if (ImGui.IsItemClicked())
      {
        FacadeLocationOverlayOpen = false;
      }
    }
  }

  private (ISharedImmediateTexture? icon, string description) GetExteriorIcon(sbyte plot, Facade? facade)
  {
    PlotSize? plotSize = _exteriorService.GetPlotSize(plot);
    uint defaultIconId = 60751 + (uint)(plotSize ?? 0);
    uint individualIconId = 60761 + (uint)(plotSize ?? 0);
    uint unitedIconId = defaultIconId;
    string description = "Existing Exterior";
    if (facade != null)
    {
      ExteriorItem? exteriorItem = ExteriorItems.FirstOrNull(item => item.Id == facade.PackedExteriorIds);
      if (exteriorItem != null && exteriorItem.HasValue && exteriorItem.Value.UnitedExteriorId != null)
      {
        unitedIconId = (uint)exteriorItem.Value.UnitedExteriorId - 276880;
        description = exteriorItem.Value.Name;
      }

      if (!facade.IsUnitedExterior) description = "Mixed Individual Exterior";
    }
    uint iconId = facade == null ? defaultIconId : facade.IsUnitedExterior ? unitedIconId : individualIconId;
    if (!_textureProvider.TryGetFromGameIcon(new GameIconLookup(iconId), out ISharedImmediateTexture? icon)) return (null, "Unknown");
    return (icon, description);
  }

  private bool DrawEditScreen()
  {
    if (_editingFacade == null && !_addingFacade) return false;

    if (_addingFacade) DrawCenteredText("Adding Facade", true, false);
    if (_editingFacade != null) DrawCenteredText($"Editing Facade (Plot {_selectedPlot})", true, false);

    if (_addingFacade) DrawPlotDropdown();

    using (ImRaii.Disabled(_selectedPlot == null))
    {
      ImGui.Dummy(ScaledVector2(60, 0));
      ImGui.SameLine();
      if (ImGui.RadioButton("United", _unitedExteriorSelection))
        _unitedExteriorSelection = true;
      ImGui.SameLine();
      if (ImGui.RadioButton("Individual", !_unitedExteriorSelection))
        _unitedExteriorSelection = false;
    }

    DrawExteriorDropdown();
    DrawStainDropdown();

    ImGui.Dummy(ScaledVector2(4));

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
            PackedExteriorIds = _selectedExterior,
            PackedStainIds = _selectedStain,
            IsUnitedExterior = _unitedExteriorSelection,
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
          if (_editingFacade.IsUnitedExterior != _unitedExteriorSelection)
          {
            {
              (uint? n1, uint? n2, uint? n3, uint? n4, uint? n5, uint? n6, uint? n7, uint? n8) = Facade.Unpack(_selectedExterior);
              (uint? o1, uint? o2, uint? o3, uint? o4, uint? o5, uint? o6, uint? o7, uint? o8) = Facade.Unpack(_editingFacade.PackedExteriorIds);
              if (n1 == o1) n1 = null;
              if (n2 == o2) n2 = null;
              if (n3 == o3) n3 = null;
              if (n4 == o4) n4 = null;
              if (n5 == o5) n5 = null;
              if (n6 == o6) n6 = null;
              if (n7 == o7) n7 = null;
              if (n8 == o8) n8 = null;
              _selectedExterior = Facade.Pack(n1, n2, n3, n4, n5, n6, n7, n8);
            }

            if (_unitedExteriorSelection)
            {
              (uint? n1, uint? n2, uint? n3, uint? n4, uint? n5, uint? n6, uint? n7, uint? n8) = Facade.Unpack(_selectedStain);
              _selectedStain = Facade.Pack(n1, n1, n1, n1, n1, n1, n1, n1);
            }
          }

          _editingFacade.Plot = (sbyte)(_selectedPlot! - 1);
          _editingFacade.PackedExteriorIds = _selectedExterior;
          _editingFacade.PackedStainIds = _selectedStain;
          _editingFacade.IsUnitedExterior = _unitedExteriorSelection;
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

    return true;
  }

  private void DrawPlotDropdown()
  {
    IEnumerable<Facade> facades = _exteriorService.GetCurrentFacades();
    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
    using (ImRaii.IEndObject dropdown = ImRaii.Combo("##PlotSelect", _selectedPlot == null ? "Select Plot" : _selectedPlot.ToString()))
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
    using (ImRaii.Disabled(_selectedPlot == null))
    {
      (uint? roof, uint? walls, uint? windows, uint? door, uint? optionalRoof, uint? optionalWall, uint? optionalSignboard, uint? fence) = Facade.Unpack(_selectedExterior);

      void onSelect(string text, uint? exterior, Int128? exteriorId)
      {
        if (_unitedExteriorSelection)
        {
          if (exteriorId == null) _selectedExterior = Facade.Pack(null, null, null, null, null, null, null, null);
          else _selectedExterior = (Int128)exteriorId;
          return;
        }

        switch (text)
        {
          case "Roof":
            roof = exterior;
            break;
          case "Walls":
            walls = exterior;
            break;
          case "Windows":
            windows = exterior;
            break;
          case "Door":
            door = exterior;
            break;
          case "Roof Decoration":
            optionalRoof = exterior;
            break;
          case "Wall Decoration":
            optionalWall = exterior;
            break;
          case "Signboard":
            optionalSignboard = exterior;
            break;
          case "Fence":
            fence = exterior;
            break;
        }

        _selectedExterior = Facade.Pack(roof, walls, windows, door, optionalRoof, optionalWall, optionalSignboard, fence);
      }

      if (_unitedExteriorSelection)
      {
        DrawSingularExteriorDropdown(plotSize, ExteriorItemType.UnitedExterior, _selectedExterior, "Exterior", onSelect);
      }
      else
      {
        DrawSingularExteriorDropdown(plotSize, ExteriorItemType.Roof, roof, "Roof", onSelect);
        DrawSingularExteriorDropdown(plotSize, ExteriorItemType.Walls, walls, "Walls", onSelect);
        DrawSingularExteriorDropdown(plotSize, ExteriorItemType.Windows, windows, "Windows", onSelect);
        DrawSingularExteriorDropdown(plotSize, ExteriorItemType.Door, door, "Door", onSelect);
        DrawSingularExteriorDropdown(plotSize, ExteriorItemType.OptionalRoof, optionalRoof, "Roof Decoration", onSelect);
        DrawSingularExteriorDropdown(plotSize, ExteriorItemType.OptionalWall, optionalWall, "Wall Decoration", onSelect);
        DrawSingularExteriorDropdown(plotSize, ExteriorItemType.OptionalSignboard, optionalSignboard, "Signboard", onSelect);
        DrawSingularExteriorDropdown(plotSize, ExteriorItemType.Fence, fence, "Fence", onSelect);
      }
    }
  }

  private void DrawSingularExteriorDropdown(PlotSize? plotSize, ExteriorItemType type, Int128? exterior, string text, Action<string, uint?, Int128?> onSelect)
  {
    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);

    string selectedExterior = ExteriorItems.Find(item => item.Id == exterior && item.Type == type).Name ?? $"Existing {text}";
    using (ImRaii.IEndObject dropdown = ImRaii.Combo($"##ExteriorSelect{text}", selectedExterior))
    {
      if (!dropdown.Success) return;

      ISharedImmediateTexture? icon2 = GetExteriorIcon((sbyte)((_selectedPlot ?? 0) - 1), null).icon;
      if (icon2 == null) return;
      ImGui.Image(icon2.GetWrapOrEmpty().Handle, ScaledVector2(16));
      ImGui.SameLine();
      if (ImGui.Selectable($"Existing {text}", exterior == null))
      {
        onSelect(text, null, null);
      }

      foreach (ExteriorItem exteriorItem in ExteriorItems.Where(item => (item.Size == plotSize || item.Size == null) && item.Type == type))
      {
        if (!_textureProvider.TryGetFromGameIcon(new GameIconLookup(exteriorItem.Icon), out ISharedImmediateTexture? icon)) continue;
        ImGui.Image(icon.GetWrapOrEmpty().Handle, ScaledVector2(16));
        ImGui.SameLine();
        if (ImGui.Selectable(exteriorItem.Name, exterior == exteriorItem.Id))
        {
          onSelect(text, (uint)exteriorItem.Id, exteriorItem.Id);
        }
      }
    }
  }

  private void DrawStainDropdown()
  {
    using (ImRaii.Disabled(_selectedPlot == null))
    {
      (uint? roofStain, uint? wallsStain, uint? windowsStain, uint? doorStain, uint? optionalRoofStain, uint? optionalWallStain, uint? optionalSignboardStain, uint? fenceStain) = Facade.Unpack(_selectedStain);

      void onSelect(string text, uint? stain)
      {
        if (_unitedExteriorSelection)
        {
          _selectedStain = Facade.Pack(stain, stain, stain, stain, stain, stain, stain, stain);
          return;
        }

        switch (text)
        {
          case "Roof":
            roofStain = stain;
            break;
          case "Walls":
            wallsStain = stain;
            break;
          case "Windows":
            windowsStain = stain;
            break;
          case "Door":
            doorStain = stain;
            break;
          case "Roof Decoration":
            optionalRoofStain = stain;
            break;
          case "Wall Decoration":
            optionalWallStain = stain;
            break;
          case "Signboard":
            optionalSignboardStain = stain;
            break;
          case "Fence":
            fenceStain = stain;
            break;
        }

        _selectedStain = Facade.Pack(roofStain, wallsStain, windowsStain, doorStain, optionalRoofStain, optionalWallStain, optionalSignboardStain, fenceStain);
      }

      if (_unitedExteriorSelection)
      {
        DrawSingularStainDropdown(roofStain, string.Empty, onSelect);
      }
      else
      {
        DrawSingularStainDropdown(roofStain, "Roof", onSelect);
        DrawSingularStainDropdown(wallsStain, "Walls", onSelect);
        DrawSingularStainDropdown(windowsStain, "Windows", onSelect);
        DrawSingularStainDropdown(doorStain, "Door", onSelect);
        DrawSingularStainDropdown(optionalRoofStain, "Roof Decoration", onSelect);
        DrawSingularStainDropdown(optionalWallStain, "Wall Decoration", onSelect);
        DrawSingularStainDropdown(optionalSignboardStain, "Signboard", onSelect);
        DrawSingularStainDropdown(fenceStain, "Fence", onSelect);
      }
    }
  }

  private void DrawSingularStainDropdown(uint? stain, string text, Action<string, uint?> onSelect)
  {
    Stain? selectedStain = _dataManager.GetExcelSheet<Stain>().GetRowOrDefault(stain ?? 0);
    string selectedStainString = stain == null || selectedStain == null || !selectedStain.HasValue ? text.IsNullOrEmpty() ? "Existing Color" : $"Existing {text} Color" : selectedStain.Value.Name.ToString();
    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
    using (ImRaii.IEndObject dropdown = ImRaii.Combo($"##StainSelect{text}", selectedStainString))
    {
      if (!dropdown.Success) return;

      DrawColorCircle(null, 16);
      ImGui.Dummy(ScaledVector2(16));
      ImGui.SameLine();
      if (ImGui.Selectable($"Existing {text} Color", stain == null))
      {
        onSelect(text, null);
      }

      foreach (Stain stainRow in _dataManager.GetExcelSheet<Stain>())
      {
        if (stainRow.RowId == 0 || stainRow.Name.IsEmpty) continue;

        DrawColorCircle(stainRow.RowId, 16);
        ImGui.Dummy(ScaledVector2(16));
        ImGui.SameLine();

        if (ImGui.Selectable(stainRow.Name.ToString(), stain == stainRow.RowId))
        {
          onSelect(text, stainRow.RowId);
        }
      }
    }
  }
}

public struct ExteriorItem
{
  public required PlotSize? Size;
  public required ExteriorItemType Type;
  public required Int128 Id;
  public required string Name;
  public required ushort Icon;
  public required uint? UnitedExteriorId;
}

public struct Festival
{
  public required ushort Id { get; set; }
  public required string Name { get; set; }
}
