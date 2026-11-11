using Dalamud.Bindings.ImGui;
using Dalamud.Game.Command;
using Dalamud.Interface.Windowing;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using PvpStats.Helpers;
using System;
using System.IO;
using System.Numerics;

namespace PvpStats.Access;

/// <summary>
/// Plugin entry point. Holds the access-code lock: nothing is tracked, hooked or loaded
/// until a valid code has been entered. Once unlocked, the real plugin starts.
/// </summary>
public sealed class Entry : IDalamudPlugin {
    private const string MainCommand = BuildFlavor.CommandPrefix + "";
    private const string OwnerCommand = BuildFlavor.CommandPrefix + "owner";
    private const string CodeFile = "access-code.txt";
    private const string OwnerKeyFile = "owner-key.pem";
    private const string IssuedFile = "issued-codes.csv";

    private readonly IDalamudPluginInterface _pi;
    private readonly ICommandManager _commands;
    private readonly IFramework _framework;
    private readonly IPluginLog _log;
    private readonly Func<Plugin> _makePlugin;
    private readonly WindowSystem _windows = new("FLStatsPlusAccess");
    private readonly UnlockWindow _unlock;
    private readonly OwnerWindow _owner;
    private Plugin? _plugin;
    private bool _gateCommand;

    internal string ConfigDir => _pi.GetPluginConfigDirectory();

    public Entry(
        IDalamudPluginInterface pluginInterface,
        ICommandManager commandManager,
        IDataManager dataManager,
        IClientState clientState,
        ICondition condition,
        IDutyState dutyState,
        IPartyList partyList,
        IChatGui chatGui,
        IGameGui gameGui,
        IFramework framework,
        IPluginLog log,
        IAddonEventManager addonEventManager,
        IAddonLifecycle addonLifecycle,
        IObjectTable objectTable,
        ITextureProvider textureProvider,
        IGameInteropProvider interopProvider,
        ISigScanner sigScanner) {
        _pi = pluginInterface;
        _commands = commandManager;
        _framework = framework;
        _log = log;
        _makePlugin = () => new Plugin(pluginInterface, commandManager, dataManager, clientState, condition, dutyState,
            partyList, chatGui, gameGui, framework, log, addonEventManager, addonLifecycle, objectTable,
            textureProvider, interopProvider, sigScanner);

        _unlock = new UnlockWindow(this);
        _owner = new OwnerWindow(this);
        _windows.AddWindow(_unlock);
        _windows.AddWindow(_owner);
        _pi.UiBuilder.Draw += Draw;

        _commands.AddHandler(OwnerCommand, new CommandInfo((_, _) => _owner.IsOpen = true) {
            HelpMessage = "Make access codes (owner only).",
            ShowInHelp = false,
        });

        if(AccessCode.TryValidate(AccessCode.ReadFile(Path.Combine(ConfigDir, CodeFile)), out _, out _)) {
            StartPlugin();
        } else {
            ShowGate();
        }
    }

    private void ShowGate() {
        _gateCommand = true;
        _commands.AddHandler(MainCommand, new CommandInfo((_, _) => _unlock.IsOpen = true) {
            HelpMessage = "Opens FLStats Plus.",
        });
        _pi.UiBuilder.OpenMainUi += OpenUnlock;
        _pi.UiBuilder.OpenConfigUi += OpenUnlock;
        _unlock.IsOpen = true;
    }

    private void HideGate() {
        if(!_gateCommand) return;
        _gateCommand = false;
        _commands.RemoveHandler(MainCommand);
        _pi.UiBuilder.OpenMainUi -= OpenUnlock;
        _pi.UiBuilder.OpenConfigUi -= OpenUnlock;
        _unlock.IsOpen = false;
    }

    private void OpenUnlock() => _unlock.IsOpen = true;

    private void StartPlugin() {
        try {
            _plugin = _makePlugin();
        } catch(Exception e) {
            _log.Error(e, "FLStats Plus failed to start");
            _plugin = null;
        }
    }

    internal bool TryUnlock(string code, out string message) {
        if(!AccessCode.TryValidate(code, out var info, out message)) return false;
        try {
            Directory.CreateDirectory(ConfigDir);
            File.WriteAllText(Path.Combine(ConfigDir, CodeFile), code.Trim());
        } catch(Exception e) {
            _log.Error(e, "Could not save access code");
        }
        message = string.IsNullOrEmpty(info!.Label) ? "Unlocked." : $"Unlocked. Welcome, {info.Label}!";
        // Start the tracker outside the draw call.
        _framework.RunOnTick(() => {
            HideGate();
            StartPlugin();
        });
        return true;
    }

    internal string? LoadOwnerKey() => AccessCode.ReadFile(Path.Combine(ConfigDir, OwnerKeyFile));

    internal bool SaveOwnerKey(string pem) {
        if(!AccessCode.IsOwnerKey(pem)) return false;
        Directory.CreateDirectory(ConfigDir);
        File.WriteAllText(Path.Combine(ConfigDir, OwnerKeyFile), pem.Trim() + "\n");
        return true;
    }

    internal void ForgetOwnerKey() {
        try { File.Delete(Path.Combine(ConfigDir, OwnerKeyFile)); } catch { }
    }

    internal string IssuedPath => Path.Combine(ConfigDir, IssuedFile);

