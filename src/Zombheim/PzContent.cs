using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

namespace Zombheim
{
    /// <summary>One Project Zomboid item as read from the player's own install.</summary>
    internal sealed class PzItem
    {
        public string FullType;                       // Base.BaseballBat
        public string DisplayName;                    // from the script or the EN translation
        public Dictionary<string, string> Props = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        public object Icon;                           // a UnityEngine.Sprite in game (kept as object so the reader runs outside Unity)
        public string Source;                         // file it was read from (for the log)

        public float Num(string key, float fallback)
        {
            if (Props.TryGetValue(key, out var s) &&
                float.TryParse(s.Trim().TrimEnd(';'), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var f))
                return f;
            return fallback;
        }
    }

    /// <summary>
    /// Finds the player's Project Zomboid folder and reads only what the mashup needs from it:
    /// the item scripts, item names, item icons (from its texture packs) and its intro line.
    /// Nothing is copied out of the game; everything is read into memory while Valheim runs.
    /// </summary>
    internal static class PzContent
    {
        public static string Folder { get; private set; }
        public static readonly Dictionary<string, PzItem> Items = new Dictionary<string, PzItem>();
        public static string IntroLine = "This is how you died.";
        public static string IntroSource = "built-in";

        public static string FindFolder(string configured)
        {
            var args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "--zombheim-pz") return Check(args[i + 1], "launch argument");
            var env = Environment.GetEnvironmentVariable("ZOMBHEIM_PZ");
            if (!string.IsNullOrEmpty(env)) return Check(env, "ZOMBHEIM_PZ");
            if (!string.IsNullOrEmpty(configured) && Check(configured, "config") != null) return configured;

            // Same Steam library as Valheim, then every library Steam knows about.
            var candidates = new List<string>();
            try
            {
                var common = Directory.GetParent(BepInEx.Paths.GameRootPath)?.FullName;
                if (common != null) candidates.Add(Path.Combine(common, "ProjectZomboid"));
                var steamapps = Directory.GetParent(common ?? "")?.FullName;
                var vdfs = new List<string>();
                if (steamapps != null) vdfs.Add(Path.Combine(steamapps, "libraryfolders.vdf"));
                vdfs.Add(@"C:\Program Files (x86)\Steam\steamapps\libraryfolders.vdf");
                vdfs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local/share/Steam/steamapps/libraryfolders.vdf"));
                foreach (var vdf in vdfs.Where(File.Exists))
                    foreach (Match m in Regex.Matches(File.ReadAllText(vdf), "\"path\"\\s+\"([^\"]+)\""))
                        candidates.Add(Path.Combine(m.Groups[1].Value.Replace("\\\\", "\\"), "steamapps", "common", "ProjectZomboid"));
            }
            catch (Exception e) { Z.Log.LogDebug("library scan: " + e.Message); }
            foreach (var c in candidates)
                if (Check(c, null) != null) return c;
            return null;
        }

        static string Check(string dir, string from)
        {
            if (!string.IsNullOrEmpty(dir) && Directory.Exists(Path.Combine(dir, "media", "scripts")))
            {
                if (from != null) Z.Log.LogInfo($"Project Zomboid folder from {from}: {dir}");
                return dir;
            }
            if (from != null) Z.Log.LogWarning($"{from} points at '{dir}', which has no media/scripts");
            return null;
        }

        public static void Load(string folder, IEnumerable<string> wanted)
        {
            Folder = folder;
            if (folder == null) return;
            var media = Path.Combine(folder, "media");
            var want = new HashSet<string>(wanted);
            ReadScripts(Path.Combine(media, "scripts"), want);
            ReadNames(Path.Combine(media, "lua", "shared", "Translate", "EN"));
            ReadIntro(Path.Combine(media, "lua", "shared", "Translate", "EN"));
            ReadIcons(media);
            foreach (var w in want)
                if (Items.TryGetValue(w, out var it))
                    Z.Log.LogInfo($"Zomboid item {w}: '{it.DisplayName}' from {it.Source}, {it.Props.Count} properties, icon {((object)it.Icon != null ? "yes" : "no")}");
                else
                    Z.Log.LogWarning($"Zomboid item {w} not found in {folder}");
        }

        // ---- item scripts: media/scripts/**/*.txt, "module Base { item BaseballBat { Key = Value, ... } }"
        static readonly Regex ModuleRx = new Regex(@"\bmodule\s+(\w+)", RegexOptions.Compiled);
        static readonly Regex ItemRx = new Regex(@"\bitem\s+(\w+)\s*\{", RegexOptions.Compiled);

