using Grasshopper.Kernel;
using Rhino.Geometry;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Nuclei3
{
    public class Voxel_Vectors_Discretize : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the Voxel_Vectors class.
        /// </summary>
        public Voxel_Vectors_Discretize()
          : base("Define Discrete Vectors", "Voxel Discrete Vectors",
              "Define Voxel Discrete Vectors",
              "Nuclei3", " Environment")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddVectorParameter("Voxel Discrete Vectors", "discreteVectors", "Vector Assigned to Voxel", GH_ParamAccess.list);
            pManager[0].DataMapping = GH_DataMapping.Flatten;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddTextParameter("Discrete Vector Settings", "discreteSettings", "Settings For Discrete Vectors", GH_ParamAccess.list);
        }

        public override GH_Exposure Exposure
        {
            get { return GH_Exposure.quarternary; }
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            //set inputs
            voxelVectors = new List<Vector3d>();
            DA.GetDataList("Voxel Discrete Vectors", voxelVectors);

            String voxelVectorsSettings = "DiscreteVectors";

            for (int i=0; i < voxelVectors.Count; i++)
            {
                Vector3d V = voxelVectors[i];
                V.Unitize();
                voxelVectorsSettings += " " + V.X.ToString() + "," + V.Y.ToString() + "," +  V.Z.ToString();
            }

            DA.SetData(0,voxelVectorsSettings);

        }

        //-------------------------------------------------------------------

        //inputs
        List<Vector3d> voxelVectors;

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
                return Nuclei3.Properties.Resources.discretize;
                //return null;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("78272e15-9001-5093-ac07-1d95c974886e"); }
        }
    }
}