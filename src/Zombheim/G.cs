using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Zombheim
{
    /// <summary>Small, named wrappers over the Valheim calls the systems share.</summary>
    internal static class G
    {
        public static Component LocalPlayer => V.Get(V.T("Player"), "m_localPlayer") as Component;

        public static IEnumerable<Component> Players()
        {
            if (V.Call(V.T("Player"), "GetAllPlayers") is System.Collections.IEnumerable e)
                foreach (var p in e) if (p is Component c && c != null) yield return c;
        }

        public static IEnumerable<Component> Characters()
        {
            if (V.Call(V.T("Character"), "GetAllCharacters") is System.Collections.IEnumerable e)
                foreach (var p in e) if (p is Component c && c != null) yield return c;
        }

        public static bool InWorld => V.Get(V.T("ZNet"), "instance") != null && LocalPlayer != null;

        public static bool IsNight()
        {
            var env = V.T("EnvMan");
            if (env == null) return false;
            var inst = V.Get(env, "instance");
            if (inst == null) return false;
            // IsNight became static in later Valheim versions; try both.
            var m = env.GetMethod("IsNight", System.Type.EmptyTypes);
            if (m == null) return false;
            return (bool)m.Invoke(m.IsStatic ? null : inst, null);
        }

        public static void Message(string text, bool center = false)
        {
            var hud = V.Get(V.T("MessageHud"), "instance");
            if (hud != null) V.Call(hud, "ShowMessage", center ? "Center" : "TopLeft", text);
            Z.Log.LogInfo("[msg] " + text);
            MeltyLink.Note("message: " + text);
        }

        public static bool IsDead(Component ch) => ch == null || V.Call(ch, "IsDead") is bool b && b;
        public static float Health(Component ch) => V.Call(ch, "GetHealth") is float f ? f : 0f;

        public static void Hurt(Component ch, float amount)
        {
            if (ch == null || amount <= 0 || IsDead(ch)) return;
            var hit = V.New("HitData");
            if (hit == null) return;
            V.SetIn(hit, "m_damage", "m_damage", amount);
            V.Set(hit, "m_point", ch.transform.position);
            V.Call(ch, "Damage", hit);
        }

        public static void Heal(Component ch, float amount) => V.Call(ch, "Heal", amount, true);

        public static bool IsOwner(Component c)
        {
            var nv = c.GetComponent(V.T("ZNetView"));
            return nv != null && V.Call(nv, "IsOwner") is bool b && b;
        }

        public static float NoiseRange(Component player)
        {
            if (V.Call(player, "GetNoiseRange") is float f) return f;
            var body = player.GetComponent<Rigidbody>();
            float speed = body != null ? body.velocity.magnitude : 0f;
            return speed > 5f ? 30f : speed > 1f ? 15f : 5f;
        }

        public static Vector3? GroundPointNear(Vector3 center, float rMin, float rMax)
        {
            var zs = V.Get(V.T("ZoneSystem"), "instance");
            float water = V.Get<float>(zs, "m_waterLevel", 30f);
            for (int i = 0; i < 12; i++)
            {
                float a = Random.Range(0f, Mathf.PI * 2f), r = Random.Range(rMin, rMax);
                var p = center + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
                if (!(V.Call(zs, "GetGroundHeight", p) is float y) || y < water + 0.5f) continue;
                p.y = y + 0.2f;
                return p;
            }
            return null;
        }

        public static GameObject Spawn(GameObject prefab, Vector3 pos)
        {
            if (prefab == null) return null;
            return Object.Instantiate(prefab, pos, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
        }

        public static GameObject GamePrefab(string name)
        {
            var zns = V.Get(V.T("ZNetScene"), "instance");
            return V.Call(zns, "GetPrefab", name) as GameObject;
        }

        public static int CountNear(Vector3 at, float radius, string enemyId = "shambler")
            => Characters().Count(c => Content.IsEnemy(c, enemyId) && !IsDead(c) && Vector3.Distance(c.transform.position, at) < radius);

        // Per-character saved flags (Player.m_customData), with a memory fallback.
        static readonly Dictionary<string, string> memory = new Dictionary<string, string>();

        public static string GetFlag(Component player, string key)
        {
            if (V.Get(player, "m_customData") is Dictionary<string, string> d) return d.TryGetValue(key, out var v) ? v : null;
            return memory.TryGetValue(key, out var m) ? m : null;
        }

        public static void SetFlag(Component player, string key, string value)
        {
            if (V.Get(player, "m_customData") is Dictionary<string, string> d)
            {
                if (value == null) d.Remove(key); else d[key] = value;
            }
            else if (value == null) memory.Remove(key);
            else memory[key] = value;
        }

        public static string WorldId()
        {
            var znet = V.Get(V.T("ZNet"), "instance");
            return V.Call(znet, "GetWorldUID")?.ToString() ?? "world";
        }

        public static string ItemPrefabName(object itemData) => (V.Get(itemData, "m_dropPrefab") as GameObject)?.name;
    }
}
