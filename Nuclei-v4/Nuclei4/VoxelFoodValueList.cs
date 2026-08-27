using Grasshopper.Kernel;
using Grasshopper.Kernel.Special;
using System.Linq;

namespace Nuclei4
{
    /// <summary>
    /// Retrofits already-saved definitions whose value list still offers a single
    /// "Food" entry. Mirrors the V3 helper so both toolsets migrate identically.
    /// </summary>
    internal static class VoxelFoodValueList
    {
        public static void EnsureSeparateFoodChoices(GH_Component component, int inputIndex)
        {
            if (component == null || component.Params == null || component.Params.Input == null
                || inputIndex < 0 || inputIndex >= component.Params.Input.Count
                || component.Params.Input[inputIndex].SourceCount != 1)
            {
                return;
            }

            GH_ValueList valueList = component.Params.Input[inputIndex].Sources[0] as GH_ValueList;
            if (valueList == null) return;

            GH_ValueListItem slimeFood = valueList.ListItems.FirstOrDefault(item => item.Expression == "6");
            if (slimeFood != null)
            {
                slimeFood.Name = "Slime Food";
            }

            if (!valueList.ListItems.Any(item => item.Expression == "13"))
            {
                valueList.ListItems.Add(new GH_ValueListItem("Ant Food", "13"));
            }
        }
    }
}
