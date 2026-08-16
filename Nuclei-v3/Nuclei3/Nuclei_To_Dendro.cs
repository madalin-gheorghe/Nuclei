using System;
using System.Collections.Generic;
using System.Reflection;

using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using Grasshopper.Kernel.Types;

using Rhino.Geometry;

namespace Nuclei3
{
    public sealed class NucleiToDendro : GH_Component
    {
        object cachedVolume;
        bool previousConvert;

        public NucleiToDendro()
          : base(
                "Nuclei3 To Dendro Volume",
                "To Dendro",
                "Converts a Nuclei voxel field into a native Dendro volume on demand",
                "Nuclei3",
                "Voxels")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Voxels", "voxels", "Nuclei voxel field", GH_ParamAccess.item);
            pManager.AddIntegerParameter("Type", "type", "Voxel value used to define the volume", GH_ParamAccess.item, VoxelPreviewField.SlimeChemoattractants);
            pManager.AddNumberParameter("Iso Value", "iso", "Values at or above this level become part of the Dendro volume", GH_ParamAccess.item, 0.01);
            pManager.AddGenericParameter("Dendro Settings", "settings", "Optional Dendro Volume Settings", GH_ParamAccess.item);
            pManager[3].Optional = true;
            pManager.AddBooleanParameter("Convert", "convert", "Pulse true to rebuild the cached Dendro volume", GH_ParamAccess.item, false);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Dendro Volume", "volume", "Native Dendro volume", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            EnsureTypeValueList();

            int valueIndex = VoxelPreviewField.SlimeChemoattractants;
            double isoValue = 0.01;
            object settingsInput = null;
            bool convert = false;

            DA.GetData(1, ref valueIndex);
            DA.GetData(2, ref isoValue);
            DA.GetData(3, ref settingsInput);
            DA.GetData(4, ref convert);

            bool requested = convert && !previousConvert;
            previousConvert = convert;

            if (requested)
            {
                object voxelInput = null;
                DA.GetData(0, ref voxelInput);
                Voxel[,,] field = Unwrap(voxelInput) as Voxel[,,];
                if (field == null)
                {
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Connect a valid Nuclei voxel field.");
                }
                else
                {
                    TryConvert(field, valueIndex, isoValue, settingsInput);
                }
            }

            if (cachedVolume != null)
            {
                DA.SetData(0, cachedVolume);
            }
            else
            {
                Message = "Pulse Convert";
            }
        }

