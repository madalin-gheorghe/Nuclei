using System;
using System.Drawing;
using System.Linq;
using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;

namespace Nuclei4
{
    public sealed class Voxel_PeriodicSurface : GH_Component
    {
        long selectedVoxelCount;

        public override void ClearData()
        {
            base.ClearData();
            selectedVoxelCount = 0;
            VoxelAttractorOutputs.WriteCount(this, ref selectedVoxelCount, 0);
        }

        // Keep surviving IDs stable. Retired IDs 8, 13 and 17 must not be reused.
        internal static readonly int[] Values = { 1, 2, 3, 4, 5, 6, 7, 9, 10, 11, 12, 14, 15, 16, 18 };
        internal static readonly string[] Names = { "Gyroid", "Schwarz D", "Schwarz G", "Schwarz P", "Neovius", "Diamond", "P W Hybrid", "IWP", "Fischer-Koch S", "Lidinoid", "Twisted Sheets", "Interference Field", "Tanglecube", "Trefoil Knot", "Warped Caves" };
        internal static readonly string[] Formulas = {
            "cos(x)*sin(y)+cos(y)*sin(z)+cos(z)*sin(x)",
            "cos(x)*cos(y)*cos(z)-sin(x)*sin(y)*sin(z)",
            "sin(x)*cos(y)+sin(z)*cos(x)+sin(y)*cos(z)",
            "-cos(x)+cos(y)+cos(z)",
            "3*(cos(x)+cos(y)+cos(z))+4*cos(x)*cos(y)*cos(z)",
            "sin(x)*sin(y)*sin(z)+sin(x)*cos(y)*cos(z)+cos(x)*sin(y)*cos(z)+cos(x)*cos(y)*sin(z)",
            "cos(x)+cos(y)+cos(z)+4*cos(x)*cos(y)*cos(z)",
            "2*(cos(x)*cos(y)+cos(y)*cos(z)+cos(z)*cos(x))-cos(2*x)-cos(2*y)-cos(2*z)",
            "cos(2*x)*sin(y)*cos(z)+cos(2*y)*sin(z)*cos(x)+cos(2*z)*sin(x)*cos(y)",
            "sin(2*x)*cos(y)*sin(z)+sin(2*y)*cos(z)*sin(x)+sin(2*z)*cos(x)*sin(y)-cos(2*x)*cos(2*y)-cos(2*y)*cos(2*z)-cos(2*z)*cos(2*x)+0.3",
            "y*cos(z+0.12*z*z*z)-x*sin(z+0.12*z*z*z)",
            "sin(x*y)+sin(y*z)+sin(z*x)",
            "x*x*x*x-5*x*x+y*y*y*y-5*y*y+z*z*z*z-5*z*z+11.8",
            TrefoilFormula(),
            "sin(x+0.8*sin(1.7*y+0.6*sin(z)))+sin(y+0.8*sin(1.3*z+0.6*sin(x)))+sin(z+0.8*sin(1.5*x+0.6*sin(y)))" };
        internal static bool Centered(int index) => index >= 10;
        const string SurfaceDescription = "Predefined function from the value list. A connected Custom formula overrides this selection. Disconnect Custom to use the preset again.";
        const string CustomDescription = "f(x,y,z) -> here I input the formula. Connect a panel to override Surface automatically, for example: Math.Cos(x) * Math.Sin(y) + Math.Cos(y) * Math.Sin(z) + Math.Cos(z) * Math.Sin(x). Supports arithmetic and Math.Sin/Cos/Tan, inverse and hyperbolic trig, Abs, Sqrt, Pow, Exp, Log, Log10, Floor, Ceiling, Min, Max, PI and E. No assignment or semicolon.";
        const string ScaleDescription = "Coordinate span across each grid axis. Original periodic presets, IWP, Fischer-Koch S, Lidinoid and Custom: x = scale * indexX / resX (similarly y, z); 2*pi gives one period. Twisted Sheets through Warped Caves are centered: x = scale * (indexX - (resX-1)/2) / resX.";

