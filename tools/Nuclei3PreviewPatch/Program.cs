using Mono.Cecil;
using Mono.Cecil.Cil;

if (args.Length != 2)
{
    throw new ArgumentException("Expected input and output assembly paths.");
}

using AssemblyDefinition assembly = AssemblyDefinition.ReadAssembly(args[0], new ReaderParameters
{
    InMemory = true,
    ReadingMode = ReadingMode.Immediate
});

TypeDefinition preview = assembly.MainModule.GetType("Nuclei3.Preview_Particle")
    ?? throw new InvalidOperationException("Preview_Particle type not found.");
MethodDefinition constructor = preview.Methods.Single(method => method.IsConstructor && !method.IsStatic);
int renamedStrings = 0;
foreach (Instruction instruction in constructor.Body.Instructions)
{
    if (instruction.OpCode != OpCodes.Ldstr) continue;
    if ((string)instruction.Operand == "Particle Preview Settings")
    {
        instruction.Operand = "Particle Preview";
        renamedStrings++;
    }
    else if ((string)instruction.Operand == "Sets Up Dynamic Particle Preview Settings")
    {
        instruction.Operand = "Displays particles in the Rhino viewport";
        renamedStrings++;
    }
}

TypeDefinition conduit = assembly.MainModule.GetType("Nuclei3.ParticlePreviewDisplayConduit")
    ?? throw new InvalidOperationException("ParticlePreviewDisplayConduit type not found.");
MethodDefinition draw = conduit.Methods.Single(method => method.Name == "PostDrawObjects");
int disabledBackgroundLoads = 0;
foreach (Instruction instruction in draw.Body.Instructions)
{
    if (instruction.OpCode == OpCodes.Ldsfld &&
        instruction.Operand is FieldReference field &&
        field.Name == "tridimensional" &&
        field.DeclaringType.FullName == "Nuclei3.Globals")
    {
        instruction.OpCode = OpCodes.Ldc_I4_1;
        instruction.Operand = null;
        disabledBackgroundLoads++;
    }
}

if (renamedStrings != 2 || disabledBackgroundLoads != 1)
{
    throw new InvalidOperationException(
        $"Unexpected patch count: renamed={renamedStrings}, background={disabledBackgroundLoads}.");
}

assembly.Write(args[1]);
Console.WriteLine($"Patched {assembly.Name.Name}: renamed={renamedStrings}, background={disabledBackgroundLoads}.");
