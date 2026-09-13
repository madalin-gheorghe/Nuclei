using System.Reflection;

// Read archive serialization only. No Rhino host, component loading, solving or .gh writes.
if (args.Length != 3) {
    Console.Error.WriteLine("Usage: DefinitionArchiveExporter <GH_IO.dll> <definition folder> <XML output folder>");
    return 2;
}
try {
    var assembly = Assembly.LoadFrom(Path.GetFullPath(args[0]));
    var type = assembly.GetType("GH_IO.Serialization.GH_Archive", true)!;
    Directory.CreateDirectory(args[2]);
    foreach (var path in Directory.GetFiles(args[1], "*.gh").OrderBy(p => p)) {
        var archive = Activator.CreateInstance(type)!;
        if (!(bool)type.GetMethod("ReadFromFile", new[] { typeof(string) })!.Invoke(archive, new object[] { path })!)
            throw new IOException("Cannot read " + path);
        var xml = (string)type.GetMethod("Serialize_Xml", Type.EmptyTypes)!.Invoke(archive, null)!;
        File.WriteAllText(Path.Combine(args[2], Path.GetFileNameWithoutExtension(path) + ".xml"), xml);
        Console.WriteLine(Path.GetFileName(path));
    }
    return 0;
} catch (Exception error) {
    Console.Error.WriteLine(error.GetBaseException().Message);
    return 1;
}
