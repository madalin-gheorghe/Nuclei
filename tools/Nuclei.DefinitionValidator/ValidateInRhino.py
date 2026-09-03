from __future__ import print_function

import hashlib
import datetime
import json
import os
import re
import shutil
import sys
import time
import traceback

import clr
import Rhino
from System import Guid, Object
from System.IO import File
from System.Reflection import AssemblyName, BindingFlags
from System.Threading import Thread


GRASSHOPPER_ID = Guid("b45a29b1-4343-4035-989e-044e8580d9cf")
SOURCE_LIBRARY = "fe53d2b8-e56d-da70-cde9-0b078f8bc65d"
DENDRO_SMOOTH_VOLUME = "cc7da05b-aea7-47ce-b74d-6d84d25ebac3"
DENDRO_VOLUME_TO_MESH = "858d89ff-5854-4e5f-aeb2-0e43e580835e"


def required_environment(name):
    value = os.environ.get(name)
    if not value:
        raise Exception("Missing environment variable: " + name)
    return os.path.abspath(value)


def sha256_file(path):
    digest = hashlib.sha256()
    stream = open(path, "rb")
    try:
        while True:
            block = stream.read(1024 * 1024)
            if not block:
                break
            digest.update(block)
    finally:
        stream.close()
    return digest.hexdigest().upper()


def hash_strings(values):
    return hashlib.sha256("\n".join(values).encode("utf-8")).hexdigest().upper()


def write_json_atomic(path, value):
    temporary = path + ".tmp"
    output = open(temporary, "w")
    try:
        json.dump(value, output, indent=2, sort_keys=True)
    finally:
        output.close()
    replace_file_atomic(temporary, path)


def replace_file_atomic(temporary, path):
    """Atomically publish a file under both Rhino netcore and net48.

    The progress monitor or an indexer can briefly hold the old destination
    without delete sharing. Retry that narrow File.Replace race; never expose a
    partially-written JSON file.
    """
    for attempt in range(40):
        try:
            if File.Exists(path):
                File.Replace(temporary, path, None)
            else:
                File.Move(temporary, path)
            return
        except IOError:
            if attempt == 39:
                raise
            Thread.Sleep(25)


def write_progress(path, status):
    write_json_atomic(path, {"formatVersion": 1, "complete": False, "status": status})


def dotnet_type_name(value):
    return value.GetType().FullName


def load_gha(server, path, required):
    if not os.path.isfile(path):
        if required:
            raise Exception("Required GHA was not found: " + path)
        return False
    from Grasshopper.Kernel import GH_ExternalFile
    method = None
    flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
    for candidate in server.GetType().GetMethods(flags):
        if candidate.Name == "LoadGHA" and len(candidate.GetParameters()) == 2:
            method = candidate
            break
    if method is None:
        raise Exception("Grasshopper component server has no LoadGHA method")
    # loadOneByOne=True opens an interactive protection dialog and will hang a
    # hidden validation host. The normal non-interactive loader is deterministic.
    loaded = method.Invoke(server, (GH_ExternalFile(path), False))
    if required and not loaded:
        # LoadGHA can return false if the exact assembly has already been loaded.
        # The proxy-origin check below is authoritative for Nuclei4.
        return False
    return bool(loaded)


def default_external_ghas(roaming):
    output = [os.path.join(roaming, "Grasshopper", "Libraries", "Pufferfish3-0.gha")]
    packages = os.path.join(roaming, "McNeel", "Rhinoceros", "packages", "9.0")
    for plugin, filename in (("DendroGH", "DendroGH.gha"), ("ghgl", "ghgl.gha"), ("MeshEdit-Components", "Meshedit2000.gha")):
        root = os.path.join(packages, plugin)
        if not os.path.isdir(root):
            continue
        for version in sorted(os.listdir(root)):
            candidate = os.path.join(root, version, filename)
            if os.path.isfile(candidate):
                output.append(candidate)
    return output


def prepare_autoload_folder(root, v4_gha, extras):
    libraries = os.path.join(root, "Libraries")
    if not os.path.isdir(libraries):
        os.makedirs(libraries)
    allowed = (".gha", ".dll", ".json", ".config")
    sources = []
    v4_directory = os.path.dirname(v4_gha)
    sources.extend(os.path.join(v4_directory, name) for name in os.listdir(v4_directory))
    for extra in extras:
        if not os.path.isfile(extra):
            continue
        directory = os.path.dirname(extra)
        if os.path.basename(directory).lower() == "libraries":
            sources.append(extra)
        else:
            sources.extend(os.path.join(directory, name) for name in os.listdir(directory))
    for source in sources:
        if os.path.isfile(source):
            name = os.path.basename(source)
            if os.path.isfile(source) and name.lower().endswith(allowed):
                destination = os.path.join(libraries, name)
                if os.path.isfile(destination) and sha256_file(destination) != sha256_file(source):
                    raise Exception("Autoload dependency filename conflict: " + name)
                if not os.path.isfile(destination):
                    shutil.copy2(source, destination)
    return libraries


def source_count(value):
    try:
        return int(value.SourceCount)
    except Exception:
        return 0


def wire_count(objects):
    count = 0
    for obj in objects:
        try:
            params = obj.Params
            for item in params.Input:
                count += source_count(item)
        except Exception:
            count += source_count(obj)
    return count


