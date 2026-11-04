// SPDX-License-Identifier: MIT
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace G7ProXboxWireless
{
    internal static class Bytes
    {
        internal static byte[] Part(byte[] b, int start, int count)
        {
            if (start < 0 || count < 0 || start > b.Length - count) throw new InvalidDataException("Data exceeds file bounds.");
            var result = new byte[count]; Buffer.BlockCopy(b, start, result, 0, count); return result;
        }
        internal static ushort U16(byte[] b, int n) { return BitConverter.ToUInt16(Part(b, n, 2), 0); }
        internal static uint U32(byte[] b, int n) { return BitConverter.ToUInt32(Part(b, n, 4), 0); }
        internal static void Put16(byte[] b, int n, ushort value) { Buffer.BlockCopy(BitConverter.GetBytes(value), 0, b, n, 2); }
        internal static void Put32(byte[] b, int n, uint value) { Buffer.BlockCopy(BitConverter.GetBytes(value), 0, b, n, 4); }
        internal static uint Big32(byte[] b, int n) { return ((uint)b[n] << 24) | ((uint)b[n+1] << 16) | ((uint)b[n+2] << 8) | b[n+3]; }
        internal static byte[] Big(uint n) { return new[] { (byte)(n >> 24), (byte)(n >> 16), (byte)(n >> 8), (byte)n }; }
        internal static byte[] Join(params byte[][] parts) { using (var s = new MemoryStream()) { foreach (var p in parts) s.Write(p, 0, p.Length); return s.ToArray(); } }
        internal static string Hash(byte[] b) { using (var h = SHA256.Create()) return BitConverter.ToString(h.ComputeHash(b)).Replace("-", "").ToLowerInvariant(); }
        internal static ushort Crc16(byte[] data) { ushort c = 0; foreach (byte b in data) { c ^= (ushort)(b << 8); for (int i = 0; i < 8; i++) c = (ushort)((c << 1) ^ ((c & 0x8000) != 0 ? 0x1021 : 0)); } return c; }
        internal static uint Crc32(byte[] data, uint seed) { uint c = seed; foreach (byte b in data) { c ^= b; for (int i = 0; i < 8; i++) c = (c >> 1) ^ ((c & 1) != 0 ? 0xedb88320U : 0); } return c; }
        internal static uint Adler(byte[] data) { uint a = 1, b = 0; foreach (byte v in data) { a = (a + v) % 65521; b = (b + a) % 65521; } return (b << 16) | a; }
        internal static string Text(byte[] b) { int length=b.Length;while(length>0 && (b[length-1]==0 || b[length-1]==255))length--;return Encoding.ASCII.GetString(b,0,length); }
        internal static void Require(bool value, string message) { if (!value) throw new InvalidDataException(message); }
    }

    // Reproduces the vendor container and integrity records; never changes a UFW byte.
    internal static class FirmwarePackage
    {
        internal const string OfficialHash = "5040fe4c38311fc7971ff390a331d1b38b9ecd537075619c396278e7be381eb1";
        internal const string KeyHash = "8d455017df79ea81b5373013602427ca75faf709c7f766bab1ddc761eb7752b7";
        internal const string NoKeyHash = "26acd29c8c0190889e847ae5ac7d01409ab6c77874d2072d1c388cb7b2dd6d43";
        internal const string OfficialPage = "https://gamesir.com/support/manuals/gamesir-g7prozzz";
        internal const string OfficialDownload = "https://drive.google.com/file/d/1jHdA1OJnj-lCQrfHqujVzdLEVh6yPcp-/view";
        internal const string UpdaterName = "Wuchang-Dongle-5.12-EXPERIMENTAL.exe";

        internal static byte[] ReadOfficial(string path)
        {
            Bytes.Require(new FileInfo(path).Length == 9618768, "Select the original Wuchang Dongle 5.12 PID Protected EXE, extracted from GameSir's package.");
            byte[] b = File.ReadAllBytes(path);
            Bytes.Require(Bytes.Hash(b) == OfficialHash, "Unsupported or modified updater. Only the exact official Wuchang dongle 5.12 package is supported.");
            return b;
        }

        internal static byte[] DecodeRecord(byte[] record)
        {
            Bytes.Require(record.Length == 32, "Invalid integrity record size.");
            ushort key = Bytes.U16(record, 16); byte[] plain = Crypt(record, key); Bytes.Put16(plain, 16, key);
            Bytes.Require(Bytes.U16(plain, 0) == 32 && Bytes.U16(plain, 2) == Bytes.Crc16(Bytes.Part(plain, 4, 28)), "Invalid integrity record header.");
            return plain;
        }
        private static byte[] Crypt(byte[] data, uint key)
        {
            byte[] result = (byte[])data.Clone();
            for (int i = 0; i < result.Length; i++) { key = unchecked(key * 214013 + 2531011); result[i] ^= (byte)(key >> 16); }
            return result;
        }
        internal static byte[] CheckRecord(byte[] encoded, byte[] data, int kind)
        {
            byte[] p = DecodeRecord(encoded);
            Bytes.Require(Bytes.U16(p, 4) == kind && Bytes.U32(p, 12) == data.Length && Bytes.U16(p, 6) == Bytes.Crc16(data) && Bytes.U32(p, 8) == Bytes.Crc32(data, 0x26536734), "Integrity record does not match its data.");
            return p;
        }
        internal static byte[] EncodeRecord(byte[] originalPlain, byte[] data)
        {
            byte[] p = (byte[])originalPlain.Clone();
            Bytes.Put16(p, 6, Bytes.Crc16(data)); Bytes.Put32(p, 8, Bytes.Crc32(data, 0x26536734)); Bytes.Put32(p, 12, (uint)data.Length);
            Bytes.Put16(p, 2, Bytes.Crc16(Bytes.Part(p, 4, 28))); ushort key = Bytes.U16(p, 16);
            byte[] result = Crypt(p, key); Bytes.Put16(result, 16, key); CheckRecord(result, data, Bytes.U16(p, 4)); return result;
        }
        internal static byte[] Inflate(byte[] q)
        {
            Bytes.Require(q.Length >= 10 && q[4] == 0x78 && ((q[4] << 8) + q[5]) % 31 == 0 && (q[5] & 32) == 0, "Invalid zlib stream.");
            uint size = Bytes.Big32(q, 0); Bytes.Require(size >= 4096 && size <= 4 * 1024 * 1024, "Unsupported expanded package size.");
            using (var input = new MemoryStream(q, 6, q.Length - 10))
            using (var zip = new DeflateStream(input, CompressionMode.Decompress))
            using (var output = new MemoryStream())
            {
                var block = new byte[8192]; int n;
                while ((n = zip.Read(block, 0, block.Length)) > 0) { Bytes.Require(output.Length + n <= size, "Expanded package exceeds its declared size."); output.Write(block, 0, n); }
                byte[] b = output.ToArray(); Bytes.Require(b.Length == size && Bytes.Adler(b) == Bytes.Big32(q, q.Length - 4), "Invalid expanded package checksum or size."); return b;
            }
        }
        internal static byte[] Deflate(byte[] b)
        {
            byte[] raw;
            using (var output = new MemoryStream()) { using (var z = new DeflateStream(output, CompressionLevel.Optimal, true)) z.Write(b, 0, b.Length); raw = output.ToArray(); }
            return Bytes.Join(Bytes.Big((uint)b.Length), new byte[] { 0x78, 0x9c }, raw, Bytes.Big(Bytes.Adler(b)));
        }
        internal static byte[] ModifyContainer(byte[] original)
        {
            Bytes.Require(original.Length == 2586624 && Bytes.U32(original, 4) == 5 && Bytes.U32(original, 8) == original.Length, "Unexpected package table.");
            Bytes.Require(Bytes.U16(original, 0) == Bytes.Crc16(Bytes.Part(original, 2, 30)) && Bytes.U16(original, 2) == Bytes.Crc16(Bytes.Part(original, 32, 5 * 528)), "Package table CRC mismatch.");
            byte[] b = (byte[])original.Clone(); bool changed = false; var names = new HashSet<string>(); var regions = new List<Tuple<int,int>>();
            for (int i = 0; i < 5; i++)
            {
                int h = 32 + i * 528, a = checked((int)Bytes.U32(b, h+4)), n = checked((int)Bytes.U32(b, h+8));
                string name = Bytes.Text(Bytes.Part(b, h+16, 512)); Bytes.Require(names.Add(name), "Duplicate embedded file.");
                Bytes.Require(a >= 4096 && a <= b.Length - n && n > 0, "Invalid embedded file range.");
                foreach (var region in regions) Bytes.Require(a + n <= region.Item1 || a >= region.Item2, "Embedded files overlap.");
                regions.Add(Tuple.Create(a, a+n)); byte[] data = Bytes.Part(b, a, n);
                Bytes.Require((Bytes.U32(b, h) >> 16) == Bytes.Crc16(data), "Embedded file CRC mismatch.");
                if (name == "data\\config.ini")
                {
                    Bytes.Require(n == 8114, "Unexpected configuration length.");
                    string text = Encoding.UTF8.GetString(data), old = "[PidMatch]\r\nenable               = true\t";
                    Bytes.Require(text.IndexOf(old, StringComparison.Ordinal) >= 0 && text.IndexOf(old, StringComparison.Ordinal) == text.LastIndexOf(old, StringComparison.Ordinal), "Unexpected product-match configuration.");
                    Bytes.Require(text.Contains("[EraseAll]") && Setting(text, "EraseAll", "checked") == "false" && Setting(text, "RewriteAuthCode", "checked") == "false" && Setting(text, "AutoUpgrade", "checked") == "false", "Unsafe updater defaults.");
                    byte[] updated = Encoding.UTF8.GetBytes(text.Replace(old, old.Replace("true\t", "false")));
                    Bytes.Require(updated.Length == n, "Configuration size changed."); Buffer.BlockCopy(updated, 0, b, a, n);
                    Bytes.Put32(b, h, (Bytes.U32(b, h) & 0xffff) | ((uint)Bytes.Crc16(updated) << 16)); changed = true;
                }
                else if (name == "data\\JS_3103_U_DG_V512_Key.ufw") Bytes.Require(Bytes.Hash(data) == KeyHash, "Key firmware payload differs from the tested official image.");
                else if (name == "data\\JS_3103_U_DG_V512_No_Key.ufw") Bytes.Require(Bytes.Hash(data) == NoKeyHash, "No-Key firmware payload differs from the tested official image.");
                else Bytes.Require(name == "data\\window.png" || name == "tail.bin", "Unknown embedded file.");
            }
            Bytes.Require(changed && names.Contains("data\\JS_3103_U_DG_V512_Key.ufw") && names.Contains("data\\JS_3103_U_DG_V512_No_Key.ufw"), "Required files missing.");
            Bytes.Put16(b, 2, Bytes.Crc16(Bytes.Part(b, 32, 5*528))); Bytes.Put16(b, 0, Bytes.Crc16(Bytes.Part(b, 2, 30))); return b;
        }
        private static string Setting(string text, string section, string key)
        {
            var match = System.Text.RegularExpressions.Regex.Match(text, @"(?ms)^\[" + section + @"\]\s*\r?\n(?:(?!^\[).)*?^" + key + @"\s*=\s*(\w+)");
            return match.Success ? match.Groups[1].Value : "";
        }
        internal static byte[] Build(byte[] original)
        {
            Bytes.Require(Bytes.Hash(original) == OfficialHash, "Only the exact original Wuchang dongle 5.12 updater is accepted.");
            byte[] r1 = CheckRecord(Bytes.Part(original, original.Length-32, 32), Bytes.Part(original, 0, original.Length-32), 1);
            byte[] p2 = DecodeRecord(Bytes.Part(original, original.Length-64, 32));
            int size = checked((int)Bytes.U32(p2, 12)), start = original.Length - 64 - size;
            Bytes.Require(start == 7204363 && Bytes.U16(p2, 18) == 11, "Unexpected updater layout.");
            byte[] q = Bytes.Part(original, start, size); CheckRecord(Bytes.Part(original, original.Length-64, 32), q, 2);
            byte[] updated = Deflate(ModifyContainer(Inflate(q)));
            byte[] body = Bytes.Join(Bytes.Part(original, 0, start), updated, EncodeRecord(p2, updated));
            byte[] result = Bytes.Join(body, EncodeRecord(r1, body));
            CheckRecord(Bytes.Part(result, result.Length-32, 32), body, 1);
            Bytes.Require(Inflate(updated).SequenceEqual(ModifyContainer(Inflate(q))), "Rebuilt package failed verification.");
            return result;
        }
    }
}
