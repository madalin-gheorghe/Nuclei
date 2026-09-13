using System.Collections.Generic;
using Grasshopper.Kernel.Special;

namespace Nuclei4
{
    internal static class VoxelTypeChoices
    {
        // Called at SolutionStart, after restored wires exist but before input validation.
        internal static void Ensure(Grasshopper.Kernel.GH_Component component, int maximumValue, float offsetX = 250)
        {
            var document = component.OnPingDocument();
            if (component.Params.Input[1].SourceCount != 0 || document == null || component.Attributes == null) return;
            var list = new GH_ValueList { ListMode = GH_ValueListMode.DropDown };
            list.CreateAttributes();
            list.Attributes.Pivot = new System.Drawing.PointF(component.Attributes.Pivot.X - offsetX, component.Attributes.Pivot.Y - 31);
            list.ListItems.Clear();
            var items = Create();
            items.RemoveAll(item => int.Parse(item.Expression) > maximumValue && item.Expression != "13");
            list.ListItems.AddRange(items);
            SelectStoredValue(component, 1, list);
            document.AddObject(list, false);
            component.Params.Input[1].AddSource(list);
        }

        internal static void SelectStoredValue(Grasshopper.Kernel.GH_Component component, int inputIndex, GH_ValueList list)
        {
            var input = component.Params.Input[inputIndex] as Grasshopper.Kernel.Parameters.Param_Integer;
            int selected = 0;
            if (input != null)
            {
                foreach (var value in input.PersistentData.AllData(true))
                {
                    var integer = value as Grasshopper.Kernel.Types.GH_Integer;
                    if (integer == null) continue;
                    string expression = integer.Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    int index = list.ListItems.FindIndex(item => item.Expression == expression);
                    if (index >= 0) selected = index;
                    break;
                }
            }
            list.SelectItem(selected);
        }

        internal static List<GH_ValueListItem> Create()
        {
            var items = new List<GH_ValueListItem>();
                items.Add(new GH_ValueListItem("Minimum Density", "0"));
                items.Add(new GH_ValueListItem("Maximum Density", "1"));
                items.Add(new GH_ValueListItem("Speed", "2"));
                items.Add(new GH_ValueListItem("Sensor Distance", "3"));
                items.Add(new GH_ValueListItem("Sensor Angle", "4"));
                items.Add(new GH_ValueListItem("Rotation Angle", "5"));
                items.Add(new GH_ValueListItem("Slime Food", "6"));
                items.Add(new GH_ValueListItem("Ant Food", "13"));
                items.Add(new GH_ValueListItem("Slime Chemoattractants", "7"));
                items.Add(new GH_ValueListItem("Ant Food Pheromones", "8"));
                items.Add(new GH_ValueListItem("Ant Base Pheromones", "9"));
                items.Add(new GH_ValueListItem("Ant Pheromones", "10"));
                items.Add(new GH_ValueListItem("Ants and Slime", "11"));
            return items;
        }
    }
}
