"""Rasterize SVG icons to PNG with headless Microsoft Edge (+ Pillow to check / convert).

Used by gen_project.py for the PNG icons the Script Editor keeps next to the SVGs (package icon
64 px, project icon 24 px + .ico, category icon 16 px). Returns None when Edge or Pillow is missing,
in which case gen_project.py keeps whatever PNGs the project already has.
"""
import base64
import io
import os
import subprocess
import tempfile

EDGE_PATHS = [
    r"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
    r"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
]


def edge():
    for p in EDGE_PATHS:
        if os.path.exists(p):
            return p
    return None


def render(svg_text, size):
    """PNG bytes of the SVG at size x size (transparent background), or None."""
    exe = edge()
    if exe is None:
        return None
    try:
        from PIL import Image
    except ImportError:
        return None
    svg_text = svg_text.replace('width="24" height="24"', 'width="%d" height="%d"' % (size, size), 1)
    with tempfile.TemporaryDirectory() as tmp:
        svg_path = os.path.join(tmp, "icon.svg")
        png_path = os.path.join(tmp, "icon.png")
        with open(svg_path, "w", encoding="utf-8") as f:
            f.write(svg_text)
        url = "file:///" + svg_path.replace("\\", "/")
        cmd = [exe, "--headless=new", "--disable-gpu", "--hide-scrollbars", "--default-background-color=00000000",
               "--window-size=%d,%d" % (size, size), "--screenshot=" + png_path, url]
        try:
            subprocess.run(cmd, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL, timeout=60, check=False)
        except (OSError, subprocess.TimeoutExpired):
            return None
        if not os.path.exists(png_path):
            return None
        im = Image.open(png_path).convert("RGBA")
        if im.size != (size, size):
            im = im.crop((0, 0, size, size)) if im.size[0] >= size and im.size[1] >= size else im.resize((size, size))
        buf = io.BytesIO()
        im.save(buf, format="PNG")
        return buf.getvalue()


def ico(png_bytes):
    """A single-image .ico (PNG inside, like the Script Editor's projectIcon.icoData)."""
    import struct
    from PIL import Image
    im = Image.open(io.BytesIO(png_bytes))
    w, h = im.size
    header = struct.pack("<HHH", 0, 1, 1)
    entry = struct.pack("<BBBBHHII", w % 256, h % 256, 0, 0, 1, 32, len(png_bytes), 6 + 16)
    return header + entry + png_bytes


def ico_multi(pngs):
    """An .ico holding several PNG images, one per size (what a Rhino panel icon wants: 16 to 64 px)."""
    import struct
    from PIL import Image
    header = struct.pack("<HHH", 0, 1, len(pngs))
    offset = 6 + 16 * len(pngs)
    entries, data = b"", b""
    for png in pngs:
        w, h = Image.open(io.BytesIO(png)).size
        entries += struct.pack("<BBBBHHII", w % 256, h % 256, 0, 0, 1, 32, len(png), offset)
        offset += len(png)
        data += png
    return header + entries + data


def b64(data):
    return base64.b64encode(data).decode("ascii")
