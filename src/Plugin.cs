using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ILogger = Facade.Services.ILogger;

namespace Facade;

public sealed class Plugin : IDalamudPlugin
{
  private readonly IHost _host;

  public Plugin(
    IChatGui chatGui,
    IToastGui toastGui,
    IPluginLog pluginLog,
    IFramework framework,
    IDataManager dataManager,
    IClientState clientState,
    IPlayerState playerState,
    ICommandManager commandManager,
    ITextureProvider textureProvider,
    IDalamudPluginInterface pluginInterface
  )
  {
    _host = new HostBuilder()
      .UseContentRoot(pluginInterface.ConfigDirectory.FullName).ConfigureLogging(lb =>
      {
        lb.ClearProviders();
        lb.SetMinimumLevel(LogLevel.Trace);
      }).ConfigureServices(collection =>
      {
        collection.AddSingleton(chatGui);
        collection.AddSingleton(toastGui);
        collection.AddSingleton(pluginLog);
        collection.AddSingleton(framework);
        collection.AddSingleton(dataManager);
        collection.AddSingleton(clientState);
        collection.AddSingleton(playerState);
        collection.AddSingleton(commandManager);
        collection.AddSingleton(textureProvider);
        collection.AddSingleton(pluginInterface);

        collection.AddSingleton<ConfigWindow>();
        collection.AddSingleton<ILogger, Logger>();
        collection.AddSingleton<IWindowService, WindowService>();
        collection.AddSingleton<ICommandService, CommandService>();
        collection.AddSingleton<IExteriorService, ExteriorService>();

        collection.AddSingleton(InitializeConfiguration);
        collection.AddSingleton(new WindowSystem(pluginInterface.InternalName));

        collection.AddHostedService(sp => sp.GetRequiredService<ConfigWindow>());
        collection.AddHostedService(sp => sp.GetRequiredService<IWindowService>());
        collection.AddHostedService(sp => sp.GetRequiredService<ICommandService>());
        collection.AddHostedService(sp => sp.GetRequiredService<IExteriorService>());
      }).Build();

    _host.StartAsync();
  }

  private Configuration InitializeConfiguration(IServiceProvider s)
  {
    IDalamudPluginInterface pluginInterface = s.GetRequiredService<IDalamudPluginInterface>();
    Configuration configuration = pluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
    configuration.Initialize(pluginInterface);
    return configuration;
  }

  public void Dispose()
  {
    _host.StopAsync().ConfigureAwait(false).GetAwaiter().GetResult();
    _host.Dispose();
  }
}
