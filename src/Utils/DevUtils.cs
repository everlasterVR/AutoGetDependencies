using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using UnityEngine;

namespace everlaster
{
    static class DevUtils
    {
        public static string ObjectPropertiesString(object obj, string prefix = "")
        {
            try
            {
                var sb = new StringBuilder();

                // sb.AppendFormat("{0}--- Properties:\n", prefix);
                var properties = TypeDescriptor.GetProperties(obj);
                for(int i = 0; i < properties.Count; i++)
                {
                    var property = properties[i];
                    sb.AppendFormat("{0}{1} = {2}\n", prefix, property.Name, property.GetValue(obj));
                }

                // sb.AppendFormat("{0}--- Fields:\n", prefix);
                var fields = obj.GetType().GetFields();
                for(int i = 0; i < fields.Length; i++)
                {
                    var field = fields[i];
                    sb.AppendFormat("{0}{1} = {2}\n", prefix, field.Name, field.GetValue(obj));
                }

                return sb.ToString();
            }
            catch(Exception e)
            {
                Debug.LogError($"Error getting object properties: {e}");
            }

            return "";
        }
    }

    static partial class TransformExtensions
    {
        public static string HierarchyToString(this Transform t, int? maxDepth = null)
        {
            var sb = new StringBuilder();
            BuildObjectHierarchyStringRecursive(t, sb, maxDepth);
            return sb.ToString();
        }

        static void BuildObjectHierarchyStringRecursive(
            Transform t,
            StringBuilder sb,
            int? maxDepth,
            int currentDepth = 0
        )
        {
            if(currentDepth > maxDepth)
            {
                return;
            }

            for(int i = 0; i < currentDepth; i++)
            {
                sb.Append("|   ");
            }

            sb.Append(t.name + "\n");
            foreach(Transform child in t)
            {
                BuildObjectHierarchyStringRecursive(child, sb, maxDepth, currentDepth + 1);
            }
        }

        public static string GetAllParentsDebugString(this Transform transform)
        {
            var names = new List<string>();
            while(transform != null)
            {
                names.Add(transform.name);
                transform = transform.parent;
            }

            names.Reverse();
            var sb = new StringBuilder();
            for(int i = 0; i < names.Count; i++)
            {
                int indentation = i < 10 ? i * 2 : i * 2 - 1;
                sb.AppendLine($"{i} {new string(' ', indentation)}{names[i]}");
            }

            return sb.ToString();
        }
    }
}
