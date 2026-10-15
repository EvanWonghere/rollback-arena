using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;

namespace Arena.Tests.Architecture;

/// <summary>
/// 架构守卫 V3：读程序集的元数据和 IL，找出会破坏确定性的用法。
/// 只依赖框架自带的 System.Reflection.Metadata，不加载被扫描的程序集。
/// </summary>
/// <remarks>
/// 扫描范围：每个类型（含编译器生成的嵌套类型）的基类、接口、字段、方法签名、局部变量、
/// 泛型约束、特性，以及方法体里每条指令引用的类型、字段、方法和浮点指令。
/// 已知盲区（要靠代码审查）：
/// - 把 Dictionary/HashSet 当成 IEnumerable、ICollection、ISet 等通用接口传出去再遍历（比如交给 LINQ），
///   IL 里只看得到接口上的调用，分不出背后是不是哈希容器；
/// - string.GetHashCode() 每个进程的种子不同，但 C# 把它编译成对 object.GetHashCode 的调用，无法和其他类型区分。
/// </remarks>
public static class DeterminismScanner
{
    // 内部用浮点存储和运算的类型：签名里看不到 float，所以要按名字禁。
    static readonly HashSet<string> FloatTypes = new()
    {
        "System.Single", "System.Double", "System.Half",
        "System.Numerics.Vector2", "System.Numerics.Vector3", "System.Numerics.Vector4",
        "System.Numerics.Quaternion", "System.Numerics.Plane", "System.Numerics.Matrix3x2",
        "System.Numerics.Matrix4x4", "System.Numerics.Complex",
    };

    static readonly Dictionary<string, string> ForbiddenTypes = new()
    {
        ["System.Random"] = "随机数要用模拟层自己的、带种子的确定性随机数",
        ["System.DateTime"] = "模拟只能依赖帧号，不能读真实时间",
        ["System.DateTimeOffset"] = "模拟只能依赖帧号，不能读真实时间",
        ["System.Diagnostics.Stopwatch"] = "模拟只能依赖帧号，不能读真实时间",
        ["System.Security.Cryptography.RandomNumberGenerator"] = "随机数要用模拟层自己的、带种子的确定性随机数",
        ["System.HashCode"] = "种子每个进程都不同，两端算出的哈希不一致；校验和要用自己的确定性哈希",
        ["System.Collections.Hashtable"] = "遍历顺序取决于哈希和增删历史；模拟层用数组或定长结构",
        ["System.Collections.Concurrent.ConcurrentDictionary`2"] = "遍历顺序不确定，模拟层也不该有并发容器",
    };

    static readonly Dictionary<(string Type, string Member), string> ForbiddenMembers = new()
    {
        [("System.Environment", "get_TickCount")] = "模拟只能依赖帧号，不能读真实时间",
        [("System.Environment", "get_TickCount64")] = "模拟只能依赖帧号，不能读真实时间",
        [("System.Guid", "NewGuid")] = "每次调用结果都不同；需要标识就用确定性的计数器",
    };

    // 遍历顺序取决于哈希和插入/删除历史的容器，以及会把它们交出去遍历的成员。
    static readonly HashSet<(string Type, string Member)> UnorderedEnumeration = new()
    {
        ("System.Collections.Generic.Dictionary`2", "GetEnumerator"),
        ("System.Collections.Generic.Dictionary`2", "get_Keys"),
        ("System.Collections.Generic.Dictionary`2", "get_Values"),
        ("System.Collections.Generic.Dictionary`2/KeyCollection", "GetEnumerator"),
        ("System.Collections.Generic.Dictionary`2/ValueCollection", "GetEnumerator"),
        ("System.Collections.Generic.Dictionary`2/KeyCollection", "CopyTo"),
        ("System.Collections.Generic.Dictionary`2/ValueCollection", "CopyTo"),
        ("System.Collections.Generic.IDictionary`2", "get_Keys"),
        ("System.Collections.Generic.IDictionary`2", "get_Values"),
        ("System.Collections.Generic.IReadOnlyDictionary`2", "get_Keys"),
        ("System.Collections.Generic.IReadOnlyDictionary`2", "get_Values"),
        ("System.Collections.Generic.HashSet`1", "GetEnumerator"),
        ("System.Collections.Generic.HashSet`1", "CopyTo"),
        ("System.Collections.Generic.HashSet`1", "RemoveWhere"),
    };

