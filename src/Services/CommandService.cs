namespace Facade.Services;

public interface ICommandService : IHostedService;

public class CommandService(
  ILogger _logger,
  ConfigWindow _configWindow,
  ICommandManager _commandManager,
  IDalamudPluginInterface _pluginInterface
#if SAVE_MODE
  , IPlotService _plotService
#endif
) : ICommandService
{
  private const string FacadeCommand = "/facade";

  public Task StartAsync(CancellationToken cancellationToken)
  {
    _commandManager.AddHandler(FacadeCommand, new CommandInfo(OnCommand)
    {
      HelpMessage = $"See '{FacadeCommand} help' for more."
    });

    return _logger.ServiceLifecycle();
  }

  public Task StopAsync(CancellationToken cancellationToken)
  {
    _commandManager.RemoveHandler(FacadeCommand);

    return _logger.ServiceLifecycle();
  }

  private void OnCommand(string command, string arguments)
  {
    _logger.Debug($"command::'{command}' arguments::'{arguments}'");

    string[] args = arguments.Split(" ", StringSplitOptions.RemoveEmptyEntries);
    if (args.Length == 0)
    {
      _configWindow.SettingsOpen = false;
      _configWindow.OverlayOpen = false;
      _configWindow.Toggle();
      return;
    }

    switch (args[0])
    {
#if SAVE_MODE
      case "save-corner":
        _plotService.SaveCorner();
        break;
      case "save-json":
        _plotService.SaveJson();
        break;
#endif
      case "version":
        _logger.Chat($"v{_pluginInterface.Manifest.AssemblyVersion}");
        break;
      case "help":
      case "?":
        _logger.Chat("Available commands:");
        _logger.Chat($"  {command} help - Display this help menu");
        _logger.Chat($"  {command} version - Print the plugin's version");
        _logger.Chat($"  {command} - Toggle the configuration window");
        break;
      default:
        _logger.Chat("Invalid command:");
        _logger.Chat($"  {command} {arguments}");
        goto case "help";
    }
  }
}
