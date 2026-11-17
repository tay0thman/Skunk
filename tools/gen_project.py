"""Generate the Skunk Script Editor project from tools/commands.json.

    python gen_project.py

Writes, in ../ScriptEditor:
    <Command>.cs     one-line C# command scripts -> Skunk.SkunkEntry.<Command>(...) in shared/Skunk.Core.dll
    Skunk.rhproj     the Rhino 9 Script Editor project (commands, icons, ids, resources, toolbar link)
    Skunk.rui        the toolbar: ONE "Skunk" toolbar; a toolbar separator between its sections (Architecture,
                     Annotate, Manage, View) and between the groups of similar buttons in a section
                     (commands.json "group")

Safe to re-run: an existing Skunk.rhproj keeps what the Script Editor owns (version, identity text,
publisher, build / publish settings) and the ids of existing commands; only the command list,
icons, resources and toolbar ids are refreshed. Ids are uuid5 values derived from the project id,
so they are the same on every machine.
"""
import base64
import json
import os
import sys
import uuid
from xml.sax.saxutils import escape

HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, HERE)
import icons  # noqa: E402

ROOT = os.path.abspath(os.path.join(HERE, ".."))
SE = os.path.join(ROOT, "ScriptEditor")
RHPROJ = os.path.join(SE, "Skunk.rhproj")
RUI = os.path.join(SE, "Skunk.rui")
COMMANDS = os.path.join(HERE, "commands.json")

PROJECT_ID = str(uuid.uuid5(uuid.NAMESPACE_URL, "https://tayothman.com/rhino/skunk"))
NS = uuid.UUID(PROJECT_ID)
DLL = "Skunk.Core.dll"
CSHARP = {"id": "*.*.csharp", "version": "*.*.*"}

SCRIPT = """// {name}: {desc}
//
// Skunk's engine is shared/{dll} (a project resource: referenced automatically here and shipped in the
// package). Change behaviour in src/Skunk.Core, then run src/build-core.cmd before Build / Publish.
using Skunk;

__rhino_command_result__ = SkunkEntry.{name}(__rhino_doc__, __rhino_runmode__, __rhino_command__);
"""


def new_id(name):
    return str(uuid.uuid5(NS, name))


def b64(text):
    return base64.b64encode(text.encode("utf-8")).decode("ascii")


def image(icon_name, tab):
    return {"light": {"type": "svg", "data": b64(icons.svg(icon_name, "light", tab))},
            "dark": {"type": "svg", "data": b64(icons.svg(icon_name, "dark", tab))}}


def load_json(path):
    with open(path, encoding="utf-8-sig") as f:
        return json.load(f)


def save_json(obj, path):
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        json.dump(obj, f, indent=2)
        f.write("\n")


def default_project():
    return {
        "host": {"name": "Rhino3D", "version": "9.1.26300+6001", "os": "windows", "arch": "x64"},
        "id": PROJECT_ID,
        "identity": {
            "name": "Skunk",
            "version": "1.0.0",
            "publisher": {"email": "tay@tayothman.com", "name": "Tay Othman"},
            "description": ("Skunk brings project coordinate systems, datum levels and spot annotations to Rhino: internal origin, project base "
                            "point and survey point with true north on its own named CPlane; parametric levels with heads, "
                            "named CPlanes and attached objects; spot elevations, spot coordinates and spot slopes on surfaces, "
                            "terrain meshes and edges that update when they or their host move; north arrows that follow true "
                            "north; a familiar Properties palette and Type Properties. Every annotation is a native Rhino "
                            "leader or dimension, so it models, prints and exports. For architectural modeling, documentation "
                            "and site planning."),
            "tags": ["BIM", "Architecture", "Levels", "Spot Elevation", "Coordinates", "Survey", "Site", "Annotation"],
            "copyright": "Tay Othman",
        },
        "settings": {
            "buildPath": "build/",
            "runTestsOnBuild": False,
            "packageBuild": {"host": {"name": "Rhino3D", "version": "9.*"}, "title": "Rhino3D (9.*)", "slug": "rh9"},
            "packageServer": {"id": "mcneel.yak.public", "title": "McNeel Yak Server (yak.rhino3d.com)"},
        },
    }


