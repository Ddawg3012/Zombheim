using System.Linq;
using UnityEngine;

namespace Zombheim
{
    /// <summary>System "start": This is how you died. A looted start with a crate of Zomboid supplies, then stragglers.</summary>
    internal static class StartSystem
    {
        static float crateAt = -1f, stragglersAt = -1f;
        public static bool CrateSpawned, StragglersSpawned;

        public static void OnPlayerSpawned(object __instance)
        {
            var me = G.LocalPlayer;
            if (me == null || !ReferenceEquals(__instance, me)) return;
            var key = "zombheim_start_" + G.WorldId();
            if (G.GetFlag(me, key) != null && !TestMode.Active) return;
            G.SetFlag(me, key, "1");
            crateAt = Time.time + 3f;
        }

        public static void Tick()
        {
            var me = G.LocalPlayer;
            if (me == null) return;
            if (crateAt > 0 && Time.time >= crateAt)
            {
                crateAt = -1f;
                G.Message(PzContent.IntroLine, center: true);
                SpawnCrate(me);
                stragglersAt = Time.time + (TestMode.Active ? 5f : Tuning.StragglerDelaySeconds);
            }
            if (stragglersAt > 0 && Time.time >= stragglersAt)
            {
                stragglersAt = -1f;
                int n = Horde.SpawnAround(me, (int)Tuning.StragglerCount, Tuning.StragglerRadius, Tuning.StragglerRadius + 10f, "shambler");
                if (n > 0) { G.Message("Something is shuffling out of the treeline."); StragglersSpawned = true; }
            }
        }

        static void SpawnCrate(Component me)
        {
            var t = me.transform;
            var pos = t.position + t.forward * Tuning.CrateDistance;
            var zs = V.Get(V.T("ZoneSystem"), "instance");
            if (V.Call(zs, "GetGroundHeight", pos) is float y) pos.y = y;
            var chest = G.GamePrefab("piece_chest_wood");
            var crate = chest != null ? Object.Instantiate(chest, pos, Quaternion.LookRotation(-t.forward)) : null;
            var inv = crate != null ? V.Call(crate.GetComponent(V.T("Container")), "GetInventory") : null;
            int placed = 0;
            foreach (var row in Sheets.Items)
            {
                if (!Content.ItemPrefabs.TryGetValue(row.Id, out var prefab)) continue;
                bool added = inv != null && V.Call(inv, "AddItem", prefab, (int)row.CrateAmount) is bool ok && ok;
                if (!added)
                    for (int i = 0; i < (int)row.CrateAmount; i++)
                        Object.Instantiate(prefab, pos + Vector3.up * (1f + i * 0.3f), Quaternion.identity);
                placed++;
            }
            CrateSpawned = crate != null || placed > 0;
            if (placed > 0) G.Message("An abandoned crate. Someone left in a hurry.");
            else G.Message("Your crate is empty: Project Zomboid wasn't found on this PC.");
        }
    }

    /// <summary>System "noise": shamblers hear players from their noise range times a multiplier and go after them.</summary>
    internal static class Noise
    {
        static float next;
        public static int Heard;

        public static void Tick()
        {
            if (Time.time < next) return;
            next = Time.time + Tuning.NoiseTickSeconds;
            var players = G.Players().Where(p => !G.IsDead(p)).ToList();
            if (players.Count == 0) return;
            foreach (var z in G.Characters())
            {
                if (!Content.IsEnemy(z) || G.IsDead(z) || !G.IsOwner(z)) continue;
                var ai = z.GetComponent(V.T("MonsterAI"));
                if (ai == null || V.Get(ai, "m_targetCreature") != null) continue;
                Component loudest = null; float best = float.MaxValue;
                foreach (var p in players)
                {
                    float d = Vector3.Distance(p.transform.position, z.transform.position);
                    if (d <= G.NoiseRange(p) * Tuning.NoiseHearingMultiplier && d < best) { best = d; loudest = p; }
                }
                if (loudest == null) continue;
                if (V.Has(ai, "SetTarget")) V.Call(ai, "SetTarget", loudest);
                else V.Set(ai, "m_targetCreature", loudest);
                V.Call(ai, "SetAlerted", true);
                Heard++;
            }
        }
    }

    /// <summary>System "horde": waves of shamblers after nightfall, spawned by each player's game around that player.</summary>
    internal static class Horde
    {
        static bool wasNight;
        static float nightStart;
        static readonly bool[] done = new bool[Sheets.Waves.Length];
        static float next;
        public static int Spawned;

        public static void Tick()
        {
            if (Time.time < next) return;
            next = Time.time + 2f;
            var me = G.LocalPlayer;
            if (me == null || G.IsDead(me)) return;
            bool night = G.IsNight();
            if (night && !wasNight)
            {
                nightStart = Time.time;
                for (int i = 0; i < done.Length; i++) done[i] = false;
            }
            wasNight = night;
            if (!night) return;
            for (int i = 0; i < Sheets.Waves.Length; i++)
            {
                var w = Sheets.Waves[i];
                if (done[i] || Time.time < nightStart + w.DelaySeconds) continue;
                done[i] = true;
                int n = SpawnAround(me, (int)w.Count, w.RadiusMin, w.RadiusMax, w.Enemy);
                if (n > 0 && i == 0) G.Message("The dead are out tonight. Keep quiet.");
            }
        }