        static void ReadScripts(string dir, HashSet<string> want)
        {
            if (!Directory.Exists(dir)) return;
            var shortNames = want.Select(w => w.Substring(w.IndexOf('.') + 1)).ToList();
            foreach (var file in Directory.GetFiles(dir, "*.txt", SearchOption.AllDirectories))
            {
                string text;
                try { text = File.ReadAllText(file); } catch { continue; }
                if (!shortNames.Any(n => text.Contains(n))) continue;
                text = Regex.Replace(text, @"/\*.*?\*/", "", RegexOptions.Singleline);
                var module = ModuleRx.Match(text) is var mm && mm.Success ? mm.Groups[1].Value : "Base";
                foreach (Match m in ItemRx.Matches(text))
                {
                    var full = module + "." + m.Groups[1].Value;
                    if (!want.Contains(full) || Items.ContainsKey(full)) continue;
                    int start = m.Index + m.Length, depth = 1, i = start;
                    while (i < text.Length && depth > 0) { if (text[i] == '{') depth++; else if (text[i] == '}') depth--; i++; }
                    var it = new PzItem { FullType = full, Source = file.Substring(Folder.Length).TrimStart('/', '\\') };
                    foreach (var part in text.Substring(start, Math.Max(0, i - start - 1)).Split(',', '\n'))
                    {
                        var kv = part.Split(new[] { '=' }, 2);
                        if (kv.Length == 2 && kv[0].Trim().Length > 0) it.Props[kv[0].Trim()] = kv[1].Trim();
                    }
                    if (it.Props.TryGetValue("DisplayName", out var dn)) it.DisplayName = dn;
                    Items[full] = it;
                }
            }
        }

        // ---- names: Build 41 ItemName_EN.txt (ItemName_Base.X = "Name",) or Build 42 ItemName.json ("Base.X": "Name")
        static void ReadNames(string dir)
        {
            if (!Directory.Exists(dir)) return;
            foreach (var file in Directory.GetFiles(dir, "ItemName*", SearchOption.TopDirectoryOnly))
            {
                string text;
                try { text = File.ReadAllText(file); } catch { continue; }
                foreach (var it in Items.Values.Where(x => string.IsNullOrEmpty(x.DisplayName)))
                {
                    var key = Regex.Escape(it.FullType);
                    var m = Regex.Match(text, "(?:ItemName_)?\"?" + key + "\"?\\s*[=:]\\s*\"([^\"]+)\"");
                    if (m.Success) it.DisplayName = m.Groups[1].Value;
                }
            }
        }

        static void ReadIntro(string dir)
        {
            if (!Directory.Exists(dir)) return;
            foreach (var file in Directory.GetFiles(dir, "*", SearchOption.TopDirectoryOnly))
            {
                string text;
                try { text = File.ReadAllText(file); } catch { continue; }
                var m = Regex.Match(text, "\"([^\"]*how you died[^\"]*)\"", RegexOptions.IgnoreCase);
                if (m.Success) { IntroLine = m.Groups[1].Value.Replace("<br>", " ").Trim(); IntroSource = Path.GetFileName(file); return; }
            }
        }

