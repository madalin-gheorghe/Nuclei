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
                Grasshopper.Instances.ComponentServer.AddCategoryIcon("Nuclei3", Nuclei3.Properties.Resources.Nuclei2);
                Grasshopper.Instances.ComponentServer.AddCategoryShortName("Nuclei3", "N3");
                Grasshopper.Instances.ComponentServer.AddCategorySymbolName("Nuclei3", 'N');

                return Grasshopper.Kernel.GH_LoadingInstruction.Proceed;
            }
        }
}