def wire_connections(objects):
    connections = []
    for obj in objects:
        parameters = None
        try:
            parameters = list(obj.Params.Input)
        except Exception:
            try:
                # Standalone Grasshopper parameters are document objects and
                # hold their incoming sources directly.
                list(obj.Sources)
                parameters = [obj]
            except Exception:
                parameters = []
        for parameter in parameters:
            destination = str(parameter.InstanceGuid).lower()
            for source in parameter.Sources:
                connections.append(destination + "|" + str(source.InstanceGuid).lower())
    return sorted(connections)


def validate_v4_component_schema(obj, expected_converted):
    type_name = dotnet_type_name(obj)
    inputs = []
    outputs = []
    try:
        inputs = [str(item.Name) for item in obj.Params.Input]
        outputs = [str(item.Name) for item in obj.Params.Output]
    except Exception:
        return
    if type_name == "Nuclei4.ParticleGroup_Constructor_Slime":
        if len(inputs) != 10 or inputs[8] != "Exploration":
            raise Exception("Resolved V4 Slime Group schema is not ten inputs with Exploration at index 8: " + repr(inputs))
        expected_mode = expected_converted.get("probabilisticSteering")
        if expected_mode is not None and bool(obj.ProbabilisticSteering) != bool(expected_mode):
            raise Exception("Resolved V4 Slime Group lost ProbabilisticSteering state")
    elif type_name == "Nuclei4.EnivronmentSettings":
        wanted = ["Diffuse Rate", "Decay Rate", "Falloff", "Diffuse Range"]
        if inputs != wanted:
            raise Exception("Resolved V4 Slime Settings schema is %r, expected %r" % (inputs, wanted))
    elif type_name == "Nuclei4.SolverGPU":
        if len(outputs) != 3 or outputs[2] != "GPU Status":
            raise Exception("Resolved V4 Solver schema does not include GPU Status output 2: " + repr(outputs))
    elif type_name == "Nuclei4.GpuVolumeToMesh":
        wanted = ["Voxels", "Iso Value", "Method", "Maximum Elements", "Update", "Smoothing Iterations"]
        if inputs != wanted:
            raise Exception("Resolved V4 Dendro schema is %r, expected %r" % (inputs, wanted))


def open_document_for_validation(path, report_progress, phase):
    """Open the exact saved graph through Grasshopper's standard IO path."""
    from Grasshopper.Kernel import GH_DocumentIO
    report_progress(phase + ":document-io-creating")
    io = GH_DocumentIO()
    report_progress(phase + ":document-open-started")
    if not io.Open(path):
        raise Exception("Grasshopper could not open " + path)
    report_progress(phase + ":document-open-complete")
    document = io.Document
    document.Enabled = False
    report_progress(phase + ":document-disabled")
    return io, document


def inspect_document(path, expected, components_by_target, report_progress, phase):
    io, document = open_document_for_validation(path, report_progress, phase)
    report_progress(phase + ":objects-materializing")
    objects = list(document.Objects)
    report_progress(phase + ":objects-materialized")
    missing = []
    for obj in objects:
        name = dotnet_type_name(obj)
        if "Placeholder" in name or "UnknownObject" in name or "ProxyObject" in name:
            missing.append(name + " " + str(obj.InstanceGuid))
    if missing:
        raise Exception(os.path.basename(path) + " contains missing objects: " + ", ".join(missing))
    if len(objects) != int(expected["objectCount"]):
        raise Exception("%s has %d objects; expected %d" % (os.path.basename(path), len(objects), int(expected["objectCount"])))

    report_progress(phase + ":converted-components-checking")
    by_instance = dict((str(obj.InstanceGuid).lower(), obj) for obj in objects)
    for converted in expected["convertedObjects"]:
        instance = converted["instanceGuid"].lower()
        target = converted["targetGuid"].lower()
        if instance not in by_instance:
            raise Exception(os.path.basename(path) + " lost component instance " + instance)
        obj = by_instance[instance]
        actual_guid = str(obj.ComponentGuid).lower()
        if actual_guid != target:
            raise Exception("%s instance %s resolved to %s, expected %s" % (os.path.basename(path), instance, actual_guid, target))
        actual_type = dotnet_type_name(obj)
        expected_type = components_by_target[target]["targetType"]
        if actual_type != expected_type:
            raise Exception("%s instance %s resolved as %s, expected %s" % (os.path.basename(path), instance, actual_type, expected_type))
        validate_v4_component_schema(obj, converted)

    report_progress(phase + ":wire-counting")
    wires = wire_count(objects)
    if wires != int(expected["targetWireCount"]):
        raise Exception("%s has %d wires; expected %d" % (os.path.basename(path), wires, int(expected["targetWireCount"])))
    report_progress(phase + ":wire-endpoints-reading")
    connections = wire_connections(objects)
    connection_hash = hash_strings(connections)
    expected_connection_hash = expected["expectedTargetWireConnectionHash"]
    if connection_hash.upper() != expected_connection_hash.upper():
        raise Exception(os.path.basename(path) + " changed one or more exact wire endpoints")
    report_progress(phase + ":instance-guids-reading")
    instances = sorted(str(obj.InstanceGuid).lower() for obj in objects)
    instance_hash = hash_strings(instances)
    if instance_hash.upper() != expected["objectInstanceGuidHash"].upper():
        raise Exception(os.path.basename(path) + " changed its object InstanceGuid set")
    report_progress(phase + ":nuclei-components-counting")
    nuclei = [obj for obj in objects if str(obj.ComponentGuid).lower() in components_by_target]
    report_progress(phase + ":inspection-complete")
    return {
        "io": io,
        "document": document,
        "objectCount": len(objects),
        "wireCount": wires,
        "wireConnectionHash": connection_hash,
        "nuclei4ObjectCount": len(nuclei),
        "missingObjectCount": 0,
        "objectInstanceGuidHash": instance_hash,
        "componentTypes": sorted(set(dotnet_type_name(obj) for obj in nuclei))
    }


