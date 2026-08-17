using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nuclei4.Properties
{
   public class Nuclei2Icon : Grasshopper.Kernel.GH_AssemblyPriority
        {
            public override Grasshopper.Kernel.GH_LoadingInstruction PriorityLoad()
            {
                if (Rhino.RhinoApp.ExeVersion < 9)
                {
                    Rhino.RhinoApp.WriteLine("Nuclei4 requires Rhino 9 or newer.");
                    return Grasshopper.Kernel.GH_LoadingInstruction.Abort;
                }

                Grasshopper.Instances.ComponentServer.AddCategoryIcon("Nuclei4", Nuclei4.Properties.Resources.Nuclei2);
                Grasshopper.Instances.ComponentServer.AddCategoryShortName("Nuclei4", "N4");
                Grasshopper.Instances.ComponentServer.AddCategorySymbolName("Nuclei4", 'N');

                return Grasshopper.Kernel.GH_LoadingInstruction.Proceed;
            }
        }
}
