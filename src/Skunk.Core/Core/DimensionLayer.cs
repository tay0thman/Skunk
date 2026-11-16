using System;
using System.Collections.Generic;
using System.Drawing;
using Rhino;
using Rhino.DocObjects;
using Rhino.Geometry;
using Skunk.Elements;

namespace Skunk.Core
{
    /// <summary>
    /// The "Rhino dimensions on a Skunk sublayer" option (SkSettings): Rhino's own dimensions and leaders
    /// go onto Skunk::&lt;name&gt; as they are added. Skunk's annotations keep their own layers, and text
    /// objects stay where they are.
    /// </summary>
    public static class DimensionLayer
    {
        /// <summary>A dimension (linear, aligned, angular, radial, ordinate, centermark) or leader drawn with Rhino.</summary>
        public static bool IsRhinoDimension(RhinoObject o) =>
            o != null && !o.IsDeleted && (o.Geometry is Dimension || o.Geometry is Leader) && ElementIO.KindOf(o) == ElementKind.None;

        /// <summary>The sublayer name to use: the given one, or "Dimensions" when it is empty or a path.</summary>
        public static string LayerName(string name)
        {
            var n = (name ?? "").Trim();
            return n.Length == 0 || n.Contains("::") ? Keys.DimensionsLayer : n;
        }

        /// <summary>Move the Rhino dimensions among <paramref name="objects"/> onto Skunk::name. Returns how many moved.</summary>
        public static int Move(RhinoDoc doc, IEnumerable<RhinoObject> objects, string name)
        {
            int layer = -1, moved = 0;
            foreach (var o in objects)
            {
                if (!IsRhinoDimension(o))
                    continue;
                if (layer < 0)
                    layer = Layers.Ensure(doc, LayerName(name), Color.FromArgb(40, 40, 40), 0.18);
                if (layer < 0 || o.Attributes.LayerIndex == layer)
                    continue;
                var a = o.Attributes.Duplicate();
                a.LayerIndex = layer;
                if (doc.Objects.ModifyAttributes(o, a, true))
                    moved++;
            }
            return moved;
        }

        /// <summary>Objects added in one operation: their Rhino dimensions move when the option is on.</summary>
        public static int Apply(RhinoDoc doc, IEnumerable<Guid> added)
        {
            var settings = DocStore.Settings(doc);
            if (!settings.DimensionsOnSkunkLayer)
                return 0;
            var objects = new List<RhinoObject>();
            foreach (var id in added)
            {
                var o = doc.Objects.FindId(id);
                if (o != null)
                    objects.Add(o);
            }
            return Move(doc, objects, settings.DimensionsLayer);
        }

        /// <summary>Every Rhino dimension already in the model (normal, locked and hidden ones).</summary>
        public static int MoveAll(RhinoDoc doc, string name)
        {
            var filter = new ObjectEnumeratorSettings
            {
                ObjectTypeFilter = ObjectType.Annotation,
                NormalObjects = true,
                LockedObjects = true,
                HiddenObjects = true,
                IncludeLights = false,
                IncludeGrips = false,
            };
            return Move(doc, doc.Objects.GetObjectList(filter), name);
        }
    }
}
