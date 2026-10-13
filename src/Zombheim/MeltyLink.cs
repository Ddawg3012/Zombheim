using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEngine;

namespace Zombheim
{
    /// <summary>
    /// Melty's playing-together contract (system "melty"): read join= from melty.properties at start,
    /// write "Melty join address: ..." once this player's world is open to others, and
    /// "Melty joined: ..." once a joiner is in the host's game. Files live in %LOCALAPPDATA%/Zombheim.
    /// Valheim's own Steam networking carries the connection (no port forwarding).
    /// </summary>
    internal static class MeltyLink
    {
        static string dir, log, settings;
        public static string Join = "";
        static bool hostSaid, joinSaid, queued;
        static float next;
        static readonly Queue<string> pending = new Queue<string>();

        public static void Init()
        {
            MeltyTest.Start();
            dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Zombheim");
            settings = Path.Combine(dir, "melty.properties");
            log = Path.Combine(dir, "melty.log");
            try
            {
                Directory.CreateDirectory(dir);
                File.WriteAllText(log, "");
                if (File.Exists(settings))
                    Join = File.ReadAllLines(settings).Select(l => l.Split(new[] { '=' }, 2))
                        .Where(p => p[0].Trim() == "join").Select(p => p.Length > 1 ? p[1].Trim() : "").FirstOrDefault() ?? "";
            }
            catch (Exception e) { Z.Log.LogWarning("melty files: " + e.Message); }
            Z.Log.LogInfo(Join == "" ? "Melty: hosting (no join address)" : "Melty: joining " + Join);
            new Thread(Writer) { IsBackground = true, Name = "Zombheim log" }.Start();
        }

        static void Say(string line) { lock (pending) pending.Enqueue(line); }

        /// <summary>Diagnostic lines go to the BepInEx log only; melty.log holds just the contract lines.</summary>
        public static void Note(string what) => Z.Log.LogDebug("note: " + what);

        // File writes happen off the main thread.
        static void Writer()
        {
            while (true)
            {
                string line = null;
                lock (pending) if (pending.Count > 0) line = pending.Dequeue();
                if (line == null) { Thread.Sleep(200); continue; }
                try { File.AppendAllText(log, line + "\n"); } catch { }
            }
        }

        public static void Tick()
        {
            if (Time.unscaledTime < next) return;
            next = Time.unscaledTime + 1f;
            var znetType = V.T("ZNet");
            var znet = V.Get(znetType, "instance");
            if (Join != "") { TickJoin(znet, znetType); return; }
            if (hostSaid || znet == null || G.LocalPlayer == null) return;
            if (!(V.Call(znet, "IsServer") is bool server) || !server) return;
            if (!(V.Get(znetType, "m_openServer") is bool open) || !open) return; // the player hosts with Valheim's own "Start server"
            var address = HostAddress(znetType);
            if (address == null) return;
            hostSaid = true;
            Say("Melty join address: " + address);
            Z.Log.LogInfo("Melty join address: " + address);
        }

        // Valheim P2P hosts are reached through Steam's relay by the host's SteamID.
        static string HostAddress(Type znetType)
        {
            var backend = V.Get(znetType, "m_onlineBackend")?.ToString() ?? "Steamworks";
            if (backend.IndexOf("PlayFab", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                var code = V.Get(V.T("ZPlayFabMatchmaking"), "JoinCode")?.ToString();
                return string.IsNullOrEmpty(code) ? null : code.ToLowerInvariant() + ".playfab";
            }
            var id = V.Call(V.T("Steamworks.SteamUser"), "GetSteamID");
            var raw = V.Get(id, "m_SteamID")?.ToString();
            return string.IsNullOrEmpty(raw) ? null : raw + ".steamuser";
        }


        static void TickJoin(object znet, Type znetType)
        {
            if (joinSaid) return;
            if (znet == null)
            {
                if (!queued) queued = QueueJoin();
                return;
            }
            var status = V.Call(znetType, "GetConnectionStatus")?.ToString() ?? "";
            if (status.StartsWith("Error"))
            {
                joinSaid = true;
                Say("Melty join failed: " + status);
                return;
            }
            if (G.LocalPlayer != null && V.Call(znet, "IsServer") is bool server && !server)
            {
                joinSaid = true;
                Say("Melty joined: " + Join);
                Z.Log.LogInfo("Melty joined: " + Join);
            }
        }

        // Queue the host as the server to join; Valheim connects once the player picks a character.
        static bool QueueJoin()
        {
            var fs = V.Get(V.T("FejdStartup"), "instance");
            if (fs == null) return false;
            object data = null;
            if (Join.EndsWith(".steamuser") && ulong.TryParse(Join.Substring(0, Join.Length - 10), out var sid))
            {
                var csid = Activator.CreateInstance(V.T("Steamworks.CSteamID"), sid);
                data = V.New("ServerJoinDataSteamUser", csid);
            }
            else if (Join.EndsWith(".playfab"))
            {
                Say("Melty join failed: crossplay hosts share a join code; enter " + Join.Substring(0, Join.Length - 8).ToUpperInvariant() + " under Join game");
                joinSaid = true;
                return true;
            }
            else data = V.New("ServerJoinDataDedicated", Join);
            if (data == null) { Say("Melty join failed: this Valheim version has no join data type for " + Join); joinSaid = true; return true; }

            var field = fs.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                .FirstOrDefault(f => f.FieldType.IsAssignableFrom(data.GetType()) && f.Name.IndexOf("queued", StringComparison.OrdinalIgnoreCase) >= 0)
                ?? fs.GetType().GetFields(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic)
                .FirstOrDefault(f => f.FieldType.IsAssignableFrom(data.GetType()) && f.Name.IndexOf("join", StringComparison.OrdinalIgnoreCase) >= 0);
            if (field == null) { Say("Melty join failed: couldn't queue the join in this Valheim version"); joinSaid = true; return true; }
            field.SetValue(field.IsStatic ? null : fs, data);
            Z.Log.LogInfo($"Melty: queued join to {Join} via FejdStartup.{field.Name}; pick a character to connect");
            G.Message("Pick a character: you'll join your friend's game.", center: true);
            return true;
        }
    }

