using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization.Formatters.Binary;

public static class OrdinaryConfigurationHarness
{
    private static string managed;
    private static object Field(object value, string name) { return value.GetType().GetField(name).GetValue(value); }
    private static void Attributes(object value)
    {
        foreach (object a in (IEnumerable)Field(value, "EntityAttributes"))
        {
            FieldInfo v = a.GetType().GetField("Value");
            Console.WriteLine("  " + Field(a, "Name") + "=" + (v == null ? "?" : v.GetValue(a)));
        }
    }
    private static void Dump(object value, string prefix, int depth)
    {
        if (value == null) { Console.WriteLine(prefix + "NULL"); return; }
        Type t = value.GetType();
        if (t.IsPrimitive || t.IsEnum || value is string) { Console.WriteLine(prefix + value); return; }
        Console.WriteLine(prefix + t.FullName);
        if (depth <= 0) return;
        IEnumerable list = value is ICollection ? (IEnumerable)value : null;
        if (list != null) { int i = 0; foreach (object v in list) Dump(v, prefix + "[" + i++ + "] ", depth - 1); return; }
        foreach (FieldInfo f in t.GetFields()) Dump(f.GetValue(value), prefix + f.Name + ": ", depth - 1);
    }
    public static void Main(string[] args)
    {
        managed = args[0];
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs e)
        {
            string path = Path.Combine(managed, new AssemblyName(e.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        object config;
        using (FileStream stream = File.OpenRead(args[1])) config = new BinaryFormatter().Deserialize(stream);
        Console.WriteLine("Deserialized installed game configuration using original types.");
        foreach (object p in (IEnumerable)Field(config, "PlayerDefinitions")) { Console.WriteLine("PLAYER"); Attributes(p); }
        foreach (object s in (IEnumerable)Field(config, "Scenarios"))
        {
            object id = Field(s, "ResourceId");
            if ((string)Field(id, "UniqueName") != "Default") continue;
            Console.WriteLine("DEFAULT SCENARIO");
            foreach (string n in new string[] { "ResourceId", "StartingPlayer", "AvatarOverrides", "PlayerOverrides", "DeckOverrides", "GameZoneOverrides", "TriggeredEffects", "IsSinglePlayer" })
            {
                object v = Field(s, n); ICollection c = v as ICollection;
                Console.WriteLine("  " + n + "=" + (v == null ? "NULL" : c == null ? v.ToString() : "count " + c.Count));
            }
            object props = Field(s, "GameProperties");
            foreach (FieldInfo f in props.GetType().GetFields())
            { object v = f.GetValue(props); ICollection c = v as ICollection; Console.WriteLine("  Property." + f.Name + "=" + (v == null ? "NULL" : c == null ? v.ToString() : "count " + c.Count)); }
            Dump(Field(props, "GameStartActions"), "START ", 7);
            Dump(Field(s, "GameZoneOverrides"), "ZONES ", 7);
        }
        foreach (object set in (IEnumerable)Field(config, "CardCollection"))
            foreach (object card in (IEnumerable)Field(set, "Cards"))
                if ((string)Field(card, "Name") == "Avatar")
                {
                    Console.WriteLine("BASE AVATAR " + Field(Field(card, "ResourceId"), "Id")); Attributes(card);
                    Console.WriteLine("  subtypes=" + string.Join(",", ((IEnumerable)Field(card, "Subtypes")).CastStrings()));
                }
    }
    private static string[] CastStrings(this IEnumerable values)
    { System.Collections.Generic.List<string> result = new System.Collections.Generic.List<string>(); foreach (object v in values) result.Add(v.ToString()); return result.ToArray(); }
}
