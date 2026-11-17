"""Skunk command icons (SVG, 24 x 24), in the BA palette.

Light theme: the glyph uses the tab colour (Architecture = Burgundy, Annotate = Olive,
Manage = Chocolate, View = Burgundy), host geometry hints are Sand with a dark outline.
Dark theme: the glyph is Sky, hints Sand. Coral is not used (it is reserved for alerts).

    python icons.py preview            writes ../docs/icons-preview.html (every icon, both themes)
"""
import math
import os
import sys

PALETTE = {
    "Burgundy": "#4F0329",
    "Chocolate": "#493825",
    "Olive": "#344217",
    "Parchment": "#FFF6EE",
    "Sand": "#C1B0A3",
    "Sky": "#BBEAFF",
    "White": "#FFFFFF",
}

TAB_COLOUR = {
    "Architecture": "Burgundy",
    "Annotate": "Olive",
    "Manage": "Chocolate",
    "View": "Burgundy",
}

FONT = "Segoe UI, Arial, Helvetica, sans-serif"


def _target(cx, cy, r, P, filled=True, sw=1.3):
    """Datum target symbol: circle, cross, two filled quadrants."""
    s = f'<circle cx="{cx}" cy="{cy}" r="{r}" fill="none" stroke="{P}" stroke-width="{sw}"/>'
    if filled:
        s += (f'<path d="M{cx} {cy} L{cx} {cy - r} A{r} {r} 0 0 1 {cx + r} {cy} Z" fill="{P}"/>'
              f'<path d="M{cx} {cy} L{cx} {cy + r} A{r} {r} 0 0 1 {cx - r} {cy} Z" fill="{P}"/>')
    else:
        s += (f'<line x1="{cx - r}" y1="{cy}" x2="{cx + r}" y2="{cy}" stroke="{P}" stroke-width="1"/>'
              f'<line x1="{cx}" y1="{cy - r}" x2="{cx}" y2="{cy + r}" stroke="{P}" stroke-width="1"/>')
    return s


def _text(x, y, text, P, size=6.5, anchor="middle"):
    return (f'<text x="{x}" y="{y}" font-family="{FONT}" font-size="{size}" font-weight="700" '
            f'fill="{P}" text-anchor="{anchor}">{text}</text>')


def _dash(x1, y, x2, P, sw=1.5):
    return f'<line x1="{x1}" y1="{y}" x2="{x2}" y2="{y}" stroke="{P}" stroke-width="{sw}" stroke-dasharray="3.2 1.4 1 1.4"/>'


def _arrow_head(x, y, angle_deg, P, size=3.2):
    a = math.radians(angle_deg)
    bx, by = x - size * math.cos(a), y - size * math.sin(a)
    px, py = -math.sin(a) * size * 0.55, math.cos(a) * size * 0.55
    return f'<path d="M{x:.2f} {y:.2f} L{bx + px:.2f} {by + py:.2f} L{bx - px:.2f} {by - py:.2f} Z" fill="{P}"/>'


def _rotate_arc(cx, cy, r, a0, a1, P, sw=1.5):
    """Arc from a0 to a1 (degrees, screen coordinates) with an arrow head at a1."""
    x0, y0 = cx + r * math.cos(math.radians(a0)), cy + r * math.sin(math.radians(a0))
    x1, y1 = cx + r * math.cos(math.radians(a1)), cy + r * math.sin(math.radians(a1))
    large = 1 if abs(a1 - a0) > 180 else 0
    sweep = 1 if a1 > a0 else 0
    tangent = a1 + (90 if sweep else -90)
    return (f'<path d="M{x0:.2f} {y0:.2f} A{r} {r} 0 {large} {sweep} {x1:.2f} {y1:.2f}" fill="none" stroke="{P}" stroke-width="{sw}"/>'
            + _arrow_head(x1, y1, tangent, P, 3.0))


