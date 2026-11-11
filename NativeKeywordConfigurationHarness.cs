using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization.Formatters.Binary;

public static class NativeKeywordConfigurationHarness
{
    private static object Field(object value, string name) { return value.GetType().GetField(name).GetValue(value); }
    public static void Main(string[] args)
    {
        string managed = args[0];
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs e)
        {
            string path = Path.Combine(managed, new AssemblyName(e.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        object config;
        using (FileStream stream = File.OpenRead(args[1])) config = new BinaryFormatter().Deserialize(stream);
        foreach (object set in (IEnumerable)Field(config, "CardCollection"))
            foreach (object card in (IEnumerable)Field(set, "Cards"))
            {
                object id = Field(card, "ResourceId");
                if ((int)Field(id, "Id") != 794235553) continue;
                Console.WriteLine("Original Lurking Crocodile metadata:");
                Dump(card, "CARD ", 3);
                string definitionName = (string)Field(card, "Definition");
                foreach (object definition in (IEnumerable)Field(config, "CardDefinitions"))
                    if ((string)Field(definition, "Name") == definitionName)
                    {
                        Console.WriteLine("Original shared definition:");
                        Dump(definition, "DEFINITION ", 2);
                    }
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
}
