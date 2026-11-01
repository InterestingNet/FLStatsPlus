
using Dalamud.Plugin.Ipc;
using LiteDB;
using PvpStats.Types.Match;
using System;
using System.Linq;

namespace PvpStats.Services;

internal class IpcService : IDisposable {
    private readonly Plugin _plugin;
    private readonly ICallGateProvider<string, bool> _onMatchSaved;
    private readonly ICallGateProvider<string, bool> _openMatchDetailsWindow;
    //fired once a match has its final scoreboard (PvpStats' OnMatchSaved fires at match start)
    private readonly ICallGateProvider<string, bool> _onMatchCompleted;
    //returns the full match (+ timeline/combat log for Frontline) as JSON, or null
    private readonly ICallGateProvider<string, string?> _getMatchJson;
    //exports a Frontline match to HTML and returns the file path, or null
    private readonly ICallGateProvider<string, string?> _exportMatchHtml;

    internal IpcService(Plugin plugin) {
        _plugin = plugin;

        _onMatchSaved = _plugin.PluginInterface.GetIpcProvider<string, bool>($"{_plugin.PluginInterface.InternalName}.OnMatchSaved");
        _openMatchDetailsWindow = _plugin.PluginInterface.GetIpcProvider<string, bool>($"{_plugin.PluginInterface.InternalName}.OpenMatchDetailsWindow");

        _onMatchCompleted = _plugin.PluginInterface.GetIpcProvider<string, bool>($"{_plugin.PluginInterface.InternalName}.OnMatchCompleted");
        _getMatchJson = _plugin.PluginInterface.GetIpcProvider<string, string?>($"{_plugin.PluginInterface.InternalName}.GetMatchJson");
        _exportMatchHtml = _plugin.PluginInterface.GetIpcProvider<string, string?>($"{_plugin.PluginInterface.InternalName}.ExportMatchHtml");

        _openMatchDetailsWindow.RegisterFunc(OpenMatchDetailsWindow);
        _getMatchJson.RegisterFunc(GetMatchJson);
        _exportMatchHtml.RegisterFunc(ExportMatchHtml);
    }

    public void Dispose() {
        _openMatchDetailsWindow.UnregisterFunc();
        _getMatchJson.UnregisterFunc();
        _exportMatchHtml.UnregisterFunc();
    }

    public void OnMatchCompleted(string id) {
        _onMatchCompleted.SendMessage(id);
    }

    private string? GetMatchJson(string id) {
        var match = TryGetPvpMatch(id);
        if(match == null) return null;
        return _plugin.Exporter.SerializeMatch(match);
    }

    private string? ExportMatchHtml(string id) {
        if(TryGetPvpMatch(id) is not FrontlineMatch match) return null;
        return _plugin.Exporter.ExportFrontlineMatchHtml(match).GetAwaiter().GetResult();
    }

    private bool OpenMatchDetailsWindow(string id) {
        PvpMatch? match = TryGetPvpMatch(id);
        if(match == null) return false;
        _plugin.WindowManager.OpenMatchDetailsWindow(match);
        return true;
    }

    public void OnMatchSaved(string id) {
        _onMatchSaved.SendMessage(id);
    }

    public PvpMatch? TryGetPvpMatch(string id) {
        try {
            var oid = new ObjectId(id);

            return _plugin.CCCache.Matches.FirstOrDefault(x => x.Id == oid) as PvpMatch
                ?? _plugin.FLCache.Matches.FirstOrDefault(x => x.Id == oid) as PvpMatch
                ?? _plugin.RWCache.Matches.FirstOrDefault(x => x.Id == oid);
        } catch {
            return null;
        }
    }
}
