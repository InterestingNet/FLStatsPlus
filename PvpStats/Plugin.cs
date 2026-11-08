using Dalamud.Game.Command;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Utility.Signatures;
using PvpStats.Managers;
using PvpStats.Managers.Game;
using PvpStats.Managers.Stats;
using PvpStats.Services;
using PvpStats.Services.DataCache;
using PvpStats.Settings;
using PvpStats.Types.Match;
using PvpStats.Utility;
using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

namespace PvpStats;

public sealed class Plugin : IDalamudPlugin {
    public string Name => "FLStatsPlusTest";

    internal const string DatabaseName = "data.db";

    private const string SplashCommandName = BuildFlavor.CommandPrefix + "";
    private const string CCStatsCommandName = BuildFlavor.CommandPrefix + "cc";
    private const string FLStatsCommandName = BuildFlavor.CommandPrefix + "fl";
    private const string RWStatsCommandName = BuildFlavor.CommandPrefix + "rw";
    private const string DebugCommandName = BuildFlavor.CommandPrefix + "debug";
    private const string ConfigCommandName = BuildFlavor.CommandPrefix + "config";
    private const string LastMatchCommandName = BuildFlavor.CommandPrefix + "last";
    private const string ExportCommandName = BuildFlavor.CommandPrefix + "export";

    internal static IPluginLog Log2;

    //Dalamud services
    internal IDalamudPluginInterface PluginInterface { get; init; }
    private ICommandManager CommandManager { get; init; }
    internal IDataManager DataManager { get; init; }
    internal IClientState ClientState { get; init; }
    internal ICondition Condition { get; init; }
    internal IDutyState DutyState { get; init; }
    //internal IPartyList PartyList { get; init; }
    internal IChatGui ChatGui { get; init; }
    internal IGameGui GameGui { get; init; }
    internal IFramework Framework { get; init; }
    internal IPluginLog Log { get; init; }
    internal IAddonEventManager AddonEventManager { get; init; }
    internal IAddonLifecycle AddonLifecycle { get; init; }
    internal IObjectTable ObjectTable { get; init; }
    internal ITextureProvider TextureProvider { get; init; }
    internal IGameInteropProvider InteropProvider { get; init; }
    internal ISigScanner SigScanner { get; init; }

    internal CrystallineConflictMatchManager? CCMatchManager { get; init; }
    internal FrontlineMatchManager? FLMatchManager { get; init; }
    internal RivalWingsMatchManager? RWMatchManager { get; init; }
    internal WindowManager WindowManager { get; init; }
    internal ValidationManager Validation { get; init; }
    internal CrystallineConflictStatsManager CCStatsEngine { get; init; }
    internal FrontlineStatsManager FLStatsEngine { get; init; }
    internal RivalWingsStatsManager RWStatsEngine { get; init; }

    internal DataQueue DataQueue { get; init; }
    internal LocalizationService Localization { get; init; }
    internal IpcService Ipc { get; init; }
    internal StorageService Storage { get; init; }
    internal ExportService Exporter { get; init; }
    internal CCMatchCacheService CCCache { get; init; }
    internal FLMatchCacheService FLCache { get; init; }
    internal RWMatchCacheService RWCache { get; init; }
    internal GameStateService GameState { get; init; }
    internal AtkNodeService AtkNodeService { get; init; }
    internal PlayerLinkService PlayerLinksService { get; init; }

    public Configuration Configuration { get; init; }
    internal MemoryService Functions { get; init; }

    internal bool DebugMode { get; set; }

