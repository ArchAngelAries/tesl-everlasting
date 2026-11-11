using System;
using System.Collections.Generic;
using Mono.Cecil;

namespace OfflineEngine
{
    // BepInEx applies this to its in-memory assembly; the installed game DLL is never written.
    public static class OfflineAccessPatcher
    {
        public static IEnumerable<string> TargetDLLs { get { return new string[] { "Assembly-CSharp.dll" }; } }

        // Offline only (never in online launches): the same rules as OfflineEngine.OfflineLaunchMode — flags first, then the
        // saved choice in .offline-user-data/launch.json, then offline when TESL-Reborn is not installed.
        public static bool Enabled()
        {
            foreach (string arg in Environment.GetCommandLineArgs())
            {
                if (String.Equals(arg, "--tesl-offline", StringComparison.OrdinalIgnoreCase)
                    || String.Equals(arg, "--tesl-offline-native", StringComparison.OrdinalIgnoreCase)) return true;
                if (String.Equals(arg, "--tesl-online", StringComparison.OrdinalIgnoreCase)) return false;
            }
            try
            {
                string root = System.IO.Path.GetDirectoryName(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName);
                string file = System.IO.Path.Combine(System.IO.Path.Combine(root, ".offline-user-data"), "launch.json");
                bool reborn = System.IO.Directory.Exists(System.IO.Path.Combine(System.IO.Path.Combine(System.IO.Path.Combine(root, "BepInEx"), "plugins"), "TeslRebornPlugin"));
                if (System.IO.File.Exists(file))
                {
                    string text = System.IO.File.ReadAllText(file);
                    if (text.Contains("\"offline\"")) return true;
                    if (text.Contains("\"online\"")) return !reborn;   // online needs TESL-Reborn
                }
                return !reborn;
            }
            catch (Exception) { return false; }
        }

        public static void Patch(AssemblyDefinition assembly)
        {
            if (!Enabled()) return;
            Publicize(assembly);
            // Empty shipped methods can be inlined while Harmony compiles an
            // earlier caller (notably the native match launcher). Keep restored
            // entry points callable so later offline detours actually execute.
            foreach (string name in new string[] { "SPTESL.Simulation", "SPTESL.ServerCardCatalog", "SPTESL.CardCommandUtilities", "SPTESL.DiscardUtilities" })
            {
                TypeDefinition type = assembly.MainModule.GetType(name);
                if (type == null) throw new InvalidOperationException("Offline restoration type is absent: " + name);
                foreach (MethodDefinition method in type.Methods)
                    if (method.HasBody && method.Name != ".cctor")
                        method.ImplAttributes = (method.ImplAttributes & ~MethodImplAttributes.AggressiveInlining) | MethodImplAttributes.NoInlining;
            }
            // Preserve the existing protocol while restricting the embedded listener to this PC.
            TypeDefinition server = assembly.MainModule.GetType("SPTESL.TcpNetworkServer");
            int replacements = 0;
            foreach (MethodDefinition method in server.Methods)
            {
                if (method.Name != ".ctor" || !method.HasBody) continue;
                foreach (Mono.Cecil.Cil.Instruction instruction in method.Body.Instructions)
                {
                    FieldReference field = instruction.Operand as FieldReference;
                    if (field != null && field.DeclaringType.FullName == "System.Net.IPAddress" && field.Name == "Any")
                    {
                        instruction.Operand = new FieldReference("Loopback", field.FieldType, field.DeclaringType);
                        replacements++;
                    }
                }
            }
            if (replacements != 1) throw new InvalidOperationException("Offline listener patch does not match this game build.");
        }

        public static void Publicize(AssemblyDefinition assembly)
        {
            foreach (TypeDefinition type in assembly.MainModule.Types) PublicizeType(type);
        }

        private static void PublicizeType(TypeDefinition type)
        {
            if (type.Name == "<Module>" || !PublicizeFields(type.FullName)) return;
            type.Attributes = (type.Attributes & ~TypeAttributes.VisibilityMask) |
                (type.IsNested ? TypeAttributes.NestedPublic : TypeAttributes.Public);
            // Unity serializes public fields. Never expose arbitrary MonoBehaviour/model fields:
            // doing so changes the scene schema even though the on-disk scene is unchanged.
            foreach (FieldDefinition field in type.Fields)
                field.Attributes = (field.Attributes & ~FieldAttributes.FieldAccessMask) | FieldAttributes.Public;
            foreach (MethodDefinition method in type.Methods)
                if (method.Name != ".cctor")
                    method.Attributes = (method.Attributes & ~MethodAttributes.MemberAccessMask) | MethodAttributes.Public;
            foreach (TypeDefinition nested in type.NestedTypes) PublicizeType(nested);
        }

        private static bool PublicizeFields(string fullName)
        {
            string root = fullName.Split('/')[0];
            return root == "SPTESL.Simulation" || root == "SPTESL.ServerCardCatalog" || root == "SPTESL.PlayerServiceClient";
        }
    }
}