    static readonly HashSet<ILOpCode> FloatOpCodes = new()
    {
        ILOpCode.Ldc_r4, ILOpCode.Ldc_r8, ILOpCode.Conv_r4, ILOpCode.Conv_r8, ILOpCode.Conv_r_un,
        ILOpCode.Ldind_r4, ILOpCode.Ldind_r8, ILOpCode.Stind_r4, ILOpCode.Stind_r8,
        ILOpCode.Ldelem_r4, ILOpCode.Ldelem_r8, ILOpCode.Stelem_r4, ILOpCode.Stelem_r8, ILOpCode.Ckfinite,
    };

    static readonly Dictionary<short, OperandType> OperandTypes = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(f => (OpCode)f.GetValue(null)!)
        .ToDictionary(o => o.Value, o => o.OperandType);

    public sealed record Violation(string Location, string Rule, string Detail)
    {
        public override string ToString() => $"{Location}: [{Rule}] {Detail}";
    }

    public sealed record Result(int TypesScanned, int MethodBodiesScanned, IReadOnlyList<Violation> Violations);

    /// <param name="includeType">按顶层类型的完整名筛选；为空时扫描全部类型。</param>
    public static Result Scan(string assemblyPath, Func<string, bool>? includeType = null)
    {
        using var stream = File.OpenRead(assemblyPath);
        using var pe = new PEReader(stream);
        var md = pe.GetMetadataReader();
        var scan = new Scanner(md, pe);

        int types = 0, bodies = 0;
        foreach (var handle in md.TypeDefinitions)
        {
            var type = md.GetTypeDefinition(handle);
            if (includeType != null && !includeType(TopLevelName(md, type)))
                continue;
            types++;
            bodies += scan.Type(handle, type);
        }
        return new Result(types, bodies, scan.Violations);
    }

    static string TopLevelName(MetadataReader md, TypeDefinition type)
    {
        while (type.IsNested)
            type = md.GetTypeDefinition(type.GetDeclaringType());
        return Names.Of(md, type);
    }

    sealed class Scanner(MetadataReader md, PEReader pe)
    {
        readonly SigProvider sigs = new(md);
        public readonly List<Violation> Violations = new();

        void Report(string location, string rule, string detail) => Violations.Add(new(location, rule, detail));

        public int Type(TypeDefinitionHandle handle, TypeDefinition type)
        {
            string typeName = Names.Of(md, type);
            if (!type.BaseType.IsNil)
                Check(typeName, "基类", sigs.FromHandle(type.BaseType));
            foreach (var i in type.GetInterfaceImplementations())
                Check(typeName, "接口", sigs.FromHandle(md.GetInterfaceImplementation(i).Interface));
            GenericParams(typeName, type.GetGenericParameters());
            Attributes(typeName, type.GetCustomAttributes());

            foreach (var f in type.GetFields())
            {
                var field = md.GetFieldDefinition(f);
                string loc = $"{typeName}.{md.GetString(field.Name)}";
                Check(loc, "字段类型", field.DecodeSignature(sigs, null));
                Attributes(loc, field.GetCustomAttributes());
            }

            int bodies = 0;
            foreach (var m in type.GetMethods())
            {
                var method = md.GetMethodDefinition(m);
                string loc = $"{typeName}.{md.GetString(method.Name)}";
                var sig = method.DecodeSignature(sigs, null);
                Check(loc, "返回值", sig.ReturnType);
                foreach (var p in sig.ParameterTypes)
                    Check(loc, "参数", p);
                GenericParams(loc, method.GetGenericParameters());
                Attributes(loc, method.GetCustomAttributes());

                if (method.RelativeVirtualAddress != 0)
                {
                    Body(loc, pe.GetMethodBody(method.RelativeVirtualAddress));
                    bodies++;
                }
            }
            return bodies;
        }

        void GenericParams(string loc, GenericParameterHandleCollection handles)
        {
            foreach (var g in handles)
                foreach (var c in md.GetGenericParameter(g).GetConstraints())
                    Check(loc, "泛型约束", sigs.FromHandle(md.GetGenericParameterConstraint(c).Type));
        }

        void Attributes(string loc, CustomAttributeHandleCollection handles)
        {
            foreach (var a in handles)
                Member(loc, "特性", md.GetCustomAttribute(a).Constructor);
        }

