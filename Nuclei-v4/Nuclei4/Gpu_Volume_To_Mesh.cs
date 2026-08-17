using System;

using Grasshopper.Kernel;

using Rhino.Geometry;

namespace Nuclei4
{
    public sealed class GpuVolumeToMesh : GH_Component
    {
        Mesh cachedMesh;

        public GpuVolumeToMesh()
          : base(
                "Nuclei4 GPU Volume To Mesh",
                "Nuclei4 GPU Volume To Mesh",
                "Extracts a Rhino mesh directly from the Solver GPU density field",
                "Nuclei4",
                "Voxels")
        {
        }

        protected override void RegisterInputParams(GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Voxels", "voxels", "Voxel output from Nuclei4 Solver GPU", GH_ParamAccess.item);
            pManager.AddNumberParameter("Iso Value", "iso", "Density level used to extract the surface", GH_ParamAccess.item, 0.8);
            pManager.AddIntegerParameter("Maximum Triangles", "max", "Safety limit for the generated Rhino mesh", GH_ParamAccess.item, 5000000);
            pManager.AddBooleanParameter("Update", "update", "Rebuild the mesh whenever the component receives updated inputs", GH_ParamAccess.item, false);
            pManager.AddIntegerParameter("Smoothing Iterations", "smooth", "GPU volume-smoothing passes before meshing; 0 disables smoothing", GH_ParamAccess.item, 1);
        }

        protected override void RegisterOutputParams(GH_OutputParamManager pManager)
        {
            pManager.AddMeshParameter("Mesh", "mesh", "GPU-extracted density isosurface", GH_ParamAccess.item);
        }

        protected override void SolveInstance(IGH_DataAccess DA)
        {
            VoxelField field;
            double isoValue = 0.8;
            int maximumTriangles = 5000000;
            bool update = false;
            int smoothingIterations = 1;

            VoxelFieldAccess.TryGet(DA, 0, Globals.voxelSize, out field);
            DA.GetData(1, ref isoValue);
            DA.GetData(2, ref maximumTriangles);
            DA.GetData(3, ref update);
            DA.GetData(4, ref smoothingIterations);

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
                    int triangleLimit = Math.Max(1, maximumTriangles);
                    int smoothPasses = Math.Max(0, Math.Min(8, smoothingIterations));
                    GpuVolumeMeshResult result = field.GpuVolumeMeshProvider(threshold, triangleLimit, smoothPasses);
                    if (result == null || !result.Success)
                    {
                        AddRuntimeMessage(
                            GH_RuntimeMessageLevel.Error,
                            result != null ? result.Error : "GPU volume meshing failed.");
                    }
                    else
                    {
                        cachedMesh = result.Mesh;
                        Message = result.TriangleCount.ToString("N0") + " tris | " + smoothPasses + " smooth | " + result.Milliseconds.ToString("0.0") + " ms";
                    }
                }
            }

            if (cachedMesh != null)
            {
                DA.SetData(0, cachedMesh);
            }
            else if (!update)
            {
                Message = "Update Off";
            }
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
