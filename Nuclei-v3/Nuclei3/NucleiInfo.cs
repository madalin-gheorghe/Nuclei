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
                return "Nuclei3";
            }
        }

        public override Bitmap Icon
        {
            get
            {
                return null;
            }
        }

        public override string Description
        {
            get
            {
                return "Neighbour Sensing Models Plugin";
            }
        }

        public override Guid Id
        {
            get
            {
                return new Guid("fe53d2b8-e56d-da70-cde9-0b078f8bc65d");
            }
        }

        public override string AuthorName
        {
            get
            {
                return "Madalin Gheorghe";
            }
        }

        public override string AuthorContact
        {
            get
            {
                return "";
            }
        }
    }
}

