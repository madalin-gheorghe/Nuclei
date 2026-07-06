using System;
using System.Drawing;
using Grasshopper.Kernel;

namespace Nuclei3
{
    public class Nuclei3Info : GH_AssemblyInfo
    {
        public override string Name
        {
            get
            {
                return "Nuclei4";
            }
        }
        public override Bitmap Icon
        {
            get
            {
                //Return a 24x24 pixel bitmap to represent this GHA library.
                return null;
            }
        }
        public override string Description
        {
            get
            {
                //Return a short string describing the purpose of this GHA library.
                return "Neighbour Sensing Models Plugin";
            }
        }
        public override Guid Id
        {
            get
            {
                return new Guid("a4810f34-10b6-480c-a6d0-607aac4e8d2a");
            }
        }

        public override string AuthorName
        {
            get
            {
                //Return a string identifying you or your company.
                return "Madalin Gheorghe";
            }
        }
        public override string AuthorContact
        {
            get
            {
                //Return a string representing your preferred contact details.
                return "";
            }
        }
    }
}