        /// <summary>Spawn up to count enemies around a player, respecting the nearby cap; they drift toward the player.</summary>
        public static int SpawnAround(Component player, int count, float rMin, float rMax, string enemyId)
        {
            if (!Content.EnemyPrefabs.TryGetValue(enemyId, out var prefab)) return 0;
            var at = player.transform.position;
            int room = (int)Tuning.MaxShamblersNearby - G.CountNear(at, Tuning.NearbyRadius, enemyId);
            int n = 0;
            for (int i = 0; i < Mathf.Min(count, room); i++)
            {
                var p = G.GroundPointNear(at, rMin, rMax);
                if (p == null) continue;
                var go = G.Spawn(prefab, p.Value);
                var ai = go != null ? go.GetComponent(V.T("BaseAI")) : null;
                if (ai != null && V.Has(ai, "SetPatrolPoint")) V.Call(ai, "SetPatrolPoint", at);
                n++;
            }
            Spawned += n;
            return n;
        }
    }

    /// <summary>System "wounds": scratches, lacerations and bites from shambler hits; bleeding until bandaged.</summary>
    internal static class Wounds
    {
        static float bleedUntil = -1f, nextBleed;
        static object bandageItem; static int bandageStack;
        public static string LastWound;

        public static bool Bleeding => bleedUntil > Time.time;

        public static void OnApplyDamage(object __instance, object[] __args)
        {
            var me = G.LocalPlayer;
            if (me == null || !ReferenceEquals(__instance, me) || __args == null || __args.Length == 0) return;
            var hit = __args[0];
            var attacker = V.Call(hit, "GetAttacker") as Component;
            if (!Content.IsEnemy(attacker)) return;
            if (V.Call(hit, "GetTotalDamage") is float dmg && dmg < 1f) return; // fully blocked
            Inflict(RollWound());
        }

        static WoundRow RollWound()
        {
            float r = Random.value, acc = 0f;
            foreach (var w in Sheets.Wounds) { acc += w.Chance; if (r <= acc) return w; }
            return Sheets.Wounds[Sheets.Wounds.Length - 1];
        }

        public static void Inflict(WoundRow w)
        {
            LastWound = w.Id;
            G.Message(w.Label + "!");
            if (w.Bleeds) bleedUntil = Time.time + Tuning.BleedMaxSeconds;
            if (Random.value < w.InfectChance) Infection.Infect();
        }

        public static void BeforeConsume(object __instance, object[] __args)
        {
            bandageItem = null;
            if (!ReferenceEquals(__instance, G.LocalPlayer) || __args == null) return;
            var bandage = Sheets.Items.First(i => i.Id == "bandage").Prefab;
            var item = __args.FirstOrDefault(a => a != null && a.GetType().Name == "ItemData");
            if (item == null || G.ItemPrefabName(item) != bandage) return;
            bandageItem = item;
            bandageStack = V.Get<int>(item, "m_stack", 1);
        }

        public static void AfterConsume(object __instance, object[] __args)
        {
            if (bandageItem == null) return;
            bool used = V.Get<int>(bandageItem, "m_stack", 0) < bandageStack;
            bandageItem = null;
            if (!used) return;
            bleedUntil = -1f;
            G.Heal(G.LocalPlayer, Tuning.BandageHeal);
            G.Message(Infection.Infected ? "Bandaged. The bleeding stops, but the wound burns." : "Bandaged. The bleeding stops.");
        }

        public static void Tick()
        {
            if (!Bleeding || Time.time < nextBleed) return;
            nextBleed = Time.time + 1f;
            G.Hurt(G.LocalPlayer, Tuning.BleedDrainPerSecond);
        }

        public static void Clear() => bleedUntil = -1f;
    }

    /// <summary>System "infection": the Knox infection. Silent, incurable, and you rise when you die.</summary>
    internal static class Infection
    {
        const string Key = "zombheim_infection_seconds";
        static float next;
        static int stage = -1;
        static Vector3? riseAt; static float riseTime;

        public static bool Infected => G.LocalPlayer != null && G.GetFlag(G.LocalPlayer, Key) != null;
        public static int Stage => stage;

        public static void Infect()
        {
            var me = G.LocalPlayer;
            if (me == null || Infected) return;
            G.SetFlag(me, Key, "0");
            stage = 0;
            Z.Log.LogInfo("Knox infection contracted");
            MeltyLink.Note("infected");
        }

        public static void OnPlayerDeath(object __instance)
        {
            if (!ReferenceEquals(__instance, G.LocalPlayer)) return;
            Wounds.Clear();
            if (!Infected) return;
            riseAt = ((Component)__instance).transform.position;
            riseTime = Time.time + Tuning.RiseDelaySeconds;
            G.SetFlag(G.LocalPlayer, Key, null);
            stage = -1;
        }

        public static void Tick()
        {
            if (riseAt.HasValue && Time.time >= riseTime && Content.EnemyPrefabs.TryGetValue("shambler", out var prefab))
            {
                G.Spawn(prefab, riseAt.Value);
                riseAt = null;
                G.Message("Where you fell, something got back up.");
            }
            var me = G.LocalPlayer;
            if (me == null || G.IsDead(me) || Time.time < next) return;
            next = Time.time + 1f;
            var s = G.GetFlag(me, Key);
            if (s == null) { stage = -1; return; }
            float secs = float.Parse(s, System.Globalization.CultureInfo.InvariantCulture) + 1f;
            G.SetFlag(me, Key, secs.ToString(System.Globalization.CultureInfo.InvariantCulture));
            int now = 0;
            for (int i = 0; i < Sheets.Infection.Length; i++) if (secs >= Sheets.Infection[i].AfterSeconds) now = i;
            var row = Sheets.Infection[now];
            if (now != stage)
            {
                stage = now;
                if (row.Message != "-") G.Message(row.Message);
            }
            G.Hurt(me, row.DrainPerSecond);
        }
    }
}