def _gear(cx, cy, r_out, r_in, teeth, P):
    pts = []
    for i in range(teeth * 2):
        r = r_out if i % 2 == 0 else r_in
        for k in (-0.28, 0.28):
            a = math.pi * 2 * (i + 0.5 + k) / (teeth * 2)
            pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    d = "M" + " L".join(f"{x:.2f} {y:.2f}" for x, y in pts) + " Z"
    return f'<path d="{d}" fill="{P}"/><circle cx="{cx}" cy="{cy}" r="{r_in * 0.42:.2f}" fill="{{BG}}"/>'


# Each icon: function(P = glyph colour, S = hint fill, H = hint outline) -> SVG body
ICONS = {}


def icon(name):
    def deco(f):
        ICONS[name] = f
        return f
    return deco


@icon("level")
def _(P, S, H):
    return (_dash(1.5, 14, 13.2, P) + _target(18, 14, 4.2, P)
            + f'<rect x="2" y="6.5" width="9" height="2.2" rx="1.1" fill="{P}"/>'
            + f'<rect x="2" y="17" width="6" height="1.8" rx="0.9" fill="{S}" stroke="{H}" stroke-width="0.5"/>')


@icon("levels")
def _(P, S, H):
    out = ""
    for y in (4.5, 12, 19.5):
        out += _dash(1.5, y, 15.5, P, 1.3) + _target(19.5, y, 2.9, P, sw=1.1)
    return out


@icon("datum_box")
def _(P, S, H):
    # a dashed box with corner grips holding two level lines; their heads sit outside it
    out = (f'<rect x="2.5" y="3" width="13" height="18" fill="none" stroke="{P}" stroke-width="1.1" '
           f'stroke-dasharray="2.2 1.3"/>')
    for x, y in ((2.5, 3), (15.5, 3), (2.5, 21), (15.5, 21)):
        out += f'<rect x="{x - 1.4}" y="{y - 1.4}" width="2.8" height="2.8" fill="{P}"/>'
    for y in (8.5, 15.5):
        out += (f'<line x1="2.5" y1="{y}" x2="15.5" y2="{y}" stroke="{P}" stroke-width="1.3" stroke-dasharray="3.2 1.4 1 1.4"/>'
                + _target(19.6, y, 2.7, P, sw=1.1))
    return out


@icon("grid")
def _(P, S, H):
    # two grid lines, each with its bubble (an opaque circle with the grid's name)
    out = (f'<line x1="15" y1="9.4" x2="15" y2="23" stroke="{P}" stroke-width="1.3" stroke-dasharray="3.2 1.4 1 1.4"/>'
           f'<line x1="8.6" y1="16" x2="23" y2="16" stroke="{P}" stroke-width="1.3" stroke-dasharray="3.2 1.4 1 1.4"/>')
    for cx, cy, r, t in ((15, 5, 4.2, "1"), (4.4, 16, 4.0, "A")):
        out += f'<circle cx="{cx}" cy="{cy}" r="{r}" fill="{{BG}}" stroke="{P}" stroke-width="1.2"/>' + _text(cx, cy + 2.1, t, P, 6)
    return out


@icon("renumber_grids")
def _(P, S, H):
    # three grid bubbles numbered in turn, and the direction they are numbered in
    out = ""
    for cx, t in ((4.4, "1"), (12, "2"), (19.6, "3")):
        out += (f'<line x1="{cx}" y1="11.4" x2="{cx}" y2="16.4" stroke="{P}" stroke-width="1.2" stroke-dasharray="2.4 1.2"/>'
                f'<circle cx="{cx}" cy="7.4" r="3.4" fill="{{BG}}" stroke="{P}" stroke-width="1.1"/>' + _text(cx, 9.3, t, P, 5.3))
    out += f'<line x1="2.2" y1="20.4" x2="18.4" y2="20.4" stroke="{P}" stroke-width="1.3"/>' + _arrow_head(21.6, 20.4, 0, P, 3.6)
    return out


