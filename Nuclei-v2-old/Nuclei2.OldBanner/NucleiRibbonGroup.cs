using System;
using System.Collections.Generic;
using System.Windows.Forms;
using Grasshopper;
using Grasshopper.GUI.Ribbon;

namespace Nuclei2.OldBanner
{
    internal static class NucleiRibbonGroup
    {
        private static bool registered;
        private static Control editor;
        private static GH_Ribbon ribbon;

        internal static void Register()
        {
            if (registered)
                return;

            registered = true;
            // Ribbon construction and later plug-in loads can occur after PriorityLoad.
            // Idle runs on the UI thread, outside ribbon layout/paint enumeration.
            Rhino.RhinoApp.Idle += OnIdle;
        }

        private static void OnIdle(object sender, EventArgs e)
        {
            Control currentEditor = Instances.DocumentEditor;
            if (currentEditor == null || currentEditor.IsDisposed || currentEditor.Disposing)
                return;
            if (editor != currentEditor || ribbon == null || ribbon.IsDisposed)
            {
                editor = currentEditor;
                ribbon = FindRibbon(currentEditor);
            }
            if (ribbon != null && !ribbon.IsDisposed && !ribbon.Disposing)
                GroupTabs(ribbon);
        }

        private static GH_Ribbon FindRibbon(Control parent)
        {
            if (parent is GH_Ribbon found)
                return found;
            foreach (Control child in parent.Controls)
            {
                GH_Ribbon ribbon = FindRibbon(child);
                if (ribbon != null)
                    return ribbon;
            }
            return null;
        }

        internal static void GroupTabs(GH_Ribbon ribbon)
        {
            List<GH_RibbonTab> tabs = ribbon.Tabs;
            int n2 = -1, n3 = -1, n4 = -1;
            for (int i = 0; i < tabs.Count; i++)
            {
                switch (tabs[i].NameFull)
                {
                    case "Nuclei2": n2 = i; break;
                    case "Nuclei3": n3 = i; break;
                    case "Nuclei4": n4 = i; break;
                }
            }

            if (n2 < 0 || (n3 < 0 && n4 < 0))
                return;
            if ((n3 < 0 || n3 == n2 + 1) &&
                (n4 < 0 || n4 == (n3 < 0 ? n2 + 1 : n3 + 1)))
                return;

            int anchor = Math.Min(n2, Math.Min(n3 < 0 ? n2 : n3, n4 < 0 ? n2 : n4));
            var family = new List<GH_RibbonTab> { tabs[n2] };
            if (n3 >= 0) family.Add(tabs[n3]);
            if (n4 >= 0) family.Add(tabs[n4]);
            string activeName = ribbon.ActiveTabName;

            // GH1 exposes no reorder operation. Reuse the existing tab objects so
            // panels, proxies and selection survive; never rebuild or replace tabs.
            foreach (GH_RibbonTab tab in family)
                tabs.Remove(tab);
            tabs.InsertRange(anchor, family);
            ribbon.ActiveTabName = activeName;
            ribbon.LayoutRibbon();
            ribbon.Invalidate();
        }
    }
}