    internal void RecordIssued(string label, string code) {
        try {
            var path = IssuedPath;
            if(!File.Exists(path)) File.WriteAllText(path, "date,name,id,code\n");
            var safe = label.Replace("\"", "'");
            File.AppendAllText(path, $"{DateTime.Now:yyyy-MM-dd HH:mm},\"{safe}\",{AccessCode.IdOf(code)},{code}\n");
        } catch(Exception e) {
            _log.Error(e, "Could not record issued code");
        }
    }

    private void Draw() {
        if(!BuildFlavor.UseReportTheme) {
            _windows.Draw();
            return;
        }
        Theme.Push();
        try {
            _windows.Draw();
        } finally {
            Theme.Pop();
        }
    }

    public void Dispose() {
        _pi.UiBuilder.Draw -= Draw;
        HideGate();
        _commands.RemoveHandler(OwnerCommand);
        _windows.RemoveAllWindows();
        _plugin?.Dispose();
    }
}

internal sealed class UnlockWindow : Window {
    private readonly Entry _entry;
    private string _code = "";
    private string _message = "";
    private bool _error;

    public UnlockWindow(Entry entry) : base("FLStats Plus — Access code" + BuildFlavor.TitleSuffix + "##flpUnlock") {
        _entry = entry;
        Size = new Vector2(460, 0);
        SizeCondition = ImGuiCond.Always;
        Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse;
    }

    public override void Draw() {
        ImGui.TextWrapped("FLStats Plus needs an access code before it starts tracking. Paste the code you were given below.");
        ImGui.Spacing();
        ImGui.SetNextItemWidth(-1);
        bool enter = ImGui.InputTextWithHint("##code", "FLP-…", ref _code, 400, ImGuiInputTextFlags.EnterReturnsTrue);
        if(ImGui.Button("Paste")) {
            _code = ImGui.GetClipboardText() ?? "";
        }
        ImGui.SameLine();
        if(ImGui.Button("Unlock") || enter) {
            _error = !_entry.TryUnlock(_code, out _message);
        }
        if(_message.Length > 0) {
            ImGui.Spacing();
            ImGui.PushStyleColor(ImGuiCol.Text, _error ? new Vector4(1f, 0.45f, 0.45f, 1f) : new Vector4(0.55f, 0.85f, 1f, 1f));
            ImGui.TextWrapped(_message);
            ImGui.PopStyleColor();
        }
    }
}

internal sealed class OwnerWindow : Window {
    private readonly Entry _entry;
    private string? _key;
    private bool _loaded;
    private string _pasteKey = "";
    private string _name = "";
    private string _lastCode = "";
    private string _message = "";

    public OwnerWindow(Entry entry) : base("FLStats Plus — Make access codes" + BuildFlavor.TitleSuffix + "##flpOwner") {
        _entry = entry;
        Size = new Vector2(520, 0);
        SizeCondition = ImGuiCond.Always;
        Flags = ImGuiWindowFlags.NoResize | ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoCollapse;
    }

    public override void OnOpen() {
        _key = _entry.LoadOwnerKey();
        _loaded = true;
        _message = "";
    }

    public override void Draw() {
        if(!_loaded) OnOpen();
        if(_key == null) {
            ImGui.TextWrapped("Only the owner can make codes. Open your owner key file (FLStatsPlus-owner-key.pem) in Notepad, copy everything, and paste it here once.");
            ImGui.InputTextMultiline("##key", ref _pasteKey, 4000, new Vector2(-1, 110));
            if(ImGui.Button("Save owner key")) {
                if(_entry.SaveOwnerKey(_pasteKey)) {
                    _key = _entry.LoadOwnerKey();
                    _pasteKey = "";
                    _message = "Owner key saved on this PC.";
                } else {
                    _message = "That isn't the owner key for this plugin.";
                }
            }
            if(_message.Length > 0) ImGui.TextWrapped(_message);
            return;
        }

        ImGui.TextWrapped("Type who the code is for (so you can tell codes apart), then make a code and send it to them.");
        ImGui.SetNextItemWidth(260);
        ImGui.InputTextWithHint("##name", "Name", ref _name, 64);
        ImGui.SameLine();
        if(ImGui.Button("Make code")) {
            try {
                _lastCode = AccessCode.Create(_key, _name);
                _entry.RecordIssued(_name, _lastCode);
                ImGui.SetClipboardText(_lastCode);
                _message = "Copied to clipboard.";
            } catch(Exception e) {
                _message = e.Message;
            }
        }
        if(_lastCode.Length > 0) {
            ImGui.Spacing();
            ImGui.SetNextItemWidth(-1);
            var c = _lastCode;
            ImGui.InputText("##last", ref c, 400, ImGuiInputTextFlags.ReadOnly);
            if(ImGui.Button("Copy again")) {
                ImGui.SetClipboardText(_lastCode);
                _message = "Copied to clipboard.";
            }
        }
        if(_message.Length > 0) ImGui.TextWrapped(_message);
        ImGui.Spacing();
        ImGui.TextDisabled("Every code you make is listed in issued-codes.csv in the plugin's config folder.");
        if(ImGui.SmallButton("Open that folder")) {
            try {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_entry.ConfigDir) { UseShellExecute = true });
            } catch { }
        }
        ImGui.SameLine();
        if(ImGui.SmallButton("Remove owner key from this PC")) {
            _entry.ForgetOwnerKey();
            _key = null;
            _message = "";
        }
    }
}
