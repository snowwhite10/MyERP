using System.Reflection;
using System.Reflection.Emit;

namespace MyERP.ArchitectureTests.Infrastructure;

/// <summary>
/// 最小 IL 讀取器：逐一解析方法本體的指令，回傳其引用的方法、欄位與型別。
/// 用於架構測試（例如禁止呼叫 DateTime.Now），不需額外套件（ADR-003 決定 2）。
/// </summary>
internal static class IlReader
{
    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(f => (OpCode)f.GetValue(null)!)
        .DistinctBy(o => o.Value)
        .ToDictionary(o => o.Value);

    public static IEnumerable<MemberInfo> GetReferencedMembers(MethodBase method)
    {
        byte[]? il;
        try
        {
            il = method.GetMethodBody()?.GetILAsByteArray();
        }
        catch (InvalidOperationException)
        {
            yield break;
        }

        if (il is null)
        {
            yield break;
        }

        var typeArgs = method.DeclaringType is { IsGenericType: true } dt ? dt.GetGenericArguments() : null;
        var methodArgs = method is MethodInfo { IsGenericMethod: true } mi ? mi.GetGenericArguments() : null;

        var pos = 0;
        while (pos < il.Length)
        {
            short value = il[pos++];
            if (value == 0xFE)
            {
                value = unchecked((short)(0xFE00 | il[pos++]));
            }

            if (!OpCodesByValue.TryGetValue(value, out var opCode))
            {
                throw new InvalidOperationException($"Unknown IL opcode 0x{value:X4} in {method.DeclaringType}.{method.Name}");
            }

            var operandSize = GetOperandSize(opCode.OperandType, il, pos);
            if (opCode.OperandType is OperandType.InlineMethod or OperandType.InlineField
                or OperandType.InlineType or OperandType.InlineTok)
            {
                var member = TryResolve(method.Module, BitConverter.ToInt32(il, pos), typeArgs, methodArgs);
                if (member is not null)
                {
                    yield return member;
                }
            }

            pos += operandSize;
        }
    }

    private static int GetOperandSize(OperandType operandType, byte[] il, int pos) => operandType switch
    {
        OperandType.InlineNone => 0,
        OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
        OperandType.InlineVar => 2,
        OperandType.InlineI8 or OperandType.InlineR => 8,
        OperandType.InlineSwitch => 4 + (4 * BitConverter.ToInt32(il, pos)),
        _ => 4,
    };

    private static MemberInfo? TryResolve(Module module, int token, Type[]? typeArgs, Type[]? methodArgs)
    {
        try
        {
            return module.ResolveMember(token, typeArgs, methodArgs);
        }
        catch (Exception ex) when (ex is ArgumentException or BadImageFormatException or TypeLoadException
            or FileNotFoundException or MissingMemberException)
        {
            return null;
        }
    }
}