def main():
    cfg = load_json(COMMANDS)
    commands = cfg["commands"]
    os.makedirs(os.path.join(SE, "shared"), exist_ok=True)

    project = load_json(RHPROJ) if os.path.exists(RHPROJ) else default_project()
    old_codes = {c.get("title"): c for c in project.get("codes", [])}

    # ---- scripts and codes -----------------------------------------------------------------------
    codes = []
    for c in commands:
        name = c["name"]
        with open(os.path.join(SE, name + ".cs"), "w", encoding="utf-8", newline="\n") as f:
            f.write(SCRIPT.format(name=name, desc=c["description"], dll=DLL))
        old = old_codes.get(name, {})
        codes.append({
            "id": old.get("id") or new_id("command." + name),
            "language": CSHARP,
            "title": name,
            "uri": name + ".cs",
            "image": image(c["icon"], c["section"]),
            "description": c["description"],
            "box": old.get("box") or {"id": new_id(name + ".item"), "imageId": new_id(name + ".icon")},
        })
    # remove scripts of commands that are gone
    for title in old_codes:
        if title not in {c["name"] for c in commands}:
            path = os.path.join(SE, title + ".cs")
            if os.path.exists(path):
                os.remove(path)

    group_id = new_id("toolbar.group")
    toolbar_id = new_id("toolbar.main")
    logo_icon = new_id("logo.icon")
    project["host"] = project.get("host") or default_project()["host"]
    project["bar"] = {"id": group_id, "tray": {"id": toolbar_id, "box": {"id": new_id("toolbar.box"), "imageId": logo_icon}}}
    project["id"] = project.get("id") or PROJECT_ID
    identity = project.setdefault("identity", default_project()["identity"])
    img = identity.setdefault("image", {})
    logo = image("skunk", "Architecture")
    img["light"] = logo["light"]
    img["dark"] = logo["dark"]
    png_icons(img)
    settings = project.setdefault("settings", default_project()["settings"])
    # no toolbar file in the package: Rhino opens a plug-in's own toolbar file late, when the plug-in loads, and up to
    # 1.6.5 that copy collided with Skunk's every start. The toolbar (ScriptEditor/Skunk.rui, written below) is embedded
    # in Skunk.Core, and Skunk writes it where Rhino opens it at start-up (Skunk.UI.SkunkToolbar).
    settings.pop("ruiPath", None)
    settings["ruiAuto"] = False
    project["codes"] = codes
    project["resources"] = [{"id": new_id("resource." + DLL), "uri": "shared/" + DLL}]

    # key order like the Script Editor writes it
    ordered = {k: project[k] for k in ("host", "bar", "id", "identity", "settings", "codes", "resources") if k in project}
    for k, v in project.items():
        ordered.setdefault(k, v)
    save_json(ordered, RHPROJ)
    print("wrote", RHPROJ, "(%d commands)" % len(codes))

    write_scripts_check(commands)

    # ---- toolbar -----------------------------------------------------------------------------------
    by_name = {c["title"]: c for c in codes}
    cfg_by_name = {c["name"]: c for c in commands}
    with open(RUI, "w", encoding="utf-8", newline="\n") as f:
        f.write(build_rui(cfg, by_name, cfg_by_name, group_id, toolbar_id, logo_icon, logo))
    print("wrote", RUI)

    # ---- compiled, always-on plug-in (src/Skunk -> Skunk.rhp) ------------------------------------------
    write_compiled_plugin(ordered, commands, codes)
    write_command_list(commands)
    panel_icons(commands)
    about_logo()


PANEL_ICON_SIZES = (16, 20, 24, 32, 40, 48, 64)


def about_logo():
    """The Skunk logo for the About dialog (SkAbout), light and dark, embedded in Skunk.Core (src/Skunk.Core/Resources)."""
    import render_png
    out = os.path.join(ROOT, "src", "Skunk.Core", "Resources")
    os.makedirs(out, exist_ok=True)
    for theme in ("light", "dark"):
        png = render_png.render(icons.svg("skunk", theme, "Architecture"), 96)
        if png is None:
            print("About logo skipped (needs Microsoft Edge and Pillow); keeping the existing one")
            return
        with open(os.path.join(out, "Skunk.About.%s.png" % theme), "wb") as f:
            f.write(png)
    print("wrote", out, "(Skunk.About.light.png, Skunk.About.dark.png)")


