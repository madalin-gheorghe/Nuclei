using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;

namespace Nuclei3
{
    public class Voxel_Extractor_Box : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the Voxel_Extractor_Box class.
        /// </summary>
        public Voxel_Extractor_Box()
          : base("Extract Voxel Bounding Box", "Voxel Box",
              "Extract Voxel Design Space Bounding Box",
              "Nuclei3", " Environment")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Voxels", "voxels", "Connects to Voxel Constructor", GH_ParamAccess.item);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddBoxParameter("Voxel Box", "BBox", "Voxel Bounding Box", GH_ParamAccess.item);
        }

        public override GH_Exposure Exposure
        {
            get { return GH_Exposure.septenary; }
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            DA.GetData(0, ref voxel);

            //determine voxel settings
            int resX = voxel.GetLength(0);
            int resY = voxel.GetLength(1);
            int resZ = voxel.GetLength(2);

            double voxelSize = Globals.voxelSize;

            Box bBox = new Box(Rhino.Geometry.Plane.WorldXY, new Interval(0, resX * voxelSize), new Interval(0, resY * voxelSize), new Interval(0, resZ * voxelSize));
            DA.SetData(0, bBox);
        }

        //-------------------------------------------------------------------

        //inputs
        Voxel[,,] voxel;

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
                return Nuclei3.Properties.Resources.VoxelBox;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("65bed543-3aed-593f-bf4f-4a2a6a57e41d"); }
        }
    }
}