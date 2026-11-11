using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization.Formatters.Binary;

// Native metadata/graph preflight only; no game process or Unity invocation.
public static class OriginalSimpleSummonConfigurationHarness
{
    public static void Main(string[] args)
    {
        string build = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        AppDomain.CurrentDomain.AssemblyResolve += delegate(object s, ResolveEventArgs e) {
            string name = new AssemblyName(e.Name).Name + ".dll";
            foreach(string folder in new string[]{args[0],build,args[3]}) { string p=Path.Combine(folder,name); if(File.Exists(p))return Assembly.LoadFrom(p); } return null; };
        object config; using(FileStream f=File.OpenRead(args[1]))config=new BinaryFormatter().Deserialize(f);
        Type rules=Assembly.LoadFrom(args[2]).GetType("OfflineEngine.OriginalSimpleSummonRules");
        IList cards=(IList)rules.GetMethod("Validate").Invoke(null,new object[]{config});
        Console.WriteLine("exact_original_summon_draw="+cards.Count);
        FieldInfo desc=cards[1].GetType().GetField("Description"); object text=desc.GetValue(cards[1]);
        FieldInfo abilities=cards[0].GetType().GetField("TriggeredEffects"); object before=abilities.GetValue(cards[0]);
        try {
            desc.SetValue(cards[1],"Unknown extra behavior");
            try { rules.GetMethod("Restore").Invoke(null,new object[]{config}); throw new Exception("Changed metadata admitted"); }
            catch(TargetInvocationException e){if(!(e.InnerException is NotSupportedException))throw;}
            if(!Object.ReferenceEquals(before,abilities.GetValue(cards[0])))throw new Exception("Mutation before validation");
            Console.WriteLine("atomic_exact_metadata_rejection=PASS");
        } finally{desc.SetValue(cards[1],text);}
        int[] ids=(int[])rules.GetMethod("Restore").Invoke(null,new object[]{config}); object restored=abilities.GetValue(cards[0]);
        rules.GetMethod("Restore").Invoke(null,new object[]{config});
        if(!Object.ReferenceEquals(restored,abilities.GetValue(cards[0])))throw new Exception("Idempotence failed");
        Console.WriteLine("idempotent_native_summon_draw_wrapper=PASS");
        foreach(int id in ids)Console.WriteLine("restored_original_id="+id);
    }
}

