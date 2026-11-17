using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Rhino;
using Rhino.Commands;
using Rhino.DocObjects;
using Skunk.Core;
using Skunk.Elements;
using Skunk.Site;

namespace Skunk.Service
{
    /// <summary>
    /// The event service that keeps Skunk elements parametric.
    ///
    /// Rhino document events (add / delete / undelete / replace / modify attributes) only record
    /// which objects changed. The work happens later, at one of two safe points:
    ///   1. Command.UndoRedo "before end recording": the command that moved a host is closing its
    ///      undo record, so the dependent updates go into the SAME record - one Undo reverts the
    ///      host and its annotations together.
    ///   2. RhinoApp.Idle, for changes made outside commands (panels, scripts): processed in a
    ///      "Skunk Update" undo record of their own.
    /// Changes replayed by Undo / Redo are ignored (the undo record already holds a consistent state),
    /// and so are the changes Skunk makes itself.
    /// </summary>
    public sealed class SkunkService
    {
        const string AppDomainKey = "Skunk.Service.Instance";

        sealed class DeletedInfo
        {
            public ElementKind Kind;
            public string Data;
        }

        sealed class DocState
        {
            public readonly HashSet<Guid> Changed = new HashSet<Guid>();
            public readonly HashSet<Guid> Added = new HashSet<Guid>();
            public readonly Dictionary<Guid, DeletedInfo> Deleted = new Dictionary<Guid, DeletedInfo>();
            public readonly DependencyIndex Index = new DependencyIndex();
            public bool IndexBuilt;
            public bool Full;
            public bool Context;
            public bool PostUndo;
            public string SiteSignature;
            /// <summary>Rhino changed the model units and scaled everything by this (applied at the next processing).</summary>
            public double UnitScale = 1;
            /// <summary>The annotation scale and Rhino's scaling switch as last seen (see CheckAnnotationScale).</summary>
            public string ScaleKey;
            /// <summary>The layout details as last seen (see CheckGridViews); Views asks for their grid overrides again.</summary>
            public string ViewsKey;
            public bool Views;
            /// <summary>True north after the last processing: an undo that turns it back takes Rhino's north along (PostUndoSync).</summary>
            public Rhino.Geometry.Vector3d? TrueNorth;
            /// <summary>The model's geographic location as last seen (see CheckGeoLocation).</summary>
            public string GeoKey;

            public bool Pending => Changed.Count > 0 || Deleted.Count > 0 || Full || Context || UnitScale != 1 || Views;
        }

        public static SkunkService Instance { get; private set; }

        /// <summary>Raised on idle after anything that may change what the UI shows.</summary>
        public static event Action<RhinoDoc> UiRefresh;

        readonly Dictionary<uint, DocState> _docs = new Dictionary<uint, DocState>();
        readonly Stopwatch _clock = Stopwatch.StartNew();
        long _lastSiteCheck;
        int _suppress;
        int _undoDepth;
        bool _processing;
        bool _subscribed;
        bool _uiDirty;
        MarkerConduit _conduit;
        DatumBoxConduit _boxConduit;

        public bool Running => _subscribed;

        // ---- lifetime ---------------------------------------------------------------------------

        /// <summary>Start the service (idempotent). Called by every Skunk command.</summary>
        public static SkunkService Ensure()
        {
            if (Instance != null && Instance._subscribed)
                return Instance;
            // another copy of Skunk.Core (e.g. loaded from the Script Editor and from the package)
            // may be running: hand over so only one service reacts to document events
            try
            {
                var other = AppDomain.CurrentDomain.GetData(AppDomainKey);
                if (other != null && !ReferenceEquals(other, Instance))
                    other.GetType().GetMethod("Shutdown")?.Invoke(other, null);
            }
            catch (Exception ex)
            {
                Log.Debug("hand-over: " + ex.Message);
            }
            Instance = Instance ?? new SkunkService();
            Instance.Subscribe();
            AppDomain.CurrentDomain.SetData(AppDomainKey, Instance);
            foreach (var doc in RhinoDoc.OpenDocuments())
                Instance.State(doc).Full = true;
            return Instance;
        }

        /// <summary>Stop listening (another copy took over, or the user turned Skunk off).</summary>
        public void Shutdown()
        {
            Unsubscribe();
            if (_conduit != null)
                _conduit.Enabled = false;
            if (_boxConduit != null)
                _boxConduit.Enabled = false;
        }

