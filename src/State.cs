using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace TaskBoost
{
    // Undo record: the value each setting had before TaskBoost first changed it.
    // One line per setting: kind <tab> key <tab> original value.
    static class State
    {
        public class Entry { public string Kind, Key, Value; }

        // Gaming mode keeps its own, separate undo record by switching this in its helper process
        public static string FileName = "state.txt";

        static string FilePath { get { return Path.Combine(App.DataDir, FileName); } }

        public static void Clear() { File.Delete(FilePath); }

        public static List<Entry> Load()
        {
            var list = new List<Entry>();
            if (!File.Exists(FilePath)) return list;
            foreach (string line in File.ReadAllLines(FilePath))
            {
                string[] p = line.Split('\t');
                if (p.Length == 3) list.Add(new Entry { Kind = p[0], Key = p[1], Value = p[2] });
            }
            return list;
        }

        public static bool Has(string kind, string key)
        {
            return Load().Exists(delegate(Entry e) { return e.Kind == kind && e.Key == key; });
        }

        // Only the first recording counts, so repeated runs never overwrite the true original
        public static void Record(string kind, string key, string value)
        {
            if (Has(kind, key)) return;
            File.AppendAllText(FilePath, kind + "\t" + key + "\t" + value + Environment.NewLine, Encoding.UTF8);
        }

        public static void Remove(Predicate<Entry> match)
        {
            var keep = Load();
            keep.RemoveAll(match);
            var sb = new StringBuilder();
            foreach (var e in keep) sb.Append(e.Kind).Append('\t').Append(e.Key).Append('\t').Append(e.Value).AppendLine();
            File.WriteAllText(FilePath, sb.ToString(), Encoding.UTF8);
        }

        public static bool IsSystem(Entry e)
        {
            return e.Kind == "svc" || e.Kind == "task" || e.Kind == "bcd" || (e.Kind == "reg" && e.Key.StartsWith("HKLM"));
        }
    }

    // Registry writes that remember the original value in State
    static class Reg
    {
        static RegistryKey Root(string hive) { return hive == "HKLM" ? Registry.LocalMachine : Registry.CurrentUser; }
        static string Id(string hive, string path, string name) { return hive + "\\" + path + "|" + name; }

        public static void Record(string hive, string path, string name)
        {
            string id = Id(hive, path, name);
            if (State.Has("reg", id)) return;
            using (var k = Root(hive).OpenSubKey(path))
            {
                object v = k == null ? null : k.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                string encoded = v == null ? null : Encode(v, k.GetValueKind(name));
                State.Record("reg", id, encoded == null ? "absent" : k.GetValueKind(name) + ":" + encoded);
            }
        }

        public static void Set(string hive, string path, string name, int value) { Set(hive, path, name, value, RegistryValueKind.DWord); }
        public static void Set(string hive, string path, string name, string value) { Set(hive, path, name, value, RegistryValueKind.String); }

        public static void Set(string hive, string path, string name, object value, RegistryValueKind kind)
        {
            try
            {
                Record(hive, path, name);
                using (var k = Root(hive).CreateSubKey(path)) k.SetValue(name, value, kind);
            }
            catch (Exception ex) { Log.Error(Id(hive, path, name) + ": " + ex.Message); }
        }

        public static void Delete(string hive, string path, string name)
        {
            try
            {
                Record(hive, path, name);
                using (var k = Root(hive).OpenSubKey(path, true))
                    if (k != null) k.DeleteValue(name, false);
            }
            catch (Exception ex) { Log.Error(Id(hive, path, name) + ": " + ex.Message); }
        }

        public static void Restore(State.Entry e)
        {
            try
            {
                int bar = e.Key.LastIndexOf('|');
                string hive = e.Key.Substring(0, 4), path = e.Key.Substring(5, bar - 5), name = e.Key.Substring(bar + 1);
                if (e.Value == "absent")
                {
                    using (var k = Root(hive).OpenSubKey(path, true))
                        if (k != null) k.DeleteValue(name, false);
                    return;
                }
                int colon = e.Value.IndexOf(':');
                var kind = (RegistryValueKind)Enum.Parse(typeof(RegistryValueKind), e.Value.Substring(0, colon));
                using (var k = Root(hive).CreateSubKey(path)) k.SetValue(name, Decode(e.Value.Substring(colon + 1), kind), kind);
            }
            catch (Exception ex) { Log.Error("restore " + e.Key + ": " + ex.Message); }
        }

        static string Encode(object v, RegistryValueKind kind)
        {
            switch (kind)
            {
                case RegistryValueKind.DWord: return ((int)v).ToString();
                case RegistryValueKind.QWord: return ((long)v).ToString();
                case RegistryValueKind.String:
                case RegistryValueKind.ExpandString: return Convert.ToBase64String(Encoding.UTF8.GetBytes((string)v));
                case RegistryValueKind.MultiString: return Convert.ToBase64String(Encoding.UTF8.GetBytes(string.Join("\0", (string[])v)));
                case RegistryValueKind.Binary: return Convert.ToBase64String((byte[])v);
                default: return null;
            }
        }

        static object Decode(string s, RegistryValueKind kind)
        {
            switch (kind)
            {
                case RegistryValueKind.DWord: return int.Parse(s);
                case RegistryValueKind.QWord: return long.Parse(s);
                case RegistryValueKind.MultiString: return Encoding.UTF8.GetString(Convert.FromBase64String(s)).Split('\0');
                case RegistryValueKind.Binary: return Convert.FromBase64String(s);
                default: return Encoding.UTF8.GetString(Convert.FromBase64String(s));
            }
        }
    }
}
