using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace PvpStats.Access;

/// <summary>
/// Signed access codes. A code carries a small payload (id, issue date, label) and an
/// ECDSA P-256 signature made with the owner's private key. The plugin only holds the
/// public key, so it can check codes but cannot make them.
/// </summary>
internal static class AccessCode {
    // Public half of the owner's signing key (SubjectPublicKeyInfo, base64).
    private const string PublicKey =
        "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAEF2zVCtM2qKWBrePzqIl5Q16AJU9mxmxpquL2MR4sMH7WEuSOmxzlLd4WEBu5jFaUcy44TMJFWsYdHRPZdoVbpQ==";

    // Code ids that no longer work. Add an id here and publish an update to cut someone off.
    private static readonly HashSet<uint> Revoked = new() {
    };

    internal const string Prefix = "FLP-";
    private const byte Version = 1;
    private static readonly DateTime Epoch = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    private const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ"; // Crockford base32
    internal const int MaxLabelBytes = 24;

    internal record Info(uint Id, DateTime Issued, string Label);

    internal static bool TryValidate(string? text, out Info? info, out string error) {
        info = null;
        error = "";
        if(string.IsNullOrWhiteSpace(text)) {
            error = "Enter your access code.";
            return false;
        }
        byte[] raw;
        try {
            raw = Decode(Normalize(text));
        } catch {
            error = "That doesn't look like an access code. Check for a missing or extra character.";
            return false;
        }
        if(raw.Length < 8 + 64 || raw[0] != Version) {
            error = "That doesn't look like an access code. Check for a missing or extra character.";
            return false;
        }
        int labelLen = raw[7];
        int payloadLen = 8 + labelLen;
        if(raw.Length != payloadLen + 64) {
            error = "That doesn't look like an access code. Check for a missing or extra character.";
            return false;
        }
        var payload = raw.AsSpan(0, payloadLen);
        var sig = raw.AsSpan(payloadLen, 64);
        bool ok;
        try {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(Convert.FromBase64String(PublicKey), out _);
            ok = ecdsa.VerifyData(payload, sig, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        } catch {
            ok = false;
        }
        if(!ok) {
            error = "This access code isn't valid.";
            return false;
        }
        uint id = (uint)(raw[1] << 24 | raw[2] << 16 | raw[3] << 8 | raw[4]);
        if(Revoked.Contains(id)) {
            error = "This access code has been turned off.";
            return false;
        }
        ushort days = (ushort)(raw[5] << 8 | raw[6]);
        var label = Encoding.UTF8.GetString(raw, 8, labelLen);
        info = new Info(id, Epoch.AddDays(days), label);
        return true;
    }

    /// <summary>Makes a code with the owner's private key (PKCS#8 PEM).</summary>
    internal static string Create(string privateKeyPem, string label) {
        label = (label ?? "").Trim();
        var labelBytes = Encoding.UTF8.GetBytes(label);
        if(labelBytes.Length > MaxLabelBytes) {
            throw new ArgumentException($"Name is too long (max {MaxLabelBytes} letters).");
        }
        var payload = new byte[8 + labelBytes.Length];
        payload[0] = Version;
        var id = BitConverter.ToUInt32(RandomNumberGenerator.GetBytes(4));
        payload[1] = (byte)(id >> 24); payload[2] = (byte)(id >> 16); payload[3] = (byte)(id >> 8); payload[4] = (byte)id;
        var days = (ushort)Math.Clamp((DateTime.UtcNow - Epoch).TotalDays, 0, ushort.MaxValue);
        payload[5] = (byte)(days >> 8); payload[6] = (byte)days;
        payload[7] = (byte)labelBytes.Length;
        labelBytes.CopyTo(payload, 8);
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(privateKeyPem);
        var sig = ecdsa.SignData(payload, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        var all = new byte[payload.Length + sig.Length];
        payload.CopyTo(all, 0);
        sig.CopyTo(all, payload.Length);
        return Prefix + Group(Encode(all));
    }

    /// <summary>True if the PEM is the private half of the key this build trusts.</summary>
    internal static bool IsOwnerKey(string privateKeyPem) {
        try {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(privateKeyPem);
            var pub = Convert.ToBase64String(ecdsa.ExportSubjectPublicKeyInfo());
            return pub == PublicKey;
        } catch {
            return false;
        }
    }

    internal static uint IdOf(string code) {
        var raw = Decode(Normalize(code));
        return (uint)(raw[1] << 24 | raw[2] << 16 | raw[3] << 8 | raw[4]);
    }

    private static string Normalize(string text) {
        var sb = new StringBuilder(text.Length);
        foreach(var ch in text.ToUpperInvariant()) {
            if(char.IsWhiteSpace(ch) || ch == '-') continue;
            sb.Append(ch switch { 'O' => '0', 'I' => '1', 'L' => '1', _ => ch });
        }
        var s = sb.ToString();
        // Payload always starts with version 1, which encodes to '0', so "FLP" is never part of the data.
        if(s.StartsWith("F1P")) s = s[3..];
        return s;
    }

    private static string Group(string s) {
        var sb = new StringBuilder();
        for(int i = 0; i < s.Length; i += 6) {
            if(i > 0) sb.Append('-');
            sb.Append(s, i, Math.Min(6, s.Length - i));
        }
        return sb.ToString();
    }

    private static string Encode(byte[] data) {
        var sb = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;
        foreach(var b in data) {
            buffer = (buffer << 8) | b;
            bits += 8;
            while(bits >= 5) {
                sb.Append(Alphabet[(buffer >> (bits - 5)) & 31]);
                bits -= 5;
            }
        }
        if(bits > 0) sb.Append(Alphabet[(buffer << (5 - bits)) & 31]);
        return sb.ToString();
    }

    private static byte[] Decode(string s) {
        var output = new List<byte>(s.Length * 5 / 8);
        int buffer = 0, bits = 0;
        foreach(var ch in s) {
            int v = Alphabet.IndexOf(ch);
            if(v < 0) throw new FormatException();
            buffer = (buffer << 5) | v;
            bits += 5;
            if(bits >= 8) {
                output.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }
        return output.ToArray();
    }

    // Small helpers for files kept in the plugin's config folder.
    internal static string? ReadFile(string path) {
        try { return File.Exists(path) ? File.ReadAllText(path).Trim() : null; } catch { return null; }
    }
}