    public Plugin(
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
        try {
            PluginInterface = pluginInterface;
            CommandManager = commandManager;
            DataManager = dataManager;
            ClientState = clientState;
            Condition = condition;
            DutyState = dutyState;
            //PartyList = partyList;
            ChatGui = chatGui;
            GameGui = gameGui;
            Framework = framework;
            Log = log;
            Log2 = log;
            AddonEventManager = addonEventManager;
            AddonLifecycle = addonLifecycle;
            ObjectTable = objectTable;
            TextureProvider = textureProvider;
            InteropProvider = interopProvider;
            SigScanner = sigScanner;

            try {
                Configuration = PluginInterface.GetPluginConfig() as Configuration ?? new Configuration();
            } catch(Exception e) {
                Configuration = new();
                Log.Error(e, "Error in configuration setup.");
            }
            Configuration.Initialize(this);

            DataQueue = new();
            Ipc = new(this);
            SeedDatabase();
            Storage = new(this, $"{PluginInterface.GetPluginConfigDirectory()}\\{DatabaseName}");
            CCCache = new(this);
            FLCache = new(this);
            RWCache = new(this);
            Exporter = new(this);
            Functions = new(this);
            GameState = new(this);
            AtkNodeService = new(this);
            PlayerLinksService = new(this);
            Localization = new(this);
            CCStatsEngine = new(this);
            FLStatsEngine = new(this);
            RWStatsEngine = new(this);
            WindowManager = new(this);
            Validation = new(this);
            try {
                CCMatchManager = new(this);
            } catch(SignatureException e) {
                Log.Error(e, $"failed to initialize cc match manager");
            }
            try {
                FLMatchManager = new(this);
            } catch(SignatureException e) {
                Log.Error(e, $"failed to initialize fl match manager");
            }
            try {
                RWMatchManager = new(this);
            } catch(SignatureException e) {
                Log.Error(e, $"failed to initialize rw match manager");
            }

            CommandManager.AddHandler(SplashCommandName, new CommandInfo(OnSplashCommand) {
                HelpMessage = "Opens launcher window."
            });
            CommandManager.AddHandler(CCStatsCommandName, new CommandInfo(OnCCCommand) {
                HelpMessage = "Opens Crystalline Conflict tracker."
            });
            CommandManager.AddHandler(FLStatsCommandName, new CommandInfo(OnFLCommand) {
                HelpMessage = "Opens Frontline tracker."
            });
            CommandManager.AddHandler(RWStatsCommandName, new CommandInfo(OnRWCommand) {
                HelpMessage = "Opens Rival Wings tracker."
            });
            CommandManager.AddHandler(ConfigCommandName, new CommandInfo(OnConfigCommand) {
                HelpMessage = "Opens config window."
            });
            CommandManager.AddHandler(LastMatchCommandName, new CommandInfo(OnLastMatchCommand) {
                HelpMessage = "Opens match details window of last played match."
            });
            CommandManager.AddHandler(ExportCommandName, new CommandInfo(OnExportCommand) {
                HelpMessage = "Export Frontline data. /flpexport → last match as HTML. /flpexport all → full Frontline history as HTML. /flpexport json | csv → full history as JSON/CSV. /flpexport folder → open export folder."
            });

#if DEBUG
            CommandManager.AddHandler(DebugCommandName, new CommandInfo(OnDebugCommand) {
                HelpMessage = "Opens debug window."
            });
#endif
            DataQueue.QueueDataOperation(Initialize);
        } catch(Exception e) {
            //remove handlers and release database if we fail to start
            Log!.Error(e, "Failed to initialize plugin constructor");
            Dispose();
            //re-throw to prevent constructor from initializing
            throw;
        }

    }

    public void Dispose() {
#if DEBUG
        Log.Debug("disposing plugin");
#endif

        CommandManager.RemoveHandler(SplashCommandName);
        CommandManager.RemoveHandler(CCStatsCommandName);
        CommandManager.RemoveHandler(FLStatsCommandName);
        CommandManager.RemoveHandler(RWStatsCommandName);
        CommandManager.RemoveHandler(ConfigCommandName);
        CommandManager.RemoveHandler(LastMatchCommandName);
        CommandManager.RemoveHandler(ExportCommandName);
#if DEBUG
        CommandManager.RemoveHandler(DebugCommandName);
#endif

        Functions?.Dispose();
        CCMatchManager?.Dispose();
        FLMatchManager?.Dispose();
        RWMatchManager?.Dispose();
        WindowManager?.Dispose();
        Ipc?.Dispose();
        Storage?.Dispose();
        DataQueue?.Dispose();
        GameState?.Dispose();

        Configuration?.Save();
    }

    private void OnSplashCommand(string command, string args) {
        WindowManager.OpenSplashWindow();
    }

    private void OnLastMatchCommand(string command, string args) {
        Task.Run(() => {
            var lastMatchCC = CCCache.Matches.OrderByDescending(x => x.DutyStartTime).FirstOrDefault(x => x.IsCompleted && !x.IsQuarantined && !x.IsDeleted);
            var lastMatchFL = FLCache.Matches.OrderByDescending(x => x.DutyStartTime).FirstOrDefault(x => x.IsCompleted && !x.IsQuarantined && !x.IsDeleted);
            var lastMatchRW = RWCache.Matches.OrderByDescending(x => x.DutyStartTime).FirstOrDefault(x => x.IsCompleted && !x.IsQuarantined && !x.IsDeleted);
            List<PvpMatch> matches = [];
            if(lastMatchCC != null) {
                matches.Add(lastMatchCC);
            }
            if(lastMatchFL != null) {
                matches.Add(lastMatchFL);
            }
            if(lastMatchRW != null) {
                matches.Add(lastMatchRW);
            }
            var lastMatch = matches.OrderByDescending(x => x.DutyStartTime).FirstOrDefault();
            if(lastMatch != null) {
                WindowManager.OpenMatchDetailsWindow(lastMatch);
            }
        });
    }