def dispose_snapshot(snapshot):
    try:
        snapshot["document"].Dispose()
    except Exception:
        pass


def owner_of_parameter(objects, parameter):
    wanted = str(parameter.InstanceGuid).lower()
    for obj in objects:
        try:
            for candidate in list(obj.Params.Input) + list(obj.Params.Output):
                if str(candidate.InstanceGuid).lower() == wanted:
                    return obj
        except Exception:
            pass
    return None


def set_boolean_source(source, value):
    for name in ("Value", "ToggleValue"):
        try:
            setattr(source, name, bool(value))
            source.ExpireSolution(False)
            return
        except Exception:
            pass
    flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic
    for name in ("Value", "ToggleValue"):
        prop = source.GetType().GetProperty(name, flags)
        if prop is not None and prop.CanWrite:
            prop.SetValue(source, bool(value), None)
            source.ExpireSolution(False)
            return
    raise Exception("Cannot control boolean source " + dotnet_type_name(source))


def volatile_type_names(parameter):
    output = []
    for item in parameter.VolatileData.AllData(True):
        outer = dotnet_type_name(item)
        inner = ""
        try:
            if item.Value is not None:
                inner = dotnet_type_name(item.Value)
        except Exception:
            pass
        output.append(outer + (" -> " + inner if inner else ""))
    return output


def volatile_values(parameter):
    output = []
    for item in parameter.VolatileData.AllData(True):
        try:
            output.append(str(item.Value))
        except Exception:
            output.append(str(item))
    return output


def runtime_messages(obj):
    from Grasshopper.Kernel import GH_RuntimeMessageLevel
    return {
        "errors": [str(value) for value in obj.RuntimeMessages(GH_RuntimeMessageLevel.Error)],
        "warnings": [str(value) for value in obj.RuntimeMessages(GH_RuntimeMessageLevel.Warning)],
        "remarks": [str(value) for value in obj.RuntimeMessages(GH_RuntimeMessageLevel.Remark)]
    }


def targeted_stage_state(stage, milliseconds, solver, dendro, smooth, volume_to_mesh):
    return {
        "stage": stage,
        "milliseconds": milliseconds,
        "solverMessage": str(solver.Message),
        "solverResetValues": volatile_values(solver.Params.Input[0]),
        "solverOutputTypes": volatile_type_names(solver.Params.Output[1]),
        "solverRuntimeMessages": runtime_messages(solver),
        "dendroMessage": str(dendro.Message),
        "dendroUpdateValues": volatile_values(dendro.Params.Input[4]),
        "dendroOutputTypes": volatile_type_names(dendro.Params.Output[0]),
        "dendroRuntimeMessages": runtime_messages(dendro),
        "smoothOutputTypes": volatile_type_names(smooth.Params.Output[0]),
        "smoothRuntimeMessages": runtime_messages(smooth),
        "volumeToMeshOutputTypes": volatile_type_names(volume_to_mesh.Params.Output[0]),
        "volumeToMeshRuntimeMessages": runtime_messages(volume_to_mesh)
    }


