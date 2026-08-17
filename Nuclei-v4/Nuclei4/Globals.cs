using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using Grasshopper.Kernel;
using Rhino.Geometry;
using Grasshopper;
using System.Drawing;
using System.Diagnostics;
using System.Threading;

namespace Nuclei4
{
	public class Globals
	{


        //voxel properties
        public static double voxelSize = 1;
        public static bool tridimensional = false;

        //ParticleGroup
        public static List <ParticleGroup> particleGroups = new List <ParticleGroup> (); 

        //random
        public static Random randomNameNumber = new Random();

        //display
        public static List <Color> particleColorList = new List<Color>();
        public static List <Color> antColorList = new List<Color>();
        public static List <Color> antColorList_foundFood = new List<Color>();

        public static List<Color> voxelColorList_White = new List<Color>();
        public static List<Color> voxelColorList_chemoAttractants = new List<Color>();
        public static List<Color> voxelColorList_antFoodPheromones = new List<Color>();
        public static List<Color> voxelColorList_antBasePheromones = new List<Color>();

        public static List<Point3d> bgPolygon = new List<Point3d>();

    }
}