    /// <summary>
    /// Melty test runs: while %LOCALAPPDATA%/Melty/test/zombheim.json exists, seed the random rolls, go straight to the
    /// mashup's main features and report each check as "pass name" / "fail name: why" to the file it names.
    /// </summary>
    internal static class TestMode
    {
        public static bool Active { get; private set; }
        static string checksFile;
        static readonly HashSet<string> reported = new HashSet<string>();
        static float startedInWorld = -1f, next;
        static bool bitten;

        public static void Init()
        {
            MeltyTest.Start();
            var cfg = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Melty", "test", "zombheim.json");
            if (!File.Exists(cfg)) return;
            try
            {
                var text = File.ReadAllText(cfg);
                checksFile = Field(text, "checks");
                if (int.TryParse(Field(text, "seed"), out var seed)) UnityEngine.Random.InitState(seed);
                Active = !string.IsNullOrEmpty(checksFile);
                Z.Log.LogInfo("Melty test mode, checks -> " + checksFile);
            }
            catch (Exception e) { Z.Log.LogWarning("test config: " + e.Message); }
        }

        static string Field(string json, string key)
        {
            var m = System.Text.RegularExpressions.Regex.Match(json, "\"" + key + "\"\\s*:\\s*(\"((?:[^\"\\\\]|\\\\.)*)\"|-?\\d+)");
            if (!m.Success) return null;
            return m.Groups[2].Success ? System.Text.RegularExpressions.Regex.Unescape(m.Groups[2].Value) : m.Groups[1].Value;
        }

        static void Report(string name, bool ok, string why = null)
        {
            if (!Active || !reported.Add(name)) return;
            Z.Log.LogInfo("[test] " + (ok ? "pass " : "fail ") + name + (ok ? "" : ": " + why));
            MeltyTest.Report(name, ok, why);
        }

        public static void Tick()
        {
            if (!Active || Time.unscaledTime < next) return;
            next = Time.unscaledTime + 1f;
            if (PzContent.Folder == null) Report("zomboid-items", false, "Project Zomboid folder not found");
            else
            {
                var missing = Sheets.Items.Where(i => !PzContent.Items.ContainsKey(i.PzItem)).Select(i => i.PzItem).ToList();
                Report("zomboid-items", missing.Count == 0, "not in the Zomboid scripts: " + string.Join(", ", missing));
            }
            AutoStart.Tick();
            var me = G.LocalPlayer;
            if (me == null) return;
            if (startedInWorld < 0) startedInWorld = Time.time;
            Report("in-world", true);
            if (Content.ItemPrefabs.Count == Sheets.Items.Length) Report("items-registered", true);
            if (StartSystem.CrateSpawned) Report("start-crate", true);
            if (StartSystem.StragglersSpawned && G.CountNear(me.transform.position, 120f) > 0) Report("shamblers", true);
            if (!bitten && Time.time > startedInWorld + 20f)
            {
                bitten = true;
                Wounds.Inflict(Sheets.Wounds.First(w => w.Id == "bite"));
            }
            if (bitten) Report("infection", Infection.Infected, "a bite didn't infect");
            if (Time.time > startedInWorld + 90f)
            {
                if (Content.ItemPrefabs.Count != Sheets.Items.Length) Report("items-registered", false, $"{Content.ItemPrefabs.Count} of {Sheets.Items.Length} Zomboid items registered");
                if (!StartSystem.CrateSpawned) Report("start-crate", false, "no crate after 90 s");
                if (!StartSystem.StragglersSpawned) Report("shamblers", false, "no shamblers spawned after 90 s");
            }
        }
    }

    /// <summary>Test runs only: take the first character and a "Zombheim Test" world straight from the main menu.</summary>
    internal static class AutoStart
    {
        static bool tried;

        public static void Tick()
        {
            if (tried) return;
            var fs = V.Get(V.T("FejdStartup"), "instance");
            if (fs == null || V.Get(V.T("ZNet"), "instance") != null) return;
            if (Time.unscaledTime < 8f) return; // let the menu finish loading
            tried = true;
            try
            {
                var profiles = V.List(fs, "m_profiles");
                if (profiles == null || profiles.Count == 0) { Z.Log.LogWarning("test: no character to start with"); return; }
                var profile = profiles[0];
                V.Call(V.T("Game"), "SetProfile", V.Call(profile, "GetFilename"), V.Get(profile, "m_fileSource"));
                var fileSource = V.Get(profile, "m_fileSource");
                var world = V.Call(V.T("World"), "GetCreateWorld", "ZombheimTest", fileSource);
                if (world == null) { Z.Log.LogWarning("test: couldn't create the test world"); return; }
                V.Set(fs, "m_world", world);
                V.Call(V.T("ZNet"), "SetServer", true, false, false, V.Get(world, "m_name"), "", world);
                V.Call(fs, "LoadMainScene");
                Z.Log.LogInfo("test: starting ZombheimTest with " + V.Call(profile, "GetName"));
            }
            catch (Exception e) { Z.Log.LogWarning("test autostart: " + e.Message); }
        }
    }
}