def solve_dendro_path(path, expected, components_by_target, progress_path):
    """Exercise the exact 15_3D solver -> V4 mesher -> Dendro -> mesh path.

    The saved timer is locked. We explicitly perform reset, five non-reset
    solver solutions so the saved Iso 0.5 has deposited density to cross, then
    enable Update and advance the solver once more while Update remains true.
    Both toggles are restored without saving.
    """
    filename = os.path.basename(path)

    def report_progress(stage):
        write_progress(progress_path, "solving-targeted-dendro-path:%s:%s" % (filename, stage))

    io, document = open_document_for_validation(path, report_progress, "runtime")
    report_progress("runtime-objects-materializing")
    objects = list(document.Objects)
    report_progress("runtime-objects-materialized")
    disk_sha_before = sha256_file(path)
    by_instance = dict((str(obj.InstanceGuid).lower(), obj) for obj in objects)
    dendro_expected = [item for item in expected["convertedObjects"] if item.get("adapter") == "dendro-schema"]
    solver_expected = [item for item in expected["convertedObjects"] if item.get("adapter") == "solver-gpu-extra-status-output"]
    if len(dendro_expected) != 1 or len(solver_expected) != 1:
        raise Exception("Targeted runtime definition does not contain exactly one mapped Solver and Dendro component")
    dendro = by_instance[dendro_expected[0]["instanceGuid"].lower()]
    solver = by_instance[solver_expected[0]["instanceGuid"].lower()]

    dendro_recipients = list(dendro.Params.Output[0].Recipients)
    if len(dendro_recipients) != 1:
        raise Exception("Dendro output does not have exactly one downstream recipient")
    smooth = owner_of_parameter(objects, dendro_recipients[0])
    if smooth is None or str(smooth.ComponentGuid).lower() != DENDRO_SMOOTH_VOLUME:
        raise Exception("Dendro output does not feed the expected Dendro Smooth Volume component")
    smooth_recipients = list(smooth.Params.Output[0].Recipients)
    if len(smooth_recipients) != 1:
        raise Exception("Smooth Volume output does not have exactly one downstream recipient")
    volume_to_mesh = owner_of_parameter(objects, smooth_recipients[0])
    if volume_to_mesh is None or str(volume_to_mesh.ComponentGuid).lower() != DENDRO_VOLUME_TO_MESH:
        raise Exception("Smooth Volume output does not feed the expected Dendro Volume to Mesh component")

    if dendro.Params.Input[2].SourceCount != 0:
        raise Exception("Targeted Dendro Method input is unexpectedly wired")
    method_values = [int(item.Value) for item in dendro.Params.Input[2].PersistentData.AllData(True)]
    if method_values != [0]:
        raise Exception("Targeted Dendro Method is not the approved Continuous value 0")

    reset_sources = list(solver.Params.Input[0].Sources)
    update_sources = list(dendro.Params.Input[4].Sources)
    if len(reset_sources) != 1 or len(update_sources) != 1:
        raise Exception("Targeted Solver Reset and Dendro Update must each have one boolean source")
    reset_source = reset_sources[0]
    update_source = update_sources[0]

    stages = []
    started = time.time()
    try:
        report_progress("gpu-reset-preparing")
        document.Enabled = True
        set_boolean_source(update_source, False)
        set_boolean_source(reset_source, True)
        stage = time.time()
        report_progress("gpu-reset-solution-started")
        document.NewSolution(False)
        report_progress("gpu-reset-solution-complete")
        stages.append(targeted_stage_state(
            "gpu-reset", (time.time() - stage) * 1000.0,
            solver, dendro, smooth, volume_to_mesh))

        set_boolean_source(reset_source, False)
        for step_index in range(1, 6):
            solver.ExpireSolution(False)
            stage = time.time()
            report_progress("solver-step-%d-solution-started" % step_index)
            document.NewSolution(False)
            report_progress("solver-step-%d-solution-complete" % step_index)
            stages.append(targeted_stage_state(
                "solver-step-" + str(step_index), (time.time() - stage) * 1000.0,
                solver, dendro, smooth, volume_to_mesh))
        iteration_match = re.search(r"(?:Iteration:|Complete:)\s*(\d+)", str(solver.Message))
        if iteration_match is None or int(iteration_match.group(1)) < 5:
            raise Exception("Controlled solver did not reach iteration 5 before Dendro Update: "
                + str(solver.Message) + "; stages=" + json.dumps(stages, sort_keys=True))

        set_boolean_source(update_source, True)
        stage = time.time()
        report_progress("dendro-update-enabled-solution-started")
        document.NewSolution(False)
        report_progress("dendro-update-enabled-solution-complete")
        stages.append(targeted_stage_state(
            "dendro-update-enabled", (time.time() - stage) * 1000.0,
            solver, dendro, smooth, volume_to_mesh))

        enabled_dendro_types = volatile_type_names(dendro.Params.Output[0])
        enabled_smooth_types = volatile_type_names(smooth.Params.Output[0])
        enabled_mesh_types = volatile_type_names(volume_to_mesh.Params.Output[0])
        enabled_dendro_outputs = list(dendro.Params.Output[0].VolatileData.AllData(True))
        if len(enabled_dendro_outputs) != 1:
            raise Exception("Enabled Dendro Update did not emit exactly one cached output: "
                + repr(enabled_dendro_types) + "; stages=" + json.dumps(stages, sort_keys=True))
        enabled_dendro_output = enabled_dendro_outputs[0]
        enabled_dendro_volume = enabled_dendro_output.Value
        if not any("DendroGH.VolumeGOO" in value and "DendroGH.DendroVolume" in value for value in enabled_dendro_types):
            raise Exception("Enabled Dendro Update did not emit a native Dendro VolumeGOO: "
                + repr(enabled_dendro_types) + "; stages=" + json.dumps(stages, sort_keys=True))
        if not any("DendroGH.VolumeGOO" in value and "DendroGH.DendroVolume" in value for value in enabled_smooth_types):
            raise Exception("Smooth Volume did not accept the enabled-update Dendro volume: " + repr(enabled_smooth_types))
        if not any("Rhino.Geometry.Mesh" in value for value in enabled_mesh_types):
            raise Exception("Dendro Volume to Mesh did not emit a Rhino mesh after enabling Update: " + repr(enabled_mesh_types))

        solver.ExpireSolution(False)
        stage = time.time()
        report_progress("dendro-update-held-true-solution-started")
        document.NewSolution(False)
        report_progress("dendro-update-held-true-solution-complete")
        stages.append(targeted_stage_state(
            "dendro-update-held-true", (time.time() - stage) * 1000.0,
            solver, dendro, smooth, volume_to_mesh))

        held_update_values = volatile_values(dendro.Params.Input[4])
        if held_update_values != ["True"]:
            raise Exception("Dendro Update was not held true for the second solver solution: "
                + repr(held_update_values) + "; stages=" + json.dumps(stages, sort_keys=True))
        dendro_types = volatile_type_names(dendro.Params.Output[0])
        smooth_types = volatile_type_names(smooth.Params.Output[0])
        mesh_types = volatile_type_names(volume_to_mesh.Params.Output[0])
        held_dendro_outputs = list(dendro.Params.Output[0].VolatileData.AllData(True))
        if len(held_dendro_outputs) != 1:
            raise Exception("Held-true Dendro Update did not emit exactly one cached output: "
                + repr(dendro_types) + "; stages=" + json.dumps(stages, sort_keys=True))
        held_dendro_output = held_dendro_outputs[0]
        held_dendro_volume = held_dendro_output.Value
        dendro_output_identity_changed = (
            not Object.ReferenceEquals(enabled_dendro_output, held_dendro_output)
            and not Object.ReferenceEquals(enabled_dendro_volume, held_dendro_volume))
        if not dendro_output_identity_changed:
            raise Exception("Dendro Update reused its cached output while held true; stages="
                + json.dumps(stages, sort_keys=True))
        method_runtime_values = [int(item.Value) for item in dendro.Params.Input[2].VolatileData.AllData(True)]
        if method_runtime_values != [0]:
            raise Exception("Targeted Dendro runtime Method value is not Continuous 0: " + repr(method_runtime_values))

        checked = [solver, dendro, smooth, volume_to_mesh]
        messages = dict((str(obj.InstanceGuid).lower(), {
            "component": str(obj.Name),
            "componentGuid": str(obj.ComponentGuid).lower(),
            "messages": runtime_messages(obj)
        }) for obj in checked)
        path_errors = []
        for value in messages.values():
            path_errors.extend(value["messages"]["errors"])
        if path_errors:
            raise Exception("Targeted GPU/Dendro path has runtime errors: " + " | ".join(path_errors)
                + "; stages=" + json.dumps(stages, sort_keys=True))

        if not any("DendroGH.VolumeGOO" in value and "DendroGH.DendroVolume" in value for value in dendro_types):
            raise Exception("Method 0 did not emit a native Dendro VolumeGOO: " + repr(dendro_types)
                + "; stages=" + json.dumps(stages, sort_keys=True))
        if not any("DendroGH.VolumeGOO" in value and "DendroGH.DendroVolume" in value for value in smooth_types):
            raise Exception("Smooth Volume did not accept and emit the native Dendro volume: " + repr(smooth_types))
        if not any("Rhino.Geometry.Mesh" in value for value in mesh_types):
            raise Exception("Dendro Volume to Mesh did not emit a Rhino mesh: " + repr(mesh_types))

        disk_sha_after = sha256_file(path)
        if disk_sha_after != disk_sha_before or disk_sha_after.upper() != expected["targetSha256"].upper():
            raise Exception("Targeted runtime check unexpectedly changed the saved definition")

        return {
            "file": os.path.basename(path),
            "solved": True,
            "savedDocumentModified": False,
            "savedDocumentSha256Before": disk_sha_before,
            "savedDocumentSha256After": disk_sha_after,
            "totalMilliseconds": (time.time() - started) * 1000.0,
            "stages": stages,
            "method": 0,
            "methodName": "Continuous",
            "methodRuntimeValues": method_runtime_values,
            "methodRuntimeSourceCount": int(dendro.Params.Input[2].SourceCount),
            "dendroOutputIdentityChangedWhileUpdateHeldTrue": dendro_output_identity_changed,
            "runtimeDocumentObjectCount": len(list(document.Objects)),
            "runtimeAddedObjectCount": len(list(document.Objects)) - len(objects),
            "dendroOutputTypes": dendro_types,
            "smoothVolumeOutputTypes": smooth_types,
            "volumeToMeshOutputTypes": mesh_types,
            "downstreamFlow": [
                {"instanceGuid": str(dendro.InstanceGuid).lower(), "component": str(dendro.Name)},
                {"instanceGuid": str(smooth.InstanceGuid).lower(), "component": str(smooth.Name)},
                {"instanceGuid": str(volume_to_mesh.InstanceGuid).lower(), "component": str(volume_to_mesh.Name)}
            ],
            "runtimeMessages": messages,
            "noPathRuntimeErrors": True
        }
    finally:
        report_progress("runtime-cleanup-started")
        try:
            set_boolean_source(update_source, False)
            set_boolean_source(reset_source, True)
        except Exception:
            pass
        report_progress("runtime-cleanup-complete")
        try:
            document.Enabled = False
            document.Dispose()
        except Exception:
            pass


