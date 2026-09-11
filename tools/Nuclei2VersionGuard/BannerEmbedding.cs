using Mono.Cecil;
using Mono.Cecil.Cil;

internal static class BannerEmbedding
{
    internal const string ResourceName = "Nuclei2.Embedded.OldBanner.dll";

    // Keep all loader references on the original .NET Framework core library.
    // No external banner file or dependency on the patch tool's runtime is added.
    internal static MethodDefinition Add(ModuleDefinition module, TypeDefinition owner, byte[] bytes)
    {
        using var source = AssemblyDefinition.ReadAssembly(new MemoryStream(bytes, writable: false));
        var banner = source.MainModule.GetType("Nuclei2.OldBanner.OldBannerPriority")
            ?? throw new InvalidOperationException("Banner entry point is missing.");
        if (!banner.Methods.Any(m => m.Name == "Register" && m.IsPublic && m.IsStatic && m.Parameters.Count == 0) ||
            source.MainModule.Types.Any(t => t.BaseType?.FullName is "Grasshopper.Kernel.GH_AssemblyPriority" or "Grasshopper.Kernel.GH_AssemblyInfo"))
            throw new InvalidOperationException("Expected an embedded banner library without Grasshopper registration hooks.");

        module.Resources.Add(new EmbeddedResource(ResourceName, ManifestResourceAttributes.Private, bytes));
        var core = module.TypeSystem.CoreLibrary;
        TypeReference Type(string ns, string name) => new(ns, name, module, core);
        var assembly = Type("System.Reflection", "Assembly");
        var stream = Type("System.IO", "Stream");
        var memory = Type("System.IO", "MemoryStream");
        var runtimeType = Type("System", "Type");
        var methodInfo = Type("System.Reflection", "MethodInfo");
        var methodBase = Type("System.Reflection", "MethodBase");
        var appDomain = Type("System", "AppDomain");
        MethodReference Method(TypeReference type, string name, TypeReference result, bool instance, params TypeReference[] parameters)
        {
            var method = new MethodReference(name, result, type) { HasThis = instance };
            foreach (var parameter in parameters) method.Parameters.Add(new ParameterDefinition(parameter));
            return method;
        }

        var loader = new MethodDefinition("LoadLegacyBanner",
            MethodAttributes.Private | MethodAttributes.Static | MethodAttributes.HideBySig, module.TypeSystem.Void);
        owner.Methods.Add(loader);
        loader.Body.InitLocals = true;
        var resource = new VariableDefinition(stream);
        var buffer = new VariableDefinition(memory);
        loader.Body.Variables.Add(resource);
        loader.Body.Variables.Add(buffer);
        var il = loader.Body.GetILProcessor();
        const string loadedKey = "Nuclei2.Embedded.OldBanner.Loaded";
        var end = Instruction.Create(OpCodes.Ret);
        var currentDomain = Method(appDomain, "get_CurrentDomain", appDomain, false);
        il.Emit(OpCodes.Call, currentDomain);
        il.Emit(OpCodes.Ldstr, loadedKey);
        il.Emit(OpCodes.Callvirt, Method(appDomain, "GetData", module.TypeSystem.Object, true, module.TypeSystem.String));
        il.Emit(OpCodes.Brtrue, end);
        il.Emit(OpCodes.Call, Method(assembly, "GetExecutingAssembly", assembly, false));
        il.Emit(OpCodes.Ldstr, ResourceName);
        il.Emit(OpCodes.Callvirt, Method(assembly, "GetManifestResourceStream", stream, true, module.TypeSystem.String));
        il.Emit(OpCodes.Stloc, resource);
        il.Emit(OpCodes.Newobj, Method(memory, ".ctor", module.TypeSystem.Void, true));
        il.Emit(OpCodes.Stloc, buffer);
        var start = Instruction.Create(OpCodes.Ldloc, resource);
        il.Append(start);
        il.Emit(OpCodes.Ldloc, buffer);
        il.Emit(OpCodes.Callvirt, Method(stream, "CopyTo", module.TypeSystem.Void, true, stream));
        il.Emit(OpCodes.Ldloc, buffer);
        il.Emit(OpCodes.Callvirt, Method(memory, "ToArray", new ArrayType(module.TypeSystem.Byte), true));
        il.Emit(OpCodes.Call, Method(assembly, "Load", assembly, false, new ArrayType(module.TypeSystem.Byte)));
        il.Emit(OpCodes.Ldstr, banner.FullName);
        il.Emit(OpCodes.Callvirt, Method(assembly, "GetType", runtimeType, true, module.TypeSystem.String));
        il.Emit(OpCodes.Ldstr, "Register");
        il.Emit(OpCodes.Callvirt, Method(runtimeType, "GetMethod", methodInfo, true, module.TypeSystem.String));
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Ldnull);
        il.Emit(OpCodes.Callvirt, Method(methodBase, "Invoke", module.TypeSystem.Object, true,
            module.TypeSystem.Object, new ArrayType(module.TypeSystem.Object)));
        il.Emit(OpCodes.Pop);
        il.Emit(OpCodes.Call, currentDomain);
        il.Emit(OpCodes.Ldstr, loadedKey);
        il.Emit(OpCodes.Ldstr, "loaded");
        il.Emit(OpCodes.Callvirt, Method(appDomain, "SetData", module.TypeSystem.Void, true, module.TypeSystem.String, module.TypeSystem.Object));
        il.Emit(OpCodes.Leave, end);
        var cleanup = Instruction.Create(OpCodes.Ldloc, buffer);
        il.Append(cleanup);
        il.Emit(OpCodes.Callvirt, Method(stream, "Dispose", module.TypeSystem.Void, true));
        il.Emit(OpCodes.Ldloc, resource);
        il.Emit(OpCodes.Callvirt, Method(stream, "Dispose", module.TypeSystem.Void, true));
        il.Emit(OpCodes.Endfinally);
        il.Append(end);
        loader.Body.ExceptionHandlers.Add(new ExceptionHandler(ExceptionHandlerType.Finally)
        {
            TryStart = start, TryEnd = cleanup, HandlerStart = cleanup, HandlerEnd = end
        });
        return loader;
    }
}