def panel_icons(commands):
    """The Skunk Attributes panel's tab icon (SkProperties' button icon), light and dark, as multi-size .ico
    files embedded in Skunk.Core (src/Skunk.Core/Resources); Rhino loads a panel icon from an assembly resource."""
    import render_png
    cmd = next(c for c in commands if c["name"] == "SkProperties")
    out = os.path.join(ROOT, "src", "Skunk.Core", "Resources")
    os.makedirs(out, exist_ok=True)
    for theme in ("light", "dark"):
        pngs = []
        for size in PANEL_ICON_SIZES:
            png = render_png.render(icons.svg(cmd["icon"], theme, cmd["section"]), size)
            if png is None:
                print("panel icons skipped (needs Microsoft Edge and Pillow); keeping existing ones")
                return
            pngs.append(png)
        with open(os.path.join(out, "SkunkAttributes.%s.ico" % theme), "wb") as f:
            f.write(render_png.ico_multi(pngs))
    print("wrote", out, "(SkunkAttributes.light.ico, SkunkAttributes.dark.ico)")


def write_command_list(commands):
    """The engine's own list of Skunk commands (name, toolbar label, section): the keyboard alias dialog uses it."""
    def cs(text):
        return text.replace("\\", "\\\\").replace('"', '\\"')
    lines = ["// <auto-generated> by tools/gen_project.py from tools/commands.json - do not edit. </auto-generated>",
             "namespace Skunk.Core", "{",
             "    /// <summary>Every Skunk command: name, toolbar label and toolbar section.</summary>",
             "    internal static class CommandList", "    {",
             "        internal static readonly (string Name, string Label, string Section)[] All =", "        {"]
    lines += ['            ("%s", "%s", "%s"),' % (cs(c["name"]), cs(c.get("label", c["name"])), cs(c.get("section", "")))
              for c in commands]
    lines += ["        };", "    }", "}", ""]
    path = os.path.join(ROOT, "src", "Skunk.Core", "Core", "CommandList.g.cs")
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines))
    print("wrote", path)


def write_compiled_plugin(project, commands, codes):
    """Command classes, version, Yak manifest and icons of the compiled plug-in. Same plug-in id and
    command names as the Script Editor project: install one or the other."""
    plugin_dir = os.path.join(ROOT, "src", "Skunk")
    os.makedirs(os.path.join(plugin_dir, "Resources"), exist_ok=True)
    os.makedirs(os.path.join(plugin_dir, "package"), exist_ok=True)
    identity = project["identity"]
    version = identity.get("version", "1.0.0")

    # the version of every build comes from the rhproj (edit it in the Script Editor)
    with open(os.path.join(ROOT, "src", "Version.props"), "w", encoding="utf-8", newline="\n") as f:
        f.write("<Project>\n  <!-- Generated by tools/gen_project.py from ScriptEditor/Skunk.rhproj identity.version. -->\n"
                "  <PropertyGroup>\n    <Version>%s</Version>\n  </PropertyGroup>\n</Project>\n" % version)

    ids = {c["title"]: c["id"] for c in codes}
    lines = ["// Generated by tools/gen_project.py from tools/commands.json - do not edit.",
             "// The compiled plug-in's commands: the same names and ids as the Script Editor project.",
             "using Rhino;", "using Rhino.Commands;", "", "namespace Skunk.Plugin", "{"]
    for c in commands:
        name = c["name"]
        cls = name + "Command"
        lines += ["    /// <summary>%s</summary>" % escape(c["description"]),
                  '    [System.Runtime.InteropServices.Guid("%s")]' % ids[name],
                  "    public sealed class %s : Command" % cls, "    {",
                  '        public override string EnglishName => "%s";' % name, "",
                  "        protected override Result RunCommand(RhinoDoc doc, RunMode mode) => SkunkEntry.%s(doc, mode, this);" % name,
                  "    }", ""]
    lines += ["    internal static class CommandList", "    {",
              "        internal static readonly string[] Names =", "        {"]
    lines += ['            "%s",' % c["name"] for c in commands]
    lines += ["        };", "    }", "}", ""]
    with open(os.path.join(plugin_dir, "Commands.g.cs"), "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines))

    # Yak manifest + icons (the package is built by the project's AfterBuild target with Yak.exe)
    def yaml_str(s):
        return "'" + str(s).replace("'", "''") + "'"
    manifest = ["name: skunk", "version: " + version, "authors:"]
    manifest += ["- " + yaml_str(identity.get("publisher", {}).get("name", "Tay Othman"))]
    manifest += ["description: >", "  " + identity.get("description", "").replace("\n", " "),
                 "url: 'https://github.com/tay0thman/Skunk'", "icon: icon.png", "keywords:"]
    manifest += ["- " + yaml_str(t.lower()) for t in identity.get("tags", [])]
    manifest += ["- guid:" + project["id"]]
    with open(os.path.join(plugin_dir, "package", "manifest.yml"), "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(manifest) + "\n")
    pkg = identity.get("image", {}).get("packageIcon", {}).get("light", {}).get("bytes")
    if pkg:
        png = base64.b64decode(pkg)
        with open(os.path.join(plugin_dir, "package", "icon.png"), "wb") as f:
            f.write(png)
        try:
            import io
            from PIL import Image
            Image.open(io.BytesIO(png)).convert("RGBA").save(
                os.path.join(plugin_dir, "Resources", "Skunk.ico"), format="ICO", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64)])
        except ImportError:
            print("Skunk.ico skipped (needs Pillow)")
    print("wrote", plugin_dir, "(Commands.g.cs, package/manifest.yml, icons) and src/Version.props =", version)