        // ---- icons: Icon = BaseballBat -> texture "Item_BaseballBat", loose png or inside media/texturepacks/*.pack
        static void ReadIcons(string media)
        {
            var need = new Dictionary<string, PzItem>();
            foreach (var it in Items.Values)
            {
                var icon = it.Props.TryGetValue("Icon", out var ic) ? ic.Trim() : it.FullType.Substring(it.FullType.IndexOf('.') + 1);
                need["Item_" + icon] = it;
            }
            foreach (var kv in need.ToList())
            {
                foreach (var sub in new[] { "textures", "ui", Path.Combine("textures", "Item") })
                {
                    var png = Path.Combine(Path.Combine(media, sub), kv.Key + ".png");
                    if (File.Exists(png)) { kv.Value.Icon = SpriteFrom(File.ReadAllBytes(png), null); need.Remove(kv.Key); break; }
                }
            }
            var packs = Path.Combine(media, "texturepacks");
            if (need.Count == 0 || !Directory.Exists(packs)) return;
            foreach (var pack in Directory.GetFiles(packs, "*.pack"))
            {
                try
                {
                    foreach (var hit in PzPack.Find(pack, need.Keys))
                    {
                        need[hit.Name].Icon = SpriteFrom(hit.PagePng, hit);
                        need.Remove(hit.Name);
                    }
                }
                catch (Exception e) { Z.Log.LogWarning($"reading {Path.GetFileName(pack)}: {e.Message}"); }
                if (need.Count == 0) break;
            }
        }

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        static object SpriteFrom(byte[] png, PzPack.Entry region)
        {
            var page = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            if (!ImageConversion.LoadImage(page, png)) return null;
            Texture2D tex = page;
            if (region != null)
            {
                // Pack coordinates are top-left based; Unity's are bottom-left.
                int y = page.height - region.Y - region.H;
                tex = new Texture2D(region.W, region.H, TextureFormat.RGBA32, false);
                tex.SetPixels(page.GetPixels(region.X, y, region.W, region.H));
                tex.Apply();
                UnityEngine.Object.Destroy(page);
            }
            tex.filterMode = FilterMode.Point;
            return Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
        }
    }

    /// <summary>
    /// Reader for Project Zomboid's .pack texture atlases. Format: optional "PZPK" + int version, int page count;
    /// per page: string name, int entry count, int mask flag, entries (string name, 8 ints: x y w h ox oy fw fh),
    /// then the page PNG (length-prefixed from version 1, otherwise followed by the 0xDEADBEEF marker).
    /// Ints are little-endian; strings are an int length then ASCII bytes.
    /// </summary>
    internal static class PzPack
    {
        public sealed class Entry { public string Name; public int X, Y, W, H; public byte[] PagePng; }

        public static List<Entry> Find(string path, ICollection<string> names)
        {
            var found = new List<Entry>();
            using (var r = new BinaryReader(File.OpenRead(path)))
            {
                int version = 0;
                var magic = r.ReadBytes(4);
                if (Encoding.ASCII.GetString(magic) == "PZPK") version = r.ReadInt32();
                else r.BaseStream.Position = 0;
                int pages = r.ReadInt32();
                if (pages < 0 || pages > 10000) throw new InvalidDataException("not a texture pack");
                for (int p = 0; p < pages; p++)
                {
                    Str(r);
                    int count = r.ReadInt32();
                    r.ReadInt32();
                    var hits = new List<Entry>();
                    for (int e = 0; e < count; e++)
                    {
                        var name = Str(r);
                        int x = r.ReadInt32(), y = r.ReadInt32(), w = r.ReadInt32(), h = r.ReadInt32();
                        r.ReadInt32(); r.ReadInt32(); r.ReadInt32(); r.ReadInt32();
                        if (names.Contains(name)) hits.Add(new Entry { Name = name, X = x, Y = y, W = w, H = h });
                    }
                    byte[] png = version >= 1 ? ReadLen(r, hits.Count > 0) : ReadUntilIend(r, hits.Count > 0);
                    foreach (var h in hits) { h.PagePng = png; found.Add(h); }
                    if (found.Count == names.Count) break;
                }
            }
            return found;
        }

        static string Str(BinaryReader r)
        {
            int n = r.ReadInt32();
            if (n < 0 || n > 4096) throw new InvalidDataException("bad string length");
            return Encoding.ASCII.GetString(r.ReadBytes(n));
        }

        static byte[] ReadLen(BinaryReader r, bool keep)
        {
            int n = r.ReadInt32();
            if (!keep) { r.BaseStream.Seek(n, SeekOrigin.Current); return null; }
            return r.ReadBytes(n);
        }

        // Walk the PNG's chunks to its IEND, then skip the 0xDEADBEEF marker.
        static byte[] ReadUntilIend(BinaryReader r, bool keep)
        {
            long start = r.BaseStream.Position;
            r.BaseStream.Seek(8, SeekOrigin.Current);
            while (true)
            {
                var lenBytes = r.ReadBytes(4);
                int len = (lenBytes[0] << 24) | (lenBytes[1] << 16) | (lenBytes[2] << 8) | lenBytes[3];
                var type = Encoding.ASCII.GetString(r.ReadBytes(4));
                r.BaseStream.Seek(len + 4, SeekOrigin.Current);
                if (type == "IEND") break;
            }
            long end = r.BaseStream.Position;
            byte[] png = null;
            if (keep) { r.BaseStream.Position = start; png = r.ReadBytes((int)(end - start)); }
            if (r.BaseStream.Position + 4 <= r.BaseStream.Length)
            {
                if ((uint)r.ReadInt32() != 0xDEADBEEF) r.BaseStream.Seek(-4, SeekOrigin.Current);
            }
            return png;
        }
    }
}