        void Subscribe()
        {
            if (_subscribed)
                return;
            RhinoDoc.AddRhinoObject += OnAdd;
            RhinoDoc.DeleteRhinoObject += OnDelete;
            RhinoDoc.UndeleteRhinoObject += OnUndelete;
            RhinoDoc.ReplaceRhinoObject += OnReplace;
            RhinoDoc.ModifyObjectAttributes += OnModifyAttributes;
            RhinoDoc.EndOpenDocument += OnEndOpen;
            RhinoDoc.NewDocument += OnNewDocument;
            RhinoDoc.CloseDocument += OnCloseDocument;
            RhinoDoc.DocumentPropertiesChanged += OnPropertiesChanged;
            RhinoDoc.UnitsChangedWithScaling += OnUnitsScaled;
            RhinoDoc.SelectObjects += OnSelection;
            RhinoDoc.DeselectObjects += OnSelection;
            RhinoDoc.DeselectAllObjects += OnDeselectAll;
            RhinoDoc.ActiveDocumentChanged += OnActiveDocumentChanged;
            Command.UndoRedo += OnUndoRedo;
            RhinoApp.Idle += OnIdle;
            if (!RhinoApp.IsRunningHeadless)
            {
                _conduit = _conduit ?? new MarkerConduit();
                _conduit.Enabled = true;
                _boxConduit = _boxConduit ?? new DatumBoxConduit();
                _boxConduit.Enabled = true;
            }
            _subscribed = true;
        }

        void Unsubscribe()
        {
            if (!_subscribed)
                return;
            RhinoDoc.AddRhinoObject -= OnAdd;
            RhinoDoc.DeleteRhinoObject -= OnDelete;
            RhinoDoc.UndeleteRhinoObject -= OnUndelete;
            RhinoDoc.ReplaceRhinoObject -= OnReplace;
            RhinoDoc.ModifyObjectAttributes -= OnModifyAttributes;
            RhinoDoc.EndOpenDocument -= OnEndOpen;
            RhinoDoc.NewDocument -= OnNewDocument;
            RhinoDoc.CloseDocument -= OnCloseDocument;
            RhinoDoc.DocumentPropertiesChanged -= OnPropertiesChanged;
            RhinoDoc.UnitsChangedWithScaling -= OnUnitsScaled;
            RhinoDoc.SelectObjects -= OnSelection;
            RhinoDoc.DeselectObjects -= OnSelection;
            RhinoDoc.DeselectAllObjects -= OnDeselectAll;
            RhinoDoc.ActiveDocumentChanged -= OnActiveDocumentChanged;
            Command.UndoRedo -= OnUndoRedo;
            RhinoApp.Idle -= OnIdle;
            _subscribed = false;
        }

        // ---- public API for commands / UI -------------------------------------------------------

        /// <summary>Suppress event recording while Skunk itself edits the document.</summary>
        public static IDisposable Suppress()
        {
            var s = Instance;
            if (s == null)
                return new Scope(null);
            s._suppress++;
            return new Scope(s);
        }

        sealed class Scope : IDisposable
        {
            SkunkService _s;
            public Scope(SkunkService s) => _s = s;
            public void Dispose()
            {
                if (_s != null)
                    _s._suppress = Math.Max(0, _s._suppress - 1);
                _s = null;
            }
        }

        /// <summary>Coordinate systems / types / settings changed: update every element.</summary>
        public void ContextChanged(RhinoDoc doc)
        {
            if (doc == null)
                return;
            State(doc).Context = true;
            _uiDirty = true;
        }

        /// <summary>
        /// The annotation scale (Rhino's model space scale, or Skunk's own when Rhino's scaling is off) or
        /// Rhino's annotation scaling switch changed outside Skunk - Document Properties raises no event -
        /// so every element is refreshed (north arrow sizes, styles, the linetype display scale). The idle
        /// loop checks the active model twice a second; tests call this directly. True when it asked for
        /// a refresh.
        /// </summary>
        public bool CheckAnnotationScale(RhinoDoc doc)
        {
            if (doc == null)
                return false;
            var st = State(doc);
            string key = ScaleKey(doc);
            bool changed = st.ScaleKey != null && st.ScaleKey != key;
            st.ScaleKey = key;
            if (changed)
            {
                st.Context = true;
                _uiDirty = true;
            }
            return changed;
        }

        /// <summary>
        /// A layout detail was added, removed or turned (Rhino raises no event for that): grids get their Hide in Detail
        /// overrides again, so each detail shows them the way it reads (GridViews). The idle loop checks the active
        /// model twice a second when it has grids; tests call this directly. True when it asked for an update.
        /// </summary>
        public bool CheckGridViews(RhinoDoc doc)
        {
            if (doc == null || Index(doc).Grids.Count == 0)
                return false;
            var st = State(doc);
            string key = GridViews.Signature(GridViews.Details(doc));
            bool changed = st.ViewsKey != key;
            st.ViewsKey = key;
            if (changed)
                st.Views = true;
            return changed;
        }

