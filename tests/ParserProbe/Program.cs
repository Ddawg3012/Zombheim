// Offline check of Zombheim's Project Zomboid readers against synthetic Build 41 / Build 42 style files.
// Uses no real game files: the fixtures below are written by this test.
using System;
using System.IO;
using System.Linq;
using System.Text;
using Zombheim;

static class Program
{
    static int fails;
    static PzItem Item(string k) => PzContent.Items.TryGetValue(k, out var v) ? v : null;
    static void Expect(bool ok, string what) { Console.WriteLine((ok ? "PASS " : "FAIL ") + what); if (!ok) fails++; }

    static void WStr(BinaryWriter w, string s) { w.Write(s.Length); w.Write(Encoding.ASCII.GetBytes(s)); }
    static byte[] FakePng() // signature + IHDR + IEND chunks (content irrelevant to the reader)
    {
        var ms = new MemoryStream(); var w = new BinaryWriter(ms);
        w.Write(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });
        void Chunk(string t, int len) { w.Write(new byte[] { 0, 0, 0, (byte)len }); w.Write(Encoding.ASCII.GetBytes(t)); w.Write(new byte[len]); w.Write(new byte[4]); }
        Chunk("IHDR", 13); Chunk("IEND", 0);
        return ms.ToArray();
    }
    static void WritePack(string path, int version)
    {
        using var w = new BinaryWriter(File.Create(path));
        if (version == 1) { w.Write(Encoding.ASCII.GetBytes("PZPK")); w.Write(1); }
        w.Write(2);
        foreach (var (page, names) in new[] { ("UI_1", new[] { "Item_Hammer", "Item_BaseballBat" }), ("UI_2", new[] { "Item_Bandage" }) })
        {
            WStr(w, page); w.Write(names.Length); w.Write(1);
            int i = 0;
            foreach (var n in names) { WStr(w, n); foreach (var v in new[] { 32 * i, 8, 32, 30, 0, 1, 32, 32 }) w.Write(v); i++; }
            var png = FakePng();
            if (version == 1) { w.Write(png.Length); w.Write(png); }
            else { w.Write(png); w.Write(unchecked((int)0xDEADBEEF)); }
        }
    }

    static int Main()
    {

        var tmp = Path.Combine(Path.GetTempPath(), "zombheim-probe-" + Guid.NewGuid().ToString("N"));

        foreach (int version in new[] { 0, 1 })
        {
            var p = Path.Combine(tmp, $"v{version}.pack"); Directory.CreateDirectory(tmp); WritePack(p, version);
            var hits = PzPack.Find(p, new[] { "Item_BaseballBat", "Item_Bandage" });
            Expect(hits.Count == 2, $"pack v{version}: finds both entries across pages");
            var bat = hits.FirstOrDefault(h => h.Name == "Item_BaseballBat");
            Expect(bat != null && bat.X == 32 && bat.Y == 8 && bat.W == 32 && bat.H == 30 && bat.PagePng?.Length == FakePng().Length, $"pack v{version}: bat region and page PNG");
        }

        // Build 41 layout: DisplayName in the script.
        var b41 = Path.Combine(tmp, "b41");
        Directory.CreateDirectory(Path.Combine(b41, "media/scripts"));
        Directory.CreateDirectory(Path.Combine(b41, "media/lua/shared/Translate/EN"));
        File.WriteAllText(Path.Combine(b41, "media/scripts/items_weapons.txt"),
            "module Base\n{\n  imports { Base }\n  /* item Fake { } */\n  item BaseballBat\n  {\n    DisplayCategory = Weapon,\n    Type = Weapon,\n    DisplayName = Baseball Bat,\n    MinDamage = 0.8,\n    MaxDamage = 1.1,\n    Weight = 1.5,\n    Icon = BaseballBat,\n    ConditionMax = 13,\n  }\n  item Hammer { DisplayName = Hammer, Weight = 1, }\n}\n");
        File.WriteAllText(Path.Combine(b41, "media/scripts/items_food.txt"),
            "module Base {\n\titem TinnedBeans\n\t{\n\t\tDisplayName\t=\tBeans,\n\t\tHungerChange = -30,\n\t\tCalories = 600,\n\t\tWeight = 0.4,\n\t}\n}\n");
        File.WriteAllText(Path.Combine(b41, "media/lua/shared/Translate/EN/UI_EN.txt"),
            "UI_EN = {\n  UI_Intro3 = \"This is how you died.\",\n}\n");
        PzContent.Items.Clear();
        PzContent.Load(b41, new[] { "Base.BaseballBat", "Base.TinnedBeans", "Base.Bandage" });
        var it = Item("Base.BaseballBat");
        Expect(it != null && it.DisplayName == "Baseball Bat" && Math.Abs(it.Num("MaxDamage", 0) - 1.1f) < 1e-4, "B41: bat name and MaxDamage");
        Expect(Item("Base.TinnedBeans")?.Num("HungerChange", 0) == -30f, "B41: beans HungerChange with tabs");
        Expect(!PzContent.Items.ContainsKey("Base.Bandage"), "B41: missing item is reported missing, not invented");
        Expect(PzContent.IntroLine == "This is how you died." && PzContent.IntroSource == "UI_EN.txt", "B41: intro line read from the player's translation file");

        // Build 42 layout: names only in ItemName.json, scripts in subfolders.
        var b42 = Path.Combine(tmp, "b42");
        Directory.CreateDirectory(Path.Combine(b42, "media/scripts/generated/items"));
        Directory.CreateDirectory(Path.Combine(b42, "media/lua/shared/Translate/EN"));
        File.WriteAllText(Path.Combine(b42, "media/scripts/generated/items/medical.txt"),
            "module Base\n{\n    item Bandage\n    {\n        ItemType = base:normal,\n        Weight = 0.1,\n        CanBandage = true,\n        Icon = Bandage,\n    }\n}\n");
        File.WriteAllText(Path.Combine(b42, "media/lua/shared/Translate/EN/ItemName.json"), "{\n  \"Base.Bandage\": \"Bandage\",\n  \"Base.Hammer\": \"Hammer\"\n}\n");
        PzContent.Items.Clear();
        PzContent.Load(b42, new[] { "Base.Bandage" });
        Expect(Item("Base.Bandage")?.DisplayName == "Bandage", "B42: bandage name from ItemName.json");

        Directory.Delete(tmp, true);
        Console.WriteLine(fails == 0 ? "all parser checks passed" : $"{fails} parser checks failed");
        return fails == 0 ? 0 : 1;
    }
}
