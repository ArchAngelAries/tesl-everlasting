using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization.Formatters.Binary;

// Read-only audit of the installed configuration with the original managed types.
public static class OriginalCardConfigurationHarness
{
    private static object Field(object value, string name) { return value.GetType().GetField(name).GetValue(value); }
    private static string Text(object value) { return value == null ? "" : value.ToString().Replace("\t", " ").Replace("\r", " ").Replace("\n", " "); }
    private static string List(object value, string field)
    {
        List<string> result = new List<string>();
        if (value != null) foreach (object item in (IEnumerable)value) result.Add(Text(field == null ? item : Field(item, field)));
        return String.Join(",", result.ToArray());
    }
    private static int Count(object value) { return value == null ? -1 : ((ICollection)value).Count; }
    public static void Main(string[] args)
    {
        string managed = args[0];
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object sender, ResolveEventArgs e) {
            string path = Path.Combine(managed, new AssemblyName(e.Name).Name + ".dll");
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        };
        object config;
        using (FileStream stream = File.OpenRead(args[1])) config = new BinaryFormatter().Deserialize(stream);
        if (args.Length > 2)
        {
            Assembly recovery = Assembly.LoadFrom(args[2]);
            Type rules = recovery.GetType("OfflineEngine.OriginalCardRules");
            IList validated = (IList)rules.GetMethod("Validate").Invoke(null, new object[] { config });
            Console.WriteLine("validated_originals=" + validated.Count);
            int[] restored = (int[])rules.GetMethod("Restore").Invoke(null, new object[] { config });
            Console.WriteLine("restored_originals=" + restored.Length);
            recovery.GetType("OfflineEngine.OriginalCardRuleTests").GetMethod("AssertOriginalInventory").Invoke(null, new object[] { config });
            Console.WriteLine("exact_metadata_and_atomic_preflight=PASS");
            Type special = recovery.GetType("OfflineEngine.OriginalSpecialCreatureRules");
            if (special != null)
            {
                special.GetMethod("Validate").Invoke(null, new object[] { config });
                int[] specialIds = (int[])special.GetMethod("Restore").Invoke(null, new object[] { config });
                Console.WriteLine("restored_original_snowy=" + specialIds[0]);
            }
            return;
        }
        Console.WriteLine("id\tname\tdefinition\tproduction\tcompletion\tdescription\tdescription_key\tattributes\tmechanics\tsubtypes\thangers\treferences\tdouble_refs\tdynamic_text\trare_attributes\thunt\tupgrades\tmobilize\tattack_requirements\texport\tname_key\timage\tpack\trarity\tunique\tmax_copies\tsource\tresource_name");
        foreach (object set in (IEnumerable)Field(config, "CardCollection")) foreach (object card in (IEnumerable)Field(set, "Cards"))
        {
            object id = Field(card, "ResourceId");
            List<string> values = new List<string>();
            foreach (object attr in (IEnumerable)Field(card, "EntityAttributes")) values.Add(Text(Field(attr, "Name")) + ":" + Text(Field(attr, "Value")));
            List<string> mechanics = new List<string>();
            foreach (object entry in (IEnumerable)Field(card, "MechanicsEntries")) mechanics.Add(Text(Field(entry, "MechanicType")) + ":" + Text(Field(entry, "DisplayHanger")) + ":" + Text(Field(entry, "DisplayIcon")));
            object attack = Field(card, "AttackRequirements");
            List<string> row = new List<string>();
            row.Add(Text(Field(id, "Id"))); row.Add(Text(Field(card, "Name"))); row.Add(Text(Field(card, "Definition")));
            row.Add(Text(Field(card, "Production"))); row.Add(Text(Field(id, "CompletionType")));
            row.Add(Text(Field(card, "Description"))); row.Add(Text(Field(Field(card, "DescriptionKey"), "RawKey")));
            row.Add(String.Join(",", values.ToArray())); row.Add(String.Join(",", mechanics.ToArray())); row.Add(List(Field(card, "Subtypes"), null));
            foreach (string field in new string[] { "PermanentHangers", "CardReferences", "DoubleCardReferences", "DynamicTextAssignments", "DefaultRareAttributes", "HuntTypes", "UpgradesInto" }) row.Add(Text(Count(Field(card, field))));
            row.Add(Text(Field(card, "MobilizeRecruitId")));
            row.Add(attack == null ? "NULL" : Count(Field(attack,"PlayerRequirements")) + "," + Count(Field(attack,"CardRequirements")) + "," + Count(Field(attack,"LaneRequirements")));
            row.Add(Text(Field(card,"ExportCode"))); row.Add(Text(Field(Field(card,"NameKey"),"RawKey"))); row.Add(Text(Field(card,"Image")));
            foreach (string field in new string[] { "ContentPackIndex", "Rarity", "Unique", "MaxCopies", "Source" }) row.Add(Text(Field(card,field)));
            row.Add(Text(Field(id, "UniqueName")));
            Console.WriteLine(String.Join("\t", row.ToArray()));
        }
    }
}
