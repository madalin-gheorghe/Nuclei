using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;

const string OriginalHash = "A146083ADB37AB945CE7B3F22AA27CB4F08378A73CB7A7ED4C95461D4354C645";
const string PriorityType = "Nuclei2.Properties.Nuclei2Icon";
const string HelperName = "IsSupportedRhino";
const string BannerLoaderName = "LoadLegacyBanner";

try
{
if (args.Length != 3)
    throw new ArgumentException("Expected original input .gha, a new output .gha path, and the banner .dll to embed.");

string input = Path.GetFullPath(args[0]);
string output = Path.GetFullPath(args[1]);
if (string.Equals(input, output, StringComparison.OrdinalIgnoreCase) || File.Exists(output))
    throw new InvalidOperationException("Output must be a new path, different from the input.");

byte[] originalBytes = File.ReadAllBytes(input);
string originalHash = Convert.ToHexString(SHA256.HashData(originalBytes));
if (originalHash != OriginalHash)
    throw new InvalidOperationException($"Unrecognized input SHA-256: {originalHash}");

using var originalStream = new MemoryStream(originalBytes, writable: false);
using AssemblyDefinition original = AssemblyDefinition.ReadAssembly(originalStream);
using var workingStream = new MemoryStream(originalBytes, writable: false);
using AssemblyDefinition working = AssemblyDefinition.ReadAssembly(workingStream);
ModuleDefinition module = working.MainModule;
byte[] bannerBytes = File.ReadAllBytes(Path.GetFullPath(args[2]));
if (working.Name.HasPublicKey || module.Attributes.HasFlag(ModuleAttributes.StrongNameSigned))
    throw new InvalidOperationException("Expected the unsigned original assembly.");

TypeDefinition priorityType = module.GetType(PriorityType)
    ?? throw new InvalidOperationException("Original assembly-priority type is missing.");
MethodDefinition priority = priorityType.Methods.Single(m => m.Name == "PriorityLoad");
if (priorityType.Methods.Any(m => m.Name == HelperName) || priority.Body.ExceptionHandlers.Count != 0)
    throw new InvalidOperationException("Unexpected original loading-hook structure.");

FindCategoryShortName(priority, "Nuc").Operand = "N2";

// Reference the original RhinoCommon 6.10 dependency, not the patch tool's runtime.
AssemblyNameReference rhino = module.AssemblyReferences.Single(r => r.Name == "RhinoCommon");
var rhinoApp = new TypeReference("Rhino", "RhinoApp", module, rhino);
var exeVersion = new MethodReference("get_ExeVersion", module.TypeSystem.Int32, rhinoApp)
{
    HasThis = false
};
var writeLine = new MethodReference("WriteLine", module.TypeSystem.Void, rhinoApp)
{
    HasThis = false
};
writeLine.Parameters.Add(new ParameterDefinition(module.TypeSystem.String));

var helper = new MethodDefinition(HelperName,
    MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig,
    module.TypeSystem.Boolean);
helper.Parameters.Add(new ParameterDefinition("major", ParameterAttributes.None, module.TypeSystem.Int32));
priorityType.Methods.Add(helper);
ILProcessor helperIl = helper.Body.GetILProcessor();
Instruction allow = Instruction.Create(OpCodes.Ldc_I4_1);
helperIl.Append(Instruction.Create(OpCodes.Ldarg_0));
helperIl.Append(Instruction.Create(OpCodes.Ldc_I4_6));
helperIl.Append(Instruction.Create(OpCodes.Beq_S, allow));
helperIl.Append(Instruction.Create(OpCodes.Ldarg_0));
helperIl.Append(Instruction.Create(OpCodes.Ldc_I4_7));
helperIl.Append(Instruction.Create(OpCodes.Beq_S, allow));
helperIl.Append(Instruction.Create(OpCodes.Ldarg_0));
helperIl.Append(Instruction.Create(OpCodes.Ldc_I4_8));
helperIl.Append(Instruction.Create(OpCodes.Beq_S, allow));
helperIl.Append(Instruction.Create(OpCodes.Ldarg_0));
helperIl.Append(Instruction.Create(OpCodes.Ldc_I4, 9));
helperIl.Append(Instruction.Create(OpCodes.Ceq));
helperIl.Append(Instruction.Create(OpCodes.Ret));
helperIl.Append(allow);
helperIl.Append(Instruction.Create(OpCodes.Ret));

MethodDefinition bannerLoader = BannerEmbedding.Add(module, priorityType, bannerBytes);
Instruction firstOriginal = priority.Body.Instructions[0];
Instruction checkBannerHost = Instruction.Create(OpCodes.Call, exeVersion);
Instruction[] prefix =
[
    Instruction.Create(OpCodes.Call, exeVersion),
    Instruction.Create(OpCodes.Call, helper),
    Instruction.Create(OpCodes.Brtrue, checkBannerHost),
    Instruction.Create(OpCodes.Ldstr,
        "Nuclei 2 supports Rhino 6, 7, 8 and 9."),
    Instruction.Create(OpCodes.Call, writeLine),
    Instruction.Create(OpCodes.Ldc_I4_1), // GH_LoadingInstruction.Abort
    Instruction.Create(OpCodes.Ret),
    checkBannerHost,
    Instruction.Create(OpCodes.Ldc_I4_8),
    Instruction.Create(OpCodes.Blt, firstOriginal), // Supported hosts 6/7 skip the banner; 8/9 load it.
    Instruction.Create(OpCodes.Call, bannerLoader)
];
ILProcessor priorityIl = priority.Body.GetILProcessor();
foreach (Instruction instruction in prefix)
    priorityIl.InsertBefore(firstOriginal, instruction);

using var patchedStream = new MemoryStream();
working.Write(patchedStream);
byte[] patchedBytes = patchedStream.ToArray();
using var verifiedStream = new MemoryStream(patchedBytes, writable: false);
using AssemblyDefinition verified = AssemblyDefinition.ReadAssembly(verifiedStream);
VerifyPreservation(original, verified, prefix.Length);
Equal("embedded banner bytes", Convert.ToHexString(SHA256.HashData(bannerBytes)),
    Convert.ToHexString(SHA256.HashData(((EmbeddedResource)verified.MainModule.Resources.Single(r => r.Name == BannerEmbedding.ResourceName)).GetResourceData())));

Directory.CreateDirectory(Path.GetDirectoryName(output)!);
using (FileStream destination = new(output, FileMode.CreateNew, FileAccess.Write, FileShare.None))
    destination.Write(patchedBytes);

Console.WriteLine($"Input SHA-256:  {originalHash}");
Console.WriteLine($"Output SHA-256: {Convert.ToHexString(SHA256.HashData(patchedBytes))}");
Console.WriteLine($"Preserved {AllTypes(original.MainModule).Count()} types, " +
    $"{AllTypes(original.MainModule).Sum(t => t.Methods.Count) - 1} unchanged original methods, " +
    "the original PriorityLoad body except the Nuc-to-N2 tab abbreviation, all resources and metadata.");
Console.WriteLine($"Written: {output}");
return 0;
}
catch (Exception error)
{
    Console.Error.WriteLine(error.Message);
    return 1;
}

