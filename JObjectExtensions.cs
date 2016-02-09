using System;
using System.Reflection;
using Newtonsoft.Json.Linq;

// ReSharper disable once CheckNamespace
namespace NuGet.Modules
{
    public static class JObjectExtensions
    {
        public static JObject AddProperties(this JObject obj, Type type)
        {
            foreach (var propertyInfo in type.GetProperties())
            {
                obj.Add(propertyInfo.Name.ToCamelCase(), new JValue(
                    propertyInfo.PropertyType.IsNullable()
                        ? propertyInfo.PropertyType.GenericTypeArguments[0].Name
                        : propertyInfo.PropertyType.Name));
            }
            return obj;
        }

        public static void AddParameterInfo(this JObject obj, ParameterInfo parameterInfo)
        {
            if (parameterInfo.ParameterType.IsPrimitive
                || parameterInfo.ParameterType == typeof(string)
                || parameterInfo.ParameterType == typeof(Guid))
            {
                obj.Add(parameterInfo.Name, parameterInfo.ParameterType.Name);
            }
            else if (parameterInfo.ParameterType.IsNullable())
            {
                obj.Add(parameterInfo.Name, parameterInfo.ParameterType.GenericTypeArguments[0].Name);
            }
            else
            {
                obj.AddProperties(parameterInfo.ParameterType);
            }
        }

        private static string ToCamelCase(this string value)
        {
            if (string.IsNullOrEmpty(value)) return value;
            if (value.Length == 1) return value.ToLower();
            return string.Concat(value.Substring(0, 1).ToLower(), value.Substring(1));
        }
    }
}