        void Body(string loc, MethodBodyBlock body)
        {
            if (!body.LocalSignature.IsNil)
            {
                var locals = md.GetStandaloneSignature(body.LocalSignature).DecodeLocalSignature(sigs, null);
                foreach (var l in locals)
                    Check(loc, "局部变量", l);
            }

            var il = body.GetILReader();
            while (il.RemainingBytes > 0)
            {
                int offset = il.Offset;
                short value = il.ReadByte();
                if (value == 0xFE)
                    value = unchecked((short)(0xFE00 | il.ReadByte()));
                var op = (ILOpCode)(ushort)value;
                string at = $"{loc} IL_{offset:X4}";

                if (FloatOpCodes.Contains(op))
                    Report(at, "浮点", $"指令 {op.ToString().ToLowerInvariant().Replace('_', '.')}");

                switch (OperandTypes[value])
                {
                    case OperandType.InlineNone:
                        break;
                    case OperandType.ShortInlineBrTarget:
                    case OperandType.ShortInlineI:
                    case OperandType.ShortInlineVar:
                        il.ReadByte();
                        break;
                    case OperandType.InlineVar:
                        il.ReadUInt16();
                        break;
                    case OperandType.InlineBrTarget:
                    case OperandType.InlineI:
                    case OperandType.InlineString:
                    case OperandType.ShortInlineR:
                        il.ReadInt32();
                        break;
                    case OperandType.InlineI8:
                    case OperandType.InlineR:
                        il.ReadInt64();
                        break;
                    case OperandType.InlineSwitch:
                        int count = il.ReadInt32();
                        il.Offset += count * 4;
                        break;
                    case OperandType.InlineField:
                    case OperandType.InlineMethod:
                    case OperandType.InlineType:
                    case OperandType.InlineTok:
                    case OperandType.InlineSig:
                        Token(at, MetadataTokens.EntityHandle(il.ReadInt32()));
                        break;
                    default:
                        throw new InvalidOperationException($"{at}: 未知的操作数类型 {OperandTypes[value]}");
                }
            }
        }

        void Token(string at, EntityHandle handle)
        {
            switch (handle.Kind)
            {
                case HandleKind.TypeDefinition:
                case HandleKind.TypeReference:
                case HandleKind.TypeSpecification:
                    Check(at, "引用类型", sigs.FromHandle(handle));
                    break;
                case HandleKind.FieldDefinition:
                    var field = md.GetFieldDefinition((FieldDefinitionHandle)handle);
                    Check(at, "访问字段", field.DecodeSignature(sigs, null));
                    break;
                case HandleKind.StandaloneSignature:
                    var sig = md.GetStandaloneSignature((StandaloneSignatureHandle)handle).DecodeMethodSignature(sigs, null);
                    CheckMethodSig(at, "间接调用", sig);
                    break;
                default:
                    Member(at, "调用", handle);
                    break;
            }
        }

        /// <summary>检查 MethodDef / MemberRef / MethodSpec：所属类型、签名、泛型实参，以及禁用成员。</summary>
        void Member(string at, string what, EntityHandle handle)
        {
            switch (handle.Kind)
            {
                case HandleKind.MethodDefinition:
                    var def = md.GetMethodDefinition((MethodDefinitionHandle)handle);
                    CheckMethodSig(at, what, def.DecodeSignature(sigs, null));
                    break;
                case HandleKind.MethodSpecification:
                    var spec = md.GetMethodSpecification((MethodSpecificationHandle)handle);
                    foreach (var arg in spec.DecodeSignature(sigs, null))
                        Check(at, $"{what}的泛型实参", arg);
                    Member(at, what, spec.Method);
                    break;
                case HandleKind.MemberReference:
                    var mref = md.GetMemberReference((MemberReferenceHandle)handle);
                    string name = md.GetString(mref.Name);
                    var parent = mref.Parent.Kind is HandleKind.TypeReference or HandleKind.TypeDefinition or HandleKind.TypeSpecification
                        ? sigs.FromHandle(mref.Parent)
                        : null;
                    if (parent != null)
                    {
                        Check(at, $"{what} {name} 所属类型", parent);
                        if (ForbiddenMembers.TryGetValue((parent.Name, name), out var why))
                            Report(at, "禁用成员", $"{parent.Name}.{name}：{why}");
                        if (UnorderedEnumeration.Contains((parent.Name, name)))
                            Report(at, "无序遍历", $"{parent.Name}.{name}：遍历顺序取决于哈希和增删历史，各端可能不一致；改用数组或按键排序后遍历");
                    }
                    if (mref.GetKind() == MemberReferenceKind.Field)
                        Check(at, $"访问字段 {name}", mref.DecodeFieldSignature(sigs, null));
                    else
                        CheckMethodSig(at, $"{what} {name}", mref.DecodeMethodSignature(sigs, null));
                    break;
            }
        }

        void CheckMethodSig(string at, string what, MethodSignature<Sig> sig)
        {
            Check(at, $"{what}的返回值", sig.ReturnType);
            foreach (var p in sig.ParameterTypes)
                Check(at, $"{what}的参数", p);
        }

