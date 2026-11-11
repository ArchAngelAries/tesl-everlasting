using System;
using System.Collections;
using System.Reflection;
using System.Text;

namespace OfflineEngine
{
    // Diagnostic: indented dump of an installed data object's public fields (depth-limited), to read
    // scenario rules (GameProperties, scripted TriggeredEffects) that have no other readable form.
    public static class OfflineObjectDump
    {
        public static string Dump(object value, int maxDepth)
        {
            StringBuilder s = new StringBuilder();
            Write(s, value, 0, maxDepth, "");
            return s.ToString();
        }

        private static void Write(StringBuilder s, object value, int depth, int maxDepth, string name)
        {
            string pad = new string(' ', depth * 2);
            if (value == null) { s.Append(pad).Append(name).AppendLine(" = null"); return; }
            Type type = value.GetType();
            if (type.IsPrimitive || type.IsEnum || value is string || value is decimal)
            { s.Append(pad).Append(name).Append(" = ").AppendLine(value.ToString()); return; }
            IList list = value as IList;
            if (list != null)
            {
                s.Append(pad).Append(name).Append(" [").Append(list.Count).AppendLine("]");
                if (depth >= maxDepth) return;
                for (int i = 0; i < list.Count && i < 40; i++) Write(s, list[i], depth + 1, maxDepth, "#" + i);
                return;
            }
            s.Append(pad).Append(name).Append(" : ").AppendLine(type.Name);
            if (depth >= maxDepth) return;
            foreach (FieldInfo field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                object child;
                try { child = field.GetValue(value); } catch (Exception) { continue; }
                Write(s, child, depth + 1, maxDepth, field.Name);
            }
        }
    }
}
