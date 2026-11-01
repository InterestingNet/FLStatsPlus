using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PvpStats.Types.Match.Timeline;

/// <summary>
/// Stores the combat log as gzip-compressed JSON instead of thousands of LiteDB sub-documents.
/// Default values (false / 0 / null) are omitted, so a typical entry packs down to a few dozen bytes.
/// Packing runs once per match on the background data queue; unpacking only happens on export.
/// </summary>
internal static class CombatLogCodec {

    private static readonly JsonSerializerOptions Options = new() {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    };

    public static byte[] Pack(List<CombatLogEntry> log) {
        using var ms = new MemoryStream();
        using(var gz = new GZipStream(ms, CompressionLevel.Optimal, leaveOpen: true)) {
            JsonSerializer.Serialize(gz, log, Options);
        }
        return ms.ToArray();
    }

    public static List<CombatLogEntry> Unpack(byte[] data) {
        using var ms = new MemoryStream(data);
        using var gz = new GZipStream(ms, CompressionMode.Decompress);
        return JsonSerializer.Deserialize<List<CombatLogEntry>>(gz, Options) ?? [];
    }
}