@icon("grid_bubbles")
def _(P, S, H):
    # a grid cross with bubbles on the top and left, and the bottom and right ones it can also show (hints)
    out = (f'<line x1="13" y1="7" x2="13" y2="17.6" stroke="{P}" stroke-width="1.2" stroke-dasharray="2.4 1.2"/>'
           f'<line x1="7" y1="13" x2="17.6" y2="13" stroke="{P}" stroke-width="1.2" stroke-dasharray="2.4 1.2"/>')
    for cx, cy in ((13, 3.9), (3.9, 13)):
        out += f'<circle cx="{cx}" cy="{cy}" r="3.1" fill="{{BG}}" stroke="{P}" stroke-width="1.2"/>'
    for cx, cy in ((13, 20.4), (20.4, 13)):
        out += f'<circle cx="{cx}" cy="{cy}" r="2.6" fill="{S}" stroke="{H}" stroke-width="0.9" stroke-dasharray="1.4 0.9"/>'
    return out


@icon("goto_level")
def _(P, S, H):
    return (f'<path d="M2 19 L14 19 L22 13.5 L10 13.5 Z" fill="{S}" stroke="{H}" stroke-width="0.9"/>'
            f'<path d="M6 16.25 L18 16.25 M8 13.5 L4 19 M14 13.5 L10 19 M20 13.5 L16 19" stroke="{H}" stroke-width="0.5"/>'
            f'<line x1="12" y1="2" x2="12" y2="11" stroke="{P}" stroke-width="1.8"/>' + _arrow_head(12, 13, 90, P, 4)
            + _target(19.5, 5, 2.6, P, sw=1.1))


@icon("attach")
def _(P, S, H):
    return (f'<rect x="5" y="3.5" width="10" height="8" fill="{S}" stroke="{H}" stroke-width="1"/>'
            f'<path d="M8 11.5 V17.5 M12 11.5 V17.5" stroke="{P}" stroke-width="1.6"/>'
            + _dash(1.5, 18.5, 15.5, P) + _target(19.5, 18.5, 3, P, sw=1.1))


@icon("detach")
def _(P, S, H):
    return (f'<rect x="5" y="2.5" width="10" height="8" fill="{S}" stroke="{H}" stroke-width="1"/>'
            f'<path d="M8 10.5 V12.6 M12 10.5 V12.6 M8 15.4 V17.5 M12 15.4 V17.5" stroke="{P}" stroke-width="1.6"/>'
            f'<path d="M5.5 15.8 L14.5 12.2" stroke="{P}" stroke-width="1.4"/>'
            + _dash(1.5, 18.5, 15.5, P) + _target(19.5, 18.5, 3, P, sw=1.1))


@icon("spot_elevation")
def _(P, S, H):
    return (f'<path d="M1.5 20.5 L22.5 15 L22.5 22.5 L1.5 22.5 Z" fill="{S}" stroke="{H}" stroke-width="0.9"/>'
            + _target(7.5, 18.9, 3, P, sw=1.1)
            + f'<path d="M9.6 16.8 L14 9.5 L22.5 9.5" fill="none" stroke="{P}" stroke-width="1.3"/>'
            + _text(18.2, 7.6, "EL", P, 7))


@icon("spot_coordinate")
def _(P, S, H):
    return (_target(6.5, 17, 3.4, P, filled=False, sw=1.2)
            + f'<path d="M8.9 14.6 L13 9 L22.5 9" fill="none" stroke="{P}" stroke-width="1.3"/>'
            + _text(17.8, 7.2, "N", P, 6.5) + _text(17.8, 15.8, "E", P, 6.5))


@icon("spot_slope")
def _(P, S, H):
    return (f'<path d="M1.5 19.5 L22.5 11 L22.5 22.5 L1.5 22.5 Z" fill="{S}" stroke="{H}" stroke-width="0.9"/>'
            f'<line x1="19.5" y1="7.6" x2="6.8" y2="12.8" stroke="{P}" stroke-width="1.5"/>'
            + _arrow_head(4.2, 13.85, 157.7, P, 3.6) + _text(14.5, 7.4, "%", P, 7.5))


