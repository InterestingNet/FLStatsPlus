namespace PvpStats;

/// <summary>
/// Settings that differ between the normal plugin and the side-by-side test build (branch <c>ui-test</c>).
/// The test build changes only this file plus its manifest/project name, so the two install and run
/// together with separate commands, window titles and databases.
/// </summary>
internal static class BuildFlavor {
    /// <summary>Appended to window titles (test build: " [TEST]").</summary>
    internal const string TitleSuffix = "";
    /// <summary>Prefix for all slash commands (test build: "/flpt").</summary>
    internal const string CommandPrefix = "/flp";
    /// <summary>
    /// If set, and this install has no database yet, copy the database of the install with this internal
    /// name on first start (test build: "FLStatsPlus", so it starts with your real match history).
    /// </summary>
    internal static readonly string? SeedDatabaseFrom = null;
    /// <summary>Draw every plugin window with the blue theme from the HTML reports (see Helpers/Theme.cs).</summary>
    internal const bool UseReportTheme = true;
}
