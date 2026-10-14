using Dalamud.Game.Text.SeStringHandling;

namespace Facade.Services;

public interface ILogger
{
  void Chat(string pre,
    string italic = "",
    string post = "",
    string name = "",
    XivChatType type = XivChatType.Debug,
    bool addPrefix = true);

  void Debug(string text,
    [CallerFilePath] string callerPath = "",
    [CallerMemberName] string callerName = "",
    [CallerLineNumber] int lineNumber = -1);

  void Warn(Exception ex,
    [CallerFilePath] string callerPath = "",
    [CallerMemberName] string callerName = "",
    [CallerLineNumber] int lineNumber = -1);

  void Error(string text,
    [CallerFilePath] string callerPath = "",
    [CallerMemberName] string callerName = "",
    [CallerLineNumber] int lineNumber = -1);

  void Error(Exception ex,
    [CallerFilePath] string callerPath = "",
    [CallerMemberName] string callerName = "",
    [CallerLineNumber] int lineNumber = -1);

  Task ServiceLifecycle(string? status = null,
    [CallerFilePath] string callerPath = "",
    [CallerMemberName] string callerName = "",
    [CallerLineNumber] int lineNumber = -1);
}

public class Logger(
  IChatGui _chatGui,
  IPluginLog _pluginLog,
  IDalamudPluginInterface _pluginInterface) : ILogger
{
  public void Chat(string pre,
    string italic = "",
    string post = "",
    string name = "",
    XivChatType type = XivChatType.Debug,
    bool addPrefix = true)
  {
    XivChatEntry chatMessage = new()
    {
      Type = type,
      Name = new SeStringBuilder().AddText(name).Build(),
      Message = new SeStringBuilder()
        .AddUiForeground(addPrefix ? $"[{_pluginInterface.InternalName}] " : "", 35)
        .AddText(pre)
        .AddItalics(italic)
        .AddText(post)
        .Build(),
    };
    _chatGui.Print(chatMessage);
    Debug($"Printed chatMessage::'{chatMessage.Message}'");
  }

  private string FormatCallsite(string callerPath, string callerName, int lineNumber)
    => $"[{Path.GetFileName(callerPath)}:{callerName}:{lineNumber}]";

  public void Debug(string text,
    [CallerFilePath] string callerPath = "",
    [CallerMemberName] string callerName = "",
    [CallerLineNumber] int lineNumber = -1)
    => _pluginLog.Debug($"{FormatCallsite(callerPath, callerName, lineNumber)} {text}");

  public void Warn(Exception ex,
    [CallerFilePath] string callerPath = "",
    [CallerMemberName] string callerName = "",
    [CallerLineNumber] int lineNumber = -1)
    => _pluginLog.Warning($"{FormatCallsite(callerPath, callerName, lineNumber)} Exception: {ex}");

  public void Error(string text,
    [CallerFilePath] string callerPath = "",
    [CallerMemberName] string callerName = "",
    [CallerLineNumber] int lineNumber = -1)
    => _pluginLog.Error($"{FormatCallsite(callerPath, callerName, lineNumber)} {text}");

  public void Error(Exception ex,
    [CallerFilePath] string callerPath = "",
    [CallerMemberName] string callerName = "",
    [CallerLineNumber] int lineNumber = -1)
    => _pluginLog.Error($"{FormatCallsite(callerPath, callerName, lineNumber)} Exception: {ex}");

  public Task ServiceLifecycle(string? status = null,
    [CallerFilePath] string callerPath = "",
    [CallerMemberName] string callerName = "",
    [CallerLineNumber] int lineNumber = -1)
  {
    string className = Path.GetFileNameWithoutExtension(callerPath);
    string lifecycleStage = status ??
      (callerName.Contains("Start")
      ? "started" : callerName.Contains("Stop")
      ? "stopped" : "changed");

    Debug($"{className} {lifecycleStage}", callerPath, callerName, lineNumber);
    return Task.CompletedTask;
  }
}
