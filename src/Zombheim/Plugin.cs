using System;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;

namespace Zombheim
{
    [BepInPlugin(Guid, "Zombheim", Version)]
    public class Plugin : BaseUnityPlugin
    {
        public const string Guid = "gg.melty.zombheim";
        public const string Version = "0.1.0";
        internal static ManualLogSource Log => Z.Log;
        ConfigEntry<string> pzFolder;

        void Awake()
        {
            pzFolder = Config.Bind("General", "ProjectZomboidFolder", "",
                "Your Project Zomboid install folder. Melty fills this in at launch; set it only when running without Melty.");

            var folder = PzContent.FindFolder(pzFolder.Value);
            if (folder == null)
                Log.LogWarning("Project Zomboid not found: Zombheim's crates will be empty. Install Project Zomboid, or set ProjectZomboidFolder in BepInEx/config/" + Guid + ".cfg");
            else if (pzFolder.Value != folder) pzFolder.Value = folder;
            PzContent.Load(folder, Array.ConvertAll(Sheets.Items, i => i.PzItem));
            Log.LogInfo($"Intro line from {PzContent.IntroSource}: {PzContent.IntroLine}");

            MeltyLink.Init();
            TestMode.Init();
            ApplyHooks(new Harmony(Guid));
        }

        /// <summary>Patch every row of sheets/hooks.json, resolving Valheim's types and methods by name.</summary>
        static void ApplyHooks(Harmony harmony)
        {
            foreach (var h in Sheets.Hooks)
            {
                try
                {
                    var type = V.T(h.Type);
                    var target = type == null ? null : AccessTools.Method(type, h.Method);
                    var parts = h.Handler.Split('.');
                    var handler = typeof(Plugin).Assembly.GetType("Zombheim." + parts[0])?.GetMethod(parts[1], BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                    if (target == null || handler == null) { Log.LogWarning($"hook {h.Id}: {h.Type}.{h.Method} not found, skipped"); continue; }
                    var hm = new HarmonyMethod(handler);
                    if (h.Kind == "prefix") harmony.Patch(target, prefix: hm); else harmony.Patch(target, postfix: hm);
                    Log.LogInfo($"hook {h.Id}: {h.Kind} on {h.Type}.{h.Method}");
                }
                catch (Exception e) { Log.LogWarning($"hook {h.Id}: {e.Message}"); }
            }
        }

        void Update()
        {
            Run(MeltyLink.Tick);
            Run(TestMode.Tick);
            if (!G.InWorld) return;
            Run(StartSystem.Tick);
            Run(Noise.Tick);
            Run(Horde.Tick);
            Run(Wounds.Tick);
            Run(Infection.Tick);
        }

        static readonly System.Collections.Generic.HashSet<string> failed = new System.Collections.Generic.HashSet<string>();

        static void Run(Action tick)
        {
            try { tick(); }
            catch (Exception e)
            {
                if (failed.Add(tick.Method.DeclaringType?.Name)) Log.LogError($"{tick.Method.DeclaringType?.Name}: {e}");
            }
        }
    }
}