static void VerifyPreservation(AssemblyDefinition before, AssemblyDefinition after, int prefixCount)
{
    Equal("assembly identity", before.Name.FullName, after.Name.FullName);
    Equal("assembly attributes", Attributes(before), Attributes(after));
    ModuleDefinition a = before.MainModule, b = after.MainModule;
    Equal("module properties", $"{a.Name}|{a.Kind}|{a.RuntimeVersion}|{a.Architecture}|{a.Attributes}|{a.Mvid}",
        $"{b.Name}|{b.Kind}|{b.RuntimeVersion}|{b.Architecture}|{b.Attributes}|{b.Mvid}");
    Equal("module attributes", Attributes(a), Attributes(b));
    Equal("assembly references", Lines(a.AssemblyReferences.Select(r => r.FullName)),
        Lines(b.AssemblyReferences.Select(r => r.FullName)));
    Equal("module references", Lines(a.ModuleReferences.Select(r => r.Name)),
        Lines(b.ModuleReferences.Select(r => r.Name)));
    Equal("resources", Lines(a.Resources.Select(ResourceSnapshot)),
        Lines(b.Resources.Where(r => r.Name != BannerEmbedding.ResourceName).Select(ResourceSnapshot)));
    if (b.Resources.Count != a.Resources.Count + 1)
        throw new InvalidOperationException("Unexpected resource count.");
    TypeDefinition[] oldTypes = AllTypes(a).ToArray(), newTypes = AllTypes(b).ToArray();
    Equal("type list", Lines(oldTypes.Select(t => t.FullName)), Lines(newTypes.Select(t => t.FullName)));

    foreach ((TypeDefinition oldType, TypeDefinition newType) in oldTypes.Zip(newTypes))
    {
        Equal($"type metadata {oldType.FullName}", TypeSnapshot(oldType), TypeSnapshot(newType));
        MethodDefinition[] newOriginalMethods = newType.Methods
            .Where(m => !(newType.FullName == PriorityType && (m.Name == HelperName || m.Name == BannerLoaderName))).ToArray();
        Equal($"methods in {oldType.FullName}", Lines(oldType.Methods.Select(m => m.FullName)),
            Lines(newOriginalMethods.Select(m => m.FullName)));
        foreach ((MethodDefinition oldMethod, MethodDefinition newMethod) in oldType.Methods.Zip(newOriginalMethods))
        {
            int skip = oldType.FullName == PriorityType && oldMethod.Name == "PriorityLoad" ? prefixCount : 0;
            Equal($"method {oldMethod.FullName}", MethodSnapshot(oldMethod, renameCategory: skip != 0), MethodSnapshot(newMethod, skip));
        }
    }
    MethodDefinition helper = b.GetType(PriorityType).Methods.Single(m => m.Name == HelperName);
    if (!helper.IsPrivate || !helper.IsStatic || helper.Parameters.Count != 1 ||
        helper.Parameters[0].ParameterType.FullName != "System.Int32" ||
        helper.ReturnType.FullName != "System.Boolean" ||
        newTypes.Sum(t => t.Methods.Count) != oldTypes.Sum(t => t.Methods.Count) + 2)
        throw new InvalidOperationException("Unexpected guard-helper contract.");
}

