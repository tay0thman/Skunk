using System;
using System.Collections.Generic;
using Rhino;
using Rhino.DocObjects;
using Skunk.Core;
using Skunk.Elements;

namespace Skunk.Service
{
    /// <summary>Who depends on whom: host -> spots, level -> relative spots / plan cuts; and every level, datum box, arrow, cut.</summary>
    public sealed class DependencyIndex
    {
        public readonly Dictionary<Guid, HashSet<Guid>> HostToSpots = new Dictionary<Guid, HashSet<Guid>>();
        public readonly Dictionary<Guid, HashSet<Guid>> LevelToRelative = new Dictionary<Guid, HashSet<Guid>>();
        readonly Dictionary<Guid, (Guid Host, Guid Level)> _spot = new Dictionary<Guid, (Guid, Guid)>();
        public readonly HashSet<Guid> Spots = new HashSet<Guid>();
        public readonly HashSet<Guid> Levels = new HashSet<Guid>();
        public readonly HashSet<Guid> Arrows = new HashSet<Guid>();
        public readonly HashSet<Guid> Cuts = new HashSet<Guid>();
        public readonly HashSet<Guid> Boxes = new HashSet<Guid>();
        public readonly HashSet<Guid> Grids = new HashSet<Guid>();

        public void Clear()
        {
            HostToSpots.Clear();
            LevelToRelative.Clear();
            _spot.Clear();
            Spots.Clear();
            Levels.Clear();
            Arrows.Clear();
            Cuts.Clear();
            Boxes.Clear();
            Grids.Clear();
        }

        public void Rebuild(RhinoDoc doc)
        {
            Clear();
            foreach (var o in ElementIO.All(doc))
                Add(o);
        }

        public void Refresh(RhinoDoc doc, Guid id)
        {
            Remove(id);
            var o = ElementIO.Find(doc, id);
            if (o != null)
                Add(o);
        }

        void Add(RhinoObject o)
        {
            var k = ElementIO.KindOf(o);
            switch (k)
            {
                case ElementKind.SpotElevation:
                case ElementKind.SpotCoordinate:
                case ElementKind.SpotSlope:
                    {
                        var d = ElementIO.Read<SpotData>(o);
                        if (d == null)
                            return;
                        Spots.Add(o.Id);
                        var host = d.Anchor?.Host ?? Guid.Empty;
                        _spot[o.Id] = (host, d.RelativeLevel);
                        if (host != Guid.Empty)
                            Get(HostToSpots, host).Add(o.Id);
                        if (d.RelativeLevel != Guid.Empty)
                            Get(LevelToRelative, d.RelativeLevel).Add(o.Id);
                        break;
                    }
                case ElementKind.Level:
                    Levels.Add(o.Id);
                    break;
                case ElementKind.NorthArrow:
                    Arrows.Add(o.Id);
                    break;
                case ElementKind.PlanCut:
                    Cuts.Add(o.Id);
                    break;
                case ElementKind.DatumBox:
                    Boxes.Add(o.Id);
                    break;
                case ElementKind.Grid:
                    Grids.Add(o.Id);
                    break;
            }
        }

        public void Remove(Guid id)
        {
            if (_spot.TryGetValue(id, out var links))
            {
                if (links.Host != Guid.Empty && HostToSpots.TryGetValue(links.Host, out var set))
                    set.Remove(id);
                if (links.Level != Guid.Empty && LevelToRelative.TryGetValue(links.Level, out var rel))
                    rel.Remove(id);
                _spot.Remove(id);
            }
            Spots.Remove(id);
            Levels.Remove(id);
            Arrows.Remove(id);
            Cuts.Remove(id);
            Boxes.Remove(id);
            Grids.Remove(id);
        }

        public IEnumerable<Guid> SpotsOn(Guid host) =>
            HostToSpots.TryGetValue(host, out var set) ? (IEnumerable<Guid>)set : Array.Empty<Guid>();

        public IEnumerable<Guid> RelativeTo(Guid level) =>
            LevelToRelative.TryGetValue(level, out var set) ? (IEnumerable<Guid>)set : Array.Empty<Guid>();

        static HashSet<Guid> Get(Dictionary<Guid, HashSet<Guid>> map, Guid key)
        {
            if (!map.TryGetValue(key, out var set))
            {
                set = new HashSet<Guid>();
                map[key] = set;
            }
            return set;
        }
    }
}