@icon("north_arrow")
def _(P, S, H):
    return (f'<circle cx="12" cy="13.5" r="8.4" fill="none" stroke="{P}" stroke-width="1.4"/>'
            f'<path d="M12 6.4 L7.8 20 L12 16.9 Z" fill="{P}"/>'
            f'<path d="M12 6.4 L16.2 20 L12 16.9 Z" fill="none" stroke="{P}" stroke-width="1.1" stroke-linejoin="round"/>'
            + _text(12, 4.9, "N", P, 5.8))


@icon("edit_type")
def _(P, S, H):
    rows = "".join(f'<line x1="5.5" y1="{y}" x2="13.5" y2="{y}" stroke="{P}" stroke-width="1.2"/>' for y in (7, 10, 13, 16))
    return (f'<rect x="3" y="2.5" width="13" height="18" rx="1" fill="none" stroke="{P}" stroke-width="1.4"/>' + rows
            + f'<path d="M12.5 21.5 L13.2 18.3 L20.2 11.3 L22.7 13.8 L15.7 20.8 Z" fill="{S}" stroke="{H}" stroke-width="0.9" stroke-linejoin="round"/>'
            f'<path d="M12.5 21.5 L13.2 18.3 L15.7 20.8 Z" fill="{P}"/>')


@icon("properties")
def _(P, S, H):
    rows = ""
    for y in (10, 13.5, 17):
        rows += f'<line x1="5" y1="{y}" x2="10" y2="{y}" stroke="{P}" stroke-width="1.2"/>'
        rows += f'<rect x="12" y="{y - 1.3}" width="7" height="2.6" fill="{S}" stroke="{H}" stroke-width="0.6"/>'
    return (f'<rect x="2.5" y="2.5" width="19" height="19" rx="1.2" fill="none" stroke="{P}" stroke-width="1.4"/>'
            f'<rect x="2.5" y="2.5" width="19" height="4.2" rx="1.2" fill="{P}"/>' + rows)


@icon("location")
def _(P, S, H):
    return (f'<ellipse cx="12" cy="19.5" rx="8.5" ry="2.6" fill="{S}" stroke="{H}" stroke-width="0.8"/>'
            f'<path d="M12 19 C8.5 14.2 6 11.4 6 8.2 A6 6 0 0 1 18 8.2 C18 11.4 15.5 14.2 12 19 Z" fill="{P}"/>'
            f'<circle cx="12" cy="8.3" r="2.4" fill="{{BG}}"/>')


@icon("project_base_point")
def _(P, S, H):
    return (f'<circle cx="12" cy="12" r="8" fill="none" stroke="{P}" stroke-width="1.8"/>'
            f'<path d="M6.3 6.3 L17.7 17.7 M17.7 6.3 L6.3 17.7" stroke="{P}" stroke-width="1.6"/>')


@icon("survey_point")
def _(P, S, H):
    return (f'<path d="M12 2.8 L21.6 19.6 L2.4 19.6 Z" fill="none" stroke="{P}" stroke-width="1.8" stroke-linejoin="round"/>'
            f'<path d="M12 9 V17.5 M7.8 13.6 H16.2" stroke="{P}" stroke-width="1.2"/>'
            f'<circle cx="12" cy="13.6" r="1.4" fill="{P}"/>')


@icon("specify_coordinates")
def _(P, S, H):
    return (f'<path d="M3 3 V21 H21" fill="none" stroke="{H}" stroke-width="1"/>'
            f'<path d="M3 11 H9.5 M11 21 V14.5" stroke="{H}" stroke-width="0.8" stroke-dasharray="1.5 1.2"/>'
            f'<circle cx="11" cy="11" r="3.2" fill="none" stroke="{P}" stroke-width="1.4"/>'
            f'<path d="M11 6.2 V15.8 M6.2 11 H15.8" stroke="{P}" stroke-width="1.1"/>'
            f'<path d="M14.5 21.5 L15 19 L20.5 13.5 L22.5 15.5 L17 21 Z" fill="{S}" stroke="{H}" stroke-width="0.8"/>')


