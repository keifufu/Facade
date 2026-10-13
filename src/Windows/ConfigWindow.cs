namespace Facade.Windows;

public class ConfigWindow(
  ILogger _logger,
  IDataManager _dataManager,
  IPlotService _plotService,
  Configuration _configuration,
  IExteriorService _exteriorService,
  ITextureProvider _textureProvider,
  IDalamudPluginInterface _pluginInterface) : Window("Facade##FacadeConfigWindow"), IHostedService
{
  private bool _addingFacade = false;
  private Facade? _editingFacade = null;
  private sbyte? _selectedPlot = null;
  private UInt128 _packedSelectedExterior = UInt128Extensions.Pack(null, null, null, null, null, null, null, null);
  private UInt128 _packedSelectedStain = UInt128Extensions.Pack(null, null, null, null, null, null, null, null);
  private bool _unitedExteriorSelection = true;
  private bool _festivalView = false;
  private OverlayContent _overlayContent = OverlayContent.FacadeLocations;
  private string _presetName = "";
  private DateTime _lastExport = DateTime.MinValue;
  private DateTime _lastFailedImport = DateTime.MinValue;
  private DateTime _lastImport = DateTime.MinValue;
  private List<Facade> _importingFacades = [];

  private bool IsShiftDown => ImGui.IsKeyDown(ImGuiKey.ModShift);
  private bool IsCtrlDown => ImGui.IsKeyDown(ImGuiKey.ModCtrl);

  public bool SettingsOpen = false;
  public bool OverlayOpen = false;

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

  private List<ExteriorItem> ExteriorItems
  {
    get
    {
      if (field.Count > 0) return field;

      IEnumerable<Item> items = _dataManager.GetExcelSheet<Item>(ClientLanguage.English)
        .Where(item => item.AdditionalData.RowId != 0 && item.ItemSearchCategory.RowId == 65);

      foreach (Item item in items)
      {
        if (_dataManager.GetExcelSheet<HousingPreset>(ClientLanguage.English)
          .TryGetRow(item.AdditionalData.RowId, out HousingPreset housingPreset))
        {
          if (housingPreset.Singular.IsEmpty) continue;
          if (!_dataManager.GetExcelSheet<Item>(ClientLanguage.English).TryGetRow(housingPreset.ExteriorRoof.RowId, out Item roofItem)) continue;
          if (!_dataManager.GetExcelSheet<Item>(ClientLanguage.English).TryGetRow(housingPreset.ExteriorWall.RowId, out Item wallItem)) continue;
          if (!_dataManager.GetExcelSheet<Item>(ClientLanguage.English).TryGetRow(housingPreset.ExteriorWindow.RowId, out Item windowItem)) continue;
          if (!_dataManager.GetExcelSheet<Item>(ClientLanguage.English).TryGetRow(housingPreset.ExteriorDoor.RowId, out Item doorItem)) continue;

          UInt128 packedId = UInt128Extensions.Pack(
            (ushort)roofItem.AdditionalData.RowId,
            (ushort)wallItem.AdditionalData.RowId,
            (ushort)windowItem.AdditionalData.RowId,
            (ushort)doorItem.AdditionalData.RowId,
            0,
            0,
            0,
            0
          );

          string[] words = housingPreset.Singular.ToString().Split(' ');
          string name =
              $"{CultureInfo.CurrentCulture.TextInfo.ToTitleCase(words[0])} " +
              $"{CultureInfo.CurrentCulture.TextInfo.ToTitleCase(words[2])} " +
              $"({char.ToUpper(words[1][0], CultureInfo.InvariantCulture)}{words[1][1..]})";
          field.Add(new()
          {
            Size = (PlotSize)housingPreset.HousingSize,
            Type = ExteriorItemType.UnitedExteriorPreset,
            Id = packedId,
            Name = name,
            Icon = roofItem.Icon,
            UnitedExteriorId = null
          });
        }
        else if (_dataManager.GetExcelSheet<HousingExterior>(ClientLanguage.English)
          .TryGetRow(item.AdditionalData.RowId, out HousingExterior housingExterior))
        {
          field.Add(new()
          {
            Size = housingExterior.HousingSize == 254 ? null : (PlotSize)housingExterior.HousingSize,
            Type = (ExteriorItemType)housingExterior.Unknown1 - 1,
            Id = housingExterior.RowId,
            Name = item.Name.ToString(),
            Icon = item.Icon,
            UnitedExteriorId = null
          });
        }
        else if (_dataManager.GetExcelSheet<HousingUnitedExterior>(ClientLanguage.English)
          .TryGetRow(item.AdditionalData.RowId, out HousingUnitedExterior housingUnitedExterior))
        {
          UInt128 packedId = UInt128Extensions.Pack(
            (ushort)housingUnitedExterior.Roof.RowId,
            (ushort)housingUnitedExterior.Walls.RowId,
            (ushort)housingUnitedExterior.Windows.RowId,
            (ushort)housingUnitedExterior.Door.RowId,
            (ushort)housingUnitedExterior.OptionalRoof.RowId,
            (ushort)housingUnitedExterior.OptionalWall.RowId,
            (ushort)housingUnitedExterior.OptionalSignboard.RowId,
            (ushort)housingUnitedExterior.Fence.RowId
          );

          field.Add(new()
          {
            Size = (PlotSize)housingUnitedExterior.PlotSize,
            Type = ExteriorItemType.UnitedExterior,
            Id = packedId,
            Name = item.Name.ToString(),
            Icon = item.Icon,
            UnitedExteriorId = housingUnitedExterior.RowId
          });
        }
      }

      field = field.OrderByDescending(item => item.Type == ExteriorItemType.UnitedExteriorPreset).ToList();
      return field;
    }
  } = [];

  private readonly IFontHandle _uiFont = _pluginInterface.UiBuilder.FontAtlas.NewDelegateFontHandle(e =>
  {
    e.OnPreBuild(tk =>
    {
      float fontPx = UiBuilder.DefaultFontSizePx;
      SafeFontConfig safeFontConfig = new() { SizePx = fontPx };
      tk.AddDalamudAssetFont(Dalamud.DalamudAsset.NotoSansCjkMedium, safeFontConfig);
      tk.AttachExtraGlyphsForDalamudLanguage(safeFontConfig);
    });
  });

  public Task StartAsync(CancellationToken cancellationToken)
  {
    _exteriorService.OnDivisionChange += OnDivisionChange;

    Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.AlwaysAutoResize;
    SizeCondition = ImGuiCond.Always;

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
    OverlayOpen = false;
    _editingFacade = null;
    _addingFacade = false;
  }

  private float Scaled(float value) => value * ImGuiHelpers.GlobalScale;

  private string SerializeToBase64(object obj)
  {
    string json = Newtonsoft.Json.JsonConvert.SerializeObject(obj);
    byte[] bytes = Encoding.UTF8.GetBytes(json);
    using MemoryStream compressedStream = new();
    using (GZipStream zipStream = new(compressedStream, CompressionMode.Compress))
      zipStream.Write(bytes, 0, bytes.Length);
    return Convert.ToBase64String(compressedStream.ToArray());
  }

  private T? DeserializeFromBase64<T>(string base64)
  {
    try
    {
      byte[] bytes = Convert.FromBase64String(base64);
      using MemoryStream compressedStream = new(bytes);
      using GZipStream zipStream = new(compressedStream, CompressionMode.Decompress);
      using MemoryStream resultStream = new();
      zipStream.CopyTo(resultStream);
      bytes = resultStream.ToArray();
      string json = Encoding.UTF8.GetString(bytes);
      T? deserializedObject = Newtonsoft.Json.JsonConvert.DeserializeObject<T>(json);
      if (deserializedObject is T typedObject)
      {
        return typedObject;
      }
    }
    catch { }

    return default;
  }

  public override void Draw()
  {
    TitleBarButtons = [
      new()
      {
        Icon = SettingsOpen ? FontAwesomeIcon.Home : FontAwesomeIcon.Cog,
        ShowTooltip = () => ImGui.SetTooltip(SettingsOpen ? "Facades" : "Settings"),
        Click = (btn) => SettingsOpen = !SettingsOpen,
      },
    ];

    SizeConstraints = new()
    {
      MinimumSize = new(300.0f, SettingsOpen ? 0.0f : 300.0f),
      MaximumSize = new(300.0f, (_addingFacade || _editingFacade != null) && !_unitedExteriorSelection ? 999.0f : 300.0f)
    };

    using IDisposable _ = _uiFont.Push();

    if (DrawSettings()) return;

    if (DrawEditScreen())
    {
      DrawOverlay();
      return;
    }

    bool buttonsDisabled = _plotService.CurrentDivision == 0 || _exteriorService.ActiveFoolery != Foolery.None;
    bool buttonsDisabledFestivalView = buttonsDisabled || _festivalView;
    bool buttonsHovered = false;
    using (ImRaii.PushStyle(ImGuiStyleVar.Alpha, ImGui.GetStyle().Alpha * 0.5f, buttonsDisabled))
    {
      float buttonAlpha = ImGui.GetStyle().Alpha * (buttonsDisabled ? 1.0f : 0.5f);
      using (ImRaii.PushStyle(ImGuiStyleVar.Alpha, buttonAlpha, buttonsDisabledFestivalView))
      {
        Vector2 buttonSize = new(
          ImGui.GetContentRegionAvail().X - Scaled(35.0f * 2.0f) - (ImGui.GetStyle().ItemSpacing.X * 2),
          Scaled(35.0f)
        );
        if (ImGui.Button("Add Facade", buttonSize))
        {
          if (!buttonsDisabled && !buttonsDisabledFestivalView)
          {
            _selectedPlot = null;
            _packedSelectedExterior = UInt128Extensions.Pack(null, null, null, null, null, null, null, null);
            _packedSelectedStain = UInt128Extensions.Pack(null, null, null, null, null, null, null, null);
            _unitedExteriorSelection = true;
            _addingFacade = true;
          }
        }
        if (!buttonsHovered) buttonsHovered = ImGui.IsItemHovered();

        ImGui.SameLine();
        bool failedImport = (DateTime.Now - _lastFailedImport).TotalSeconds <= 3;
        bool successfulImport = (DateTime.Now - _lastImport).TotalSeconds <= 3;
        bool successfulExport = (DateTime.Now - _lastExport).TotalSeconds <= 3;
        FontAwesomeIcon importExportIcon = failedImport
          ? FontAwesomeIcon.Times
          : successfulExport || successfulImport
            ? FontAwesomeIcon.Check
            : IsShiftDown
              ? FontAwesomeIcon.FileExport
              : FontAwesomeIcon.FileImport;
        if (ImGuiComponents.IconButton("##ImportExportButton", importExportIcon, new(35)))
        {
          if (!buttonsDisabled && !buttonsDisabledFestivalView && !failedImport && !successfulImport && !successfulExport)
          {
            if (IsShiftDown)
            {
              ImGui.SetClipboardText(SerializeToBase64(_exteriorService.GetCurrentFacades()));
              _lastExport = DateTime.Now;
            }
            else
            {
              List<Facade>? facades = DeserializeFromBase64<List<Facade>>(ImGui.GetClipboardText());
              if (facades == null || facades.Count == 0)
              {
                _lastFailedImport = DateTime.Now;
              }
              else
              {
                _importingFacades = facades;
                _overlayContent = OverlayContent.FacadeImport;
                OverlayOpen = true;
              }
            }
          }
        }
        if (!buttonsHovered) buttonsHovered = ImGui.IsItemHovered();

        if (ImGui.IsItemHovered() && !buttonsDisabled && !buttonsDisabledFestivalView)
        {
          using (ImRaii.Tooltip())
          {
            ImGui.Text(
              failedImport
              ? "No valid data found in Clipboard"
              : successfulImport
                ? $"Imported {_importingFacades.Count} Facades!"
                : successfulExport
                  ? "Copied to Clipboard!"
                  : IsShiftDown
                    ? "Export Facades from this division to Clipboard"
                    : "Import Facades from Clipboard\n(Hold SHIFT to export)"
            );
          }
        }
      }

      ImGui.SameLine();
      FontAwesomeIcon buttonIcon = _festivalView ? FontAwesomeIcon.Snowflake : FontAwesomeIcon.HouseChimney;
      if (ImGuiComponents.IconButton("##SwitchButton", buttonIcon, new(35.0f)))
      {
        if (!buttonsDisabled)
        {
          _festivalView = !_festivalView;
        }
      }
      if (!buttonsHovered) buttonsHovered = ImGui.IsItemHovered();

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
      if (buttonsHovered || ImGui.IsItemHovered())
      {
        using (ImRaii.Tooltip())
        {
          ImGui.Text(
            _exteriorService.ActiveFoolery != Foolery.None
              ? "Controlled by Foolery"
              : "Visit the division you want to modify an exterior in first."
          );
        }
      }
    }

    using (ImRaii.ChildDisposable child = ImRaii.Child("##facadeList"))
    {
      if (!child.Success) return;
      if (_festivalView) DrawFestivalFacadeList();
      else DrawFacadeList();
    }

    DrawOverlay();
  }

  private bool DrawSettings()
  {
    if (!SettingsOpen) return false;

    ImGui.TextWrapped("When other players are on Facade plots:");
    ImGui.Dummy(new(0.0f, Scaled(5.0f)));

    if (ImGui.RadioButton("Do Nothing", _configuration.PlayerBehavior == PlayerBehavior.Nothing))
    {
      _configuration.PlayerBehavior = PlayerBehavior.Nothing;
      _configuration.Save();
    }

    if (ImGui.IsItemHovered())
    {
      using (ImRaii.Tooltip())
      {
        ImGui.Text("Players might clip or float on the Facade.");
      }
    }

    if (ImGui.RadioButton("Reset Facade", _configuration.PlayerBehavior == PlayerBehavior.ResetFacade))
    {
      _configuration.PlayerBehavior = PlayerBehavior.ResetFacade;
      _configuration.Save();
    }

    if (ImGui.IsItemHovered())
    {
      using (ImRaii.Tooltip())
      {
        ImGui.Text("Facade plots will be reset when players are on it.");
      }
    }

    if (ImGui.RadioButton("Hide Players", _configuration.PlayerBehavior == PlayerBehavior.HidePlayers))
    {
      _configuration.PlayerBehavior = PlayerBehavior.HidePlayers;
      _configuration.Save();
    }

    if (ImGui.IsItemHovered())
    {
      using (ImRaii.Tooltip())
      {
        ImGui.Text("Players on a Facade plot will be hidden.");
      }
    }

    ImGui.Dummy(new(0.0f, Scaled(5.0f)));

    if (!_configuration.Foolery || _exteriorService.ActiveFoolery != Foolery.None)
    {
      bool foolery = _configuration.Foolery;
      if (ImGui.Checkbox($"Foolery", ref foolery))
      {
        _configuration.Foolery = foolery;
        _configuration.Save();
      }

      if (ImGui.IsItemHovered())
      {
        using (ImRaii.Tooltip())
        {
          ImGui.Text("Special behavior on certain days.");
        }
      }
    }

    return true;
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
    if (stain != null && _dataManager.GetExcelSheet<Stain>(ClientLanguage.English).TryGetRow((uint)stain, out Stain stainRow))
    {
      stainName = stainRow.RowId == 0 ? "Undyed" : stainRow.Name.ToString();
      stainColor = ABGRtoARGB(stainRow.Color);
    }
    Vector2 pos = ImGui.GetCursorScreenPos() + new Vector2(Scaled(unscaledPaddingTopLeft));
    ImGui.GetWindowDrawList().AddRectFilled(
      pos - new Vector2(Scaled(1.0f)),
      pos + new Vector2(Scaled(unscaledSize + 1.0f)),
      0xFFFFFFFF,
      Scaled((unscaledSize + 2) / 2)
    );
    ImGui.GetWindowDrawList().AddRectFilled(
      pos,
      pos + new Vector2(Scaled(unscaledSize)),
      stainColor,
      Scaled(unscaledSize / 2)
    );
    return stainName;
  }

  private void DrawFestivalFacadeList()
  {
    using (ImRaii.Disabled(_plotService.CurrentDivision == 0))
    {
      ImGui.Dummy(new(Scaled(4.0f)));

      FestivalFacade? currentFestivalFacade = _exteriorService.GetCurrentFestivalFacade();
      ushort currentFestivalId = currentFestivalFacade == null ? ushort.MaxValue : currentFestivalFacade.Id;
      Festival currentFestival = _festivals.Find(festival => festival.Id == currentFestivalId);

      ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
      using (ImRaii.ComboDisposable combo = ImRaii.Combo("###festivalCombo", currentFestival.Name))
      {
        if (combo.Success)
        {
          foreach (Festival festival in _festivals)
          {
            if (festival.Id == 43 &&
              _plotService.CurrentDistrict != District.Mist &&
              _plotService.CurrentDistrict != District.TheGoblet &&
              _plotService.CurrentDistrict != District.LavenderBeds)
            {
              continue;
            }

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
                  World = _plotService.CurrentWorld,
                  District = _plotService.CurrentDistrict,
                  Ward = _plotService.CurrentWard,
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
          ImGui.Text("""
            You can only change the Festival Facade of the current ward.
            (Click to see the location of other Festival Facades.)
          """);
        }
      }
      if (ImGui.IsItemClicked(ImGuiMouseButton.Left))
      {
        _overlayContent = OverlayContent.FacadeLocations;
        OverlayOpen = true;
      }
    }

    using (ImRaii.Disabled())
    {
      ImGui.Dummy(new(Scaled(8.0f)));
      ImGui.TextWrapped("""
        Switching from an override back to 'No override' might not re-apply the original festival.
        Re-visit the current ward to fix this.
      """);
    }
  }

  private void DrawFacadeList()
  {
    IEnumerable<Facade> facades = _exteriorService.GetCurrentFacades();

    ImGui.Dummy(new(Scaled(1.0f)));

    if (_exteriorService.ActiveFoolery != Foolery.None)
    {
      DrawCenteredText("Facades are being controlled by Foolery.", true, false);
      if (ImGui.IsItemHovered())
      {
        using (ImRaii.Tooltip())
        {
          ImGui.Text($"Today is {_exteriorService.ActiveFoolery}. You can turn this off in the settings.");
        }
      }
      return;
    }
    else if (!facades.Any())
    {
      DrawCenteredText("There are no Facades in your current division.", true, false);
    }
    else
    {
      using (ImRaii.PushColor(ImGuiCol.TableBorderStrong, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]))
      using (ImRaii.PushColor(ImGuiCol.TableBorderLight, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]))
      using (ImRaii.TableDisposable table = ImRaii.Table("facadeTable", 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
      {
        if (!table.Success) return;

        ImGui.TableSetupColumn("##icon", ImGuiTableColumnFlags.WidthFixed, Scaled(40.0f));
        ImGui.TableSetupColumn("##stain", ImGuiTableColumnFlags.WidthFixed, Scaled(40.0f));
        ImGui.TableSetupColumn("##plot", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("##action", ImGuiTableColumnFlags.WidthFixed, Scaled(40.0f));

        foreach (Facade facade in facades)
        {
          ImGui.TableNextRow();
          ImGui.TableNextColumn();
          (ISharedImmediateTexture? icon, string iconDescription) = GetExteriorIcon(facade.Plot, facade);
          if (icon == null) continue;
          ImGui.Image(icon.GetWrapOrEmpty().Handle, new(Scaled(40.0f)));

          if (ImGui.IsItemHovered())
          {
            using (ImRaii.Tooltip())
            {
              ImGui.Text(iconDescription);
            }
          }

          ImGui.TableNextColumn();
          string stainName = DrawColorCircle(facade.PackedStainIds.Unpack().u1, 28, 6);
          ImGui.Dummy(new(Scaled(40)));

          if (ImGui.IsItemHovered())
          {
            using (ImRaii.Tooltip())
            {
              ImGui.Text(stainName);
            }
          }

          ImGui.TableNextColumn();
          PlotSize? plotSize = _exteriorService.GetPlotSize(facade.Plot);
          DrawCenteredText($"Plot {facade.Plot + 1} ({plotSize?.ToString()[0]})", true, true);

          ImGui.TableNextColumn();
          if (ImGuiComponents.IconButton($"##Edit{facade.Plot}", FontAwesomeIcon.Edit, new(40)))
          {
            _editingFacade = facade;
            _unitedExteriorSelection = _editingFacade.IsUnitedExterior;
            _selectedPlot = (sbyte)(_editingFacade.Plot + 1);
            _packedSelectedExterior = _editingFacade.PackedExteriorIds;
            _packedSelectedStain = _editingFacade.PackedStainIds;
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
      _overlayContent = OverlayContent.FacadeLocations;
      OverlayOpen = true;
    }
  }

  private void DrawOverlay()
  {
    if (!OverlayOpen) return;

    ImGui.SetCursorPos(new(0.0f));

    Vector2 windowSize = ImGui.GetContentRegionAvail();
    Vector4 overlayColor = new(0f, 0f, 0f, 0.75f);
    Vector4 childColor = ImGui.GetStyle().Colors[(int)ImGuiCol.WindowBg];

    using (ImRaii.PushColor(ImGuiCol.ChildBg, overlayColor))
    {
      using (ImRaii.ChildDisposable overlay = ImRaii.Child("##overlay", windowSize, false))
      {
        if (!overlay.Success) return;

        Vector2 childSize = new(
          Scaled(230.0f),
          Scaled(
            _overlayContent == OverlayContent.FacadeLocations
            ? 230.0f
            : _overlayContent == OverlayContent.FacadeImport
              ? 140.0f
              : _overlayContent == OverlayContent.SaveNewPreset
                ? 90.0f
                : 400.0f)
        );
        childSize.Y -= windowSize.Y * 0.04f;
        Vector2 childPos = (windowSize - childSize) / 2.0f;
        childPos.Y += windowSize.Y * 0.04f;
        ImGui.SetCursorPos(new(childPos.X, childPos.Y));

        using (ImRaii.PushColor(ImGuiCol.ChildBg, childColor))
        {
          using ImRaii.ChildDisposable child = ImRaii.Child("##facadeLocation", childSize, false, ImGuiWindowFlags.AlwaysUseWindowPadding);
          if (!child.Success) return;
          if (_overlayContent == OverlayContent.FacadeLocations) DrawFacadeLocations();
          else if (_overlayContent == OverlayContent.FacadeImport) DrawFacadeImport();
          else DrawPresets();
        }
      }

      if (ImGui.IsItemClicked())
      {
        OverlayOpen = false;
      }
    }
  }

  private void DrawFacadeImport()
  {
    if (_importingFacades.Count == 0) return;
    Facade probedFacade = _importingFacades[0];
    if (!_dataManager.GetExcelSheet<World>(ClientLanguage.English).TryGetRow(probedFacade.World, out World world)) return;

    int division = probedFacade.Plot > 30 ? 2 : 1;
    ImGui.Text("Importing Facades for:");
    ImGui.Text($"({world.Name} - {probedFacade.District} - Ward {probedFacade.Ward} - Division {division})");
    ImGui.TextWrapped("This might override some of your Facades in that division.");

    if (ImGui.Button("Confirm", new(ImGui.GetContentRegionAvail().X / 2, Scaled(30))))
    {
      foreach (Facade facade in _importingFacades)
      {
        Facade? existingFacade = _configuration.Facades
          .Find(f =>
            f.World == facade.World &&
            f.District == facade.District &&
            f.Ward == facade.Ward &&
            f.Plot == facade.Plot);

        if (existingFacade != null) _configuration.Facades.Remove(existingFacade);
        _configuration.Facades.Add(facade);
      }
      _configuration.Save();
      _exteriorService.UpdateExteriors();
      _lastImport = DateTime.Now;
      OverlayOpen = false;
    }

    ImGui.SameLine();
    if (ImGui.Button("Cancel", new(ImGui.GetContentRegionAvail().X, Scaled(30.0f))))
    {
      OverlayOpen = false;
    }
  }

  private void DrawPresets()
  {
    PlotSize? plotSize = _exteriorService.GetPlotSize((sbyte)((_selectedPlot ?? 0) - 1));
    if (plotSize == null)
    {
      ImGui.Text("Something went very wrong");
      return;
    }

    if (_overlayContent == OverlayContent.SaveNewPreset)
    {
      ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
      ImGui.InputTextWithHint("##presetName", "Preset Name", ref _presetName);

      bool nameTaken = _configuration.Presets.Any(p => p.Name == _presetName);
      using (ImRaii.Disabled(_presetName.Length == 0 || nameTaken))
      {
        if (ImGui.Button("Save", new(ImGui.GetContentRegionAvail().X / 2, Scaled(30.0f))))
        {
          _configuration.Presets.Add(new Preset()
          {
            Name = _presetName,
            PlotSize = (PlotSize)plotSize,
            PackedExteriorIds = _packedSelectedExterior,
            PackedStainIds = _packedSelectedStain,
          });
          _configuration.Save();
          OverlayOpen = false;
        }
      }
      ImGui.SameLine();
      if (ImGui.Button("Cancel", new(ImGui.GetContentRegionAvail().X, Scaled(30.0f))))
      {
        _overlayContent = OverlayContent.SavePreset;
      }
      return;
    }

    using (ImRaii.Disabled(_overlayContent != OverlayContent.SavePreset))
    {
      Vector2 buttonSize = new(ImGui.GetContentRegionAvail().X - Scaled(30.0f) - ImGui.GetStyle().ItemSpacing.X, Scaled(30.0f));
      if (ImGui.Button("New Preset", buttonSize))
      {
        _presetName = "";
        _overlayContent = OverlayContent.SaveNewPreset;
      }
    }

    ImGui.SameLine();
    bool failedImport = (DateTime.Now - _lastFailedImport).TotalSeconds <= 3;
    bool successfulImport = (DateTime.Now - _lastImport).TotalSeconds <= 3;
    FontAwesomeIcon importExportIcon =
      failedImport
      ? FontAwesomeIcon.Times
      : successfulImport
        ? FontAwesomeIcon.Check
        : FontAwesomeIcon.FileImport;
    if (ImGuiComponents.IconButton("##importPresetButton", importExportIcon, new(30.0f)))
    {
      if (!failedImport && !successfulImport)
      {
        Preset? preset = DeserializeFromBase64<Preset>(ImGui.GetClipboardText());
        if (preset == null)
        {
          _lastFailedImport = DateTime.Now;
        }
        else
        {
          if (_configuration.Presets.Find(p => p.Name == preset.Name) != null)
          {
            preset.Name = $"{preset.Name} (2)";
          }
          _configuration.Presets.Add(preset);
          _configuration.Save();
          _lastImport = DateTime.Now;
        }
      }
    }

    if (ImGui.IsItemHovered())
    {
      using (ImRaii.Tooltip())
      {
        ImGui.Text(failedImport ? "No valid data found in Clipboard" : successfulImport ? "Imported Preset!" : "Import Preset");
      }
    }

    ImGui.Dummy(new(Scaled(2.0f)));

    if (_configuration.Presets.Count == 0)
    {
      DrawCenteredText("No presets found", true, false);
      return;
    }

    bool exporting = IsShiftDown;
    bool deleting = IsCtrlDown;

    using (ImRaii.PushColor(ImGuiCol.TableBorderStrong, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]))
    using (ImRaii.PushColor(ImGuiCol.TableBorderLight, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]))
    using (ImRaii.TableDisposable table = ImRaii.Table("##presetTable", 2, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
    {
      if (!table.Success) return;

      ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch);
      ImGui.TableSetupColumn("##action", ImGuiTableColumnFlags.WidthFixed, Scaled(40.0f));

      foreach (Preset preset in _configuration.Presets)
      {
        using (ImRaii.Disabled(plotSize != preset.PlotSize && !deleting && !exporting))
        {
          ImGui.TableNextRow();
          ImGui.TableNextColumn();
          DrawCenteredText($"({preset.PlotSize.ToString()[0]}) {preset.Name}", true, true);

          ImGui.TableNextColumn();
          bool successfulExport = (DateTime.Now - _lastExport).TotalSeconds <= 3;
          FontAwesomeIcon icon =
            successfulExport
            ? FontAwesomeIcon.Check
            : exporting
              ? FontAwesomeIcon.FileExport
              : deleting
                ? FontAwesomeIcon.Trash
                : _overlayContent == OverlayContent.SavePreset
                  ? FontAwesomeIcon.Save
                  : FontAwesomeIcon.Check;
          if (ImGuiComponents.IconButton($"##Action{preset.Name}", icon, new(40)))
          {
            if (!successfulExport)
            {
              if (deleting)
              {
                _configuration.Presets.Remove(preset);
                _configuration.Save();
                return;
              }
              else if (exporting)
              {
                ImGui.SetClipboardText(SerializeToBase64(preset));
                _lastExport = DateTime.Now;
              }
              else if (_overlayContent == OverlayContent.SavePreset)
              {
                preset.PackedExteriorIds = _packedSelectedExterior;
                preset.PackedStainIds = _packedSelectedStain;
                _configuration.Save();
                OverlayOpen = false;
              }
              else if (_overlayContent == OverlayContent.LoadPreset)
              {
                _packedSelectedExterior = preset.PackedExteriorIds;
                _packedSelectedStain = preset.PackedStainIds;
                OverlayOpen = false;
              }
            }
          }

          if (ImGui.IsItemHovered())
          {
            using (ImRaii.Tooltip())
            {
              ImGui.Text(
                successfulExport
                ? "Copied to Clipboard!"
                : exporting
                  ? "Export Preset to Clipboard"
                  : deleting
                    ? "Delete Preset"
                    : _overlayContent == OverlayContent.SavePreset
                      ? "Overwrite this Preset"
                      : "Load this Preset"
              );
            }
          }
        }
      }
    }

    ImGui.Dummy(new(Scaled(2.0f)));
    DrawCenteredText("Hover me for help.", true, false);
    if (ImGui.IsItemHovered())
    {
      using (ImRaii.Tooltip())
      {
        ImGui.Text("""
          You can only load presets for the correct plot size.
          You can export presets if you hold SHIFT.
          You can delete presets if you hold CTRL.
        """);
      }
    }
  }

  private void DrawFacadeLocations()
  {
    using (ImRaii.PushColor(ImGuiCol.TableBorderStrong, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]))
    using (ImRaii.PushColor(ImGuiCol.TableBorderLight, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]))
    using (ImRaii.TableDisposable table = ImRaii.Table("##facadeLocationsTable", _festivalView ? 3 : 4, ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg))
    {
      if (!table.Success) return;

      ImGui.TableSetupColumn("World", ImGuiTableColumnFlags.WidthStretch);
      ImGui.TableSetupColumn("District", ImGuiTableColumnFlags.WidthStretch);
      ImGui.TableSetupColumn("Ward", ImGuiTableColumnFlags.WidthFixed, Scaled(30.0f));
      if (!_festivalView) ImGui.TableSetupColumn("Total", ImGuiTableColumnFlags.WidthFixed, Scaled(30.0f));
      ImGui.TableHeadersRow();

      if (_festivalView)
      {
        IEnumerable<FestivalFacade> festivalFacades = _configuration.FestivalFacades
          .Where(f =>
            !(f.Ward == _plotService.CurrentWard &&
              f.District == _plotService.CurrentDistrict &&
              f.Ward == _plotService.CurrentWard)
          ).ToList();

        foreach (FestivalFacade festivalFacade in festivalFacades)
        {
          if (!_dataManager.GetExcelSheet<World>(ClientLanguage.English).TryGetRow(festivalFacade.World, out World world))
            continue;

          ImGui.TableNextRow();
          ImGui.TableNextColumn();
          ImGui.Text(world.Name.ToString());

          ImGui.TableNextColumn();
          ImGui.Text(festivalFacade.District.ToString());

          ImGui.TableNextColumn();
          ImGui.Text(festivalFacade.Ward.ToString(CultureInfo.InvariantCulture));
        }
      }
      else
      {
        var facades = _configuration.Facades
          .Where(f =>
            !(f.Ward == _plotService.CurrentWard &&
              f.District == _plotService.CurrentDistrict &&
              f.Ward == _plotService.CurrentWard)
          )
          .GroupBy(f => new { f.World, f.District, f.Ward })
          .Select(g => new { Facade = g.First(), Count = g.Count() }).ToList();

        foreach (var group in facades)
        {
          Facade facade = group.Facade;
          if (!_dataManager.GetExcelSheet<World>(ClientLanguage.English).TryGetRow(facade.World, out World world)) continue;

          ImGui.TableNextRow();
          ImGui.TableNextColumn();
          ImGui.Text(world.Name.ToString());

          ImGui.TableNextColumn();
          ImGui.Text(facade.District.ToString());

          ImGui.TableNextColumn();
          ImGui.Text(facade.Ward.ToString(CultureInfo.InvariantCulture));

          ImGui.TableNextColumn();
          ImGui.Text(group.Count.ToString(CultureInfo.InvariantCulture));
        }
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

      if (exteriorItem != null && exteriorItem.HasValue && exteriorItem.Value.Type == ExteriorItemType.UnitedExteriorPreset)
      {
        unitedIconId = exteriorItem.Value.Icon;
        description = exteriorItem.Value.Name;
      }

      if (!facade.IsUnitedExterior) description = "Mixed Individual Exterior";
    }
    uint iconId = facade == null ? defaultIconId : facade.IsUnitedExterior ? unitedIconId : individualIconId;
    if (!_textureProvider.TryGetFromGameIcon(new GameIconLookup(iconId), out ISharedImmediateTexture? icon))
      return (null, "Unknown");
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
      ImGui.Dummy(new(Scaled(60.0f), 0.0f));
      ImGui.SameLine();
      if (ImGui.RadioButton("Preset", _unitedExteriorSelection))
        _unitedExteriorSelection = true;
      ImGui.SameLine();
      if (ImGui.RadioButton("Individual", !_unitedExteriorSelection))
        _unitedExteriorSelection = false;
    }

    if (!_unitedExteriorSelection)
    {
      ImGui.Dummy(new(Scaled(4.0f)));

      if (ImGui.Button("Load Preset", new(ImGui.GetContentRegionAvail().X / 2.0f, Scaled(30.0f))))
      {
        _overlayContent = OverlayContent.LoadPreset;
        OverlayOpen = true;
      }

      ImGui.SameLine();
      if (ImGui.Button("Save Preset", new(ImGui.GetContentRegionAvail().X, Scaled(30.0f))))
      {
        _overlayContent = OverlayContent.SavePreset;
        OverlayOpen = true;
      }

      ImGui.Dummy(new(Scaled(4.0f)));
    }

    bool invalidSelection = DrawExteriorDropdown();
    DrawStainDropdown();

    ImGui.Dummy(new(Scaled(4.0f)));

    if (_addingFacade)
    {
      using (ImRaii.PushStyle(ImGuiStyleVar.Alpha, ImGui.GetStyle().Alpha * 0.5f, _selectedPlot == null || invalidSelection))
      {
        if (ImGui.Button("Confirm", new(ImGui.GetContentRegionAvail().X / 2, Scaled(30.0f))))
        {
          if (_selectedPlot != null && !invalidSelection)
          {
            _configuration.Facades.Add(new Facade
            {
              World = _plotService.CurrentWorld,
              District = _plotService.CurrentDistrict,
              Ward = _plotService.CurrentWard,
              Plot = (sbyte)(_selectedPlot! - 1),
              PackedExteriorIds = _packedSelectedExterior,
              PackedStainIds = _packedSelectedStain,
              IsUnitedExterior = _unitedExteriorSelection,
            });
            _configuration.Save();
            _exteriorService.UpdateExteriors();
            _addingFacade = false;
          }
        }
      }

      if (ImGui.IsItemHovered() && invalidSelection)
      {
        using (ImRaii.Tooltip())
        {
          ImGui.Text("Select all exterior options first");
        }
      }

      ImGui.SameLine();
      if (ImGui.Button("Cancel", new(ImGui.GetContentRegionAvail().X, Scaled(30.0f))))
      {
        _addingFacade = false;
      }
    }

    if (_editingFacade != null)
    {
      using (ImRaii.PushStyle(ImGuiStyleVar.Alpha, ImGui.GetStyle().Alpha * 0.5f, _selectedPlot == null || invalidSelection))
      {
        if (ImGui.Button("Save", new(ImGui.GetContentRegionAvail().X / 2, Scaled(30.0f))))
        {
          if (_selectedPlot != null && !invalidSelection)
          {
            // If a United Facade, make sure "Existing Exterior" option is saved if nothing was manually selected.
            if (_unitedExteriorSelection)
            {
              if (!ExteriorItems.Any(item => item.Id == _packedSelectedExterior))
              {
                _packedSelectedExterior = UInt128Extensions.Pack(null, null, null, null, null, null, null, null);
              }
            }

            // If a United Facade, save stains in all slots since we technically only selected the roof stain.
            if (_unitedExteriorSelection)
            {
              (ushort? n1, ushort? n2, ushort? n3, ushort? n4, ushort? n5, ushort? n6, ushort? n7, ushort? n8)
                = _packedSelectedStain.Unpack();
              _packedSelectedStain = UInt128Extensions.Pack(n1, n1, n1, n1, n1, n1, n1, n1);
            }

            _editingFacade.Plot = (sbyte)(_selectedPlot! - 1);
            _editingFacade.PackedExteriorIds = _packedSelectedExterior;
            _editingFacade.PackedStainIds = _packedSelectedStain;
            _editingFacade.IsUnitedExterior = _unitedExteriorSelection;
            _configuration.Save();
            _exteriorService.UpdateExteriors();
            _editingFacade = null;
          }
        }
      }

      if (ImGui.IsItemHovered() && invalidSelection)
      {
        using (ImRaii.Tooltip())
        {
          ImGui.Text("Select all exterior options first");
        }
      }

      ImGui.SameLine();
      if (ImGui.Button("Cancel", new(ImGui.GetContentRegionAvail().X, Scaled(30.0f))))
      {
        _editingFacade = null;
      }

      if (ImGui.Button("Delete", new(ImGui.GetContentRegionAvail().X, Scaled(30.0f))))
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
    using ImRaii.ComboDisposable dropdown = ImRaii.Combo(
      "##PlotSelect",
      _selectedPlot == null ? "Select Plot" : _selectedPlot?.ToString(CultureInfo.InvariantCulture)
    );
    if (!dropdown.Success) return;
    for (int i = _plotService.DivisionMin + 1; i < _plotService.DivisionMax + 1; i++)
    {
      if (facades.Any(f => f.Plot == i - 1)) continue;
      if (ImGui.Selectable(i.ToString(CultureInfo.InvariantCulture), _selectedPlot == i))
      {
        _selectedPlot = (sbyte)i;
      }
    }
  }

  private bool DrawExteriorDropdown()
  {
    bool invalidSelection = false;
    PlotSize? plotSize = _exteriorService.GetPlotSize((sbyte)((_selectedPlot ?? 0) - 1));
    using (ImRaii.Disabled(_selectedPlot == null))
    {
      (ushort? roof, ushort? walls, ushort? windows, ushort? door, ushort? optionalRoof, ushort? optionalWall,
       ushort? optionalSignboard, ushort? fence) = _packedSelectedExterior.Unpack();

      void onSelect(string text, ushort? exterior, UInt128? exteriorId)
      {
        if (_unitedExteriorSelection)
        {
          if (exteriorId == null)
            _packedSelectedExterior = UInt128Extensions.Pack(null, null, null, null, null, null, null, null);
          else _packedSelectedExterior = (UInt128)exteriorId;
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

        _packedSelectedExterior = UInt128Extensions.Pack(roof, walls, windows, door, optionalRoof, optionalWall, optionalSignboard, fence);
      }

      if (_unitedExteriorSelection)
      {
        DrawSingularExteriorDropdown(plotSize, [ExteriorItemType.UnitedExterior, ExteriorItemType.UnitedExteriorPreset], _packedSelectedExterior, "Exterior", onSelect);
      }
      else
      {
        if (DrawSingularExteriorDropdown(plotSize, [ExteriorItemType.Roof], roof, "Roof", onSelect)) invalidSelection = true;
        if (DrawSingularExteriorDropdown(plotSize, [ExteriorItemType.Walls], walls, "Walls", onSelect)) invalidSelection = true;
        if (DrawSingularExteriorDropdown(plotSize, [ExteriorItemType.Windows], windows, "Windows", onSelect)) invalidSelection = true;
        if (DrawSingularExteriorDropdown(plotSize, [ExteriorItemType.Door], door, "Door", onSelect)) invalidSelection = true;
        if (DrawSingularExteriorDropdown(plotSize, [ExteriorItemType.OptionalRoof], optionalRoof, "Roof Decoration", onSelect, true)) invalidSelection = true;
        if (DrawSingularExteriorDropdown(plotSize, [ExteriorItemType.OptionalWall], optionalWall, "Wall Decoration", onSelect, true)) invalidSelection = true;
        if (DrawSingularExteriorDropdown(plotSize, [ExteriorItemType.OptionalSignboard], optionalSignboard, "Signboard", onSelect, true)) invalidSelection = true;
        if (DrawSingularExteriorDropdown(plotSize, [ExteriorItemType.Fence], fence, "Fence", onSelect, true)) invalidSelection = true;
      }
    }

    return invalidSelection;
  }

  private bool DrawSingularExteriorDropdown(PlotSize? plotSize,
    ExteriorItemType[] types,
    UInt128? exterior,
    string text,
    Action<string,
    ushort?,
    UInt128?> onSelect,
    bool allowNone = false)
  {
    short? plotExterior = _exteriorService.GetPlotExterior((sbyte)((_selectedPlot ?? 0) - 1), types[0]);
    ExteriorItem? plotExteriorItem = ExteriorItems
      .FirstOrNull(item => item.Id == (UInt128)(plotExterior ?? 0) && types.Contains(item.Type));

    string? selectedExteriorItemName = ExteriorItems
      .FirstOrNull(item => item.Id == exterior && types.Contains(item.Type))?.Name ?? null;
    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
    bool invalidSelection = selectedExteriorItemName == null && exterior != 0;

    if (invalidSelection && plotExteriorItem != null && plotExteriorItem.HasValue)
    {
      onSelect(text, (ushort)plotExteriorItem.Value.Id, null);
    }
    else if (invalidSelection && allowNone)
    {
      onSelect(text, 0, null);
    }

    string selectedItem =
      selectedExteriorItemName ??
      (exterior == 0
        ? $"No {text}"
        : _unitedExteriorSelection
          ? $"Existing {text}"
          : plotExteriorItem != null && plotExteriorItem.HasValue
            ? plotExteriorItem.Value.Name.ToString()
            : $"Select {text}..."
      );

    using (ImRaii.ComboDisposable dropdown = ImRaii.Combo($"##ExteriorSelect{text}", selectedItem))
    {
      if (!dropdown.Success) return invalidSelection;

      if (_unitedExteriorSelection)
      {
        ISharedImmediateTexture? icon2 = GetExteriorIcon((sbyte)((_selectedPlot ?? 0) - 1), null).icon;
        if (icon2 == null) return invalidSelection;
        ImGui.Image(icon2.GetWrapOrEmpty().Handle, new(Scaled(16.0f)));
        ImGui.SameLine();
        if (ImGui.Selectable($"Existing {text}", exterior == null))
        {
          onSelect(text, null, null);
        }
      }

      if (allowNone)
      {
        ImGui.Dummy(new(Scaled(16.0f)));
        ImGui.SameLine();
        if (ImGui.Selectable($"No {text}", exterior == 0))
        {
          onSelect(text, 0, null);
        }
      }

      IEnumerable<ExteriorItem> items = ExteriorItems
        .Where(item => (item.Size == plotSize || item.Size == null) && types.Contains(item.Type));

      foreach (ExteriorItem exteriorItem in items)
      {
        if (!_textureProvider.TryGetFromGameIcon(new GameIconLookup(exteriorItem.Icon), out ISharedImmediateTexture? icon))
          continue;
        ImGui.Image(icon.GetWrapOrEmpty().Handle, new(Scaled(16.0f)));
        ImGui.SameLine();
        if (ImGui.Selectable(exteriorItem.Name, exterior == exteriorItem.Id))
        {
          onSelect(text, (ushort)exteriorItem.Id, exteriorItem.Id);
        }
      }
    }

    return invalidSelection;
  }

  private void DrawStainDropdown()
  {
    using (ImRaii.Disabled(_selectedPlot == null))
    {
      (ushort? roofStain, ushort? wallsStain, ushort? windowsStain, ushort? doorStain, ushort? optionalRoofStain,
       ushort? optionalWallStain, ushort? optionalSignboardStain, ushort? fenceStain) = _packedSelectedStain.Unpack();

      void onSelect(string text, ushort? stain)
      {
        if (_unitedExteriorSelection)
        {
          _packedSelectedStain = UInt128Extensions.Pack(stain, stain, stain, stain, stain, stain, stain, stain);
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

        _packedSelectedStain = UInt128Extensions.Pack(roofStain, wallsStain, windowsStain, doorStain,
            optionalRoofStain, optionalWallStain, optionalSignboardStain, fenceStain);
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

  private void DrawSingularStainDropdown(ushort? stain, string text, Action<string, ushort?> onSelect)
  {
    Stain? selectedStain = _dataManager.GetExcelSheet<Stain>(ClientLanguage.English).GetRowOrDefault(stain ?? 0);
    string selectedStainString =
      stain == null || selectedStain == null || !selectedStain.HasValue
      ? text.IsNullOrEmpty()
      ? "Existing Color"
        : $"Existing {text} Color"
        : selectedStain.Value.RowId == 0
          ? $"Undyed {text}"
          : $"{selectedStain.Value.Name} {text}";

    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
    using ImRaii.ComboDisposable dropdown = ImRaii.Combo($"##StainSelect{text}", selectedStainString);
    if (!dropdown.Success) return;

    DrawColorCircle(null, 16);
    ImGui.Dummy(new(Scaled(16.0f)));
    ImGui.SameLine();
    if (ImGui.Selectable("Existing Color", stain == null))
    {
      onSelect(text, null);
    }

    foreach (Stain stainRow in _dataManager.GetExcelSheet<Stain>(ClientLanguage.English))
    {
      if (stainRow.Name.IsEmpty || !stainRow.IsHousingApplicable) continue;

      DrawColorCircle(stainRow.RowId, 16);
      ImGui.Dummy(new(Scaled(16.0f)));
      ImGui.SameLine();

      if (ImGui.Selectable(stainRow.RowId == 0 ? "Undyed" : stainRow.Name.ToString(), stain == stainRow.RowId))
      {
        onSelect(text, (ushort)stainRow.RowId);
      }
    }
  }
}

public struct ExteriorItem
{
  public required PlotSize? Size;
  public required ExteriorItemType Type;
  public required UInt128 Id;
  public required string Name;
  public required ushort Icon;
  public required uint? UnitedExteriorId;
}

public struct Festival
{
  public required ushort Id;
  public required string Name;
}

public enum OverlayContent
{
  FacadeLocations,
  LoadPreset,
  SavePreset,
  SaveNewPreset,
  FacadeImport,
}
