namespace Facade.Services;

public interface IWindowService : IHostedService;

public class WindowService(ILogger _logger, ConfigWindow _configWindow, WindowSystem _windowSystem, IDalamudPluginInterface _pluginInterface) : IWindowService
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

    _logger.ServiceLifecycle();
    return Task.CompletedTask;
  }

  public Task StopAsync(CancellationToken cancellationToken)
  {
    _pluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
    _pluginInterface.UiBuilder.OpenMainUi -= ToggleMainUi;
    _pluginInterface.UiBuilder.Draw -= UiBuilderOnDraw;

    _windowSystem.RemoveAllWindows();

    _logger.ServiceLifecycle();
    return Task.CompletedTask;
  }

  private void ToggleConfigUi()
  {
    if (_configWindow.IsOpen && !_configWindow.SettingsOpen)
    {
      _configWindow.SettingsOpen = true;
    }
    else
    {
      _configWindow.SettingsOpen = true;
      _configWindow.OverlayOpen = false;
      _configWindow.Toggle();
    }
  }

  private void ToggleMainUi()
  {
    if (_configWindow.IsOpen && _configWindow.SettingsOpen)
    {
      _configWindow.SettingsOpen = false;
    }
    else
    {
      _configWindow.SettingsOpen = false;
      _configWindow.OverlayOpen = false;
      _configWindow.Toggle();
    }
  }

  private void UiBuilderOnDraw()
  {
    _windowSystem.Draw();
  }
}