@icon("report_coordinates")
def _(P, S, H):
    return (f'<circle cx="6.5" cy="17" r="3" fill="none" stroke="{P}" stroke-width="1.3"/>'
            f'<path d="M6.5 12.8 V21.2 M2.3 17 H10.7" stroke="{P}" stroke-width="1"/>'
            f'<path d="M8.7 14.6 L11.5 11" stroke="{P}" stroke-width="1.2"/>'
            f'<rect x="10" y="2.5" width="12" height="9" rx="1" fill="{S}" stroke="{H}" stroke-width="0.9"/>'
            f'<path d="M12 5.4 H20 M12 8.4 H18" stroke="{P}" stroke-width="1.3"/>')


@icon("import_coordinates")
def _(P, S, H):
    # the pasted line (a sheet) flowing into the survey point
    return (f'<rect x="2.5" y="2.5" width="11" height="8.5" rx="1" fill="{S}" stroke="{H}" stroke-width="0.9"/>'
            f'<path d="M4.6 5.4 H11.4 M4.6 8.3 H9.6" stroke="{P}" stroke-width="1.3"/>'
            f'<path d="M7 11.5 V16.5 H11.2" fill="none" stroke="{P}" stroke-width="1.5"/>' + _arrow_head(13.6, 16.5, 0, P, 3.4)
            + f'<path d="M18 9.5 L22.5 20.5 L13.5 20.5 Z" fill="none" stroke="{P}" stroke-width="1.5" stroke-linejoin="round"/>'
            f'<path d="M18 13.2 V19.2 M15.7 16.9 H20.3" stroke="{P}" stroke-width="1"/>')


@icon("rotate_true_north")
def _(P, S, H):
    return (f'<line x1="9" y1="21" x2="9" y2="7" stroke="{P}" stroke-width="1.8"/>' + _arrow_head(9, 3.5, -90, P, 4.2)
            + _rotate_arc(9, 12, 9, -40, 50, P, 1.5)
            + _text(17.3, 6, "TN", P, 5.6))


@icon("rotate_project_north")
def _(P, S, H):
    return (f'<rect x="6" y="8" width="9" height="9" fill="{S}" stroke="{H}" stroke-width="1"/>'
            + _rotate_arc(10.5, 12.5, 8.5, 200, 300, P, 1.5)
            + f'<line x1="19.5" y1="21" x2="19.5" y2="13.5" stroke="{P}" stroke-width="1.5"/>' + _arrow_head(19.5, 10.5, -90, P, 3.4)
            + _text(19.5, 7.6, "PN", P, 5.2))


@icon("update")
def _(P, S, H):
    return (_rotate_arc(12, 12, 8, 200, 330, P, 1.8) + _rotate_arc(12, 12, 8, 20, 150, P, 1.8)
            + _target(12, 12, 2.9, P, sw=1.1))


@icon("settings")
def _(P, S, H):
    return _gear(12, 12, 9.5, 7.2, 8, P) + f'<circle cx="12" cy="12" r="2.2" fill="none" stroke="{P}" stroke-width="1.2"/>'


@icon("about")
def _(P, S, H):
    # an "i" in a circle: About Skunk
    return (f'<circle cx="12" cy="12" r="9.6" fill="none" stroke="{P}" stroke-width="1.6"/>'
            f'<circle cx="12" cy="7.3" r="1.45" fill="{P}"/>'
            f'<rect x="10.75" y="10.1" width="2.5" height="8" rx="0.9" fill="{P}"/>')


@icon("skunk")
def _(P, S, H):
    # tail, body, head, stripe (the Skunk logo)
    return (f'<path d="M14.6 15.6 C20.5 15.6 23.4 10.5 22 6.4 C20.9 3.1 17 1.6 14.4 3.2 C12.6 4.3 12.9 6.6 14.9 7.1 '
            f'C17.4 7.7 18.3 10.2 16.4 12.3 C15.5 13.3 14.4 13.9 13.6 14.2 Z" fill="{P}"/>'
            f'<ellipse cx="9.4" cy="16" rx="6.8" ry="4.3" fill="{P}"/>'
            f'<path d="M3.6 13.2 C2.2 13.6 1.2 15 1.4 16.3 C1.6 17.4 2.9 17.9 4.4 17.6 Z" fill="{P}"/>'
            f'<circle cx="4.6" cy="14.9" r="2.6" fill="{P}"/>'
            f'<rect x="5.6" y="18.8" width="2" height="3" rx="0.8" fill="{P}"/>'
            f'<rect x="12" y="18.8" width="2" height="3" rx="0.8" fill="{P}"/>'
            f'<path d="M4.4 12.6 C8 11.2 12.4 11.7 15 13.2 C17.6 11.6 19.7 9 19.4 6.2" fill="none" stroke="{{STRIPE}}" stroke-width="1.5" stroke-linecap="round"/>'
            f'<circle cx="3.5" cy="14.4" r="0.6" fill="{{STRIPE}}"/>')


