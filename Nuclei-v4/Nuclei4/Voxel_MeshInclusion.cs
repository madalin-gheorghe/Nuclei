using Grasshopper.Kernel;
using Grasshopper.Kernel.Data;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nuclei4
{
    public class Voxel_MeshInclusion : GH_Component
    {
        VoxelOutputDemand outputDemand;
        public override void AddedToDocument(GH_Document document)
        {
            base.AddedToDocument(document);
            if (outputDemand == null) outputDemand = new VoxelOutputDemand(this);
            outputDemand.Attach(document);
        }
        public override void RemovedFromDocument(GH_Document document)
        {
            outputDemand?.Detach();
            base.RemovedFromDocument(document);
        }
        public Voxel_MeshInclusion() : base("Voxel Inclusion in Mesh", "Voxel Inclusion in Mesh", "Test if a Voxel Center is Inside a Mesh", "Nuclei4", " Environment") { }
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Voxels", "voxels", "Connects to Voxel Constructor", GH_ParamAccess.item);
            p.AddMeshParameter("Inclusion Meshes", "inclusionMeshes", "Inclusion Meshes", GH_ParamAccess.list);
            p[1].DataMapping = GH_DataMapping.Flatten;
            p.AddBooleanParameter("Invert Voxel Selection", "invertSelection", "Inverts the Voxel Selection", GH_ParamAccess.item, false);
        }
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Output Voxels", "voxels", "Output Voxels", GH_ParamAccess.item);
            p.AddPointParameter("Output Voxel Positions", "voxelPosition", "Selected centers grouped by first containing mesh; computed only when connected", GH_ParamAccess.list);
            p.HideParameter(1);
            p.AddIntegerParameter("Output Voxel Indices", "voxelIndex", "Zero-based selected-voxel ordinals, matching voxelPosition branches; computed only when connected", GH_ParamAccess.list);
        }
        protected override void SolveInstance(IGH_DataAccess da)
        {
            VoxelField field;
            if (!VoxelFieldAccess.TryGet(da, "Voxels", Globals.voxelSize, out field)) return;
            var meshes = new List<Mesh>();
            bool invert = false;
            if (!da.GetDataList(1, meshes) || !da.GetData(2, ref invert)) return;
            var data = field.Data;
            bool positions = Params.Output[1].Recipients.Count > 0;
            bool indices = Params.Output[2].Recipients.Count > 0;
            var owners = (positions || indices) && !invert ? new int[data.ActiveCount] : null;
            var bounds = new BoundingBox[meshes.Count];
            for (int m = 0; m < meshes.Count; m++)
            {
                bounds[m] = meshes[m]?.GetBoundingBox(true) ?? BoundingBox.Unset;
                if (bounds[m].IsValid) bounds[m].Inflate(data.VoxelSize / 2);
            }
            var selected = new VoxelSelectionBuilder(data.Count);
            Parallel.For(0, data.ActiveCount, ordinal =>
            {
                int flat = data.ActiveFlatIndexAt(ordinal);
                var point = data.CenterPoint(flat);
                int owner = -1;
                for (int m = 0; m < meshes.Count; m++)
                    if (meshes[m] != null && (!bounds[m].IsValid || bounds[m].Contains(point)) && meshes[m].IsPointInside(point, data.VoxelSize * 1e-6, true)) { owner = m; break; }
                if (owners != null) owners[ordinal] = owner;
                if ((owner >= 0) != invert) selected.SetThreadSafe(flat);
            });
            var output = selected.ApplyTo(data);
            da.SetData(0, field.WithData(output));
            if (!positions && !indices) return;
            var pts = new Grasshopper.DataTree<Point3d>();
            var ids = new Grasshopper.DataTree<int>();
            for (int n = 0; n < output.ActiveCount; n++)
            {
                int flat = output.ActiveFlatIndexAt(n);
                var path = new GH_Path(invert ? 0 : owners[data.ActiveOrdinalFromFlatIndex(flat)]);
                if (positions) pts.Add(output.CenterPoint(flat), path);
                if (indices) ids.Add(n, path);
            }
            if (positions) da.SetDataTree(1, pts);
            if (indices) da.SetDataTree(2, ids);
        }
        public override GH_Exposure Exposure => GH_Exposure.quinary;
        protected override System.Drawing.Bitmap Icon => Properties.Resources.VoxelMeshInclusion;
        public override Guid ComponentGuid => new Guid("74684ab4-cbf5-4cc1-a75a-4253eb83599b");
    }
}