static IEnumerable<TypeDefinition> AllTypes(ModuleDefinition module) => module.Types.SelectMany(Flatten);
static IEnumerable<TypeDefinition> Flatten(TypeDefinition type) =>
    new[] { type }.Concat(type.NestedTypes.SelectMany(Flatten));
static string Lines(IEnumerable<string> values) => string.Join("\n", values);
static void Equal(string subject, string expected, string actual)
{
    if (!string.Equals(expected, actual, StringComparison.Ordinal))
        throw new InvalidOperationException($"Preservation check failed: {subject}");
}

static string Attributes(ICustomAttributeProvider provider) => Lines(provider.CustomAttributes.Select(a =>
    a.Constructor.FullName + "(" + string.Join(",", a.ConstructorArguments.Select(AttributeArgument)) + ")" +
    string.Join(",", a.Fields.Select(f => f.Name + "=" + AttributeArgument(f.Argument))) + ";" +
    string.Join(",", a.Properties.Select(p => p.Name + "=" + AttributeArgument(p.Argument)))));
static string AttributeArgument(CustomAttributeArgument argument) => argument.Type.FullName + ":" +
    (argument.Value is CustomAttributeArgument[] values ? string.Join(",", values.Select(AttributeArgument)) :
        argument.Value is CustomAttributeArgument nested ? AttributeArgument(nested) : Scalar(argument.Value));
static string Scalar(object? value) => value switch
{
    null => "<null>",
    string text => Convert.ToBase64String(Encoding.UTF8.GetBytes(text)),
    byte[] bytes => Convert.ToHexString(bytes),
    IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
    _ => value.ToString() ?? "<null>"
};

static string ResourceSnapshot(Resource resource)
{
    if (resource is not EmbeddedResource embedded)
        throw new InvalidOperationException("Unexpected non-embedded resource in original assembly.");
    return $"{resource.Name}|{resource.Attributes}|{Convert.ToHexString(SHA256.HashData(embedded.GetResourceData()))}";
}

