using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nuclei3.Properties
{
   public class Nuclei2Icon : Grasshopper.Kernel.GH_AssemblyPriority
        {
            public override Grasshopper.Kernel.GH_LoadingInstruction PriorityLoad()
            {
                int rhinoVersion = Rhino.RhinoApp.ExeVersion;
                if (rhinoVersion != 8 && rhinoVersion != 9)
                {
                    Rhino.RhinoApp.WriteLine("Nuclei3 requires Rhino 8 or 9. Use Nuclei2 for Rhino 6/7.");
                    return Grasshopper.Kernel.GH_LoadingInstruction.Abort;
                }

                Grasshopper.Instances.ComponentServer.AddCategoryIcon("Nuclei3", Nuclei3.Properties.Resources.Nuclei2);
                Grasshopper.Instances.ComponentServer.AddCategoryShortName("Nuclei3", "N3");
                Grasshopper.Instances.ComponentServer.AddCategorySymbolName("Nuclei3", 'N');

                if (rhinoVersion == 9)
                    OldV3Banner.Register();

                return Grasshopper.Kernel.GH_LoadingInstruction.Proceed;
            }
        }
}
