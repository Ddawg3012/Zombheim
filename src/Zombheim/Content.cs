using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Zombheim
{
    /// <summary>
    /// Registers the Zomboid items and the Shambler as real Valheim prefabs (system "content"),
    /// so they save, sync to other players and drop like any other object.
    /// Each items/enemies sheet row becomes one cloned prefab.
    /// </summary>
    internal static class Content
    {
        static GameObject holder;
        public static readonly Dictionary<string, GameObject> ItemPrefabs = new Dictionary<string, GameObject>(); // by items.id
        public static readonly Dictionary<string, GameObject> EnemyPrefabs = new Dictionary<string, GameObject>(); // by enemies.id

        static Transform Holder()
        {
            if (holder == null)
            {
                holder = new GameObject("ZombheimPrefabs");
                holder.SetActive(false);
                Object.DontDestroyOnLoad(holder);
            }
            return holder.transform;
        }

        static GameObject Clone(GameObject src, string name)
        {
            var c = Object.Instantiate(src, Holder(), false);
            c.name = name;
            return c;
        }

        static GameObject FindIn(IList list, string name)
        {
            if (list == null) return null;
            foreach (var o in list) if (o is GameObject g && g != null && g.name == name) return g;
            return null;
        }

        // ---------------- hooks (sheets/hooks.json) ----------------

        public static void OnObjectDBAwake(object __instance)
        {
            var items = V.List(__instance, "m_items");
            BuildItems(items);
            AddItems(items);
        }

        public static void OnObjectDBCopy(object __instance)
        {
            var items = V.List(__instance, "m_items");
            BuildItems(items);
            if (AddItems(items) > 0) V.Call(__instance, "UpdateRegisters");
        }

        public static void OnZNetSceneAwake(object __instance)
        {
            var prefabs = V.List(__instance, "m_prefabs");
            if (prefabs == null) return;
            BuildItems(prefabs);
            BuildEnemies(prefabs);
            foreach (var g in ItemPrefabs.Values.Concat(EnemyPrefabs.Values))
                if (FindIn(prefabs, g.name) == null) prefabs.Add(g);
        }

        static int AddItems(IList items)
        {
            if (items == null) return 0;
            int n = 0;
            foreach (var g in ItemPrefabs.Values)
                if (FindIn(items, g.name) == null) { items.Add(g); n++; }
            return n;
        }

        // ---------------- items ----------------

        static void BuildItems(IList source)
        {
            foreach (var row in Sheets.Items)
            {
                if (ItemPrefabs.ContainsKey(row.Id)) continue;
                if (!PzContent.Items.TryGetValue(row.PzItem, out var pz)) continue; // only items the player's own Zomboid has
                var baseItem = FindIn(source, row.ValheimBase);
                if (baseItem == null) continue;
                var go = Clone(baseItem, row.Prefab);
                var drop = go.GetComponent(V.T("ItemDrop"));
                var data = V.Get(drop, "m_itemData");
                var shared = V.Get(data, "m_shared");
                if (shared == null) { Object.Destroy(go); continue; }

                var name = string.IsNullOrEmpty(pz.DisplayName) ? row.FallbackName : pz.DisplayName;
                V.Set(shared, "m_name", name);
                V.Set(shared, "m_description", Describe(row, pz));
                V.Set(shared, "m_weight", pz.Num("Weight", V.Get<float>(shared, "m_weight", 1f)) * row.WeightScale);
                if (pz.Icon is Sprite icon) V.Set(shared, "m_icons", new[] { icon });

                switch (row.Kind)
                {
                    case "weapon":
                        float avg = (pz.Num("MinDamage", 0.6f) + pz.Num("MaxDamage", 1.0f)) / 2f;
                        float dmg = Mathf.Round(avg * row.DamageScale);
                        V.SetIn(shared, "m_damages", "m_blunt", dmg);
                        float cond = pz.Num("ConditionMax", 0f);
                        if (cond > 0) V.Set(shared, "m_maxDurability", cond * 10f);
                        break;
                    case "food":
                        float hunger = Mathf.Abs(pz.Num("HungerChange", -20f));
                        float calories = pz.Num("Calories", 300f);
                        V.Set(shared, "m_food", Mathf.Round(hunger * row.FoodScale));
                        V.Set(shared, "m_foodStamina", Mathf.Round(calories / 10f * row.FoodScale));
                        break;
                    case "medical":
                        break; // healing comes from the base mead; stopping bleeding is in Wounds
                }
                ItemPrefabs[row.Id] = go;
                Z.Log.LogInfo($"Registered {row.Prefab} as '{name}' ({row.Kind})");
            }
        }

        static string Describe(ItemRow row, PzItem pz)
        {
            var cat = pz.Props.TryGetValue("DisplayCategory", out var c) ? c : row.Kind;
            switch (row.Kind)
            {
                case "weapon": return $"Knox County {cat.ToLowerInvariant()}. Brought from your own Project Zomboid.";
                case "food": return "Still in date, probably. Brought from your own Project Zomboid.";
                default: return "Stops bleeding. Does nothing for a bite. Brought from your own Project Zomboid.";
            }
        }

        // ---------------- enemies ----------------

        static void BuildEnemies(IList source)
        {
            foreach (var row in Sheets.Enemies)
            {
                if (EnemyPrefabs.ContainsKey(row.Id)) continue;
                var baseGo = FindIn(source, row.ValheimBase);
                if (baseGo == null) { V.Warn("prefab " + row.ValheimBase); continue; }
                var go = Clone(baseGo, row.Prefab);
                var ch = go.GetComponent(V.T("Character"));
                V.Set(ch, "m_name", row.DisplayName);
                V.Set(ch, "m_walkSpeed", row.WalkSpeed);
                V.Set(ch, "m_runSpeed", row.RunSpeed);
                V.Set(ch, "m_speed", row.WalkSpeed);
                V.Set(ch, "m_health", V.Get<float>(ch, "m_health", 60f) * row.HealthScale);

                var ai = go.GetComponent(V.T("BaseAI"));
                V.Set(ai, "m_viewRange", row.ViewRange);
                V.Set(ai, "m_hearRange", row.HearRange);

                if (row.MeleeOnly && V.Get(ch, "m_randomWeapon") is GameObject[] weapons)
                {
                    var melee = weapons.Where(w => w != null && !w.name.ToLowerInvariant().Contains("bow")).ToArray();
                    if (melee.Length > 0) V.Set(ch, "m_randomWeapon", melee);
                }

                var cd = go.GetComponent(V.T("CharacterDrop"));
                var drops = V.List(cd, "m_drops");
                if (drops != null)
                    foreach (var id in row.Drops)
                    {
                        var itemRow = Sheets.Items.First(i => i.Id == id);
                        if (!ItemPrefabs.TryGetValue(id, out var prefab)) continue;
                        var d = V.New("CharacterDrop+Drop");
                        if (d == null) break;
                        V.Set(d, "m_prefab", prefab);
                        V.Set(d, "m_amountMin", 1);
                        V.Set(d, "m_amountMax", 1);
                        V.Set(d, "m_chance", itemRow.DropChance);
                        drops.Add(d);
                    }
                EnemyPrefabs[row.Id] = go;
                Z.Log.LogInfo($"Registered {row.Prefab} from {row.ValheimBase}");
            }
        }

        public static bool IsEnemy(Component c, string id = "shambler")
        {
            if (c == null) return false;
            var row = Sheets.Enemies.First(e => e.Id == id);
            return c.gameObject.name.StartsWith(row.Prefab);
        }
    }
}