static string TypeSnapshot(TypeDefinition type) =>
    $"{type.FullName}|{type.Attributes}|{type.BaseType?.FullName}|{type.PackingSize}|{type.ClassSize}\n" +
    Attributes(type) + "\n" +
    Lines(type.Interfaces.Select(i => i.InterfaceType.FullName + Attributes(i))) + "\n" +
    Generics(type.GenericParameters) + "\n" +
    Lines(type.Fields.Select(f => $"{f.FullName}|{f.Attributes}|{f.HasConstant}|{Scalar(f.Constant)}|" +
        $"{f.Offset}|{Convert.ToHexString(f.InitialValue)}|{Attributes(f)}")) + "\n" +
    Lines(type.Properties.Select(p => $"{p.FullName}|{p.Attributes}|{p.GetMethod?.FullName}|" +
        $"{p.SetMethod?.FullName}|{p.HasConstant}|{Scalar(p.Constant)}|{Attributes(p)}")) + "\n" +
    Lines(type.Events.Select(e => $"{e.FullName}|{e.Attributes}|{e.AddMethod?.FullName}|" +
        $"{e.RemoveMethod?.FullName}|{e.InvokeMethod?.FullName}|{Attributes(e)}"));

static string Generics(IEnumerable<GenericParameter> parameters) => Lines(parameters.Select(p =>
    $"{p.Name}|{p.Attributes}|{Attributes(p)}|" +
    string.Join(",", p.Constraints.Select(c => c.ConstraintType.FullName + Attributes(c)))));

static Instruction FindCategoryShortName(MethodDefinition method, string expected)
{
    Instruction call = method.Body.Instructions.Single(i =>
        i.OpCode == OpCodes.Callvirt && i.Operand is MethodReference target &&
        target.DeclaringType.FullName == "Grasshopper.Kernel.GH_ComponentServer" &&
        target.Name == "AddCategoryShortName");
    Instruction value = call.Previous;
    if (value?.OpCode != OpCodes.Ldstr || !Equals(value.Operand, expected) ||
        value.Previous?.OpCode != OpCodes.Ldstr || !Equals(value.Previous.Operand, "Nuclei2"))
        throw new InvalidOperationException("Unexpected Nuclei2 category short-name registration.");
    return value;
}

static string MethodSnapshot(MethodDefinition method, int skip = 0, bool renameCategory = false)
{
    string metadata = $"{method.FullName}|{method.Attributes}|{method.ImplAttributes}|{method.CallingConvention}|" +
        $"{method.SemanticsAttributes}|{Attributes(method)}|{Attributes(method.MethodReturnType)}\n" +
        Generics(method.GenericParameters) + "\n" +
        Lines(method.Parameters.Select(p => $"{p.Name}|{p.ParameterType.FullName}|{p.Attributes}|" +
            $"{p.HasConstant}|{Scalar(p.Constant)}|{Attributes(p)}")) + "\n" +
        Lines(method.Overrides.Select(o => o.FullName));
    if (!method.HasBody) return metadata + "\n<bodyless>";
    var body = method.Body;
    var instructions = body.Instructions;
    Instruction? renamedCategory = renameCategory ? FindCategoryShortName(method, "Nuc") : null;
    int Index(Instruction? instruction) => instruction is null ? -1 : instructions.IndexOf(instruction) - skip;
    string Operand(object? operand) => operand switch
    {
        Instruction instruction => "target:" + Index(instruction),
        Instruction[] targets => "targets:" + string.Join(",", targets.Select(Index)),
        VariableDefinition variable => "local:" + variable.Index,
        ParameterDefinition parameter => "arg:" + parameter.Index,
        MemberReference member => member.FullName,
        CallSite call => call.FullName,
        _ => Scalar(operand)
    };
    return metadata + $"\n{body.InitLocals}\n" + Lines(body.Variables.Select(v => v.VariableType.FullName)) + "\n" +
        Lines(instructions.Skip(skip).Select(i => i.OpCode.Name + " " + Operand(i == renamedCategory ? "N2" : i.Operand))) + "\n" +
        Lines(body.ExceptionHandlers.Select(h => $"{h.HandlerType}|{h.CatchType?.FullName}|" +
            $"{Index(h.TryStart)}|{Index(h.TryEnd)}|{Index(h.HandlerStart)}|{Index(h.HandlerEnd)}|{Index(h.FilterStart)}"));
}
