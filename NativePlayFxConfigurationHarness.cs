using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization.Formatters.Binary;

public static class NativePlayFxConfigurationHarness
{
    private static object Field(object value, string name) { return value.GetType().GetField(name).GetValue(value); }
    public static void Main(string[] args)
    {
        string managed = args[0];
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs e)
        { string path = Path.Combine(managed, new AssemblyName(e.Name).Name + ".dll"); return File.Exists(path) ? Assembly.LoadFrom(path) : null; };
        object config;
        using (FileStream stream = File.OpenRead(args[1])) config = new BinaryFormatter().Deserialize(stream);
        foreach (object relation in (IEnumerable)Field(config, "CardFxDefaults"))
        {
            string action = Field(relation, "FXAction").ToString();
            if (action.StartsWith("PlayCard") || action == "Attack") Dump(relation, "DEFAULT ", 2);
        }
        foreach (object definition in (IEnumerable)Field(config, "CardFxDefinitions"))
        {
            string name = (string)Field(definition, "Name");
            if (name.Contains("play") || name.Contains("attack")) Dump(definition, "DEFINITION ", 2);
        }
        foreach (object set in (IEnumerable)Field(config, "CardCollection"))
            foreach (object card in (IEnumerable)Field(set, "Cards"))
                if ((string)Field(card, "Name") == "Afflicted Alit" || (string)Field(card, "Name") == "Lurking Crocodile")
                { Console.WriteLine("CARD " + Field(card, "Name")); Dump(Field(card, "CardFxRelations"), "RELATION ", 3); }
    }
    private static void Dump(object value, string prefix, int depth)
    {
        if (value == null) { Console.WriteLine(prefix + "NULL"); return; }
        Type t = value.GetType();
        if (t.IsPrimitive || t.IsEnum || value is string) { Console.WriteLine(prefix + value); return; }
        Console.WriteLine(prefix + t.FullName); if (depth <= 0) return;
        IEnumerable list = value is ICollection ? (IEnumerable)value : null;
        if (list != null) { int i = 0; foreach (object v in list) Dump(v, prefix + "[" + i++ + "] ", depth - 1); return; }
        foreach (FieldInfo f in t.GetFields()) Dump(f.GetValue(value), prefix + f.Name + ": ", depth - 1);
    }
}
