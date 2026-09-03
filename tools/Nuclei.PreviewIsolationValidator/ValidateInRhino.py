from __future__ import print_function

import json
import os
import traceback

import clr
import Rhino
from System import Activator, AppDomain, Guid
from System.Reflection import BindingFlags


GRASSHOPPER_ID = Guid("b45a29b1-4343-4035-989e-044e8580d9cf")
FLAGS = BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic


def required_path(name):
    path = os.path.abspath(os.environ[name])
    if not os.path.isfile(path):
        raise Exception("Required file was not found: " + path)
    return path


def load_gha(server, path):
    from Grasshopper.Kernel import GH_ExternalFile

    loader = None
    for method in server.GetType().GetMethods(FLAGS):
        if method.Name == "LoadGHA" and len(method.GetParameters()) == 2:
            loader = method
            break
    if loader is None:
        raise Exception("Grasshopper component server has no LoadGHA method")
    loader.Invoke(server, (GH_ExternalFile(path), False))


def loaded_assembly(name):
    for assembly in AppDomain.CurrentDomain.GetAssemblies():
        if str(assembly.GetName().Name).lower() == name.lower():
            return assembly
    raise Exception(name + " was not loaded")


def method(type_value, name):
    value = type_value.GetMethod(name, FLAGS)
    if value is None:
        raise Exception(type_value.FullName + "." + name + " was not found")
    return value


def instance(type_value):
    return Activator.CreateInstance(type_value)


def owner(value):
    return value.OnPingDocument()


def snapshot_instance(conduit_type):
    field = conduit_type.GetField("instance", FLAGS)
    if field is None:
        raise Exception(conduit_type.FullName + ".instance was not found")
    return method(conduit_type, "Snapshot").Invoke(field.GetValue(None), None)


def assert_snapshot(snapshot, expected_document, label):
    values = list(snapshot)
    if len(values) != 1:
        raise Exception(label + " returned %d previews; expected exactly one" % len(values))
    if not ObjectReferenceEquals(owner(values[0]), expected_document):
        raise Exception(label + " returned a preview owned by the inactive document")


def ObjectReferenceEquals(first, second):
    from System import Object
    return Object.ReferenceEquals(first, second)


def validate_instance_conduit(canvas, assembly, preview_name, conduit_name, label):
    from Grasshopper.Kernel import GH_Document

    preview_type = assembly.GetType(preview_name, True)
    conduit_type = assembly.GetType(conduit_name, True)
    register = method(conduit_type, "Register")
    unregister = method(conduit_type, "Unregister")
    document_a = GH_Document()
    document_b = GH_Document()
    preview_a = instance(preview_type)
    preview_b = instance(preview_type)
    previous_document = canvas.Document

    try:
        document_a.AddObject(preview_a, False)
        document_b.AddObject(preview_b, False)
        document_a.Enabled = True
        document_b.Enabled = True
        register.Invoke(None, (preview_a,))
        register.Invoke(None, (preview_b,))

        canvas.Document = document_a
        document_a.Enabled = True
        assert_snapshot(snapshot_instance(conduit_type), document_a, label + " document A")

        canvas.Document = document_b
        document_b.Enabled = True
        assert_snapshot(snapshot_instance(conduit_type), document_b, label + " document B")
    finally:
        canvas.Document = previous_document
        unregister.Invoke(None, (preview_a.InstanceGuid,))
        unregister.Invoke(None, (preview_b.InstanceGuid,))
        document_a.Dispose()
        document_b.Dispose()


