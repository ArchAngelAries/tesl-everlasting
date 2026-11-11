using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization.Formatters.Binary;
using System.Text;

// Read-only original configuration inspection. No recovery assembly or Unity calls.
public static class OriginalScenarioConfigurationHarness
{
    private static Dictionary<int, bool> references = new Dictionary<int, bool>();
    private static object Field(object value, string name) { FieldInfo field = value.GetType().GetField(name); return field == null ? null : field.GetValue(value); }
    private static string Text(object value) { return value == null ? "NULL" : value.ToString().Replace("\t", " ").Replace("\r", " ").Replace("\n", " "); }
    private static string Id(object value) { object id = Field(value, "ResourceId"); return id == null ? "?" : Text(Field(id, "Id")) + "/" + Text(Field(id, "UniqueName")); }
    private static int Count(object value) { return value == null ? -1 : ((ICollection)value).Count; }
    private static void Dump(TextWriter writer, object value, string path, int depth, List<object> ancestors)
    {
        if (value == null) { writer.WriteLine(path + "\tNULL"); return; }
        Type type = value.GetType();
        if (type.Name == "UniqueResourceReference") references[(int)Field(value,"resourceId")] = true;
        if (type.Name == "DeckCardInfo") references[(int)Field(value,"Id")] = true;
        if (type.IsPrimitive || type.IsEnum || value is string || value is decimal) { writer.WriteLine(path + "\t" + Text(value)); return; }
        writer.WriteLine(path + "\t" + type.FullName);
        if (depth == 0) { writer.WriteLine(path + "\tDEPTH_LIMIT"); return; }
        foreach (object old in ancestors) if (Object.ReferenceEquals(old, value)) { writer.WriteLine(path + "\tCYCLE"); return; }
        ancestors.Add(value);
        try
        {
            IEnumerable list = value is ICollection ? (IEnumerable)value : null;
            if (list != null) { int index = 0; foreach (object item in list) Dump(writer, item, path + "[" + index++ + "]", depth - 1, ancestors); }
            else foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance)) Dump(writer, field.GetValue(value), path + "." + field.Name, depth - 1, ancestors);
        }
        finally { ancestors.RemoveAt(ancestors.Count - 1); }
    }
    public static void Main(string[] args)
    {
        if (args.Length != 3) throw new ArgumentException("managedDirectory configurationBytes outputDirectory");
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs e) {
            string path = Path.Combine(args[0], new AssemblyName(e.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        object config; using (FileStream stream = File.OpenRead(args[1])) config = new BinaryFormatter().Deserialize(stream);
        Directory.CreateDirectory(args[2]);
        string prefix = Path.Combine(args[2], "scenario-recovery-");
        IList scenarios = (IList)Field(config, "Scenarios");
        int candidates = 0;
        using (StreamWriter index = new StreamWriter(prefix + "index.tsv", false, new UTF8Encoding(false)))
        using (StreamWriter detail = new StreamWriter(prefix + "tutorials.txt", false, new UTF8Encoding(false)))
        {
            index.WriteLine("identity\tsingle\tfirst\tdecks\tzones\teffects\tevents\trestrictions\tavatar_wrappers\tplayer_wrappers");
            foreach (object scenario in scenarios)
            {
                string id = Id(scenario);
                index.WriteLine(id + "\t" + Text(Field(scenario,"IsSinglePlayer")) + "\t" + Text(Field(scenario,"StartingPlayer")) + "\t" + Count(Field(scenario,"DeckOverrides")) + "\t" + Count(Field(scenario,"GameZoneOverrides")) + "\t" + Count(Field(scenario,"TriggeredEffects")) + "\t" + Count(Field(scenario,"DoinkerClientEvents")) + "\t" + Count(Field(scenario,"ClientRestrictions")) + "\t" + Count(Field(scenario,"AvatarOverrides")) + "\t" + Count(Field(scenario,"PlayerOverrides")));
                string name = Text(Field(Field(scenario,"ResourceId"),"UniqueName"));
                if (name != "TFH_Act1_Chapter1" && name != "TFH_Act1_Chapter2" && name != "TFH_Act1_Chapter3" && name != "Go First!" && name != "Go Second!") continue;
                candidates++; Dump(detail, scenario, "SCENARIO " + id, 16, new List<object>());
            }
        }
        // Exact linked source deck, opponent and avatar data, including null wrappers.
        using (StreamWriter detail = new StreamWriter(prefix + "dependencies.txt", false, new UTF8Encoding(false)))
        {
            foreach (string collection in new string[] { "CustomDecks", "AiOpponents", "Doinkers", "MatchDialogues" })
                foreach (object item in (IEnumerable)Field(config, collection))
                    if (Field(item,"ResourceId") != null && references.ContainsKey((int)Field(Field(item,"ResourceId"),"Id")))
                        Dump(detail, item, collection + " " + Id(item), 12, new List<object>());
            foreach (object set in (IEnumerable)Field(config,"CardCollection")) foreach (object card in (IEnumerable)Field(set,"Cards"))
                if (Text(Field(card,"Definition")) == "Avatar" || references.ContainsKey((int)Field(Field(card,"ResourceId"),"Id"))) Dump(detail, card, "CARD " + Id(card), 8, new List<object>());
        }
        using (StreamWriter item = new StreamWriter(prefix + "item-definition.txt", false, new UTF8Encoding(false)))
        {
            foreach (object definition in (IEnumerable)Field(config,"CardDefinitions"))
                if (Text(Field(definition,"Name")) == "Item") Dump(item, definition, "ITEM_DEFINITION", 20, new List<object>());
            Dump(item, Field(config,"Zones"), "ZONES", 12, new List<object>());
        }
        Console.WriteLine("original_config_type=" + config.GetType().FullName + " scenarios=" + scenarios.Count + " tutorial_or_intro_candidates=" + candidates);
        Console.WriteLine("export_prefix=" + prefix);
    }
}
