using System;
using System.IO;
using System.Linq;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Memberra.Jellyfin;

/// <summary>Persists successful command ids so a lost acknowledgement cannot execute a command twice.</summary>
public sealed class CommandReceiptStore
{
    private readonly string _path;
    private readonly ILogger<CommandReceiptStore> _log;

    public CommandReceiptStore(IApplicationPaths paths, ILogger<CommandReceiptStore> log)
        : this(Path.Combine(paths.PluginConfigurationsPath, "Memberra"), log)
    {
    }

    internal CommandReceiptStore(string memberraPath, ILogger<CommandReceiptStore> log)
    {
        _path = Path.Combine(memberraPath, "command-receipts");
        _log = log;
        Directory.CreateDirectory(_path);
        Cleanup();
    }

    public bool Contains(Guid commandId) => File.Exists(GetPath(commandId));

    public void MarkSucceeded(Guid commandId)
    {
        var final = GetPath(commandId);
        var temporary = final + ".tmp";
        File.WriteAllText(temporary, DateTimeOffset.UtcNow.ToString("O"));
        File.Move(temporary, final, true);
    }

    private string GetPath(Guid commandId) => Path.Combine(_path, commandId.ToString("N") + ".ok");

    private void Cleanup()
    {
        var cutoff = DateTime.UtcNow.AddDays(-7);
        foreach (var file in Directory.EnumerateFiles(_path, "*.ok").Take(10_000))
        {
            try { if (File.GetLastWriteTimeUtc(file) < cutoff) File.Delete(file); }
            catch (IOException ex) { _log.LogDebug(ex, "Could not clean old Memberra command receipt"); }
        }
    }
}
