using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections.Concurrent;

using Grasshopper.Kernel;
using Rhino.Geometry;
using System.Threading.Tasks;
using System.Drawing;

namespace Nuclei3
{
    public class VoxelConstructor : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the VoxelConstructor class.
        /// </summary>
        /// 

        public VoxelConstructor()
          : base("Construct Voxels", "Construct Voxels",
              "Construct Empty Voxel Field Environment",
              "Nuclei4", " Environment")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            //0
            pManager.AddNumberParameter("Voxel Size", "voxelSize", "Size of one voxel in x,y,z", GH_ParamAccess.item, 1.0);
            //1
            pManager.AddIntegerParameter("X Voxels", "xVoxels", "Number of voxels in X", GH_ParamAccess.item, 100);
            //2
            pManager.AddIntegerParameter("Y Voxels", "yVoxels", "Number of voxels in Y", GH_ParamAccess.item, 100);
            //3
            pManager.AddIntegerParameter("Z Voxels", "zVoxels", "Number of voxels in Z", GH_ParamAccess.item, 1);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            //0
            pManager.AddGenericParameter("Output Voxels", "voxels", "Output Voxels", GH_ParamAccess.item);
        }

        public override GH_Exposure Exposure
        {
            get { return GH_Exposure.primary; }
        }

        
        //make sure only one instance is allowed in the document
        public override void AddedToDocument(GH_Document document)
        {

            GH_Document doc = OnPingDocument();

            foreach (IGH_DocumentObject obj in doc.Objects)
            {
                //Looping through the objects in the active canvas document
                if ((obj.ComponentGuid == this.ComponentGuid) && (obj.InstanceGuid != this.InstanceGuid))
                {
                    //If the component Guid matches and if the instance Guids are different...
                    System.Windows.Forms.MessageBox.Show("There is already an instance of Nuclei's 'Construct Voxels' component on the canvas. Only one instance is allowed.", "Error", System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Error); //Tell the user what's going on...
                    doc.RemoveObject(this, false); //True or false - do we want to recompute the canvas? We're removing a component that was unnecessary, so I'm setting this to false - we don't need to recompute the canvas
                    break; //We found and removed our additional instance, we don't need to continue looping (saves computational time and prevents a Grasshopper complaint that the collection (Document.Objects) was modified when you're trying to use it!
                }
            }

            base.AddedToDocument(document);
        }
        

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            //set inputs
            DA.GetData(0, ref voxelSize);
            DA.GetData(1, ref resX);
            DA.GetData(2, ref resY);
            DA.GetData(3, ref resZ);

            Globals.voxelSize = voxelSize;

            resX = Math.Max(1, resX);
            resY = Math.Max(1, resY);
            resZ = Math.Max(1, resZ);
            voxelSize = voxelSize > 0 ? voxelSize : 1.0;

            bool sameGrid = voxelField != null
                && voxelField.ResX == resX
                && voxelField.ResY == resY
                && voxelField.ResZ == resZ
                && Math.Abs(cachedVoxelSize - voxelSize) < 1e-12;

            if (!sameGrid)
            {
                cachedVoxelData = VoxelGridData.CreateFullDomain(resX, resY, resZ, voxelSize);
                voxelField = new VoxelField(cachedVoxelData);
                cachedVoxelSize = voxelSize;
            }

            DA.SetData(0, voxelField);

            initializeBGPolygon();
            initializeVoxelColors();

            long voxelNumber = (long)resX * resY * resZ;
            this.Message = "Voxels: " + voxelNumber;
        }

        public override void DrawViewportMeshes(IGH_PreviewArgs args)
        {
            base.DrawViewportWires(args);
            Box boundingBox = new Box(Plane.WorldXY, new Interval(0, resX * voxelSize), new Interval(0, resY * voxelSize), new Interval(0, resZ * voxelSize));
            args.Display.DrawBox(boundingBox, System.Drawing.Color.Purple);
        }

        //-------------------------------------------------------------------

        //inputs
        double voxelSize;
        double cachedVoxelSize = double.NaN;
        int resX, resY, resZ = 1;
        VoxelField voxelField;
        VoxelGridData cachedVoxelData;

        //-------------------------------------------------------------------

        //global display

        void initializeBGPolygon()
        {
            Globals.bgPolygon = new List<Point3d>();

            //determine voxel space dimensions
            double dimX = resX * voxelSize;
            double dimY = resY * voxelSize;
            double dimZ = resZ * voxelSize;

            //determine whether 3D or 2D 
            bool planarXY = false;
            bool planarXZ = false;
            bool planarYZ = false;
            bool tridimensional = false;

            if (resX == 1)
            {
                planarXY = false;
                planarXZ = false;
                planarYZ = true;
                tridimensional = false;

                Globals.tridimensional = false;
            }
            if (resY == 1)
            {
                planarXY = false;
                planarXZ = true;
                planarYZ = false;
                tridimensional = false;

                Globals.tridimensional = false;
            }
            if (resZ == 1)
            {
                planarXY = true;
                planarXZ = false;
                planarYZ = false;
                tridimensional = false;

                Globals.tridimensional = false;
            }

            if (resX > 1 && resY > 1 && resZ > 1)
            {
                tridimensional = true;
                planarXY = false;
                planarXZ = false;
                planarYZ = false;

                Globals.tridimensional = true;
            }
            else
            {
                tridimensional = false;

                Globals.tridimensional = false;
            }

            if (planarXY)
            {
                Globals.bgPolygon.Add(new Point3d(0, 0, 0));
                Globals.bgPolygon.Add(new Point3d(dimX, 0, 0));
                Globals.bgPolygon.Add(new Point3d(dimX, dimY, 0));
                Globals.bgPolygon.Add(new Point3d(0, dimY, 0));
            }

            if (planarXZ)
            {
                Globals.bgPolygon.Add(new Point3d(0, voxelSize, 0));
                Globals.bgPolygon.Add(new Point3d(dimX, voxelSize, 0));
                Globals.bgPolygon.Add(new Point3d(dimX, voxelSize, dimZ));
                Globals.bgPolygon.Add(new Point3d(0, voxelSize, dimZ));
            }

            if (planarYZ)
            {
                Globals.bgPolygon.Add(new Point3d(0, 0, 0));
                Globals.bgPolygon.Add(new Point3d(0, dimY, 0));
                Globals.bgPolygon.Add(new Point3d(0, dimY, dimZ));
                Globals.bgPolygon.Add(new Point3d(0, 0, dimZ));
            }
        }


        void initializeVoxelColors()
        {
            VoxelPreviewPalette.EnsureInitialized();
        }

        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                //You can add image files to your project resources and access them like this:
                // return Resources.IconForThisComponent;
                return Nuclei3.Properties.Resources.Environment;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        /// 

        public override Guid ComponentGuid
        {
            get { return new Guid("a3940a4d-9015-411c-9ffa-e38ecc90d394"); }
        }
    }
}