        void Check(string loc, string what, Sig sig)
        {
            foreach (var name in sig.AllNames())
            {
                if (FloatTypes.Contains(name))
                    Report(loc, "浮点", $"{what}用到了 {name}{(sig.Name == name ? "" : $"（{sig}）")}；模拟层只能用定点数");
                else if (ForbiddenTypes.TryGetValue(name, out var why))
                    Report(loc, "禁用类型", $"{what}用到了 {name}：{why}");
            }
        }
    }

    /// <summary>解码后的类型：名字加泛型实参或元素类型。数组、指针、引用用 "[]"、"*"、"&amp;" 表示。</summary>
    sealed record Sig(string Name, ImmutableArray<Sig> Args)
    {
        public static Sig Named(string name) => new(name, ImmutableArray<Sig>.Empty);

        public IEnumerable<string> AllNames()
        {
            yield return Name;
            foreach (var a in Args)
                foreach (var n in a.AllNames())
                    yield return n;
        }

        public override string ToString() => Args.IsEmpty ? Name : $"{Name}<{string.Join(", ", Args)}>";
    }

    sealed class SigProvider(MetadataReader md) : ISignatureTypeProvider<Sig, object?>
    {
        public Sig FromHandle(EntityHandle handle) => handle.Kind switch
        {
            HandleKind.TypeDefinition => GetTypeFromDefinition(md, (TypeDefinitionHandle)handle, 0),
            HandleKind.TypeReference => GetTypeFromReference(md, (TypeReferenceHandle)handle, 0),
            HandleKind.TypeSpecification => GetTypeFromSpecification(md, null, (TypeSpecificationHandle)handle, 0),
            _ => throw new ArgumentOutOfRangeException(nameof(handle), handle.Kind, null),
        };

        public Sig GetPrimitiveType(PrimitiveTypeCode typeCode) => Sig.Named(typeCode switch
        {
            PrimitiveTypeCode.Single => "System.Single",
            PrimitiveTypeCode.Double => "System.Double",
            PrimitiveTypeCode.IntPtr => "System.IntPtr",
            PrimitiveTypeCode.UIntPtr => "System.UIntPtr",
            PrimitiveTypeCode.TypedReference => "System.TypedReference",
            _ => "System." + typeCode,
        });

        public Sig GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind) =>
            Sig.Named(Names.Of(reader, reader.GetTypeDefinition(handle)));

        public Sig GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind) =>
            Sig.Named(Names.Of(reader, handle));

        public Sig GetTypeFromSpecification(MetadataReader reader, object? context, TypeSpecificationHandle handle, byte rawTypeKind) =>
            reader.GetTypeSpecification(handle).DecodeSignature(this, context);

        public Sig GetGenericInstantiation(Sig genericType, ImmutableArray<Sig> typeArguments) => new(genericType.Name, typeArguments);
        public Sig GetSZArrayType(Sig elementType) => new("[]", ImmutableArray.Create(elementType));
        public Sig GetArrayType(Sig elementType, ArrayShape shape) => new("[]", ImmutableArray.Create(elementType));
        public Sig GetPointerType(Sig elementType) => new("*", ImmutableArray.Create(elementType));
        public Sig GetByReferenceType(Sig elementType) => new("&", ImmutableArray.Create(elementType));
        public Sig GetPinnedType(Sig elementType) => elementType;
        public Sig GetModifiedType(Sig modifier, Sig unmodifiedType, bool isRequired) => unmodifiedType;
        public Sig GetGenericTypeParameter(object? context, int index) => Sig.Named("!" + index);
        public Sig GetGenericMethodParameter(object? context, int index) => Sig.Named("!!" + index);

        public Sig GetFunctionPointerType(MethodSignature<Sig> signature) =>
            new("method*", signature.ParameterTypes.Insert(0, signature.ReturnType));
    }

    static class Names
    {
        public static string Of(MetadataReader md, TypeDefinition type)
        {
            string name = md.GetString(type.Name);
            if (type.IsNested)
                return Of(md, md.GetTypeDefinition(type.GetDeclaringType())) + "/" + name;
            string ns = md.GetString(type.Namespace);
            return ns.Length == 0 ? name : ns + "." + name;
        }

        public static string Of(MetadataReader md, TypeReferenceHandle handle)
        {
            var type = md.GetTypeReference(handle);
            string name = md.GetString(type.Name);
            if (type.ResolutionScope.Kind == HandleKind.TypeReference)
                return Of(md, (TypeReferenceHandle)type.ResolutionScope) + "/" + name;
            string ns = md.GetString(type.Namespace);
            return ns.Length == 0 ? name : ns + "." + name;
        }
    }
}
