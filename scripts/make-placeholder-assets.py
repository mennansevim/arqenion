"""Örnek VR modelleri: katalog kodu adında, gerçek boyutta renkli kutular (.glb). Gerçek modeller gelince silinir.

Çalıştırma:  python3 scripts/make-placeholder-assets.py
Çıktı:       assets/catalog/<KOD>.glb  (backend /api/assets/manifest bunları listeler)
"""
import json
import struct
from pathlib import Path

OUT = Path(__file__).resolve().parent.parent / "assets" / "catalog"

# kod → (en, boy, kalınlık metre cinsinden, RGBA)
PLACEHOLDERS = {
    "SCALPEL_HANDLE_3": ((0.125, 0.012, 0.004), (0.75, 0.77, 0.80, 1)),
    "SCALPEL_BLADE_15": ((0.035, 0.008, 0.001), (0.85, 0.87, 0.90, 1)),
    "SCALPEL_BLADE_11": ((0.040, 0.009, 0.001), (0.85, 0.80, 0.80, 1)),   # yanıltıcı alet
    "NEEDLE_HOLDER_DEBAKEY_18": ((0.180, 0.020, 0.008), (0.70, 0.72, 0.75, 1)),
    "SUTURE_POLYGLACTIN_0": ((0.090, 0.060, 0.010), (0.55, 0.35, 0.75, 1)),
}


def box_glb(size, color) -> bytes:
    x, y, z = (s / 2 for s in size)
    corners = [(sx * x, sy * y, sz * z) for sx in (-1, 1) for sy in (-1, 1) for sz in (-1, 1)]
    faces = [(0, 1, 3, 2), (4, 6, 7, 5), (0, 4, 5, 1), (2, 3, 7, 6), (0, 2, 6, 4), (1, 5, 7, 3)]
    indices = [i for a, b, c, d in faces for i in (a, b, c, a, c, d)]
    positions = b"".join(struct.pack("<3f", *p) for p in corners)
    index_bytes = b"".join(struct.pack("<H", i) for i in indices)
    binary = positions + index_bytes
    binary += b"\0" * (-len(binary) % 4)
    doc = {
        "asset": {"version": "2.0", "generator": "arqenion placeholder"},
        "scene": 0, "scenes": [{"nodes": [0]}], "nodes": [{"mesh": 0}],
        "meshes": [{"primitives": [{"attributes": {"POSITION": 0}, "indices": 1, "material": 0}]}],
        "materials": [{"pbrMetallicRoughness": {"baseColorFactor": list(color), "metallicFactor": 0.8, "roughnessFactor": 0.35}}],
        "buffers": [{"byteLength": len(binary)}],
        "bufferViews": [
            {"buffer": 0, "byteOffset": 0, "byteLength": len(positions), "target": 34962},
            {"buffer": 0, "byteOffset": len(positions), "byteLength": len(index_bytes), "target": 34963},
        ],
        "accessors": [
            {"bufferView": 0, "componentType": 5126, "count": 8, "type": "VEC3", "min": [-x, -y, -z], "max": [x, y, z]},
            {"bufferView": 1, "componentType": 5123, "count": len(indices), "type": "SCALAR"},
        ],
    }
    js = json.dumps(doc, separators=(",", ":")).encode()
    js += b" " * (-len(js) % 4)
    chunks = struct.pack("<I4s", len(js), b"JSON") + js + struct.pack("<I4s", len(binary), b"BIN\0") + binary
    return struct.pack("<4sII", b"glTF", 2, 12 + len(chunks)) + chunks


if __name__ == "__main__":
    OUT.mkdir(parents=True, exist_ok=True)
    for code, (size, color) in PLACEHOLDERS.items():
        (OUT / f"{code}.glb").write_bytes(box_glb(size, color))
        print(f"{code}.glb")