        static string TrefoilFormula()
        {
            // Stereographic S3 coordinates u=2(x+iy)/d, v=(2z+i(r2-1))/d.
            // |u^3-v^2|^2 = 0 is a trefoil; a small positive level forms its tube.
            const string r2 = "(x*x+y*y+z*z)", d = "(1+x*x+y*y+z*z)";
            string real = "(8*x*(x*x-3*y*y)-" + d + "*(4*z*z-(" + r2 + "-1)*(" + r2 + "-1)))";
            string imag = "(8*y*(3*x*x-y*y)-4*z*(" + r2 + "-1)*" + d + ")";
            return "(" + real + "*" + real + "+" + imag + "*" + imag + ")/pow(" + d + ",6)-0.02";
        }
        GpuPeriodicSurface gpu;
        VoxelOutputDemand outputDemand;
        public Voxel_PeriodicSurface() : base("Function Attractor", "Function Attractor", "Select voxels by approximate physical distance to f(x,y,z) = isoValue on the GPU.", "Nuclei4", " Environment") { }
        protected override void RegisterInputParams(GH_InputParamManager p)
        {
            p.AddGenericParameter("Voxels", "voxels", "Voxels from Construct Voxels or a voxel selection; preserves voxel settings", GH_ParamAccess.item);
            p.AddIntegerParameter("Surface", "surface", SurfaceDescription, GH_ParamAccess.item, 1);
            p[1].Optional = true;
            p.AddTextParameter("Custom", "custom", CustomDescription, GH_ParamAccess.item, "");
            p[2].Optional = true;
            p.AddNumberParameter("Scale", "scale", ScaleDescription, GH_ParamAccess.item, 2 * Math.PI);
            p.AddNumberParameter("Iso Value", "isoValue", "Function level defining the surface: f(x,y,z) = isoValue", GH_ParamAccess.item, 0);
            p.AddNumberParameter("Minimum Range", "minRange", VoxelAttractorRange.MinimumDescription + " Function distances are approximate, on both sides of the surface.", GH_ParamAccess.item, 0);
            p.AddNumberParameter("Maximum Range", "maxRange", VoxelAttractorRange.MaximumDescription + " Function distances are approximate. Limited to available input voxels and grid-resolved features.", GH_ParamAccess.item, 2);
        }
        protected override void RegisterOutputParams(GH_OutputParamManager p)
        {
            p.AddGenericParameter("Output Voxels", "voxels", "Selected voxels", GH_ParamAccess.item);
            p.AddPointParameter("Output Voxel Positions", "voxelPosition", "Selected centers; hidden and computed only when connected", GH_ParamAccess.list);
            p.HideParameter(1);
            p.AddIntegerParameter("Output Voxel Indices", "voxelIndex", "Zero-based ordinals in the output selection, matching Curve Attractor; computed only when connected", GH_ParamAccess.list);
        }
        protected override void SolveInstance(IGH_DataAccess da)
        {
            VoxelField field;
            if (!VoxelFieldAccess.TryGet(da, "Voxels", Globals.voxelSize, out field)) return;
            int type = 1; double scale = 2 * Math.PI, iso = 0, minRange = 0, maxRange = 2; string custom = "";
            bool useCustom = Params.Input[2].SourceCount > 0;
            if (useCustom && (!da.GetData(2, ref custom) || string.IsNullOrWhiteSpace(custom)))
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Custom is connected but contains no formula. Supply f(x,y,z), or disconnect Custom to use Surface."); return; }
            int preset = 0;
            if (!useCustom)
            {
                da.GetData(1, ref type);
                preset = type == 8 ? 0 : Array.IndexOf(Values, type);
                if (preset < 0) { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Choose a valid surface from the value list."); return; }
            }
            if (!da.GetData(3, ref scale) || !da.GetData(4, ref iso)) return;
            if (!da.GetData(5, ref minRange) || !da.GetData(6, ref maxRange)) return;
            if (double.IsNaN(scale) || double.IsInfinity(scale) || Math.Abs(scale) > float.MaxValue || double.IsNaN(iso) || double.IsInfinity(iso) || Math.Abs(iso) > float.MaxValue)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Use finite scale/isoValue within float range."); return; }
            if (!VoxelAttractorRange.TryNormalize(this, 6, 2, field.VoxelSize, ref minRange, ref maxRange)) return;
            if (maxRange > float.MaxValue)
            { AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "The effective range exceeds GPU float precision."); return; }
            try
            {
                string formula = GpuPeriodicSurface.Translate(useCustom ? custom : Formulas[preset]);
                var data = field.Data;
                var output = data;
                if (data.Count > 0 && data.ActiveCount > 0)
                {
                    if (gpu == null) gpu = new GpuPeriodicSurface();
                    output = data.WithActiveWords(!useCustom && Centered(preset)
                        ? gpu.SelectCentered(data, formula, (float)scale, (float)iso, (float)minRange, (float)maxRange)
                        : gpu.Select(data, formula, (float)scale, (float)iso, (float)minRange, (float)maxRange));
                }
                da.SetData(0, field.WithData(output));
                if (Params.Output[1].Recipients.Count > 0)
                    da.SetDataList(1, Enumerable.Range(0, output.ActiveCount).Select(n => output.CenterPoint(output.ActiveFlatIndexAt(n))));
                if (Params.Output[2].Recipients.Count > 0)
                    da.SetDataList(2, Enumerable.Range(0, output.ActiveCount));
                VoxelAttractorOutputs.WriteCount(this, ref selectedVoxelCount, output.ActiveCount);
            }
            catch (Exception ex)
            {
                gpu?.Dispose(); gpu = null;
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "GPU Function Attractor: " + ex.Message);
            }
        }
        void EnsureValueList()
        {
            if (Params.Input[1].SourceCount != 0 || OnPingDocument() == null || Attributes == null) return;
            var list = new GH_ValueList { ListMode = GH_ValueListMode.DropDown };
            list.CreateAttributes();
            list.Attributes.Pivot = new PointF(Attributes.Pivot.X - 230, Attributes.Pivot.Y - 30);
            PopulateValueList(list, 0);
            VoxelTypeChoices.SelectStoredValue(this, 1, list);
            OnPingDocument().AddObject(list, false);
            Params.Input[1].AddSource(list);
        }
        static void PopulateValueList(GH_ValueList list, int selected)
        {
            list.ListItems.Clear();
            for (int i = 0; i < Names.Length; i++)
                list.ListItems.Add(new GH_ValueListItem(Names[i], Values[i].ToString(System.Globalization.CultureInfo.InvariantCulture)));
            list.SelectItem(selected);
        }
        void UpgradeValueLists(object sender, GH_SolutionEventArgs args)
        {
            EnsureValueList();
            foreach (var list in Params.Input[1].Sources.OfType<GH_ValueList>())
            {
                // Recognize both older generated catalogs; leave authored lists alone.
                bool original = list.ListItems.Count == 8 && list.ListItems[7].Name == "Custom" && list.ListItems[7].Expression == "8"
                    && Enumerable.Range(0, 7).All(i => list.ListItems[i].Name == Names[i] && list.ListItems[i].Expression == (i + 1).ToString());
                bool previous = list.ListItems.Count == 17 && Enumerable.Range(0, 17).All(i =>
                {
                    int value = i < 7 ? i + 1 : i + 2;
                    string name = value == 13 ? "Chirped Labyrinth" : value == 17 ? "Blended Field" : Names[Array.IndexOf(Values, value)];
                    return list.ListItems[i].Name == name && list.ListItems[i].Expression == value.ToString();
                });
                if (!original && !previous) continue;
                string selectedValue = list.ListItems.FirstOrDefault(item => item.Selected)?.Expression;
                int selected = Array.FindIndex(Values, value => value.ToString() == selectedValue);
                PopulateValueList(list, selected >= 0 ? selected : 0);
                list.ExpireSolution(false);
            }
        }
        public override void AddedToDocument(GH_Document document)
        {
            base.AddedToDocument(document);
            if (outputDemand == null) outputDemand = new VoxelOutputDemand(this);
            outputDemand.Attach(document);
            document.SolutionStart -= UpgradeValueLists;
            document.SolutionStart += UpgradeValueLists;
        }
        public override void RemovedFromDocument(GH_Document document) { document.SolutionStart -= UpgradeValueLists; outputDemand?.Detach(); gpu?.Dispose(); gpu = null; base.RemovedFromDocument(document); }
        public override bool Write(GH_IO.Serialization.GH_IWriter writer)
        {
            writer.SetInt32("FunctionAttractorInputOrder", 1);
            return base.Write(writer);
        }
        public override bool Read(GH_IO.Serialization.GH_IReader reader)
        {
            int inputOrder = 0;
            reader.TryGetInt32("FunctionAttractorInputOrder", ref inputOrder);
            // Read old five/seven-input archives against their original typed
            // parameter order. Move the actual parameter afterward so IDs, wires
            // and persistent values travel together, including renamed parameters.
            if (inputOrder == 0) MoveCustomInput(4);
            bool result;
            try { result = base.Read(reader); }
            finally { MoveCustomInput(2); }
            Name = "Function Attractor";
            if (NickName == "Periodic Surface" || NickName == "Periodic Surface for Voxels") NickName = Name;
            Description = "Select voxels by approximate physical distance to f(x,y,z) = isoValue on the GPU.";
            Params.Input[1].Description = SurfaceDescription;
            Params.Input[1].Optional = true;
            Params.Input[2].Description = CustomDescription;
            Params.Input[3].Description = ScaleDescription;
            Params.Input[4].Description = "Function level defining the surface: f(x,y,z) = isoValue";
            Params.Input[5].Description = VoxelAttractorRange.MinimumDescription + " Function distances are approximate, on both sides of the surface.";
            Params.Input[6].Description = VoxelAttractorRange.MaximumDescription + " Function distances are approximate. Limited to available input voxels and grid-resolved features.";
            return result;
        }
        void MoveCustomInput(int targetIndex)
        {
            int index = Params.Input.FindIndex(p => p is Grasshopper.Kernel.Parameters.Param_String);
            if (index < 0 || index == targetIndex) return;
            var custom = Params.Input[index];
            Params.Input.RemoveAt(index);
            Params.Input.Insert(targetIndex, custom);
            Params.OnParametersChanged();
        }
        public override GH_Exposure Exposure => GH_Exposure.quinary;
        protected override Bitmap Icon => Properties.Resources.VoxelFunctionAttractor;
        public override Guid ComponentGuid => new Guid("99408ead-53f2-4ecb-8d3d-afd2f4dca58c");
    }
}
