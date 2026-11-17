using System;
using Rhino;

namespace Skunk.Core
{
    /// <summary>
    /// Rhino keeps linetype patterns in paper millimetres: prints and layout details show them at that size,
    /// and model views multiply them by one document-wide display scale (Document Properties > Linetypes >
    /// model space scale, or SetLinetypeScale). Skunk keeps that display scale at the annotation scale, so a
    /// level line looks in model views the way it prints - unless the model has a value of its own: one it
    /// had before Skunk (anything but Rhino's default 1) or one the user set after Skunk did. Then Skunk
    /// leaves it alone until the Settings dialog hands it back.
    /// </summary>
    public static class LinetypeDisplay
    {
        /// <summary>Bring the display scale to the annotation scale if Skunk still looks after it. True when it changed.</summary>
        public static bool Follow(RhinoDoc doc)
        {
            if (doc == null)
                return false;
            var s = DocStore.Settings(doc);
            if (!s.LinetypeScaleFollows)
                return false;
            double current = doc.Linetypes.LinetypeScale;
            double wanted = DocUnits.AnnotationScale(doc);
            // first look: Rhino's default (1) or a value that already is the annotation scale counts as Skunk's
            bool ours = s.LinetypeScaleSet > 0 ? Same(current, s.LinetypeScaleSet) : Same(current, 1) || Same(current, wanted);
            if (!ours)
            {
                s.LinetypeScaleFollows = false;
                DocStore.SaveSettings(doc, s);
                return false;
            }
            bool changed = false;
            if (!Same(current, wanted) && wanted > 0)
            {
                doc.Linetypes.LinetypeScale = wanted;
                changed = true;
            }
            if (!Same(s.LinetypeScaleSet, wanted))
            {
                s.LinetypeScaleSet = wanted;
                DocStore.SaveSettings(doc, s);
            }
            return changed;
        }

        /// <summary>Settings dialog: let Skunk look after the display scale again (it applies at the next update).</summary>
        public static void TakeOver(RhinoDoc doc, SkunkSettings s)
        {
            s.LinetypeScaleFollows = true;
            s.LinetypeScaleSet = doc.Linetypes.LinetypeScale;
        }

        static bool Same(double a, double b) => Math.Abs(a - b) <= 1e-9 * Math.Max(1, Math.Max(Math.Abs(a), Math.Abs(b)));
    }
}
