using System;

// ReSharper disable once CheckNamespace
namespace NuGet.Modules
{
    public static class ReflectionExtensions
    {
        public static bool IsNullable(this Type type)
        {
            return type.IsGenericType && type.GetGenericTypeDefinition() == typeof (Nullable<>);
        }
    }
}