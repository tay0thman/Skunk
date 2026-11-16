"""Run Skunk's coordinate snippet (taken verbatim from the engine source) against a stand-in for the
BIM API calls it uses, and write what it prints to a file for `SkunkCheck site <file>`:

    python src/tests/snippet_standin.py src/Skunk.Core/Site/SharedCoordinatesImport.cs <output.txt>

Stand-in model: true north 30 degrees east of project north, survey point at internal (10, 20, 0) with
shared E 1000 / N 5000 / Elev 100, project base point at internal (-4, 6, 0). The total transform is
given shared -> internal (the documented direction); Skunk must reproduce both points' shared values."""
import contextlib
import io
import math
import re
import sys
import types

source, out_path = sys.argv[1], sys.argv[2]
cs = open(source, encoding="utf-8").read()
m = re.search(r'public static string Snippet => @"(.*?)";\n', cs, re.S)
snippet = m.group(1).replace('""', '"')

a = math.radians(30)
tn = (math.sin(a), math.cos(a), 0.0)
te = (math.cos(a), -math.sin(a), 0.0)
sp_int, sp_shared = (10.0, 20.0, 0.0), (1000.0, 5000.0, 100.0)
origin = tuple(sp_int[i] - (sp_shared[0] * te[i] + sp_shared[1] * tn[i] + sp_shared[2] * (0, 0, 1)[i]) for i in range(3))
def shared_of(p):
    v = [p[i] - origin[i] for i in range(3)]
    return (sum(v[i] * te[i] for i in range(3)), sum(v[i] * tn[i] for i in range(3)), v[2])
pbp_int = (-4.0, 6.0, 0.0)
pbp_shared = shared_of(pbp_int)


class XYZ:
    def __init__(self, x, y, z):
        self.X, self.Y, self.Z = x, y, z


class Param:
    def __init__(self, v):
        self.v = v

    def AsDouble(self):
        return self.v


class BuiltInParameter:
    BASEPOINT_EASTWEST_PARAM = "ew"
    BASEPOINT_NORTHSOUTH_PARAM = "ns"
    BASEPOINT_ELEVATION_PARAM = "el"


class BasePoint:
    def __init__(self, is_shared, position, shared, clipped=None):
        self.IsShared = is_shared
        self.Position = XYZ(*position)
        self._p = {"ew": Param(shared[0]), "ns": Param(shared[1]), "el": Param(shared[2])}
        if clipped is not None:
            self.Clipped = clipped

    def get_Parameter(self, bip):
        return self._p.get(bip)


class Transform:
    Origin = XYZ(*origin)
    BasisX = XYZ(*te)
    BasisY = XYZ(*tn)
    BasisZ = XYZ(0.0, 0.0, 1.0)


class ProjectLocation:
    Name = "Internal"

    def GetTotalTransform(self):
        return Transform()


class Document:
    Title = "Stand-in Model"
    ActiveProjectLocation = ProjectLocation()
    points = [BasePoint(False, pbp_int, pbp_shared), BasePoint(True, sp_int, sp_shared, clipped=True)]


class FilteredElementCollector:
    def __init__(self, doc):
        self.doc = doc

    def OfClass(self, cls):
        return iter(self.doc.points)


db = types.ModuleType("Autodesk.Revit.DB")
db.BasePoint, db.BuiltInParameter, db.FilteredElementCollector = BasePoint, BuiltInParameter, FilteredElementCollector
sys.modules["Autodesk"] = types.ModuleType("Autodesk")
sys.modules["Autodesk.Revit"] = types.ModuleType("Autodesk.Revit")
sys.modules["Autodesk.Revit.DB"] = db
app = types.SimpleNamespace(ActiveUIDocument=types.SimpleNamespace(Document=Document()), Application=types.SimpleNamespace(VersionNumber="2026"))

buf = io.StringIO()
with contextlib.redirect_stdout(buf):
    exec(compile(snippet, "<skunk snippet>", "exec"), {"__revit__": app, "__name__": "__main__"})
printed = buf.getvalue()
open(out_path, "w", encoding="utf-8").write(">>> (snippet pasted into the console)\n" + printed + ">>> \n")
print("snippet ran; it printed %d line(s):" % len(printed.splitlines()))
print(printed[:220] + ("..." if len(printed) > 220 else ""))
print("expected PBP shared: %.6f, %.6f, %.6f" % pbp_shared)