def write_scripts_check(commands):
    """C# file for the test harness: every command script body compiled against the engine, with the
    Script Editor globals declared as fields - a renamed or missing SkunkEntry method fails the build."""
    lines = ["// Generated by tools/gen_project.py - do not edit.",
             "// Compiles every ScriptEditor/<Command>.cs body against Skunk.Core (the Script Editor itself only",
             "// compiles a script the first time its command runs in Rhino).",
             "using Skunk;", "", "namespace SkunkCheck", "{",
             "    internal static class ScriptsCheck", "    {",
             "        static object __rhino_doc__ = null, __rhino_runmode__ = null, __rhino_command__ = null;",
             "        static Rhino.Commands.Result __rhino_command_result__;", ""]
    for c in commands:
        with open(os.path.join(SE, c["name"] + ".cs"), encoding="utf-8") as f:
            body = [l.rstrip() for l in f if l.strip() and not l.lstrip().startswith(("//", "using "))]
        lines.append("        internal static void %s()" % c["name"])
        lines.append("        {")
        lines += ["            " + l for l in body]
        lines.append("        }")
        lines.append("")
    lines += ["        internal static int Count => %d;" % len(commands), "",
              "        internal static readonly string[] Names = { %s };" % ", ".join('"%s"' % c["name"] for c in commands),
              "    }", "}", ""]
    path = os.path.join(ROOT, "src", "tests", "SkunkCheck", "ScriptsCheck.g.cs")
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write("\n".join(lines))
    print("wrote", path)


def png_icons(img):
    """Package (64), project (24 + ico) and category (16) PNG icons, like the Script Editor stores them."""
    import render_png
    entries = {}
    for key, size in (("packageIcon", 64), ("projectIcon", 24), ("categoryIcon", 16)):
        pair = {}
        for theme in ("light", "dark"):
            png = render_png.render(icons.svg("skunk", theme, "Architecture"), size)
            if png is None:
                print("PNG icons skipped (needs Microsoft Edge and Pillow); keeping existing ones")
                return
            pair[theme] = {"bytes": render_png.b64(png), "width": size, "height": size}
        if key == "projectIcon":
            pair["icoData"] = render_png.b64(render_png.ico(base64.b64decode(pair["light"]["bytes"])))
        entries[key] = pair
    img.update(entries)
    print("rendered PNG icons:", ", ".join(entries))


def loc(tag, text, n):
    pad = " " * n
    return "%s<%s>\n%s  <locale_1033>%s</locale_1033>\n%s</%s>" % (pad, tag, pad, escape(text), pad, tag)


def indent(text, n):
    return "\n".join(" " * n + line for line in text.splitlines())


