using System;
using System.Reflection;
using System.Reflection.Emit;

namespace Incubator.Network
{

    public static class TypeExtensions
    {
        public static Type BaseType(this Type t)
        {
            return t.GetTypeInfo().BaseType;
        }

        public static bool IsInterface(this Type t)
        {
            return t.GetTypeInfo().IsInterface;
        }

        public static MethodInfo[] GetMethods(this Type t)
        {
            return t.GetTypeInfo().GetMethods();
        }

        public static Type[] GetInterfaces(this Type t)
        {
            return t.GetTypeInfo().GetInterfaces();
        }

        public static Type CreateType(this TypeBuilder b)
        {
            return b.CreateTypeInfo().AsType();
        }

        public static ConstructorInfo GetConstructor(this Type t, Type[] ctorArgTypes)
        {
            return t.GetTypeInfo().GetConstructor(ctorArgTypes);
        }

        public static MethodInfo GetMethod(this Type t, string invokeMethod, BindingFlags flags)
        {
            return t.GetTypeInfo().GetMethod(invokeMethod, flags);
        }

        public static MethodInfo[] GetMethods(this Type t, BindingFlags flags)
        {
            return t.GetTypeInfo().GetMethods(flags);
        }

        public static PropertyInfo[] GetProperties(this Type t, BindingFlags flags)
        {
            return t.GetTypeInfo().GetProperties(flags);
        }

        public static bool IsValueType(this Type t)
        {
            return t.GetTypeInfo().IsValueType;
        }

        public static Type[] GetGenericArguments(this PropertyInfo pi)
        {
            return pi.PropertyType.GetTypeInfo().GetGenericArguments();
        }

        public static bool IsGenericType(this Type t)
        {
            return t.GetTypeInfo().IsGenericType;
        }

        public static bool IsPrimitive(this Type t)
        {
            return t.GetTypeInfo().IsPrimitive;
        }
    }
}
