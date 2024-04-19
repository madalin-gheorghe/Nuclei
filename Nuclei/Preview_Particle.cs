using System;
using System.Collections.Generic;
using System.Drawing;
using Grasshopper.Kernel;
using Rhino.Geometry;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace Nuclei3
{
    public class Preview_Particle : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the DeathSettings class.
        /// </summary>
        public Preview_Particle()
          : base("Particle Preview Settings", "Particle Preview",
              "Sets Up Dynamic Particle Preview Settings",
              "Nuclei3", "Preview")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddGenericParameter("Particles", "particles", "Input Particles", GH_ParamAccess.item);

            pManager.AddNumberParameter("Point Size", "size", "Point Display Size", GH_ParamAccess.item, 2);
            pManager[1].Optional = true;
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            //pManager.AddTextParameter("Particle Preview", "particlePreview", "Settings Controlling Particle Preview. Connects to Solver's Display Input", GH_ParamAccess.list);
        }
        

        public override GH_Exposure Exposure
        {
            get { return GH_Exposure.secondary; }
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            colour = System.Drawing.Color.FromArgb(175, 220, 255, 0);

            DA.GetData(0, ref particles);
            DA.GetData("Point Size", ref size);

        }

        public override void DrawViewportMeshes(IGH_PreviewArgs args)
        {
            if (!this.Hidden && particles != null)
            {
                base.DrawViewportWires(args);

            //draw background polygon
            if (!Globals.tridimensional)
            {
                args.Display.DrawPolygon(Globals.bgPolygon, Color.Black, true);
            }
                //particles
                PointCloud slimePointCloud = new PointCloud();
                PointCloud antPointCloud1 = new PointCloud();
                PointCloud antPointCloud2 = new PointCloud();

                if (particles != null)
                {
                    for (int i = 0; i < particles.Count; i++)
                    {
                        Particle P = particles[i];
                        Point3d P_loc = P.PPlane.Origin;
                        Color particleColor;

                        if (!P.parentParticleGroup.ant)
                        {
                            particleColor = P.parentParticleGroup.color;
                            slimePointCloud.Add(P_loc, particleColor);
                        }
                        else
                        {
                            if (!P.foundFood)
                            {
                                particleColor = P.parentParticleGroup.color;
                                antPointCloud1.Add(P_loc, particleColor);
                            }
                            else
                            {
                                //int antColorIndex = Globals.tag_global.IndexOf(P.tag) % Globals.antColorList.Count;
                                int R = P.parentParticleGroup.color.R;
                                int G = P.parentParticleGroup.color.G;
                                int B = P.parentParticleGroup.color.B;

                                R = (int)Math.Floor(R * 1.75);
                                if (R > 255) R = 255;

                                G = (int)Math.Floor(G * 1.75);
                                if (G > 255) G = 255;

                                B = (int)Math.Floor(B * 1.75);
                                if (B > 255) B = 255;

                                particleColor = Color.FromArgb(175, R, G, B);
                                antPointCloud2.Add(P_loc, particleColor);
                            }
                        }
                    }
                }

                if (slimePointCloud.Count > 0) args.Display.DrawPointCloud(slimePointCloud, (float)size);
                if (antPointCloud1.Count > 0) args.Display.DrawPointCloud(antPointCloud1, (float)size);
                if (antPointCloud2.Count > 0) args.Display.DrawPointCloud(antPointCloud2, (float)(size * 1.5));
            }

        }

        //-------------------------------------------------------------------

        //inputs
        List<Particle> particles;
        //bool display;
        Color colour;
        double size;

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
                return Nuclei3.Properties.Resources.PreviewParticles;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("a4a9e7cb-f899-461a-9137-207d4601ae14"); }
        }
    }
}