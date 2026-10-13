namespace Facade.Services;

public interface IWindowService : IHostedService;

public class WindowService(
  ILogger _logger,
  ConfigWindow _configWindow,
  WindowSystem _windowSystem,
  IDalamudPluginInterface _pluginInterface) : IWindowService
{
  public Task StartAsync(CancellationToken cancellationToken)
  {
    _windowSystem.AddWindow(_configWindow);

    _pluginInterface.UiBuilder.DisableCutsceneUiHide = true;
    _pluginInterface.UiBuilder.Draw += UiBuilderOnDraw;
    _pluginInterface.UiBuilder.OpenMainUi += ToggleMainUi;
    _pluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;

#if DEBUG
    _configWindow.IsOpen = true;
#endif

    return _logger.ServiceLifecycle();
  }

  public Task StopAsync(CancellationToken cancellationToken)
  {
    _pluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
    _pluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
    _pluginInterface.UiBuilder.Draw -= UiBuilderOnDraw;

    _windowSystem.RemoveAllWindows();

    return _logger.ServiceLifecycle();
  }

  private void ToggleConfigUi() => ToggleUi(settings: true);

  private void ToggleMainUi() => ToggleUi(settings: false);

  private void ToggleUi(bool settings)
  {
    if (_configWindow.IsOpen && _configWindow.SettingsOpen != settings)
    {
      _configWindow.SettingsOpen = false;
      return;
    }

    _configWindow.SettingsOpen = settings;
    _configWindow.OverlayOpen = false;
    _configWindow.Toggle();
  }

  private void UiBuilderOnDraw() => _windowSystem.Draw();
}
