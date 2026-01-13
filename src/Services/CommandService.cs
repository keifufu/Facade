namespace Facade.Services;

public interface ICommandService : IHostedService;

public class CommandService(ILogger _logger, ConfigWindow _configWindow, ICommandManager _commandManager) : ICommandService
{
  private const string FacadeCommand = "/facade";

  public Task StartAsync(CancellationToken cancellationToken)
  {
    _commandManager.AddHandler(FacadeCommand, new CommandInfo(OnCommand)
    {
      HelpMessage = $"See '{FacadeCommand} help' for more."
    });

    _logger.ServiceLifecycle();
    return Task.CompletedTask;
  }

  public Task StopAsync(CancellationToken cancellationToken)
  {
    _commandManager.RemoveHandler(FacadeCommand);

    _logger.ServiceLifecycle();
    return Task.CompletedTask;
  }

  private async void OnCommand(string command, string arguments)
  {
    _logger.Debug($"command::'{command}' arguments::'{arguments}'");

    string[] args = arguments.Split(" ", StringSplitOptions.RemoveEmptyEntries);
    if (args.Length == 0)
    {
      _configWindow.FacadeLocationOverlayOpen = false;
      _configWindow.Toggle();
      return;
    }

    switch (args[0])
    {
      case "version":
        _logger.Chat($"v{Assembly.GetExecutingAssembly()?.GetName()?.Version?.ToString() ?? "0.0.0.0"}");
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
