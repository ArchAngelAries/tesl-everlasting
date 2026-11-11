using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization.Formatters.Binary;

// Read-only audit; excluded from plugin builds.
public static class OfflineDeckConfigurationHarness
{
    private static object F(object o, string n) { return o.GetType().GetField(n).GetValue(o); }
    private static string Identity(object o) { object r = F(o, "ResourceId"); return F(r, "Id") + "/" + F(r, "UniqueName"); }
    public static void Main(string[] args)
    {
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object s, ResolveEventArgs e) {
            string p = Path.Combine(args[0], new AssemblyName(e.Name).Name + ".dll");
            if(!File.Exists(p))p=Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),new AssemblyName(e.Name).Name+".dll");
            if(!File.Exists(p))p=Path.Combine(args[4],new AssemblyName(e.Name).Name+".dll");
            return File.Exists(p) ? Assembly.LoadFrom(p) : null; };
        object config; using (FileStream f = File.OpenRead(args[1])) config = new BinaryFormatter().Deserialize(f);
        HashSet<int> supported = new HashSet<int>();
        foreach (string line in File.ReadAllLines(args[2])) { string[] p = line.Split('\t'); int id; if (p.Length > 2 && p[2] == "staged" && int.TryParse(p[0], out id)) supported.Add(id); }
        supported.Add(794235553);
        if(args.Length>3)
        {
            Assembly engine=Assembly.LoadFrom(Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),"TESL.Offline.Engine.dll"));
            object set=engine.GetType("OfflineEngine.RuleImporter").GetField("supported",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
            foreach(int id in supported)set.GetType().GetMethod("Add").Invoke(set,new object[]{id});
            Type catalog=Assembly.LoadFrom(args[3]).GetType("OfflineEngine.OfflineDeckCatalog");
            object practice=catalog.GetMethod("CreatePracticeDeck").Invoke(null,new object[]{config});
            Console.WriteLine("practice_size="+practice.GetType().GetProperty("Size").GetValue(practice,null));
            foreach(object c in (IEnumerable)F(practice,"Cards"))Console.WriteLine("practice_card\t"+F(c,"Id")+"\tcount="+F(c,"Count"));
            ICollection entries=(ICollection)catalog.GetMethod("Create").Invoke(null,new object[]{config}); int accepted=0;
            foreach(object entry in entries)if((bool)F(entry,"CanPlay"))accepted++;
            Console.WriteLine("catalog_entries="+entries.Count+" accepted="+accepted);
            object copy=catalog.GetMethod("Clone").Invoke(null,new object[]{practice});
            foreach(object c in (IEnumerable)F(copy,"Cards")){c.GetType().GetField("Count").SetValue(c,4);break;}
            try{catalog.GetMethod("Validate").Invoke(null,new object[]{config,copy});throw new Exception("Excess copies accepted");}catch(TargetInvocationException e){if(!(e.InnerException is NotSupportedException))throw;Console.WriteLine("excess_copies_rejected=PASS");}
            Console.WriteLine("original_practice_size_unchanged="+practice.GetType().GetProperty("Size").GetValue(practice,null));
            Type tests=catalog.Assembly.GetType("OfflineEngine.OfflineDeckCatalogTests");
            if(tests!=null)tests.GetMethod("Run").Invoke(null,new object[]{config,new Action<string,Action>(delegate(string name,Action body){body();Console.WriteLine("test\t"+name+"\tPASS");})});
            catalog.GetMethod("Load").Invoke(null,new object[]{config,Path.GetDirectoryName(args[2])});
            ICollection loaded=(ICollection)catalog.GetField("Entries").GetValue(null);
            Console.WriteLine("catalog_with_original_resources="+loaded.Count);
            foreach(object entry in loaded)if(((string)F(entry,"Key")).StartsWith("resource/"))Console.WriteLine("recovered_original\t"+F(entry,"Name")+"\t"+F(entry,"Reason"));
            string realManifest=Path.Combine(Path.Combine(Path.GetDirectoryName(args[2]),"original-resource-decks"),"manifest.json");
            if(File.Exists(realManifest))
            {
                string negative=Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),"deck-recovery-negative");
                string negativeFolder=Path.Combine(negative,"original-resource-decks"); Directory.CreateDirectory(negativeFolder);
                string text=File.ReadAllText(realManifest); int begin=text.IndexOf("\"source_sha256\": \"")+18;
                text=text.Substring(0,begin)+new string('0',64)+text.Substring(begin+64);
                File.WriteAllText(Path.Combine(negativeFolder,"manifest.json"),text);
                try{catalog.Assembly.GetType("OfflineEngine.OriginalResourceDeckRecovery").GetMethod("Read").Invoke(null,new object[]{negative});throw new Exception("Changed source receipt accepted");}
                catch(TargetInvocationException e){if(!(e.InnerException is NotSupportedException))throw;Console.WriteLine("altered_source_receipt_rejected=PASS");}
            }
        }
        Dictionary<int,object> cards = new Dictionary<int,object>();
        foreach (object set in (IEnumerable)F(config,"CardCollection")) foreach (object card in (IEnumerable)F(set,"Cards")) cards.Add((int)F(F(card,"ResourceId"),"Id"),card);
        Console.WriteLine("supported_ids=" + supported.Count);
        int decks=0,presets=0,nonempty=0;
        foreach (object d in (IEnumerable)F(config,"CustomDecks")) { decks++; foreach(object p in (IEnumerable)F(d,"Decks")) { presets++; ICollection c=F(p,"Cards") as ICollection; if(c!=null && c.Count>0)nonempty++; } }
        Console.WriteLine("custom_decks="+decks+" presets="+presets+" nonempty_recipes="+nonempty);
        int infos=0,nonemptyInfos=0,fullySupported=0;
        foreach (object d in (IEnumerable)F(config,"DeckInfo")) {
            infos++; ICollection list=F(d,"Cards") as ICollection; int size=0,missing=0,unsupported=0;
            if(list!=null && list.Count>0)nonemptyInfos++;
            if(list!=null)foreach(object c in list) { int id=(int)F(c,"Id"),count=(int)F(c,"Count"); size+=count; if(!cards.ContainsKey(id))missing++; else if(!supported.Contains(id))unsupported++; }
            if(size>0 && missing==0 && unsupported==0)fullySupported++;
            Console.WriteLine("deck_info\t"+F(d,"TypeNameHash")+"/"+F(d,"TypeName")+"\t"+F(d,"DisplayName")+"\tstate="+F(d,"DeckState")+"\tsize="+size+"\tmissing="+missing+"\tunsupported="+unsupported);
        }
        Console.WriteLine("deck_infos="+infos+" nonempty_recipes="+nonemptyInfos+" fully_supported="+fullySupported);
        foreach(int id in supported) { object c=cards[id]; Console.Write("supported_card\t"+id+"\t"+F(c,"Name")); foreach(FieldInfo f in c.GetType().GetFields()) if(f.Name.ToLower().Contains("copies") || f.Name=="Subtypes") { object v=f.GetValue(c); IEnumerable values=v as IEnumerable; string text=""; if(values!=null && !(v is string)) foreach(object x in values)text+=x+","; else text=Convert.ToString(v); Console.Write("\t"+f.Name+"="+text); } Console.WriteLine(); }
    }
}
