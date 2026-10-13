using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;

namespace Zombheim
{
    /// <summary>
    /// Reaches Valheim's own code by name, with no compile-time reference to assembly_valheim.
    /// A member that moved in a game update is logged once and returns null instead of crashing the game.
    /// Pass a Type as the target for static members.
    /// </summary>
    internal static class V
    {
        const BindingFlags All = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.FlattenHierarchy;
        static readonly Dictionary<string, Type> types = new Dictionary<string, Type>();
        static readonly HashSet<string> warned = new HashSet<string>();

        public static Type T(string name)
        {
            if (types.TryGetValue(name, out var t)) return t;
            t = AccessTools.TypeByName(name);
            if (t == null) Warn("type " + name);
            return types[name] = t;
        }

        public static void Warn(string what)
        {
            if (warned.Add(what)) Z.Log.LogWarning("Valheim member not found (skipped): " + what);
        }

        static Type TypeOf(object target, out object instance)
        {
            if (target is Type ty) { instance = null; return ty; }
            instance = target;
            return target?.GetType();
        }

        static MemberInfo Member(Type t, string name)
        {
            for (var cur = t; cur != null; cur = cur.BaseType)
            {
                var f = cur.GetField(name, All | BindingFlags.DeclaredOnly);
                if (f != null) return f;
                var p = cur.GetProperty(name, All | BindingFlags.DeclaredOnly);
                if (p != null) return p;
            }
            return null;
        }

        public static object Get(object target, string name)
        {
            if (target == null) return null;
            var t = TypeOf(target, out var inst);
            switch (Member(t, name))
            {
                case FieldInfo f: return f.GetValue(inst);
                case PropertyInfo p: return p.GetValue(inst, null);
            }
            Warn(t.Name + "." + name);
            return null;
        }

        public static TR Get<TR>(object target, string name, TR fallback = default)
        {
            var v = Get(target, name);
            if (v == null) return fallback;
            try { return (TR)Convert(v, typeof(TR)); } catch { return fallback; }
        }

        public static bool Set(object target, string name, object value)
        {
            if (target == null) return false;
            var t = TypeOf(target, out var inst);
            try
            {
                switch (Member(t, name))
                {
                    case FieldInfo f: f.SetValue(inst, Convert(value, f.FieldType)); return true;
                    case PropertyInfo p when p.CanWrite: p.SetValue(inst, Convert(value, p.PropertyType), null); return true;
                }
            }
            catch (Exception e) { Z.Log.LogWarning($"set {t.Name}.{name}: {e.Message}"); return false; }
            Warn(t.Name + "." + name);
            return false;
        }

        /// <summary>Set a field inside a struct-valued field (e.g. SharedData.m_damages.m_blunt).</summary>
        public static bool SetIn(object target, string structField, string field, object value)
        {
            var box = Get(target, structField);
            if (box == null) return false;
            return Set(box, field, value) && Set(target, structField, box);
        }

        public static bool Has(object target, string method)
        {
            var t = TypeOf(target, out _);
            return t != null && t.GetMethods(All).Any(m => m.Name == method);
        }

        /// <summary>Call the best overload of a method: extra parameters take their defaults.</summary>
        public static object Call(object target, string name, params object[] args)
        {
            if (target == null) return null;
            var t = TypeOf(target, out var inst);
            args = args ?? new object[0];
            MethodInfo best = null; object[] bestArgs = null; int bestExtra = int.MaxValue;
            for (var cur = t; cur != null; cur = cur.BaseType)
            {
                foreach (var m in cur.GetMethods(All | BindingFlags.DeclaredOnly))
                {
                    if (m.Name != name || m.IsGenericMethodDefinition) continue;
                    if (inst == null && !m.IsStatic) continue;
                    var ps = m.GetParameters();
                    if (ps.Length < args.Length) continue;
                    var call = new object[ps.Length];
                    bool ok = true;
                    for (int i = 0; i < ps.Length && ok; i++)
                    {
                        var pt = ps[i].ParameterType.IsByRef ? ps[i].ParameterType.GetElementType() : ps[i].ParameterType;
                        if (i < args.Length)
                        {
                            if (!Fits(args[i], pt)) ok = false; else call[i] = Convert(args[i], pt);
                        }
                        else if (ps[i].HasDefaultValue) call[i] = ps[i].DefaultValue;
                        else ok = false;
                    }
                    if (!ok) continue;
                    int extra = ps.Length - args.Length;
                    if (extra < bestExtra) { best = m; bestArgs = call; bestExtra = extra; }
                }
                if (best != null) break;
            }
            if (best == null) { Warn(t.Name + "." + name + "(" + args.Length + " args)"); return null; }
            try { return best.Invoke(best.IsStatic ? null : inst, bestArgs); }
            catch (TargetInvocationException e) { Z.Log.LogWarning($"{t.Name}.{name}: {e.InnerException?.Message}"); return null; }
        }

        public static object New(string typeName, params object[] args)
        {
            var t = T(typeName);
            if (t == null) return null;
            try { return Activator.CreateInstance(t, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, null, args, null); }
            catch (Exception e) { Z.Log.LogWarning("new " + typeName + ": " + e.Message); return null; }
        }

        static bool Fits(object v, Type pt)
        {
            if (v == null) return !pt.IsValueType;
            var vt = v.GetType();
            if (pt.IsAssignableFrom(vt)) return true;
            if (pt.IsEnum) return v is string || v is int;
            if (pt.IsPrimitive && vt.IsPrimitive) return true;
            return false;
        }

        public static object Convert(object v, Type pt)
        {
            if (v == null) return pt.IsValueType ? Activator.CreateInstance(pt) : null;
            if (pt.IsInstanceOfType(v)) return v;
            if (pt.IsEnum) return v is string s ? Enum.Parse(pt, s) : Enum.ToObject(pt, v);
            if (pt.IsPrimitive) return System.Convert.ChangeType(v, pt);
            return v;
        }

        public static IList List(object target, string name) => Get(target, name) as IList;

        public static IEnumerable<object> Each(object target, string name)
        {
            if (Get(target, name) is IEnumerable e) foreach (var o in e) yield return o;
        }
    }
}

namespace Zombheim
{
    /// <summary>The mod's log, kept apart from the plugin class so the content readers can run outside the game.</summary>
    internal static class Z
    {
        public static BepInEx.Logging.ManualLogSource Log = BepInEx.Logging.Logger.CreateLogSource("Zombheim");
    }
}