@icon("orient_view")
def _(P, S, H):
    return (f'<rect x="2.5" y="3.5" width="19" height="17" rx="1" fill="none" stroke="{P}" stroke-width="1.4"/>'
            f'<g transform="rotate(25 12 12.5)"><path d="M12 6 L9 17.5 L12 15.4 L15 17.5 Z" fill="{P}"/></g>'
            + _rotate_arc(12, 12.5, 7.2, 200, 250, P, 1.2))


@icon("show_site")
def _(P, S, H):
    return (f'<path d="M1.5 12 C5 6.2 19 6.2 22.5 12 C19 17.8 5 17.8 1.5 12 Z" fill="none" stroke="{P}" stroke-width="1.5"/>'
            f'<path d="M12 8 L16 14.8 L8 14.8 Z" fill="{S}" stroke="{P}" stroke-width="1.2" stroke-linejoin="round"/>'
            f'<circle cx="12" cy="12.6" r="1.1" fill="{P}"/>')


def svg(icon_name, theme, tab):
    """Full SVG text for an icon in 'light' or 'dark' theme."""
    if theme == "dark":
        P, S, H, BG, stripe = PALETTE["Sky"], PALETTE["Sand"], PALETTE["Sky"], "#2B2B2B", PALETTE["Burgundy"]
    else:
        P = PALETTE[TAB_COLOUR.get(tab, "Burgundy")]
        S, H, BG, stripe = PALETTE["Sand"], P, "#FFFFFF", PALETTE["Parchment"]
    body = ICONS[icon_name](P, S, H).replace("{BG}", BG).replace("{STRIPE}", stripe)
    return f'<svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24">{body}</svg>'


def preview(path, entries):
    """HTML sheet with every icon in both themes (open it in a browser to review)."""
    rows = []
    for name, icon_name, tab in entries:
        light = svg(icon_name, "light", tab)
        dark = svg(icon_name, "dark", tab)
        big_l = light.replace('width="24" height="24"', 'width="48" height="48"')
        big_d = dark.replace('width="24" height="24"', 'width="48" height="48"')
        rows.append(f'<tr><td>{name}</td><td>{tab}</td><td class="l">{light}</td><td class="l">{big_l}</td>'
                    f'<td class="d">{dark}</td><td class="d">{big_d}</td></tr>')
    html = ("<!doctype html><meta charset='utf-8'><title>Skunk icons</title>"
            "<style>body{font-family:Segoe UI,Arial,sans-serif;margin:16px}td{padding:6px 10px;vertical-align:middle}"
            ".l{background:#f3f3f3}.d{background:#2b2b2b}</style>"
            "<h1>Skunk icons</h1><table><tr><th>Command</th><th>Tab</th><th colspan=2>Light</th><th colspan=2>Dark</th></tr>"
            + "".join(rows) + "</table>")
    with open(path, "w", encoding="utf-8", newline="\n") as f:
        f.write(html)


if __name__ == "__main__":
    if len(sys.argv) > 1 and sys.argv[1] == "preview":
        import json
        here = os.path.dirname(os.path.abspath(__file__))
        cfg = json.load(open(os.path.join(here, "commands.json"), encoding="utf-8"))
        entries = [(c["name"], c["icon"], c["section"]) for c in cfg["commands"]]
        out = os.path.join(here, "..", "docs", "icons-preview.html")
        preview(out, entries)
        print("wrote", os.path.abspath(out))