        /// <summary>
        /// The model's geographic location or Rhino's north changed (the Sun panel raises no event Skunk sees): the Skunk
        /// Attributes panel shows them, so it refreshes. The idle loop checks the active model twice a second; tests call
        /// this directly. True when it asked for a refresh.
        /// </summary>
        public bool CheckGeoLocation(RhinoDoc doc)
        {
            if (doc == null)
                return false;
            var st = State(doc);
            string key = GeoLocation.Read(doc).Signature();
            bool changed = st.GeoKey != null && st.GeoKey != key;
            st.GeoKey = key;
            if (changed)
                _uiDirty = true;
            return changed;
        }

        static string ScaleKey(RhinoDoc doc) =>
            DocUnits.RhinoScalesAnnotation(doc) + ":" + DocUnits.AnnotationScale(doc).ToString("R", System.Globalization.CultureInfo.InvariantCulture) +
            ":" + DocUnits.StyleModelSpaceScale(doc).ToString("R", System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>Ask for a full rebuild of the dependency index and all elements.</summary>
        public void FullUpdate(RhinoDoc doc)
        {
            if (doc == null)
                return;
            State(doc).Full = true;
        }

        /// <summary>Mark objects as changed (e.g. after an edit made with events suppressed).</summary>
        public void Touch(RhinoDoc doc, IEnumerable<Guid> ids)
        {
            if (doc == null)
                return;
            var st = State(doc);
            foreach (var id in ids)
                st.Changed.Add(id);
        }

        /// <summary>Mark objects as newly added (copies / pastes made with events suppressed, and tests).</summary>
        public void TouchAdded(RhinoDoc doc, IEnumerable<Guid> ids)
        {
            if (doc == null)
                return;
            var st = State(doc);
            foreach (var id in ids)
            {
                st.Added.Add(id);
                st.Changed.Add(id);
            }
        }

        /// <summary>
        /// Rhino is changing the model units and scaling every object by <paramref name="scale"/> (the
        /// UnitsChangedWithScaling event; tests call it directly). Skunk scales the lengths it stores in model
        /// units at the next processing, before it compares anything, then refreshes every element.
        /// </summary>
        public void UnitsScaled(RhinoDoc doc, double scale)
        {
            if (doc == null || !(scale > 0) || Math.Abs(scale - 1) < 1e-12)
                return;
            var st = State(doc);
            st.UnitScale *= scale;
            st.Context = true;
        }

        /// <summary>Process pending work now (inside the caller's undo record when there is one).</summary>
        public int ProcessNow(RhinoDoc doc) => Process(doc, State(doc));

        public DependencyIndex Index(RhinoDoc doc)
        {
            var st = State(doc);
            if (!st.IndexBuilt)
            {
                st.Index.Rebuild(doc);
                st.IndexBuilt = true;
            }
            return st.Index;
        }

        public void RequestUi() => _uiDirty = true;

        /// <summary>Add (or refresh) an element in the dependency index - for elements Skunk creates with events suppressed.</summary>
        public static void Register(RhinoDoc doc, Guid id)
        {
            var s = Instance;
            if (s == null || doc == null || id == Guid.Empty)
                return;
            var st = s.State(doc);
            if (!st.IndexBuilt)
            {
                st.Index.Rebuild(doc);
                st.IndexBuilt = true;
                return;
            }
            st.Index.Refresh(doc, id);
        }

        DocState State(RhinoDoc doc)
        {
            if (!_docs.TryGetValue(doc.RuntimeSerialNumber, out var st))
            {
                st = new DocState();
                _docs[doc.RuntimeSerialNumber] = st;
            }
            return st;
        }

        bool Ignore(RhinoDoc doc) =>
            doc == null || _suppress > 0 || _processing || _undoDepth > 0 || doc.UndoActive || doc.RedoActive;

        // ---- document events ----------------------------------------------------------------------

        void OnAdd(object sender, RhinoObjectEventArgs e)
        {
            var doc = e.TheObject?.Document;
            if (Ignore(doc))
                return;
            var st = State(doc);
            st.Added.Add(e.ObjectId);
            st.Changed.Add(e.ObjectId);
            st.Deleted.Remove(e.ObjectId);
        }

        void OnDelete(object sender, RhinoObjectEventArgs e)
        {
            var doc = e.TheObject?.Document;
            if (Ignore(doc))
                return;
            var st = State(doc);
            var kind = ElementIO.KindOf(e.TheObject);
            st.Deleted[e.ObjectId] = new DeletedInfo
            {
                Kind = kind,
                Data = kind == ElementKind.None ? null : e.TheObject.Attributes.GetUserString(Keys.Data),
            };
        }

        void OnUndelete(object sender, RhinoObjectEventArgs e)
        {
            var doc = e.TheObject?.Document;
            if (Ignore(doc))
                return;
            var st = State(doc);
            st.Deleted.Remove(e.ObjectId);
            st.Changed.Add(e.ObjectId);
        }

        void OnReplace(object sender, RhinoReplaceObjectEventArgs e)
        {
            var doc = e.Document;
            if (Ignore(doc))
                return;
            State(doc).Changed.Add(e.ObjectId);
        }

        void OnModifyAttributes(object sender, RhinoModifyObjectAttributesEventArgs e)
        {
            var doc = e.Document;
            if (Ignore(doc))
                return;
            var oldKind = ElementIO.KindOf(e.OldAttributes);
            var newKind = ElementIO.KindOf(e.NewAttributes);
            bool relevant = oldKind != ElementKind.None || newKind != ElementKind.None ||
                            e.OldAttributes.GetUserString(Keys.Level) != e.NewAttributes.GetUserString(Keys.Level);
            if (relevant)
                State(doc).Changed.Add(e.RhinoObject.Id);
            _uiDirty = true;
        }

        void OnEndOpen(object sender, DocumentOpenEventArgs e)
        {
            if (e.Document == null || e.Merge || e.Reference)
            {
                if (e.Document != null)
                    State(e.Document).Full = true;
                return;
            }
            DocStore.Forget(e.Document);
            _docs.Remove(e.Document.RuntimeSerialNumber);
            State(e.Document).Full = true;
            _uiDirty = true;
        }

        void OnNewDocument(object sender, DocumentEventArgs e)
        {
            if (e.Document == null)
                return;
            _docs.Remove(e.Document.RuntimeSerialNumber);
            _uiDirty = true;
        }

        void OnCloseDocument(object sender, DocumentEventArgs e)
        {
            if (e.Document == null)
                return;
            _docs.Remove(e.Document.RuntimeSerialNumber);
            DocStore.Forget(e.Document);
            _uiDirty = true;
        }

        void OnPropertiesChanged(object sender, DocumentEventArgs e)
        {
            if (e.Document == null || _processing || _suppress > 0)
                return;
            // units or annotation scale may have changed: sizes and texts need refreshing
            State(e.Document).Context = true;
        }

        void OnUnitsScaled(object sender, UnitsChangedWithScalingEventArgs e) =>
            UnitsScaled(e.Document ?? RhinoDoc.FromRuntimeSerialNumber(e.DocumentSerialNumber), e.Scale);

        void OnSelection(object sender, RhinoObjectSelectionEventArgs e) => _uiDirty = true;

        void OnDeselectAll(object sender, RhinoDeselectAllObjectsEventArgs e) => _uiDirty = true;

        void OnActiveDocumentChanged(object sender, DocumentEventArgs e) => _uiDirty = true;

        void OnUndoRedo(object sender, UndoRedoEventArgs e)
        {
            if (e.IsBeginUndo || e.IsBeginRedo)
                _undoDepth++;
            if (e.IsEndUndo || e.IsEndRedo)
            {
                _undoDepth = Math.Max(0, _undoDepth - 1);
                var d = RhinoDoc.FromRuntimeSerialNumber(e.DocumentSerialNumber);
                if (d != null)
                    State(d).PostUndo = true;
                _uiDirty = true;
            }
            if (!e.IsBeforeEndRecording || _processing || _suppress > 0 || _undoDepth > 0)
                return;
            var doc = RhinoDoc.FromRuntimeSerialNumber(e.DocumentSerialNumber);
            if (doc == null || doc.UndoActive || doc.RedoActive)
                return;
            if (!_docs.TryGetValue(doc.RuntimeSerialNumber, out var st) || !st.Pending)
                return;
            // the command that changed things is closing its undo record: update dependents inside it
            Process(doc, st);
        }

        void OnIdle(object sender, EventArgs e)
        {
            if (_processing)
                return;
            bool inCommand = Command.InCommand();
            if (!inCommand)
            {
                foreach (var doc in RhinoDoc.OpenDocuments())
                {
                    if (!_docs.TryGetValue(doc.RuntimeSerialNumber, out var st))
                        continue;
                    if (st.PostUndo)
                    {
                        st.PostUndo = false;
                        PostUndoSync(doc, st);
                    }
                    if (!st.Pending || doc.UndoRecordingIsActive || doc.UndoActive || doc.RedoActive)
                        continue;
                    bool record = doc.UndoRecordingEnabled;
                    uint sn = record ? doc.BeginUndoRecord("Skunk Update") : 0;
                    try
                    {
                        Process(doc, st);
                    }
                    finally
                    {
                        if (record)
                            doc.EndUndoRecord(sn);
                    }
                }
                // named CPlanes have no change event: look at the site planes twice a second
                if (_clock.ElapsedMilliseconds - _lastSiteCheck > 500)
                {
                    _lastSiteCheck = _clock.ElapsedMilliseconds;
                    var active = RhinoDoc.ActiveDoc;
                    // nor does the annotation scale in Document Properties, nor a layout detail that was added or turned
                    if (active != null && _docs.ContainsKey(active.RuntimeSerialNumber))
                    {
                        CheckAnnotationScale(active);
                        CheckGridViews(active);
                        CheckGeoLocation(active);
                    }
                    if (active != null && _docs.TryGetValue(active.RuntimeSerialNumber, out var ast) && ast.SiteSignature != null)
                    {
                        var sig = SiteModel.Get(active).Signature();
                        if (sig != ast.SiteSignature)
                            ast.Context = true;
                    }
                }
            }
            if (_uiDirty)
            {
                _uiDirty = false;
                try
                {
                    UiRefresh?.Invoke(RhinoDoc.ActiveDoc);
                }
                catch (Exception ex)
                {
                    Log.Debug("ui refresh: " + ex.Message);
                }
            }
        }

        /// <summary>After undo / redo: refresh side state that is not part of the undo record (named CPlanes, index).</summary>
        void PostUndoSync(RhinoDoc doc, DocState st)
        {
            st.Index.Rebuild(doc);
            st.IndexBuilt = true;
            var settings = DocStore.Settings(doc);
            if (settings.LevelCPlanes)
            {
                var site = SiteModel.Get(doc);
                foreach (var level in LevelElement.All(doc))
                {
                    var p = site.Project;
                    p.Origin = new Rhino.Geometry.Point3d(p.Origin.X, p.Origin.Y, level.Z);
                    NamedPlanes.Set(doc, level.Name, p);
                }
                LevelElement.OwnedCPlanes.RemoveStale(doc);
            }
            var now = SiteModel.Get(doc);
            st.SiteSignature = now.Signature();
            // Rhino's north is not in the undo record when its Sun keeps its own: one that went along with true north
            // goes back with it
            if (st.TrueNorth.HasValue)
                using (Suppress())
                    GeoLocation.FollowTrueNorth(doc, st.TrueNorth.Value, now.TrueNorth);
            st.TrueNorth = now.TrueNorth;
            doc.Views.Redraw();
        }

        // ---- processing -----------------------------------------------------------------------------

        /// <summary>
        /// Copies made in one operation: when a level and objects attached to it (or spots relative to
        /// it) are copied together, the copies belong to the copied level, not the original.
        /// </summary>
        static void RemapLevelCopies(RhinoDoc doc, HashSet<Guid> added)
        {
            if (added.Count < 2)
                return;
            var copies = new Dictionary<Guid, Guid>();
            foreach (var id in added)
            {
                var o = ElementIO.Find(doc, id);
                if (ElementIO.KindOf(o) != ElementKind.Level)
                    continue;
                var d = ElementIO.Read<LevelData>(o);
                if (d != null && d.Id != Guid.Empty && d.Id != o.Id)
                    copies[d.Id] = o.Id;
            }
            if (copies.Count == 0)
                return;
            foreach (var id in added)
            {
                var o = ElementIO.Find(doc, id);
                if (o == null)
                    continue;
                var kind = ElementIO.KindOf(o);
                if (kind == ElementKind.None && copies.TryGetValue(LevelElement.AttachedLevel(o), out var newLevel))
                    LevelElement.Attach(doc, o, newLevel);
                else if (ElementKinds.IsSpot(kind))
                {
                    var d = ElementIO.Read<SpotData>(o);
                    if (d != null && copies.TryGetValue(d.RelativeLevel, out var rel))
                    {
                        d.RelativeLevel = rel;
                        var a = o.Attributes.Duplicate();
                        a.SetUserString(Keys.Data, Json.Write(d));
                        doc.Objects.ModifyAttributes(o, a, true);
                    }
                }
            }
        }

        /// <summary>Levels and grids copied together with their datum box go into the copied box.</summary>
        static void RemapBoxCopies(RhinoDoc doc, HashSet<Guid> added)
        {
            if (added.Count < 2)
                return;
            var copies = new Dictionary<Guid, Guid>();
            foreach (var id in added)
            {
                var o = ElementIO.Find(doc, id);
                if (ElementIO.KindOf(o) != ElementKind.DatumBox)
                    continue;
                var d = ElementIO.Read<DatumBoxData>(o);
                if (d != null && d.Id != Guid.Empty && d.Id != o.Id)
                    copies[d.Id] = o.Id;
            }
            if (copies.Count == 0)
                return;
            foreach (var id in added)
            {
                var o = ElementIO.Find(doc, id);
                if (ElementIO.KindOf(o) != ElementKind.Level)
                    continue;
                var d = ElementIO.Read<LevelData>(o);
                if (d != null && copies.TryGetValue(d.Box, out var box))
                {
                    d.Box = box;
                    var a = o.Attributes.Duplicate();
                    a.SetUserString(Keys.Data, Json.Write(d));
                    doc.Objects.ModifyAttributes(o, a, true);
                }
            }
            foreach (var id in added)
            {
                var o = ElementIO.Find(doc, id);
                if (ElementIO.KindOf(o) != ElementKind.Grid)
                    continue;
                var d = ElementIO.Read<GridData>(o);
                if (d != null && copies.TryGetValue(d.Box, out var box))
                {
                    d.Box = box;
                    var a = o.Attributes.Duplicate();
                    a.SetUserString(Keys.Data, Json.Write(d));
                    doc.Objects.ModifyAttributes(o, a, true);
                }
            }
        }

        int Process(RhinoDoc doc, DocState st)
        {
            if (!st.Pending || _processing)
                return 0;
            var changed = new HashSet<Guid>(st.Changed);
            var added = new HashSet<Guid>(st.Added);
            var deleted = new Dictionary<Guid, DeletedInfo>(st.Deleted);
            bool full = st.Full;
            bool context = st.Context;
            bool views = st.Views;
            st.Views = false;
            st.Changed.Clear();
            st.Added.Clear();
            st.Deleted.Clear();
            st.Full = false;
            st.Context = false;
            double unitScale = st.UnitScale;
            st.UnitScale = 1;

            _processing = true;
            int modified = 0;
            try
            {
                // the model units changed: Skunk's stored lengths follow first, so the annotations Rhino
                // scaled with the change are not read as user edits (and attached objects do not move twice)
                unitScale = UnitScaling.Detect(doc, unitScale);
                if (Math.Abs(unitScale - 1) > 1e-12)
                {
                    UnitScaling.Apply(doc, unitScale);
                    context = true;
                }

                // a replaced object raises delete + add with the same id: it is a change, not a deletion
                foreach (var id in deleted.Keys.ToList())
                {
                    if (ElementIO.Find(doc, id) != null)
                    {
                        deleted.Remove(id);
                        changed.Add(id);
                    }
                }
                if (full || !st.IndexBuilt)
                {
                    st.Index.Rebuild(doc);
                    st.IndexBuilt = true;
                }

                // dependents of changed / deleted hosts, looked up before the index is refreshed
                var hostChanged = new HashSet<Guid>();
                foreach (var id in changed)
                    foreach (var s in st.Index.SpotsOn(id))
                        hostChanged.Add(s);
                var hostDeleted = new HashSet<Guid>();
                foreach (var id in deleted.Keys)
                    foreach (var s in st.Index.SpotsOn(id))
                        hostDeleted.Add(s);

                foreach (var id in changed)
                    st.Index.Refresh(doc, id);
                foreach (var id in deleted.Keys)
                    st.Index.Remove(id);

                SiteSync.RemoveDuplicates(doc, added);
                bool siteChanged = SiteSync.Run(doc, changed, st.SiteSignature, out var _);
                var ctx = new UpdateContext(doc);
                ctx.Added.UnionWith(added);
                bool everything = full || context || siteChanged;

                // models from earlier builds get their "Skunk" layer as "__Annotations__" (once), models from 1.4.1 and
                // earlier the paper Skunk Datum (once), and model views show linetypes at the annotation scale, as they print
                if (everything && Layers.Migrate(doc))
                    ctx.Modified++;
                if (everything && Types.Styles.RepairDatum(doc))
                    ctx.Modified++;
                if (everything && LinetypeDisplay.Follow(doc))
                    ctx.Modified++;

                // the "Rhino dimensions on a Skunk sublayer" option: new Rhino dimensions / leaders move there
                if (added.Count > 0)
                    modified += DimensionLayer.Apply(doc, added);

                // 0. a level copied together with its attached objects / relative spots takes them along, and
                // levels copied together with their datum box go into the copy
                RemapLevelCopies(doc, added);
                RemapBoxCopies(doc, added);

                // 1. datum boxes and levels: deletions, box edits, head edits, level edits
                var levels = new HashSet<Guid>();
                var grids = new HashSet<Guid>();
                bool levelWork = false;
                foreach (var kv in deleted)
                {
                    if (kv.Value.Kind == ElementKind.Level || kv.Value.Kind == ElementKind.DatumBox)
                        levelWork = true;
                    if (kv.Value.Kind == ElementKind.Level)
                        LevelElement.Deleted(ctx, kv.Key, Json.Read<LevelData>(kv.Value.Data));
                    else if (kv.Value.Kind == ElementKind.DatumBox)
                    {
                        // its levels and grids keep their extents and leave it
                        levels.UnionWith(DatumBoxElement.LevelIds(doc, kv.Key));
                        grids.UnionWith(DatumBoxElement.GridIds(doc, kv.Key));
                    }
                    else if (kv.Value.Kind == ElementKind.Grid)
                        GridElement.Deleted(ctx, kv.Key, Json.Read<GridData>(kv.Value.Data));
                    else if (kv.Value.Kind == ElementKind.GridHead || kv.Value.Kind == ElementKind.GridElevation)
                    {
                        var gh = Json.Read<GridHeadData>(kv.Value.Data);
                        if (gh != null && !deleted.ContainsKey(gh.Grid))
                            GridElement.HeadDeleted(ctx, gh.Grid, gh.End, gh.Label, gh.Elevation, kv.Key);
                    }
                    else if (kv.Value.Kind == ElementKind.LevelHead)
                    {
                        var hd = Json.Read<HeadData>(kv.Value.Data);
                        if (hd != null && !deleted.ContainsKey(hd.Level))
                            LevelElement.HeadDeleted(ctx, hd.Level, hd.Edge, hd.End, kv.Key);
                    }
                    else if (kv.Value.Kind == ElementKind.LevelLine)
                    {
                        var ld = Json.Read<HeadData>(kv.Value.Data);
                        if (ld != null && !deleted.ContainsKey(ld.Level))
                            LevelElement.LineDeleted(ctx, ld.Level, ld.Edge, kv.Key);
                    }
                }
                var boxes = new HashSet<Guid>();
                var heads = new List<RhinoObject>();
                var lines = new List<RhinoObject>();
                var bubbles = new List<RhinoObject>();
                foreach (var id in changed)
                {
                    var o = ElementIO.Find(doc, id);
                    switch (ElementIO.KindOf(o))
                    {
                        case ElementKind.Level: levels.Add(id); break;
                        case ElementKind.LevelHead: heads.Add(o); break;
                        case ElementKind.LevelLine: lines.Add(o); break;
                        case ElementKind.DatumBox: boxes.Add(id); break;
                        case ElementKind.Grid: grids.Add(id); break;
                        case ElementKind.GridHead:
                        case ElementKind.GridElevation: bubbles.Add(o); break;
                    }
                }
                if (everything)
                {
                    levels.UnionWith(st.Index.Levels);
                    boxes.UnionWith(st.Index.Boxes);
                    grids.UnionWith(st.Index.Grids);
                }
                // a datum box that was moved, scaled, rotated or grip-edited gives the levels in it its new outline
                foreach (var id in boxes)
                {
                    var o = ElementIO.Find(doc, id);
                    if (o != null && DatumBoxElement.Update(ctx, o, changed.Contains(id) ? UpdateReason.Self : UpdateReason.Context))
                    {
                        levels.UnionWith(DatumBoxElement.LevelIds(doc, id));
                        grids.UnionWith(DatumBoxElement.GridIds(doc, id));
                    }
                }
                levelWork |= boxes.Count > 0 || levels.Count > 0 || heads.Count > 0 || lines.Count > 0;
                foreach (var head in heads)
                {
                    var live = ElementIO.Find(doc, head.Id);
                    if (live != null)
                        LevelElement.HeadEdited(ctx, live);
                }
                foreach (var line in lines)
                {
                    var live = ElementIO.Find(doc, line.Id);
                    if (live != null)
                        LevelElement.LineEdited(ctx, live);
                }
                var levelZBefore = new Dictionary<Guid, double>();
                foreach (var id in levels)
                    levelZBefore[id] = ctx.LevelZ(id);
                foreach (var id in levels)
                {
                    var o = ElementIO.Find(doc, id);
                    if (o != null)
                        LevelElement.Update(ctx, o, changed.Contains(id) ? UpdateReason.Self : UpdateReason.Context);
                }
                // every datum box fits the heights of its levels again (one moved, joined, left or was deleted)
                if (levelWork)
                    foreach (var id in st.Index.Boxes.ToList())
                        DatumBoxElement.Fit(ctx, id);

                // 1b. grids, after the boxes that set their ends: bubble edits, then grid edits; every grid when a level
                // or a box changed, since grids in elevations reach from the lowest level to the highest
                if (levelWork)
                    grids.UnionWith(st.Index.Grids);
                foreach (var bubble in bubbles)
                {
                    var live = ElementIO.Find(doc, bubble.Id);
                    if (live != null)
                        GridElement.HeadEdited(ctx, live);
                }
                // copies last, nearest their original first: an array of grids is named in turn (2, 3, 4 going away)
                foreach (var id in GridElement.UpdateOrder(doc, grids))
                {
                    var o = ElementIO.Find(doc, id);
                    if (o != null)
                        GridElement.Update(ctx, o, changed.Contains(id) ? UpdateReason.Self : UpdateReason.Context);
                }
                // layout details show each grid the way it reads there: plans the plan grid, elevations the parts facing them
                var details = GridViews.Details(doc);
                if (details.Count > 0 && (views || grids.Count > 0))
                    ctx.Modified += GridViews.Apply(doc, views ? st.Index.Grids.ToList() : grids.ToList(), details);
                st.ViewsKey = GridViews.Signature(details);

                // 2. spots
                var spots = new Dictionary<Guid, UpdateReason>();
                void Want(Guid id, UpdateReason r)
                {
                    spots.TryGetValue(id, out var cur);
                    spots[id] = cur | r;
                }
                foreach (var id in changed)
                    if (st.Index.Spots.Contains(id))
                        Want(id, UpdateReason.Self);
                foreach (var id in hostChanged)
                    Want(id, UpdateReason.Host);
                foreach (var id in hostDeleted)
                    Want(id, UpdateReason.Host);
                foreach (var id in ctx.Moved)
                    foreach (var s in st.Index.SpotsOn(id))
                        Want(s, UpdateReason.Host);
                foreach (var id in levels)
                {
                    foreach (var s in st.Index.RelativeTo(id))
                        Want(s, UpdateReason.Context);
                    foreach (var s in st.Index.SpotsOn(id))
                        Want(s, UpdateReason.Host);
                }
                if (everything)
                    foreach (var id in st.Index.Spots)
                        Want(id, UpdateReason.Context);
                foreach (var kv in spots)
                {
                    var o = ElementIO.Find(doc, kv.Key);
                    if (o == null)
                        continue;
                    try
                    {
                        SpotElement.Update(ctx, o, kv.Value);
                    }
                    catch (Exception ex)
                    {
                        Log.Error("spot " + kv.Key + " was not updated", ex);
                    }
                }

                // 3. north arrows and plan cuts
                foreach (var id in st.Index.Arrows.ToList())
                {
                    if (everything || changed.Contains(id))
                        NorthArrowElement.Update(ctx, ElementIO.Find(doc, id));
                }
                foreach (var id in st.Index.Cuts.ToList())
                {
                    var o = ElementIO.Find(doc, id);
                    var d = o == null ? null : ElementIO.Read<PlanCutData>(o);
                    if (d != null && (everything || levels.Contains(d.Level) || changed.Contains(id)))
                        PlanCutElement.Update(ctx, o);
                }

                // 4. index entries for everything Skunk touched
                foreach (var id in spots.Keys)
                    st.Index.Refresh(doc, id);
                foreach (var id in levels)
                    st.Index.Refresh(doc, id);
                foreach (var id in boxes)
                    st.Index.Refresh(doc, id);
                foreach (var id in grids)
                    st.Index.Refresh(doc, id);
                st.SiteSignature = ctx.Site.Signature();
                // a scale Skunk itself just applied (Settings) needs no second refresh
                st.ScaleKey = ScaleKey(doc);
                modified = ctx.Modified;
                if (siteChanged)
                    st.SiteSignature = SiteModel.Get(doc).Signature();
                st.TrueNorth = SiteModel.Get(doc).TrueNorth;
            }
            catch (Exception ex)
            {
                Log.Error("update failed", ex);
            }
            finally
            {
                _processing = false;
                // anything recorded while processing came from Skunk itself
                st.Changed.Clear();
                st.Added.Clear();
                st.Deleted.Clear();
            }
            if (modified > 0)
                doc.Views.Redraw();
            _uiDirty = true;
            return modified;
        }
    }
}
