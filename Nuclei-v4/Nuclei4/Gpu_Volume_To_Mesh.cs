using System;
using System.Collections.Generic;
using System.Reflection;

using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;

using Rhino.Geometry;

namespace Nuclei4
{
    public sealed class GpuVolumeToMesh : GH_Component
    {
        const int ContinuousMethod = 0;
        const int DiscreteMethod = 1;

        object cachedOutput;

        public GpuVolumeToMesh()
          : base(
                "Nuclei4 to Dendro Volume",
                "Nuclei4 to Dendro Volume",
                "Converts the Solver GPU density field to a Dendro volume; outputs a Rhino mesh when Dendro is unavailable",
                "Nuclei4",
                "Voxels")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Voxels", "voxels", "Voxel output from Nuclei4 Solver GPU", GH_ParamAccess.item);
            pManager.AddNumberParameter("Iso Value", "iso", "Density level used to select the volume", GH_ParamAccess.item, 0.8);
            pManager.AddIntegerParameter("Method", "method", "Continuous uses GPU marching tetrahedra; Discrete uses selected voxel centres as Dendro point kernels", GH_ParamAccess.item, ContinuousMethod);
            pManager.AddIntegerParameter("Maximum Elements", "max", "Safety limit for triangles in Continuous mode or selected voxel centres in Discrete mode", GH_ParamAccess.item, 5000000);
            pManager.AddBooleanParameter("Update", "update", "Rebuild the output whenever the component receives updated inputs", GH_ParamAccess.item, false);
            pManager.AddIntegerParameter("Smoothing Iterations", "smooth", "GPU volume-smoothing passes used by Continuous mode; 0 disables smoothing", GH_ParamAccess.item, 1);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Dendro Volume / Mesh", "volume", "Native Dendro volume, or a Rhino mesh when Dendro is unavailable", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            EnsureMethodValueList();

            VoxelField field;
            double isoValue = 0.8;
            int method = ContinuousMethod;
            int maximumElements = 5000000;
            bool update = false;
            int smoothingIterations = 1;

            VoxelFieldAccess.TryGet(DA, 0, Globals.voxelSize, out field);
            DA.GetData(1, ref isoValue);
            DA.GetData(2, ref method);
            DA.GetData(3, ref maximumElements);
            DA.GetData(4, ref update);
            DA.GetData(5, ref smoothingIterations);

            if (update)
            {
                if (field == null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Connect a valid Nuclei voxel field.");
                }
                else if (field.GpuVolumeMeshProvider == null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "GPU density is unavailable. Connect the voxel output of Nuclei4 Solver GPU.");
                }
                else
                {
                    float threshold = (float)Math.Max(0.000001, isoValue);
                    int elementLimit = Math.Max(1, maximumElements);
                    int smoothPasses = Math.Max(0, Math.Min(8, smoothingIterations));
                    if (method == DiscreteMethod)
                    {
                        BuildDiscrete(field, threshold, elementLimit);
                    }
                    else
                    {
                        BuildContinuous(field, threshold, elementLimit, smoothPasses);
                    }
                }
            }