def build_rui(cfg, codes, cfgs, group_id, toolbar_id, logo_icon, logo):
    """One "Skunk" toolbar with every command. A toolbar separator (a spacer item, as in Rhino's default.rui)
    goes between its sections (Architecture, Annotate, Manage, View) and between the groups of similar buttons
    within a section (commands.json "group"; a command without one is in its section's group)."""
    title = cfg.get("title", "Skunk")
    sections = cfg["sections"]
    out = ['<?xml version="1.0" encoding="utf-8"?>',
           '<RhinoUI major_ver="5" minor_ver="0" guid="%s" localize="False" default_language_id="1033" '
           'sidebar_one="00000000-0000-0000-0000-000000000000" dpi_scale="100">' % group_id,
           "  <extend_rhino_menus />", "  <menus />", "  <tool_bar_groups>",
           '    <tool_bar_group guid="%s" dock_bar_guid64="%s" active_tool_bar_group="%s" single_file="False" '
           'hide_single_tab="True" point_floating="0,0">' % (group_id, new_id("toolbar.dockbar"), toolbar_id),
           loc("text", title, 6),
           '      <tool_bar_group_item guid="%s" major_version="1" minor_version="1">' % new_id("toolbar.groupitem.main"),
           loc("text", title, 8),
           "        <tool_bar_id>%s</tool_bar_id>" % toolbar_id,
           "      </tool_bar_group_item>",
           '      <dock_bar_info dpi_scale="0" dock_bar="False" docking="False" horz="True" visible="True" '
           'floating="True" mru_float_style="0" bar_id="0" mru_width="0" point_pos="0,0" float_point="0,0" '
           'dock_location="top" float_size="0,0" />',
           "    </tool_bar_group>", "  </tool_bar_groups>", "  <tool_bars>",
           '    <tool_bar guid="%s" bitmap_id="%s">' % (toolbar_id, logo_icon), loc("text", title, 6)]
    first = True
    for s in sections:
        members = [c for c in cfg["commands"] if c.get("toolbar", True) and c["section"] == s]
        if not members:
            continue
        if not first:
            out.append('      <tool_bar_item guid="%s" button_style="spacer" />' % new_id("toolbar.spacer." + s))
        first = False
        group = None
        for c in members:
            g = c.get("group", s)
            if group is not None and g != group:
                out.append('      <tool_bar_item guid="%s" button_style="spacer" />' % new_id("toolbar.spacer." + s + "." + g))
            group = g
            code = codes[c["name"]]
            out += ['      <tool_bar_item guid="%s">' % code["box"]["id"], loc("text", c.get("label", c["name"]), 8),
                    "        <left_macro_id>%s</left_macro_id>" % code["id"]]
            if c.get("right"):
                out.append("        <right_macro_id>%s</right_macro_id>" % new_id(c["name"] + ".right"))
            out.append("      </tool_bar_item>")
    out += ["    </tool_bar>", "  </tool_bars>", "  <macros>"]

    def macro(guid, icon_id, text, tip, script):
        return ['    <macro_item guid="%s" bitmap_id="%s">' % (guid, icon_id), loc("button_text", text, 6),
                loc("menu_text", text, 6), loc("tooltip", tip, 6), "      <script>%s</script>" % escape(script),
                "    </macro_item>"]

    for c in cfg["commands"]:
        code = codes[c["name"]]
        out += macro(code["id"], code["box"]["imageId"], c.get("label", c["name"]), c["description"], c.get("left", "! _" + c["name"]))
        if c.get("right"):
            r = c["right"]
            out += macro(new_id(c["name"] + ".right"), code["box"]["imageId"], c.get("label", c["name"]), r["tooltip"], r["macro"])
    out += ["  </macros>", "  <icons>"]

    def icon_xml(guid, light, dark):
        return ['    <icon guid="%s">' % guid, "      <light>", indent(light, 8), "      </light>",
                "      <dark>", indent(dark, 8), "      </dark>", "    </icon>"]

    out += icon_xml(logo_icon, base64.b64decode(logo["light"]["data"]).decode("utf-8"), base64.b64decode(logo["dark"]["data"]).decode("utf-8"))
    for c in cfg["commands"]:
        code = codes[c["name"]]
        out += icon_xml(code["box"]["imageId"], icons.svg(c["icon"], "light", c["section"]), icons.svg(c["icon"], "dark", c["section"]))
    out += ["  </icons>", "  <bitmaps>",
            '    <small_bitmap item_width="16" item_height="16" />',
            '    <normal_bitmap item_width="24" item_height="24" />',
            '    <large_bitmap item_width="32" item_height="32" />',
            "  </bitmaps>", "  <scripts />", "</RhinoUI>", ""]
    return "\n".join(out)


def check_build_folder():
    """Warn when the Script Editor's build folder is not linked outside Google Drive. Drive marks every
    folder read-only and the Script Editor cannot delete read-only folders, so its next package build
    fails ("Error preparing build directory"). tools/link-build-folder.cmd fixes it."""
    is_junction = getattr(os.path, "isjunction", None)
    if os.name == "nt" and is_junction and not is_junction(os.path.join(SE, "build")):
        print("warning: ScriptEditor\\build is not linked outside Google Drive - the Script Editor's next "
              "package build will fail on Drive's read-only folders. Run tools\\link-build-folder.cmd once.")


if __name__ == "__main__":
    main()
    check_build_folder()
