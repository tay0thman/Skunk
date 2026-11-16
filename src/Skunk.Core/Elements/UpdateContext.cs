using System;
using System.Collections.Generic;
using Rhino;
using Rhino.DocObjects;
using Skunk.Core;
using Skunk.Site;
using Skunk.Types;

namespace Skunk.Elements
{
    /// <summary>Why an element is being updated.</summary>
    [Flags]
    public enum UpdateReason
    {
        None = 0,
        /// <summary>The element object itself was edited (moved, grip-edited, copied, attributes changed).</summary>
        Self = 1,
        /// <summary>Its host changed.</summary>
        Host = 2,
        /// <summary>A coordinate system, type or setting changed.</summary>
        Context = 4,
        /// <summary>Created by a Skunk command (no user edit to interpret).</summary>
        Created = 8,
    }

    /// <summary>Per-batch caches shared by element updates.</summary>
    public sealed class UpdateContext
    {
        public readonly RhinoDoc Doc;
        public readonly SiteModel Site;
        public readonly TypeLibrary Types;
        public readonly SkunkSettings Settings;
        public readonly double Tol;
        readonly Dictionary<string, DimensionStyle> _styles = new Dictionary<string, DimensionStyle>();
        readonly Dictionary<Guid, double> _levelZ = new Dictionary<Guid, double>();

        /// <summary>Objects Skunk moved in this batch (their dependents must update too).</summary>
        public readonly HashSet<Guid> Moved = new HashSet<Guid>();
        /// <summary>Objects added in this batch (copies / pastes): a copied spot re-hosts on a copied host.</summary>
        public readonly HashSet<Guid> Added = new HashSet<Guid>();
        /// <summary>Objects Skunk modified in this batch.</summary>
        public int Modified;

        public UpdateContext(RhinoDoc doc)
        {
            Doc = doc;
            Site = SiteModel.Get(doc);
            Types = TypeTable.Get(doc);
            Settings = DocStore.Settings(doc);
            Tol = Math.Max(doc.ModelAbsoluteTolerance, 1e-9);
        }

        public DimensionStyle Style(SkunkType t)
        {
            string key = t.Kind + "|" + t.Name;
            if (_styles.TryGetValue(key, out var ds))
                return ds;
            ds = Styles.DimStyle(Doc, t);
            _styles[key] = ds;
            return ds;
        }

        /// <summary>World Z of a level (NaN when it does not exist).</summary>
        public double LevelZ(Guid id)
        {
            if (id == Guid.Empty)
                return double.NaN;
            if (_levelZ.TryGetValue(id, out double z))
                return z;
            z = LevelElement.TryGetZ(Doc, id, out double zz) ? zz : double.NaN;
            _levelZ[id] = z;
            return z;
        }

        public void ForgetLevel(Guid id) => _levelZ.Remove(id);

        public Frame Frame(CoordinateBase b, string cplane, Guid relativeLevel) =>
            Frames.Get(Doc, Site, b, cplane, b == CoordinateBase.Relative ? LevelZ(relativeLevel) : double.NaN);
    }
}