        void TryConvert(Voxel[,,] field, int valueIndex, double isoValue, object settingsInput)
        {
            Type settingsType = FindDendroType("DendroGH.DendroSettings");
            Type volumeType = FindDendroType("DendroGH.DendroVolume");
            Type volumeGooType = FindDendroType("DendroGH.VolumeGOO");
            if (settingsType == null || volumeType == null || volumeGooType == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Dendro is not loaded. Install Dendro and restart Rhino.");
                return;
            }

            object settings = Unwrap(settingsInput);
            if (settings == null || !settingsType.IsInstanceOfType(settings))
            {
                settings = Activator.CreateInstance(settingsType);
                SetNumberProperty(settings, "VoxelSize", Math.Max(0.01, SourceVoxelSize(field) * 0.5));
                SetNumberProperty(settings, "Bandwidth", 3.0);
            }

            double sourceVoxelSize = SourceVoxelSize(field);
            double dendroVoxelSize = GetNumberProperty(settings, "VoxelSize", sourceVoxelSize * 0.5);
            double radius = Math.Max(
                sourceVoxelSize * Math.Sqrt(3.0) * 0.5005,
                dendroVoxelSize * 1.5001);

            List<Point3d> points = new List<Point3d>();
            List<double> radii = new List<double>(1) { radius };
            for (int x = 0; x < field.GetLength(0); x++)
            {
                for (int y = 0; y < field.GetLength(1); y++)
                {
                    for (int z = 0; z < field.GetLength(2); z++)
                    {
                        Voxel voxel = field[x, y, z];
                        if (voxel == null)
                        {
                            continue;
                        }

                        double value = FieldValue(voxel, valueIndex);
                        if (double.IsNaN(value) || double.IsInfinity(value) || value < isoValue)
                        {
                            continue;
                        }

                        points.Add(voxel.loc);
                    }
                }
            }

            if (points.Count == 0)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "No voxel values meet the iso value.");
                return;
            }

            try
            {
                object nextVolume = Activator.CreateInstance(volumeType, new object[] { points, radii, settings });
                PropertyInfo validProperty = volumeType.GetProperty("IsValid", BindingFlags.Instance | BindingFlags.Public);
                bool valid = validProperty != null && Convert.ToBoolean(validProperty.GetValue(nextVolume, null));
                if (!valid)
                {
                    DisposeObject(nextVolume);
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Dendro could not construct a volume. Reduce the Dendro voxel size or increase the source voxel size.");
                    return;
                }

                object volumeGoo = Activator.CreateInstance(volumeGooType);
                PropertyInfo valueProperty = volumeGooType.GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
                if (valueProperty == null || !valueProperty.CanWrite)
                {
                    DisposeObject(nextVolume);
                    AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Dendro volume wrapper is not compatible with this Dendro version.");
                    return;
                }

                valueProperty.SetValue(volumeGoo, nextVolume, null);
                DisposeObject(cachedVolume);
                cachedVolume = volumeGoo;
                Message = points.Count.ToString("N0") + " voxels";
                ExpirePreview(true);
            }
            catch (TargetInvocationException ex)
            {
                Exception cause = ex.InnerException ?? ex;
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Dendro conversion failed: " + cause.Message);
            }
            catch (Exception ex)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Dendro conversion failed: " + ex.Message);
            }
        }

        static double SourceVoxelSize(Voxel[,,] field)
        {
            for (int x = 0; x < field.GetLength(0); x++)
            {
                for (int y = 0; y < field.GetLength(1); y++)
                {
                    for (int z = 0; z < field.GetLength(2); z++)
                    {
                        Voxel voxel = field[x, y, z];
                        if (voxel != null && voxel.voxelSize > 0)
                        {
                            return voxel.voxelSize;
                        }
                    }
                }
            }

            return Globals.voxelSize > 0 ? Globals.voxelSize : 1.0;
        }

        static double FieldValue(Voxel voxel, int valueIndex)
        {
            if (valueIndex == VoxelPreviewField.AntPheromones)
            {
                return Math.Max(voxel.towardsFoodPheromone, voxel.towardsBasePheromone);
            }

            if (valueIndex == VoxelPreviewField.AntsAndSlime)
            {
                return Math.Max(
                    voxel.density,
                    Math.Max(voxel.towardsFoodPheromone, voxel.towardsBasePheromone));
            }

            switch (valueIndex)
            {
                case VoxelPreviewField.MinimumDensity: return voxel.minDensity;
                case VoxelPreviewField.MaximumDensity: return voxel.maxDensity;
                case VoxelPreviewField.Speed: return voxel.speedMultiplier;
                case VoxelPreviewField.SensorDistance: return voxel.sensorDistanceMultiplier;
                case VoxelPreviewField.SensorAngle: return voxel.sensorAngleMultiplier;
                case VoxelPreviewField.RotationAngle: return voxel.rotationAngleMultiplier;
                case VoxelPreviewField.Food: return voxel.food;
                case VoxelPreviewField.SlimeChemoattractants: return voxel.density;
                case VoxelPreviewField.AntFoodPheromones: return voxel.towardsFoodPheromone;
                case VoxelPreviewField.AntBasePheromones: return voxel.towardsBasePheromone;
                default: return 0;
            }
        }

        void EnsureTypeValueList()
        {
            if (Params.Input[1].SourceCount != 0 || OnPingDocument() == null)
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
            list.ListItems.Add(new GH_ValueListItem("Minimum Density", "0"));
            list.ListItems.Add(new GH_ValueListItem("Maximum Density", "1"));
            list.ListItems.Add(new GH_ValueListItem("Speed", "2"));
            list.ListItems.Add(new GH_ValueListItem("Sensor Distance", "3"));
            list.ListItems.Add(new GH_ValueListItem("Sensor Angle", "4"));
            list.ListItems.Add(new GH_ValueListItem("Rotation Angle", "5"));
            list.ListItems.Add(new GH_ValueListItem("Food", "6"));
            list.ListItems.Add(new GH_ValueListItem("Slime Chemoattractants", "7"));
            list.ListItems.Add(new GH_ValueListItem("Ant Food Pheromones", "8"));
            list.ListItems.Add(new GH_ValueListItem("Ant Base Pheromones", "9"));
            list.ListItems.Add(new GH_ValueListItem("Ant Pheromones", "10"));
            list.ListItems.Add(new GH_ValueListItem("Ants and Slime", "11"));
            OnPingDocument().AddObject(list, false);
            Params.Input[1].AddSource(list);
            Params.Input[1].CollectData();
        }

        static Type FindDendroType(string fullName)
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                Type type = assemblies[i].GetType(fullName, false);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }

        static object Unwrap(object value)
        {
            GH_ObjectWrapper wrapper = value as GH_ObjectWrapper;
            if (wrapper != null)
            {
                return wrapper.Value;
            }

            IGH_Goo goo = value as IGH_Goo;
            if (goo != null)
            {
                object scriptValue = goo.ScriptVariable();
                if (scriptValue != null && !ReferenceEquals(scriptValue, value))
                {
                    return scriptValue;
                }
            }

            return value;
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
            if (property == null || !property.CanRead)
            {
                return fallback;
            }

            return Convert.ToDouble(property.GetValue(instance, null));
        }

        static void DisposeObject(object value)
        {
            IDisposable disposable = value as IDisposable;
            if (disposable != null)
            {
                disposable.Dispose();
                return;
            }

            if (value != null && value.GetType().FullName == "DendroGH.VolumeGOO")
            {
                PropertyInfo valueProperty = value.GetType().GetProperty("Value", BindingFlags.Instance | BindingFlags.Public);
                object wrappedValue = valueProperty != null ? valueProperty.GetValue(value, null) : null;
                if (wrappedValue != null && !ReferenceEquals(wrappedValue, value))
                {
                    DisposeObject(wrappedValue);
                }
            }
        }

        public override void RemovedFromDocument(Grasshopper.Kernel.GH_Document document)
        {
            DisposeObject(cachedVolume);
            cachedVolume = null;
            base.RemovedFromDocument(document);
        }

        public override Guid ComponentGuid
        {
            get { return new Guid("a112e5da-aca3-48f2-9c7a-23181d627e54"); }
        }
    }
}