    /// <summary>
    /// Test builds: on first start, copy the normal install's database (and its log) so the test build has your
    /// real match history. Read with shared access so it works while the normal plugin is running.
    /// </summary>
    private void SeedDatabase() {
        if(BuildFlavor.SeedDatabaseFrom is not string source) return;
        try {
            var dir = PluginInterface.GetPluginConfigDirectory();
            var target = Path.Combine(dir, DatabaseName);
            if(File.Exists(target)) return;
            var sourceDir = Path.Combine(Path.GetDirectoryName(dir.TrimEnd('\\', '/'))!, source);
            foreach(var file in new[] { DatabaseName, Path.GetFileNameWithoutExtension(DatabaseName) + "-log.db" }) {
                var from = Path.Combine(sourceDir, file);
                if(!File.Exists(from)) continue;
                Directory.CreateDirectory(dir);
                using var src = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var dst = new FileStream(Path.Combine(dir, file), FileMode.CreateNew, FileAccess.Write);
                src.CopyTo(dst);
            }
            Log.Information($"Seeded test database from {sourceDir}");
        } catch(Exception e) {
            Log.Warning(e, "Could not copy the main database; starting with an empty one.");
        }
    }

    private void OnExportCommand(string command, string args) {
        var arg = args.Trim().ToLowerInvariant();
        Task.Run(async () => {
            try {
                string? path = arg switch {
                    "" or "last" => await Exporter.ExportLastFrontlineMatchHtml(),
                    "all" or "history" => await Exporter.ExportFrontlineHistoryHtml(Exporter.AllFrontlineMatches()),
                    "json" => await Exporter.ExportFrontlineJson(Exporter.AllFrontlineMatches()),
                    "csv" => await Exporter.ExportFrontlineCsv(Exporter.AllFrontlineMatches()),
                    "folder" => Exporter.OpenExportFolder(),
                    _ => null,
                };
                if(arg is not ("folder")) {
                    if(path != null) {
                        ChatGui.Print($"[FLStats Plus] Exported to {path}");
                        Exporter.OpenFile(path);
                    } else {
                        ChatGui.PrintError("[FLStats Plus] Nothing to export. Usage: /flpexport [last|all|json|csv|folder]");
                    }
                }
            } catch(Exception e) {
                Log.Error(e, "export failed");
                ChatGui.PrintError($"[FLStats Plus] Export failed: {e.Message}");
            }
        });
    }

    private void OnCCCommand(string command, string args) {
        WindowManager.OpenCCWindow();
    }

    private void OnFLCommand(string command, string args) {
        WindowManager.OpenFLWindow();
    }

    private void OnRWCommand(string command, string args) {
        WindowManager.OpenRWWindow();
    }

    private void OnConfigCommand(string command, string args) {
        WindowManager.OpenConfigWindow();
    }

#if DEBUG
    private void OnDebugCommand(string command, string args) {
        WindowManager.OpenDebugWindow();
    }
#endif

    private async Task Initialize() {
        if(Configuration.EnableDBCachingCC ?? true) {
            CCCache.EnableCaching();
        }
        if(Configuration.EnableDBCachingFL ?? true) {
            FLCache.EnableCaching();
        }
        if(Configuration.EnableDBCachingRW ?? true) {
            RWCache.EnableCaching();
        }

        var lastVersion = new Version(Configuration.LastPluginVersion);
        var currentVersion = Assembly.GetExecutingAssembly().GetName().Version;

        //version update validations
        var ccValidationTask = Task.Run(async () => {
            await Validation.BulkCCUpdateValidatePlayerCount();
            if(lastVersion < new Version(2, 4, 0, 0)) {
                await Validation.BulkUpdateCCMatchTypes();
            }
            if(lastVersion < new Version(2, 5, 3, 0)) {
                await Validation.SetCrystallineConflictMatchFlags();
            }
        });

        var flValidationTask = Task.Run(async () => {
            if(lastVersion < new Version(2, 5, 2, 0)) {
                await Validation.ClearFrontlinePreProcessedData();
            }
            if(lastVersion < new Version(2, 5, 3, 0)) {
                await Validation.SetFrontlineMatchFlags();
            }
        });

        var rwValidationTask = Task.Run(async () => {
            if(lastVersion < new Version(2, 5, 3, 0)) {
                await Validation.SetRivalWingsMatchFlags();
            }
        });

        await Task.WhenAll(ccValidationTask, flValidationTask, rwValidationTask);

        await WindowManager.RefreshAll();
        Configuration.LastPluginVersion = currentVersion?.ToString() ?? "0.0.0.0";
        Configuration.Save();
        Log2.Information("PvP Tracker initialized.");
    }
}
