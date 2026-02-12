namespace Facade.Services;

public interface IWindowService : IHostedService;

public class WindowService(ILogger _logger, ConfigWindow _configWindow, WindowSystem _windowSystem, IDalamudPluginInterface _pluginInterface) : IWindowService
{
  public Task StartAsync(CancellationToken cancellationToken)
  {
    _windowSystem.AddWindow(_configWindow);

    _pluginInterface.UiBuilder.DisableCutsceneUiHide = true;
    _pluginInterface.UiBuilder.Draw += UiBuilderOnDraw;
    _pluginInterface.UiBuilder.OpenConfigUi += ToggleConfigUi;
    _pluginInterface.UiBuilder.OpenMainUi += ToggleConfigUi;

#if DEBUG
    _configWindow.IsOpen = true;
#endif

    _logger.ServiceLifecycle();
    return Task.CompletedTask;
  }

  public Task StopAsync(CancellationToken cancellationToken)
  {
    _pluginInterface.UiBuilder.OpenConfigUi -= ToggleConfigUi;
    _pluginInterface.UiBuilder.OpenMainUi -= ToggleConfigUi;
    _pluginInterface.UiBuilder.Draw -= UiBuilderOnDraw;

    _windowSystem.RemoveAllWindows();

    _logger.ServiceLifecycle();
    return Task.CompletedTask;
  }

  private void ToggleConfigUi()
  {
    _configWindow.SettingsOpen = false;
    _configWindow.OverlayOpen = false;
    _configWindow.Toggle();
  }

  private void UiBuilderOnDraw()
  {
    _windowSystem.Draw();
  }
}
