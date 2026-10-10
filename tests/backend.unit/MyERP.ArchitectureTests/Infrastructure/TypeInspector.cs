using System.Reflection;

namespace MyERP.ArchitectureTests.Infrastructure;

/// <summary>收集型別在簽章（基底、介面、欄位、屬性、方法）與方法本體中引用的其他型別。</summary>
internal static class TypeInspector
{
    private const BindingFlags AllDeclared =
        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

    public static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            return ex.Types.OfType<Type>();
        }
    }

    public static IEnumerable<MethodBase> GetDeclaredMethods(Type type) =>
        type.GetMethods(AllDeclared).Cast<MethodBase>().Concat(type.GetConstructors(AllDeclared));

    /// <summary>型別本身（含巢狀的編譯器產生型別，由呼叫端逐一傳入）引用的所有型別。</summary>
    public static IEnumerable<Type> GetReferencedTypes(Type type)
    {
        var direct = new List<Type?>();
        direct.Add(type.BaseType);
        direct.AddRange(type.GetInterfaces());
        direct.AddRange(type.GetFields(AllDeclared).Select(f => f.FieldType));
        direct.AddRange(type.GetProperties(AllDeclared).Select(p => p.PropertyType));

        foreach (var method in GetDeclaredMethods(type))
        {
            if (method is MethodInfo mi)
            {
                direct.Add(mi.ReturnType);
            }

            direct.AddRange(method.GetParameters().Select(p => p.ParameterType));

            foreach (var member in IlReader.GetReferencedMembers(method))
            {
                direct.Add(member as Type ?? member.DeclaringType);
                switch (member)
                {
                    case FieldInfo f:
                        direct.Add(f.FieldType);
                        break;
                    case MethodInfo m:
                        direct.Add(m.ReturnType);
                        break;
                }
            }
        }

        return direct.OfType<Type>().SelectMany(Expand).Distinct();
    }

    private static IEnumerable<Type> Expand(Type type)
    {
        if (type.HasElementType)
        {
            foreach (var t in Expand(type.GetElementType()!))
            {
                yield return t;
            }

            yield break;
        }

        if (type.IsGenericParameter)
        {
            yield break;
        }

        yield return type;

        if (type.IsGenericType && !type.IsGenericTypeDefinition)
        {
            foreach (var arg in type.GetGenericArguments().SelectMany(Expand))
            {
                yield return arg;
            }
        }
    }
}