            if (cachedOutput != null)
            {
                DA.SetData(0, cachedOutput);
            }
            else if (!update)
            {
                Message = "Update Off";
            }
        }

        void BuildContinuous(VoxelField field, float threshold, int triangleLimit, int smoothPasses)
        {
            GpuVolumeMeshResult result = field.GpuVolumeMeshProvider(threshold, triangleLimit, smoothPasses);
            if (result == null || !result.Success)
            {
                AddRuntimeMessage(
                    GH_RuntimeMessageLevel.Error,
                    result != null ? result.Error : "GPU volume meshing failed.");
                return;
            }

            Type settingsType;
            Type volumeType;
            Type volumeGooType;
            if (!TryGetDendroTypes(out settingsType, out volumeType, out volumeGooType))
            {
                ReplaceCachedOutput(result.Mesh);
                Message = result.TriangleCount.ToString("N0") + " tris | Mesh fallback | " + result.Milliseconds.ToString("0.0") + " ms";
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Dendro is unavailable; outputting the marching-tetrahedra Rhino mesh.");
                return;
            }

            object settings = CreateDendroSettings(settingsType, field.VoxelSize);
            TryStoreDendroVolume(
                volumeType,
                volumeGooType,
                new object[] { result.Mesh, settings },
                "Continuous | " + result.TriangleCount.ToString("N0") + " tris | " + smoothPasses + " smooth | " + result.Milliseconds.ToString("0.0") + " ms");
        }

        void BuildDiscrete(VoxelField field, float threshold, int pointLimit)
        {
            Type settingsType;
            Type volumeType;
            Type volumeGooType;
            if (!TryGetDendroTypes(out settingsType, out volumeType, out volumeGooType))
            {
                GpuVolumeMeshResult fallback = field.GpuVolumeMeshProvider(threshold, pointLimit, 0);
                if (fallback == null || !fallback.Success)
                {
                    AddRuntimeMessage(
                        GH_RuntimeMessageLevel.Error,
                        fallback != null ? fallback.Error : "GPU volume meshing failed.");
                    return;
                }

                ReplaceCachedOutput(fallback.Mesh);
                Message = fallback.TriangleCount.ToString("N0") + " tris | Mesh fallback | " + fallback.Milliseconds.ToString("0.0") + " ms";
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "Dendro is unavailable; Discrete point kernels cannot be created, so the marching-tetrahedra Rhino mesh is being output.");
                return;
            }

            field.EnsureDynamicStateCurrent();
            VoxelGridData data = field.Data;
            int selectedCount = 0;
            for (int ordinal = 0; ordinal < data.ActiveCount; ordinal++)
            {
                int flatIndex = data.ActiveFlatIndexAt(ordinal);
                double value = field.GetScalarValue(VoxelPreviewField.SlimeChemoattractants, flatIndex);
                if (!double.IsNaN(value) && !double.IsInfinity(value) && value >= threshold)
                {
                    selectedCount++;
                    if (selectedCount > pointLimit)
                    {
                        AddRuntimeMessage(
                            GH_RuntimeMessageLevel.Error,
                            "The Discrete volume exceeds the Maximum Elements limit of " + pointLimit.ToString("N0") + ". Increase the limit or use a higher iso value.");
                        return;
                    }
                }
            }

            if (selectedCount == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No voxel density values meet the iso value.");
                return;
            }

            List<Point3d> points = new List<Point3d>(selectedCount);
            for (int ordinal = 0; ordinal < data.ActiveCount; ordinal++)
            {
                int flatIndex = data.ActiveFlatIndexAt(ordinal);
                double value = field.GetScalarValue(VoxelPreviewField.SlimeChemoattractants, flatIndex);
                if (!double.IsNaN(value) && !double.IsInfinity(value) && value >= threshold)
                {
                    points.Add(data.CenterPoint(flatIndex));
                }
            }

            object settings = CreateDendroSettings(settingsType, field.VoxelSize);
            double dendroVoxelSize = GetNumberProperty(settings, "VoxelSize", field.VoxelSize * 0.5);
            double radius = Math.Max(
                field.VoxelSize * Math.Sqrt(3.0) * 0.5005,
                dendroVoxelSize * 1.5001);
            List<double> radii = new List<double>(1) { radius };

            TryStoreDendroVolume(
                volumeType,
                volumeGooType,
                new object[] { points, radii, settings },
                "Discrete | " + selectedCount.ToString("N0") + " voxel kernels");
        }

        void TryStoreDendroVolume(Type volumeType, Type volumeGooType, object[] constructorArguments, string status)
        {
            object nextVolume = null;
            try
            {
                nextVolume = Activator.CreateInstance(volumeType, constructorArguments);
                PropertyInfo validProperty = volumeType.GetProperty("IsValid", BindingFlags.Instance | BindingFlags.Public);
                bool valid = validProperty != null && Convert.ToBoolean(validProperty.GetValue(nextVolume, null));
                if (!valid)
                {
                    DisposeObject(nextVolume);
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Dendro could not construct a valid volume.");
                    return;
                }

                object volumeGoo = Activator.CreateInstance(volumeGooType);
                PropertyInfo valueProperty = volumeGooType.GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
                if (valueProperty == null || !valueProperty.CanWrite)
                {
                    DisposeObject(nextVolume);
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The installed Dendro volume wrapper is not compatible.");
                    return;
                }

                valueProperty.SetValue(volumeGoo, nextVolume, null);
                ReplaceCachedOutput(volumeGoo);
                Message = status;
                ExpirePreview(true);
            }
            catch (TargetInvocationException ex)
            {
                DisposeObject(nextVolume);
                Exception cause = ex.InnerException ?? ex;
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Dendro conversion failed: " + cause.Message);
            }
            catch (Exception ex)
            {
                DisposeObject(nextVolume);
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Dendro conversion failed: " + ex.Message);
            }
        }

        static object CreateDendroSettings(Type settingsType, double sourceVoxelSize)
        {
            object settings = Activator.CreateInstance(settingsType);
            SetNumberProperty(settings, "VoxelSize", Math.Max(0.01, sourceVoxelSize * 0.5));
            SetNumberProperty(settings, "Bandwidth", 3.0);
            return settings;
        }

        void EnsureMethodValueList()
        {
            if (Params.Input[2].SourceCount != 0 || OnPingDocument() == null || Attributes == null)
            {
                return;
            }

            GH_ValueList list = new GH_ValueList();
            list.ListMode = GH_ValueListMode.DropDown;
            list.CreateAttributes();
            list.Attributes.Pivot = new System.Drawing.PointF(
                (float)Attributes.Pivot.X - 220,
                (float)Attributes.Pivot.Y - 11);
            list.ListItems.Clear();
            list.ListItems.Add(new GH_ValueListItem("Continuous", "0"));
            list.ListItems.Add(new GH_ValueListItem("Discrete", "1"));
            OnPingDocument().AddObject(list, false);
            Params.Input[2].AddSource(list);
            Params.Input[2].CollectData();
        }

        static bool TryGetDendroTypes(out Type settingsType, out Type volumeType, out Type volumeGooType)
        {
            settingsType = FindLoadedType("DendroGH.DendroSettings");
            volumeType = FindLoadedType("DendroGH.DendroVolume");
            volumeGooType = FindLoadedType("DendroGH.VolumeGOO");
            return settingsType != null && volumeType != null && volumeGooType != null;
        }

        static Type FindLoadedType(string fullName)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type type = assemblies[i].GetType(fullName, false);
                if (type != null) return type;
            }

            return null;
        }

        static void SetNumberProperty(object instance, string name, double value)
        {
            PropertyInfo property = instance.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            if (property != null && property.CanWrite)
            {
                property.SetValue(instance, value, null);
            }
        }

        static double GetNumberProperty(object instance, string name, double fallback)
        {
            PropertyInfo property = instance.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            return property != null && property.CanRead
                ? Convert.ToDouble(property.GetValue(instance, null))
                : fallback;
        }

        void ReplaceCachedOutput(object nextOutput)
        {
            if (!ReferenceEquals(cachedOutput, nextOutput))
            {
                DisposeObject(cachedOutput);
                cachedOutput = nextOutput;
            }
        }

        static void DisposeObject(object value)
        {
            if (value == null || value is Mesh) return;

            if (value.GetType().FullName == "DendroGH.VolumeGOO")
            {
                PropertyInfo valueProperty = value.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
                object wrappedValue = valueProperty != null ? valueProperty.GetValue(value, null) : null;
                if (wrappedValue != null && !ReferenceEquals(wrappedValue, value))
                {
                    DisposeObject(wrappedValue);
                }
                return;
            }

            IDisposable disposable = value as IDisposable;
            if (disposable != null) disposable.Dispose();
        }

        internal bool UsesSolverGpuDensity
        {
            get { return true; }
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("2cc99696-1f20-4add-82d5-a317c252edb8"); }
        }
    }
}