def validate_archive_residue(path, source_guids, report_progress):
    from GH_IO.Serialization import GH_Archive
    report_progress("archive-object-creating")
    archive = GH_Archive()
    report_progress("archive-read-started")
    if not archive.ReadFromFile(path):
        raise Exception("GH_IO could not reload " + path)
    report_progress("archive-read-complete")
    report_progress("archive-xml-serializing")
    xml = archive.Serialize_Xml().lower()
    report_progress("archive-xml-serialized")
    forbidden = [SOURCE_LIBRARY] + source_guids
    for value in forbidden:
        if value.lower() in xml:
            raise Exception("V3 GUID residue remains in %s: %s" % (os.path.basename(path), value))
    if ">nuclei3<" in xml or ">nuclei3," in xml:
        raise Exception("V3 library-name residue remains in " + os.path.basename(path))


def normalize_and_validate(path, expected, components_by_target, source_guids, normalize, progress_path):
    filename = os.path.basename(path)

    def report_progress(stage):
        write_progress(progress_path, "validating:%s:%s" % (filename, stage))

    report_progress("hash-before-started")
    archive_sha = sha256_file(path)
    report_progress("hash-before-complete")
    if archive_sha.upper() != expected["targetSha256"].upper():
        raise Exception(os.path.basename(path) + " does not match the archive-validated conversion manifest")
    initial = inspect_document(path, expected, components_by_target, report_progress, "initial")
    if normalize:
        temporary = path + ".rhino9-normalized.tmp.gh"
        if os.path.exists(temporary):
            os.remove(temporary)
        if not initial["io"].SaveQuiet(temporary):
            dispose_snapshot(initial)
            raise Exception("Grasshopper could not save normalized copy of " + path)
        dispose_snapshot(initial)
        normalized = inspect_document(temporary, expected, components_by_target, report_progress, "normalized")
        dispose_snapshot(normalized)
        replace_file_atomic(temporary, path)
    else:
        report_progress("initial-dispose-started")
        dispose_snapshot(initial)
        report_progress("initial-dispose-complete")
    reopened = inspect_document(path, expected, components_by_target, report_progress, "reopened")
    report_progress("reopened-dispose-started")
    dispose_snapshot(reopened)
    report_progress("reopened-dispose-complete")
    validate_archive_residue(path, source_guids, report_progress)
    report_progress("hash-after-started")
    final_sha = sha256_file(path)
    report_progress("hash-after-complete")
    if not normalize and (final_sha != archive_sha or final_sha.upper() != expected["targetSha256"].upper()):
        raise Exception(os.path.basename(path) + " changed on disk during Rhino validation")
    return {
        "file": os.path.basename(path),
        "sha256": final_sha,
        "expectedSha256": expected["targetSha256"],
        "savedDocumentSha256Before": archive_sha,
        "savedDocumentSha256After": final_sha,
        "savedDocumentModified": final_sha != archive_sha,
        "opened": True,
        "reopened": True,
        "objectCount": reopened["objectCount"],
        "sourceWireCount": expected["sourceWireCount"],
        "wireCount": reopened["wireCount"],
        "intentionalDroppedWireCount": expected["intentionalDroppedWireCount"],
        "wireMigrations": expected["wireMigrations"],
        "wireConnectionHash": reopened["wireConnectionHash"],
        "expectedWireConnectionHash": expected["expectedTargetWireConnectionHash"],
        "nuclei4ObjectCount": reopened["nuclei4ObjectCount"],
        "missingObjectCount": 0,
        "missingObjects": [],
        "objectInstanceGuidHash": reopened["objectInstanceGuidHash"],
        "expectedObjectInstanceGuidHash": expected["objectInstanceGuidHash"],
        "structurePreserved": True,
        "noV3Residue": True,
        "componentTypes": reopened["componentTypes"]
    }