def validate_static_manager(canvas, assembly):
    from Grasshopper.Kernel import GH_Document

    preview_type = assembly.GetType("Nuclei4.Preview_Voxel", True)
    manager_type = assembly.GetType("Nuclei4.NucleiGpuDisplayManager", True)
    register = method(manager_type, "SetVoxelDensityPreview")
    unregister = method(manager_type, "DisableVoxelDensityPreview")
    snapshot = method(manager_type, "SnapshotVoxelDensityPreviews")
    document_a = GH_Document()
    document_b = GH_Document()
    preview_a = instance(preview_type)
    preview_b = instance(preview_type)
    previous_document = canvas.Document

    try:
        document_a.AddObject(preview_a, False)
        document_b.AddObject(preview_b, False)
        document_a.Enabled = True
        document_b.Enabled = True
        register.Invoke(None, (preview_a,))
        register.Invoke(None, (preview_b,))

        canvas.Document = document_a
        document_a.Enabled = True
        assert_snapshot(snapshot.Invoke(None, None), document_a, "V4 GPU voxel document A")

        canvas.Document = document_b
        document_b.Enabled = True
        assert_snapshot(snapshot.Invoke(None, None), document_b, "V4 GPU voxel document B")
    finally:
        canvas.Document = previous_document
        unregister.Invoke(None, (preview_a.InstanceGuid,))
        unregister.Invoke(None, (preview_b.InstanceGuid,))
        document_a.Dispose()
        document_b.Dispose()


def run():
    grasshopper_dll = required_path("NUCLEI_PREVIEW_VALIDATION_GRASSHOPPER_DLL")
    v3_gha = required_path("NUCLEI_PREVIEW_VALIDATION_V3_GHA")
    v4_gha = required_path("NUCLEI_PREVIEW_VALIDATION_V4_GHA")
    report_path = os.path.abspath(os.environ["NUCLEI_PREVIEW_VALIDATION_REPORT"])
    isolated_root = os.path.abspath(os.environ["NUCLEI_PREVIEW_VALIDATION_GH_APPDATA"])
    if not os.path.isdir(isolated_root):
        os.makedirs(isolated_root)

    clr.AddReferenceToFileAndPath(grasshopper_dll)
    import Grasshopper

    folders_type = clr.GetClrType(Grasshopper.Folders)
    appdata_field = folders_type.GetField("m_appdataFolder", BindingFlags.Static | BindingFlags.NonPublic)
    if appdata_field is None:
        raise Exception("Grasshopper app-data root field was not found")
    appdata_field.SetValue(None, isolated_root + os.sep)

    if not Rhino.PlugIns.PlugIn.LoadPlugIn(GRASSHOPPER_ID, False, False):
        raise Exception("Rhino did not load Grasshopper")
    server = Grasshopper.Instances.ComponentServer
    load_gha(server, v3_gha)
    load_gha(server, v4_gha)

    Rhino.RhinoApp.RunScript("_Grasshopper", False)
    canvas = Grasshopper.Instances.ActiveCanvas
    if canvas is None:
        raise Exception("Grasshopper did not create an active canvas")
    original_document = canvas.Document

    v3 = loaded_assembly("Nuclei3")
    v4 = loaded_assembly("Nuclei4")
    try:
        validate_instance_conduit(
            canvas, v3, "Nuclei3.Preview_Particle", "Nuclei3.ParticlePreviewDisplayConduit", "V3 particle")
        validate_instance_conduit(
            canvas, v4, "Nuclei4.Preview_Particle", "Nuclei4.ParticlePreviewDisplayConduit", "V4 particle")
        validate_instance_conduit(
            canvas, v4, "Nuclei4.Preview_Particle_Trails_GPU", "Nuclei4.ParticleTrailPreviewDisplayConduit", "V4 trail")
        validate_static_manager(canvas, v4)
    finally:
        canvas.Document = original_document

    with open(report_path, "w") as output:
        json.dump({"success": True, "contracts": 8}, output)
    print("Preview document isolation passed for V3 and V4.")


try:
    run()
except Exception as error:
    report_path = os.environ.get("NUCLEI_PREVIEW_VALIDATION_REPORT")
    if report_path:
        with open(report_path, "w") as output:
            json.dump({"success": False, "error": str(error), "traceback": traceback.format_exc()}, output)
    print("Preview document isolation failed: " + str(error))
    traceback.print_exc()
finally:
    Rhino.RhinoApp.Exit(False)
