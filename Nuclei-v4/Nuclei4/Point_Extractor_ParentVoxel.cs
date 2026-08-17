using Grasshopper.Kernel;
using Rhino.Geometry;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Linq;
using Grasshopper.Kernel.Data;
using Grasshopper.Kernel.Types;
using Grasshopper;
using System.Diagnostics;

namespace Nuclei4
{
    public class Point_Extractor_ParentVoxel : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the Particle_Extractor_TrailPoints class.
        /// </summary>
        public Point_Extractor_ParentVoxel()
          : base("Extract Parent Voxel", "Extract Parent Voxel",
              "Extract The Voxel in which The Point Is Contained",
              "Nuclei4", "Utility")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            //0
            pManager.AddPointParameter("Points", "points", "Input Points", GH_ParamAccess.tree);
            //1
            pManager.AddGenericParameter("Voxels", "voxels", "Connects to Voxel Constructor", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddNumberParameter("Point Parent Voxel", "parentVoxel", "Point Parent Voxel", GH_ParamAccess.list);
        }

        public override GH_Exposure Exposure
        {
            get { return GH_Exposure.primary; }
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            points = new GH_Structure<GH_Point>();
            //set inputs
            DA.GetDataTree(0, out points);
            VoxelFieldAccess.TryGet(DA, 1, Globals.voxelSize, out inputVoxelField);
            if (inputVoxelField == null)
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Warning, "A valid voxel field is required.");
                return;
            }

            voxelData = inputVoxelField.Data;
            resX = voxelData.ResX;
            resY = voxelData.ResY;
            resZ = voxelData.ResZ;
            voxelSize = voxelData.VoxelSize;

            outputVoxelIndices = new DataTree<int>();

            for (int i = 0; i < points.Branches.Count; i++)
            {
                for (int j = 0; j < points.Branches[i].Count; j++)
                {
                    List<GH_Point> gH_Points = points.Branches[i];
                    GH_Point p = gH_Points[j];

                    outputVoxelIndices.Add(getParentVoxel(p), points.get_Path(i));
                }
            }

            DA.SetDataTree(0, outputVoxelIndices);
        }

        //-------------------------------------------------------------------

        //inputs
        VoxelField inputVoxelField;
        VoxelGridData voxelData;

        GH_Structure<GH_Point> points;

        int resX,resY,resZ;
        double voxelSize;

        //-------------------------------------------------------------------

        //outputs
        DataTree<int> outputVoxelIndices;

        //-------------------------------------------------------------------

        int getParentVoxel(GH_Point p)
        {
            int result = -1;
            
            int xID = System.Convert.ToInt32((p.Value.X - Math.Abs(p.Value.X % voxelSize)) / voxelSize);
            int yID = System.Convert.ToInt32((p.Value.Y - Math.Abs(p.Value.Y % voxelSize)) / voxelSize);
            int zID = System.Convert.ToInt32((p.Value.Z - Math.Abs(p.Value.Z % voxelSize)) / voxelSize);

            if (xID >= 0 && xID < resX && yID >= 0 && yID < resY && zID >= 0 && zID < resZ)
            {
                result = voxelData.ActiveOrdinalFromFlatIndex(voxelData.FlatIndex(xID, yID, zID));
            }
            

            return result;
        }

        //-------------------------------------------------------------------

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                //You can add image files to your project resources and access them like this:
                // return Resources.IconForThisComponent;
                return Nuclei4.Properties.Resources.Point_ParentVoxel;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("f4f3534c-1921-448d-b785-dd0854bc8bed"); }
        }
    }
}