def run():
    definitions = required_environment("NUCLEI_VALIDATION_DEFINITIONS")
    v4_gha = required_environment("NUCLEI_VALIDATION_V4_GHA")
    map_path = required_environment("NUCLEI_VALIDATION_MAP")
    normalize = os.environ.get("NUCLEI_VALIDATION_NORMALIZE", "0") != "0"
    solve_dendro = os.environ.get("NUCLEI_VALIDATION_SOLVE_DENDRO", "1") != "0"
    start_at = os.environ.get("NUCLEI_VALIDATION_START_AT")
    only_file = os.environ.get("NUCLEI_VALIDATION_ONLY_FILE")
    skip_extras = set(value.strip().lower() for value in os.environ.get("NUCLEI_VALIDATION_SKIP_EXTRAS", "").split(",") if value.strip())
    autoload = os.environ.get("NUCLEI_VALIDATION_AUTOLOAD", "0") == "1"
    use_normal_profile = os.environ.get("NUCLEI_VALIDATION_USE_NORMAL_PROFILE", "0") == "1"
    if autoload and use_normal_profile:
        raise Exception("Autoload and normal-profile validation modes are mutually exclusive")
    roaming = required_environment("NUCLEI_VALIDATION_ORIGINAL_APPDATA")
    grasshopper_dll = required_environment("NUCLEI_VALIDATION_GRASSHOPPER_DLL")
    isolated_gh_appdata = None if use_normal_profile else required_environment("NUCLEI_VALIDATION_ISOLATED_GH_APPDATA")
    report_path = os.path.join(definitions, "_rhino9_validation.json")
    progress_path = os.path.join(definitions, "_rhino9_validation.progress.json")
    write_progress(progress_path, "script-started")

    expected_v4_sha = os.environ.get("NUCLEI_VALIDATION_EXPECTED_V4_SHA256", "").strip().upper()
    v4_sha_before = sha256_file(v4_gha).upper()
    if expected_v4_sha and v4_sha_before != expected_v4_sha:
        raise Exception("Requested V4 GHA hash is %s; expected final hash %s" % (v4_sha_before, expected_v4_sha))

    manifest_path = os.path.join(definitions, "_conversion_manifest.json")
    manifest = json.load(open(manifest_path, "r"))
    mapping = json.load(open(map_path, "r"))
    mapped_library = mapping["targetLibrary"]
    actual_v4_assembly = AssemblyName.GetAssemblyName(v4_gha)
    actual_v4_full_name = str(actual_v4_assembly.FullName)
    actual_v4_version = str(actual_v4_assembly.Version)
    if (str(actual_v4_assembly.Name) != mapped_library["name"]
            or actual_v4_full_name != mapped_library["assemblyFullName"]
            or actual_v4_version != mapped_library["assemblyVersion"]):
        raise Exception(
            "V4 mapping assembly metadata does not match the requested GHA: map=%s / %s, GHA=%s / %s"
            % (mapped_library["assemblyFullName"], mapped_library["assemblyVersion"],
               actual_v4_full_name, actual_v4_version))
    manifest_v4_hash = str(manifest.get("targetAssemblySha256", "")).strip().upper()
    if manifest_v4_hash and manifest_v4_hash != v4_sha_before:
        raise Exception("Conversion manifest was created against a different V4 GHA hash")
    manifest_v4_full_name = str(manifest.get("targetAssemblyFullName", "")).strip()
    manifest_v4_version = str(manifest.get("targetAssemblyVersion", "")).strip()
    if manifest_v4_full_name and manifest_v4_full_name != actual_v4_full_name:
        raise Exception("Conversion manifest target assembly full name does not match the requested V4 GHA")
    if manifest_v4_version and manifest_v4_version != actual_v4_version:
        raise Exception("Conversion manifest target assembly version does not match the requested V4 GHA")
    components_by_target = dict((item["target"].lower(), item) for item in mapping["components"])
    source_guids = [item["source"].lower() for item in mapping["components"]]
    if int(manifest["sourceWireCount"]) - int(manifest["targetWireCount"]) != int(manifest["intentionalDroppedWireCount"]):
        raise Exception("Conversion manifest wire totals do not reconcile with the recorded schema migrations")

    if isolated_gh_appdata is not None and not os.path.isdir(isolated_gh_appdata):
        os.makedirs(isolated_gh_appdata)
    extras = [path for path in default_external_ghas(roaming) if os.path.basename(path).lower() not in skip_extras]
    if autoload:
        prepare_autoload_folder(isolated_gh_appdata, v4_gha, extras)
    # Grasshopper caches this private root before scanning external libraries.
    # Set it before loading the Rhino plug-in so an installed older Nuclei4
    # cannot win the component GUIDs over the build under validation.
    clr.AddReferenceToFileAndPath(grasshopper_dll)
    import Grasshopper
    if not use_normal_profile:
        folders_type = clr.GetClrType(Grasshopper.Folders)
        appdata_field = folders_type.GetField("m_appdataFolder", BindingFlags.Static | BindingFlags.NonPublic)
        if appdata_field is None:
            raise Exception("Grasshopper app-data root field was not found")
        appdata_field.SetValue(None, isolated_gh_appdata + os.sep)

    grasshopper_preloaded = os.environ.get("NUCLEI_VALIDATION_GRASSHOPPER_PRELOADED", "0") == "1"
    if grasshopper_preloaded:
        if Grasshopper.Instances.ComponentServer is None:
            raise Exception("The validation host did not initialize the preloaded Grasshopper component server")
    elif not Rhino.PlugIns.PlugIn.LoadPlugIn(GRASSHOPPER_ID, False, False):
        raise Exception("Rhino 9 did not load Grasshopper")
    write_progress(progress_path, "grasshopper-loaded")
    clr.AddReference("GH_IO")
    server = Grasshopper.Instances.ComponentServer

    loaded_extras = []
    if grasshopper_preloaded:
        # The safe-mode hosts initialize the isolated ComponentServer and load
        # every filtered extra plus the pinned V4 GHA before Python starts.
        # Re-entering LoadGHA here can deadlock older plug-ins (notably Dendro)
        # even though the exact assembly is already registered.
        loaded_extras = [path for path in extras if os.path.isfile(path)]
        write_progress(progress_path, "preloaded-component-server-ready")
    elif use_normal_profile:
        loaded_extras = [path for path in extras if os.path.isfile(path)]
        write_progress(progress_path, "normal-profile-plugin-scan-complete")
        load_gha(server, v4_gha, True)
    elif autoload:
        loaded_extras = [path for path in extras if os.path.isfile(path)]
    else:
        for path in extras:
            write_progress(progress_path, "loading-extra:" + os.path.basename(path))
            if load_gha(server, path, False):
                loaded_extras.append(path)
        write_progress(progress_path, "loading-v4")
        load_gha(server, v4_gha, True)
    write_progress(progress_path, "v4-loaded")

    for item in mapping["components"]:
        emitted = server.EmitObject(Guid(item["target"]))
        if emitted is None:
            raise Exception("V4 component server cannot emit " + item["target"])
        actual_type = dotnet_type_name(emitted)
        if actual_type != item["targetType"]:
            raise Exception("V4 GUID %s emitted %s, expected %s" % (item["target"], actual_type, item["targetType"]))
        actual_location = os.path.abspath(emitted.GetType().Assembly.Location)
        if actual_location.lower() != v4_gha.lower() and sha256_file(actual_location) != sha256_file(v4_gha):
            raise Exception("V4 GUID %s came from %s, not the requested build %s" % (item["target"], actual_location, v4_gha))
    write_progress(progress_path, "v4-proxies-verified")

    selected = sorted(manifest["files"], key=lambda item: item["file"].lower())
    if start_at:
        matching = [index for index, item in enumerate(selected) if item["file"].lower() == start_at.lower()]
        if not matching:
            raise Exception("NUCLEI_VALIDATION_START_AT does not match a manifest file: " + start_at)
        selected = selected[matching[0]:]
    if only_file:
        selected = [item for item in selected if item["file"].lower() == only_file.lower()]
        if not selected:
            raise Exception("NUCLEI_VALIDATION_ONLY_FILE does not match a selected manifest file: " + only_file)

    reports = []
    for expected in selected:
        write_progress(progress_path, "validating:" + expected["file"])
        path = os.path.join(definitions, expected["file"])
        report = normalize_and_validate(path, expected, components_by_target, source_guids, normalize, progress_path)
        reports.append(report)
        print("Rhino 9 validated %s: %d objects, %d wires" % (report["file"], report["objectCount"], report["wireCount"]))

    runtime_checks = []
    if solve_dendro:
        dendro_files = [item for item in selected if any(converted.get("adapter") == "dendro-schema" for converted in item["convertedObjects"])]
        if len(dendro_files) != 1:
            raise Exception("Expected exactly one selected definition with the Dendro schema adapter for targeted solving")
        write_progress(progress_path, "solving-targeted-dendro-path:" + dendro_files[0]["file"])
        runtime_checks.append(solve_dendro_path(
            os.path.join(definitions, dendro_files[0]["file"]),
            dendro_files[0],
            components_by_target,
            progress_path))
        print("Rhino 9 solved targeted Dendro path in " + dendro_files[0]["file"])

    v4_sha_after = sha256_file(v4_gha).upper()
    if v4_sha_after != v4_sha_before:
        raise Exception("The V4 GHA changed on disk during Rhino validation")
    if expected_v4_sha and v4_sha_after != expected_v4_sha:
        raise Exception("Validated V4 GHA hash no longer matches the pinned final hash")

    report = {
        "formatVersion": 1,
        "success": True,
        "validatedUtc": datetime.datetime.utcnow().isoformat() + "Z",
        "rhinoVersion": str(Rhino.RhinoApp.Version),
        "grasshopperVersion": str(server.GetType().Assembly.GetName().Version),
        "conversionManifestSha256": sha256_file(manifest_path),
        "v4Gha": os.path.basename(v4_gha),
        "v4GhaSha256": v4_sha_after,
        "v4GhaSha256Before": v4_sha_before,
        "v4GhaSha256After": v4_sha_after,
        "expectedV4GhaSha256": expected_v4_sha if expected_v4_sha else v4_sha_before,
        "v4GhaUnchangedDuringValidation": True,
        "normalized": normalize,
        "validationMode": "load-save-reopen" if normalize else "load-reopen-without-saving",
        "grasshopperProfile": "normal-user-profile" if use_normal_profile else "isolated-validation-profile",
        "fileCount": len(reports),
        "sourceWireCount": int(manifest["sourceWireCount"]),
        "targetWireCount": int(manifest["targetWireCount"]),
        "intentionalDroppedWireCount": int(manifest["intentionalDroppedWireCount"]),
        "wireParityAfterApprovedSchemaAdapters": True,
        "allFilesOpened": True,
        "noMissingObjects": True,
        "noV3Residue": True,
        "structurePreserved": True,
        "fullDefinitionsSolved": False,
        "runtimeValidationScope": "Targeted saved 15_3D graph: GPU reset, five non-reset solver solutions to populate deposited density above the saved Iso 0.5, Dendro Update enabled, one additional solver solution while Update remains true with output identity replacement, Smooth Volume, and Volume to Mesh. Other definitions were load/reopen validated without solving to avoid activating their saved timers/triggers and large simulations.",
        "targetedRuntimeChecks": runtime_checks,
        "loadedExternalGhas": [os.path.basename(path) for path in loaded_extras],
        "files": reports
    }
    write_json_atomic(report_path, report)
    if os.path.isfile(progress_path):
        os.remove(progress_path)


try:
    run()
except Exception as error:
    definitions = os.environ.get("NUCLEI_VALIDATION_DEFINITIONS", os.getcwd())
    report_path = os.path.join(definitions, "_rhino9_validation.json")
    progress_path = os.path.join(definitions, "_rhino9_validation.progress.json")
    failure = {
        "formatVersion": 1,
        "success": False,
        "error": str(error),
        "traceback": traceback.format_exc()
    }
    try:
        write_json_atomic(report_path, failure)
        if os.path.isfile(progress_path):
            os.remove(progress_path)
    except Exception:
        pass
    print("Nuclei definition validation failed: " + str(error))
    traceback.print_exc()
