#!/usr/bin/env python3
"""Move the textures embedded in our GLBs out into ordinary Unity texture assets --
and put them back again.

    python3 tools/models/glb_textures.py extract      # GLB -> sidecar .png + rewritten GLB
    python3 tools/models/glb_textures.py embed        # the exact inverse
    python3 tools/models/glb_textures.py verify       # prove nothing but the images moved
    python3 tools/models/glb_textures.py report       # what is embedded today, and what it costs

WHY THIS EXISTS -- the measurement that forced it
-------------------------------------------------
Build 2026.1105.1550 measured 4,291,667,298 B and was refused by App Store Connect (ITMS-90558, the 4 GB
uncompressed limit). `Data/resources.assets.resS` was 4,024,558,864 B of that -- 93.8 %. The whole
`unity/Assets` tree is 265 MB on disk, so on-disk size says nothing about build size.

The reason is this: a texture embedded in a GLB is imported by glTFast's `ScriptedImporter`, so Unity's
platform texture pipeline NEVER SEES IT. There is no compression field in a `.glb.meta` at all and no
per-platform override is possible. The pixels ship uncompressed, four bytes each, with a full mip chain.
The 39 GLBs under `Resources/Models` hold 754,922,296 pixels; 754.92e6 x 4 x 4/3 = 4,026,252,245 B against
the recorded 4,024,558,864 B -- 0.04 % out. resS is, to within rounding, nothing but GLB textures. Not
audio (1.1 MB in the whole tree), not video (none), not meshes (166 MB), not the configs (1.4 MB).

Ty, 2026-11-10, offered the options: *"Do it - 4x4, keep all liveries"* -- ASTC 4x4, every type and every
livery. ASTC 4x4 is 8 bits per pixel against 32, so the same pixels cost a quarter.

WHY MOVING THE FILES IS THE MECHANISM, and what was rejected
------------------------------------------------------------
glTFast has no compression setting of any kind (`ImportSettings` and `EditorImportSettings` are filter mode,
mip maps, anisotropy, node naming and animation -- nothing else), and its `GltfImporter` is an `internal`
class, so it cannot be subclassed. Three ways were considered:

1. REJECTED -- our own `ScriptedImporter` for .glb, registered with `importerOverride` in each `.glb.meta`,
   calling `EditorUtility.CompressTexture` before `ctx.AddObjectToAsset`. It works, but it means copying
   glTFast's importer into this repo and maintaining the copy: a fork in all but name, and a glTFast upgrade
   would silently stop applying to us. Every per-texture exception (normal maps, cut-out alpha, maximum size)
   would have to be re-invented in our own policy code.
2. REJECTED -- KTX2 / `KHR_texture_basisu` in the GLB (glTFast supports it with the `com.unity.cloud.ktx`
   package). The transcode target is chosen from `SystemInfo` at LOAD time, so an Editor import on this Mac
   would bake BC7 into the asset and the iOS build would ship a format iOS cannot sample. It also roughly
   triples the on-disk size of a public repo.
3. CHOSEN -- take the images out of the GLB and point at them by URI. This is glTFast's own first-class
   path, not a trick. `GltfImport.LoadImageFromBytes` says, in its own comment,

       #if UNITY_EDITOR
           if (IsEditorImport) {
               // Use the original texture at Editor (asset database) import
               return false;

   which sends the load through `EditorDownloadProvider.RequestTexture` ->
   `SyncTextureLoader` -> `AssetDatabase.LoadAssetAtPath<Texture2D>(uri)`. The texture the importer uses IS
   the Unity-imported asset, with everything its `TextureImporter` says: ASTC on iOS and Android, mip maps,
   sRGB, normal-map handling, maximum size, per-platform overrides -- the whole pipeline we were locked out
   of. `GltfImporter` then sees `AssetDatabase.GetAssetPath(texture)` is not empty and does NOT copy it into
   the GLB asset, so one PNG shared by four liveries is stored once.

That last point is a second, independent win that was measured, not assumed: of 754.92 Mpix embedded,
only 483.79 Mpix are distinct, and only 449.25 Mpix are referenced by any material anywhere. An alternate
livery is the same aeroplane with the paint baked in again -- it was carrying a full duplicate of every
texture it did not change.

WHERE THE FILES GO, AND WHY THE RELATIVE PATH HAS TO BE WHAT IT IS
-------------------------------------------------------------------
`unity/Assets/_Project/ModelTextures/<model-id>/<name>-<sha1>.png`, with anything used by more than one
model id under `_shared/`. The URI written into the GLB is `../../../ModelTextures/...`.

OUTSIDE `Resources/` ON PURPOSE. Unity ships every file under a Resources folder whether anything
references it or not. Inside Resources, the 34.5 Mpix that no material references would ship, and -- worse
-- holding an alternate livery's GLB out of a phone build (`BuildScript.HoldOutAlternateLiveries`) would
stop removing that livery's paint, because the paint would no longer be inside the file being held out.
Outside Resources, a texture is in the build if and only if a material that ships references it, which is
what both of those want.

THREE LEVELS UP, EXACTLY. `GLTFast.UriHelper.GetUriString` resolves a relative URI against the GLB's own
directory, walking up one directory per leading `..` (`RemoveDotSegments` -> `Path.GetDirectoryName`).
From `Assets/_Project/Resources/Models/<id>` three levels up is `Assets/_Project`. Any other hold-out or
staging directory a GLB is moved to for a build MUST therefore also sit five segments deep
(`Assets/_Project/<a>/<b>/<id>/x.glb`) or the URI resolves to nothing and the import fails. That is why
`BuildScript`'s two hold directories gained a `/Models` level in the same commit as this tool.

Dedupe is keyed on (file bytes, glTF sampler). Fifteen images in the fleet are used with two different
samplers -- clamp in one aeroplane, the glTF default in another -- and glTFast reacts to that by
`Object.Instantiate`-ing a second copy of the texture, which would then be embedded after all. Splitting
them keeps one file per (content, sampler) and the copy never happens.

REVERSIBLE ON PURPOSE. `embed` is the exact inverse and `verify` proves it: every accessor's bytes, and
every part of the glTF JSON except `images`, `bufferViews` and `buffers`, survive the round trip unchanged.
The offline model tools that do surgery on an embedded image (`floor_windows.py`, `glb_visuals.py`,
`instrument_face_style.py`, `update_engine_faces.py`, `pitts_cockpit_v2.py`, `vr_phase2e.py`,
`pilot_body.py` and the instrument face builders) still expect `images[i].bufferView`; run `embed`, run the
tool, run `extract`. A re-export out of Blender re-embeds too, and `GlbTextureTests` fails the suite if a
shipped GLB has embedded images again, so nobody has to remember.
"""

import argparse
import hashlib
import json
import os
import re
import struct
import sys

JSON_CHUNK = 0x4E4F534A
BIN_CHUNK = 0x004E4942
GLB_MAGIC = 0x46546C67

REPO = os.path.dirname(os.path.dirname(os.path.dirname(os.path.realpath(__file__))))
MODELS_REL = "unity/Assets/_Project/Resources/Models"
TEXDIR_REL = "unity/Assets/_Project/ModelTextures"
MANIFEST_REL = "docs/models/glb-textures.json"
# Three `..` from Assets/_Project/Resources/Models/<id> lands on Assets/_Project. See the module docstring.
URI_PREFIX = "../../../ModelTextures/"

MIME = {".png": "image/png", ".jpg": "image/jpeg", ".jpeg": "image/jpeg"}


# ---------------------------------------------------------------- GLB container

def read_glb(path):
    with open(path, "rb") as fh:
        data = fh.read()
    if len(data) < 12:
        raise ValueError(f"{path}: too short to be a GLB")
    magic, version, total = struct.unpack("<III", data[:12])
    if magic != GLB_MAGIC:
        raise ValueError(f"{path}: not a GLB (magic {magic:#x})")
    if version != 2:
        raise ValueError(f"{path}: glTF version {version}, only 2 is handled")
    off, js, bins = 12, None, b""
    while off < total:
        clen, ctype = struct.unpack("<II", data[off:off + 8])
        body = data[off + 8:off + 8 + clen]
        if ctype == JSON_CHUNK:
            js = body
        elif ctype == BIN_CHUNK:
            bins = body
        off += 8 + clen + ((-clen) % 4)
    if js is None:
        raise ValueError(f"{path}: no JSON chunk")
    return json.loads(js.decode("utf-8")), bins


def write_glb(path, gltf, bins):
    js = json.dumps(gltf, separators=(",", ":"), ensure_ascii=False).encode("utf-8")
    js += b" " * ((-len(js)) % 4)
    out = bytearray()
    body = bytearray()
    body += struct.pack("<II", len(js), JSON_CHUNK) + js
    if bins:
        pad = (-len(bins)) % 4
        body += struct.pack("<II", len(bins) + pad, BIN_CHUNK) + bins + b"\0" * pad
    out += struct.pack("<III", GLB_MAGIC, 2, 12 + len(body)) + body
    tmp = path + ".tmp"
    with open(tmp, "wb") as fh:
        fh.write(bytes(out))
    os.replace(tmp, path)


def view_bytes(gltf, bins, index):
    bv = gltf["bufferViews"][index]
    start = bv.get("byteOffset", 0)
    return bins[start:start + bv["byteLength"]]


# ------------------------------------------------------- bufferView bookkeeping

def _view_refs(gltf):
    """Every place in the glTF that names a bufferView, as (container, key) pairs we can rewrite.

    Anything NOT listed here that carries a bufferView would be silently corrupted by a renumber, so
    `extract` refuses to touch a file that uses an extension it does not know about."""
    refs = []
    for acc in gltf.get("accessors", []):
        if "bufferView" in acc:
            refs.append((acc, "bufferView"))
        sparse = acc.get("sparse")
        if sparse:
            for part in ("indices", "values"):
                if part in sparse and "bufferView" in sparse[part]:
                    refs.append((sparse[part], "bufferView"))
    for img in gltf.get("images", []):
        if "bufferView" in img:
            refs.append((img, "bufferView"))
    return refs


KNOWN_EXTENSIONS = {
    # Extensions in this fleet that do NOT reference a bufferView, so a renumber cannot hurt them.
    "KHR_materials_specular", "KHR_materials_ior", "KHR_materials_emissive_strength",
    "KHR_materials_unlit", "KHR_texture_transform", "KHR_lights_punctual",
    # KHR_materials_clearcoat: added 2026-11-10 with the beaver-v2 airframe, which this check stopped dead. Checked
    # rather than assumed: its only index-bearing members are textureInfo objects (clearcoatTexture,
    # clearcoatRoughnessTexture, clearcoatNormalTexture), which hold TEXTURE indices, and this tool never reorders
    # textures -- it rewrites images[].uri and drops the image bufferViews. In the beaver itself all 13 clearcoat
    # materials carry nothing but a scalar clearcoatFactor, and the file has no images or textures at all.
    "KHR_materials_clearcoat",
}
BUFFERVIEW_EXTENSIONS = {
    # These DO hold bufferView indices in places this tool does not rewrite.
    "KHR_draco_mesh_compression", "EXT_meshopt_compression", "KHR_texture_basisu", "EXT_texture_webp",
}


def check_extensions(path, gltf):
    used = set(gltf.get("extensionsUsed", [])) | set(gltf.get("extensionsRequired", []))
    bad = used & BUFFERVIEW_EXTENSIONS
    if bad:
        raise ValueError(f"{path}: uses {sorted(bad)}, which point at bufferViews this tool does not "
                         f"renumber. Teach _view_refs about them before running here.")
    unknown = used - KNOWN_EXTENSIONS
    if unknown:
        raise ValueError(f"{path}: unrecognised glTF extension(s) {sorted(unknown)}. Check whether they "
                         f"reference bufferViews, then add them to KNOWN_EXTENSIONS or "
                         f"BUFFERVIEW_EXTENSIONS.")


def repack(gltf, bins, keep):
    """Rebuild the BIN chunk from `keep` (old bufferView indices, in order) and renumber every reference."""
    remap = {}
    new_views = []
    out = bytearray()
    for new_index, old_index in enumerate(keep):
        bv = dict(gltf["bufferViews"][old_index])
        payload = view_bytes(gltf, bins, old_index)
        out += b"\0" * ((-len(out)) % 4)
        bv["byteOffset"] = len(out)
        bv["byteLength"] = len(payload)
        bv["buffer"] = 0
        out += payload
        remap[old_index] = new_index
        new_views.append(bv)
    gltf["bufferViews"] = new_views
    for holder, key in _view_refs(gltf):
        holder[key] = remap[holder[key]]
    gltf["buffers"] = [{"byteLength": len(out)}] if out else []
    return bytes(out)


# ------------------------------------------------------------------- image bits

def image_kind(blob):
    if blob[:8] == b"\x89PNG\r\n\x1a\n":
        return ".png"
    if blob[:2] == b"\xff\xd8":
        return ".jpg"
    raise ValueError("embedded image is neither PNG nor JPEG")


def png_size(blob):
    return struct.unpack(">II", blob[16:24])


def jpeg_size(blob):
    i, n = 2, len(blob)
    while i < n:
        if blob[i] != 0xFF:
            i += 1
            continue
        marker = blob[i + 1]
        if marker in (0xD8, 0xD9) or 0xD0 <= marker <= 0xD7 or marker == 0x01:
            i += 2
            continue
        length = struct.unpack(">H", blob[i + 2:i + 4])[0]
        if 0xC0 <= marker <= 0xCF and marker not in (0xC4, 0xC8, 0xCC):
            h, w = struct.unpack(">HH", blob[i + 5:i + 9])
            return w, h
        i += 2 + length
    raise ValueError("no SOF marker in JPEG")


def image_size(blob):
    return png_size(blob) if blob[:8] == b"\x89PNG\r\n\x1a\n" else jpeg_size(blob)


def sampler_of(gltf, texture):
    index = texture.get("sampler", -1)
    if index is None or index < 0:
        return {}
    return gltf.get("samplers", [])[index]


def sampler_key(sampler):
    return json.dumps({k: sampler.get(k) for k in ("magFilter", "minFilter", "wrapS", "wrapT")},
                      sort_keys=True)


def safe_name(name, fallback):
    name = name or fallback
    name = re.sub(r"[^A-Za-z0-9._-]+", "-", name).strip("-._")
    return (name or fallback)[:60]


def colour_space(gltf):
    """glTF says which textures are colour and which are data. Unity needs to be told."""
    role = {}

    def mark(info, kind):
        if not info or "index" not in info:
            return
        source = gltf.get("textures", [])[info["index"]].get("source")
        if source is not None:
            role.setdefault(source, set()).add(kind)

    for mat in gltf.get("materials", []):
        pbr = mat.get("pbrMetallicRoughness", {})
        mark(pbr.get("baseColorTexture"), "srgb")
        mark(pbr.get("metallicRoughnessTexture"), "linear")
        mark(mat.get("occlusionTexture"), "linear")
        mark(mat.get("emissiveTexture"), "srgb")
        mark(mat.get("normalTexture"), "normal")
        for ext in (mat.get("extensions") or {}).values():
            if not isinstance(ext, dict):
                continue
            for key, value in ext.items():
                if isinstance(value, dict) and "index" in value:
                    mark(value, "srgb" if "olor" in key else "linear")
    return role



# ------------------------------------------------------------------ Unity .meta

# glTF sampler enums -> Unity's serialised TextureImporter fields. glTFast's own `Sampler.Apply` makes the
# same mapping at runtime; writing it into the .meta means the IMPORTED asset already has it, so the shared
# file does not depend on whichever GLB happened to be imported last.
WRAP = {10497: 0, 33071: 1, 33648: 2}            # REPEAT / CLAMP_TO_EDGE / MIRRORED_REPEAT
FILTER_MIN = {9728: 0, 9729: 1, 9984: 0, 9985: 1, 9986: 1, 9987: 2}   # -> Point / Bilinear / Trilinear
TEXTURE_TYPE_DEFAULT, TEXTURE_TYPE_NORMAL = 0, 1

META = """fileFormatVersion: 2
guid: {guid}
TextureImporter:
  internalIDToNameTable: []
  externalObjects: {{}}
  serializedVersion: 13
  mipmaps:
    mipMapMode: 0
    enableMipMap: 1
    sRGBTexture: {srgb}
    linearTexture: 0
    fadeOut: 0
    borderMipMap: 0
    mipMapsPreserveCoverage: 0
    alphaTestReferenceValue: 0.5
    mipMapFadeDistanceStart: 1
    mipMapFadeDistanceEnd: 3
  bumpmap:
    convertToNormalMap: 0
    externalNormalMap: 0
    heightScale: 0.25
    normalMapFilter: 0
    flipGreenChannel: 0
  isReadable: 0
  streamingMipmaps: 0
  streamingMipmapsPriority: 0
  vTOnly: 0
  ignoreMipmapLimit: 0
  grayScaleToAlpha: 0
  generateCubemap: 6
  cubemapConvolution: 0
  seamlessCubemap: 0
  textureFormat: 1
  maxTextureSize: 4096
  textureSettings:
    serializedVersion: 2
    filterMode: {filter}
    aniso: 1
    mipBias: 0
    wrapU: {wrapu}
    wrapV: {wrapv}
    wrapW: 0
  nPOTScale: 0
  lightmap: 0
  compressionQuality: 50
  spriteMode: 0
  spriteExtrude: 1
  spriteMeshType: 1
  alignment: 0
  spritePivot: {{x: 0.5, y: 0.5}}
  spritePixelsToUnits: 100
  spriteBorder: {{x: 0, y: 0, z: 0, w: 0}}
  spriteGenerateFallbackPhysicsShape: 1
  alphaUsage: 1
  alphaIsTransparency: {alphatrans}
  spriteTessellationDetail: -1
  textureType: {ttype}
  textureShape: 1
  singleChannelComponent: 0
  flipbookRows: 1
  flipbookColumns: 1
  maxTextureSizeSet: 1
  compressionQualitySet: 0
  textureFormatSet: 0
  ignorePngGamma: 0
  applyGammaDecoding: 0
  swizzle: 50462976
  cookieLightType: 0
  platformSettings:
  - serializedVersion: 4
    buildTarget: DefaultTexturePlatform
    maxTextureSize: 4096
    resizeAlgorithm: 0
    textureFormat: -1
    textureCompression: 1
    compressionQuality: 50
    crunchedCompression: 0
    allowsAlphaSplitting: 0
    overridden: 0
    ignorePlatformSupport: 0
    androidETC2FallbackOverride: 0
    forceMaximumCompressionQuality_BC6H_BC7: 0
  spriteSheet:
    serializedVersion: 2
    sprites: []
    outline: []
    customData:\x20
    physicsShape: []
    bones: []
    spriteID:\x20
    internalID: 0
    vertices: []
    indices:\x20
    edges: []
    weights: []
    secondaryTextures: []
    spriteCustomMetadata:
      entries: []
    nameFileIdTable: {{}}
  mipmapLimitGroupName:\x20
  pSDRemoveMatte: 0
  userData:\x20
  assetBundleName:\x20
  assetBundleVariant:\x20
"""


def png_has_alpha(blob):
    if blob[:8] != b"\x89PNG\r\n\x1a\n":
        return False
    colour_type = blob[25]
    return colour_type in (4, 6) or b"tRNS" in blob[:4096]


FOLDER_META = """fileFormatVersion: 2
guid: {guid}
folderAsset: yes
DefaultImporter:
  externalObjects: {{}}
  userData:\x20
  assetBundleName:\x20
  assetBundleVariant:\x20
"""


def stable_guid(key):
    """A GUID from the asset's published path, so every machine and every worktree imports the same one."""
    return hashlib.md5(("FlyingGame/ModelTextures/" + key).encode()).hexdigest()


def write_folder_meta(path, key):
    """Unity wants a .meta for a folder as much as for a file, and this repo tracks them all."""
    with open(path + ".meta", "w") as fh:
        fh.write(FOLDER_META.format(guid=stable_guid(key)).replace("\\x20", " "))


def write_meta(dest, rel, entry, blob):
    """A .meta per sidecar, with a GUID derived from the published path so it is the same on every machine
    and in every worktree. The platform FORMAT block is not written here: `ModelTextureFormat` sets it at
    import time from the C# enums, because a wrong integer in this YAML would be a silent quality bug."""
    guid = stable_guid(rel)
    sampler = entry["sampler"]
    space = entry["colourSpace"]
    text = META.format(
        guid=guid,
        srgb=0 if space in ("linear", "normal") else 1,
        ttype=TEXTURE_TYPE_NORMAL if space == "normal" else TEXTURE_TYPE_DEFAULT,
        filter=FILTER_MIN.get(sampler.get("minFilter") or 9987, 2),
        wrapu=WRAP.get(sampler.get("wrapS") or 10497, 0),
        wrapv=WRAP.get(sampler.get("wrapT") or 10497, 0),
        alphatrans=1 if png_has_alpha(blob) else 0,
    ).replace("\\x20", " ")
    with open(dest + ".meta", "w") as fh:
        fh.write(text)


# ----------------------------------------------------------------------- walk

def glb_paths(models_dir):
    out = []
    for folder in sorted(os.listdir(models_dir)):
        full = os.path.join(models_dir, folder)
        if not os.path.isdir(full):
            continue
        for name in sorted(os.listdir(full)):
            if name.endswith(".glb"):
                out.append(os.path.join(full, name))
    return out


def model_id(path):
    return os.path.basename(os.path.dirname(path))


# -------------------------------------------------------------------- commands

def cmd_report(args):
    total_px = unique_px = 0
    seen = set()
    rows = []
    for path in glb_paths(args.models):
        gltf, bins = read_glb(path)
        px = 0
        n = 0
        for img in gltf.get("images", []):
            if "bufferView" not in img:
                continue
            blob = view_bytes(gltf, bins, img["bufferView"])
            w, h = image_size(blob)
            px += w * h
            n += 1
            digest = hashlib.sha1(blob).hexdigest()
            if digest not in seen:
                seen.add(digest)
                unique_px += w * h
        total_px += px
        if n:
            rows.append((px, n, os.path.relpath(path, args.repo)))
    rows.sort(reverse=True)
    for px, n, rel in rows:
        print(f"{px / 1e6:9.2f} Mpix  {n:4d} images  {rel}")
    print()
    print(f"embedded, counting every copy : {total_px / 1e6:10.2f} Mpix"
          f"  -> RGBA32 + mips {total_px * 16 / 3 / 1e9:6.3f} GB")
    print(f"distinct image content        : {unique_px / 1e6:10.2f} Mpix"
          f"  -> ASTC 4x4 + mips {unique_px * 4 / 3 / 1e9:6.3f} GB")
    return 0


def cmd_extract(args):
    """Images out of every GLB, into (content, sampler)-deduplicated sidecar files."""
    paths = glb_paths(args.models)
    for path in paths:
        check_extensions(path, read_glb(path)[0])

    # Pass one: which (content, sampler) pairs exist, and which model ids use each.
    users = {}
    keys_seen = {}
    for path in paths:
        gltf, bins = read_glb(path)
        samplers = {}
        for tex in gltf.get("textures", []):
            source = tex.get("source")
            if source is not None:
                samplers.setdefault(source, set()).add(sampler_key(sampler_of(gltf, tex)))
        for index, img in enumerate(gltf.get("images", [])):
            if "bufferView" not in img:
                continue
            blob = view_bytes(gltf, bins, img["bufferView"])
            digest = hashlib.sha1(blob).hexdigest()
            for key in sorted(samplers.get(index, {sampler_key({})})):
                users.setdefault((digest, key), set()).add(model_id(path))
                keys_seen.setdefault(digest, set()).add(key)

    # Pass two: write the files and rewrite the GLBs.
    written = {}
    manifest = {}
    for path in paths:
        gltf, bins = read_glb(path)
        images = gltf.get("images", [])
        if not any("bufferView" in img for img in images):
            continue
        role = colour_space(gltf)
        material_sources = set(role)
        samplers = {}
        for tex in gltf.get("textures", []):
            source = tex.get("source")
            if source is not None:
                samplers.setdefault(source, set()).add(sampler_key(sampler_of(gltf, tex)))

        # An image used with two samplers has to become two files: glTFast makes a private copy of the
        # second, and a private copy is embedded in the GLB asset again, which is the thing we are undoing.
        new_images = []
        extra_textures = {}
        for index, img in enumerate(images):
            if "bufferView" not in img:
                new_images.append((index, img, None, None))
                continue
            blob = view_bytes(gltf, bins, img["bufferView"])
            keys = sorted(samplers.get(index, {sampler_key({})}))
            for n, key in enumerate(keys):
                new_images.append((index, img, blob, key))
                if n:
                    extra_textures[(index, key)] = len(new_images) - 1

        out_images = []
        index_for = {}
        for slot, (old_index, img, blob, key) in enumerate(new_images):
            if blob is None:
                out_images.append(dict(img))
                index_for[(old_index, None)] = slot
                continue
            digest = hashlib.sha1(blob).hexdigest()
            ext = image_kind(blob)
            ids = users[(digest, key)]
            folder = sorted(ids)[0] if len(ids) == 1 else "_shared"
            # The sampler goes in the file name whenever the SAME bytes are used with more than one
            # sampler anywhere in the fleet -- not just inside this GLB. Wrap and filter mode live in the
            # .meta of the shared file, so clamp in one aeroplane and repeat in another cannot share one.
            suffix = "" if len(keys_seen.get(digest, {1})) < 2 else "-s" + hashlib.sha1(
                key.encode()).hexdigest()[:4]
            base = f"{safe_name(img.get('name'), 'image%d' % old_index)}-{digest[:8]}{suffix}{ext}"
            rel = f"{folder}/{base}"
            dest = os.path.join(args.texdir, folder, base)
            if rel not in written:
                os.makedirs(os.path.dirname(dest), exist_ok=True)
                with open(dest, "wb") as fh:
                    fh.write(blob)
                written[rel] = True
                w, h = image_size(blob)
                kinds = role.get(old_index, set())
                manifest[rel] = {
                    "sha1": digest, "width": w, "height": h, "bytes": len(blob),
                    "colourSpace": ("normal" if "normal" in kinds
                                    else "linear" if kinds == {"linear"}
                                    else "srgb"),
                    "sampler": json.loads(key), "models": sorted(ids),
                    "usedBy": [], "materialUsedBy": [],
                }
                write_meta(dest, rel, manifest[rel], blob)
            # "<model id>/<file>.glb", so a reader (and the tests) never have to guess which aeroplane a
            # GLB belongs to -- instruments/attitude.glb is not named after its folder.
            glb_key = f"{model_id(path)}/{os.path.basename(path)}"
            manifest[rel]["usedBy"].append(glb_key)
            # A texture only reaches the build if a MATERIAL names it. 102 of these are in a GLB's `textures`
            # array and in no material at all; outside Resources they simply do not ship, which is 34.5 Mpix
            # that used to. Recording it here lets the tests measure a build without re-deriving it.
            if old_index in material_sources:
                manifest[rel]["materialUsedBy"].append(glb_key)
            entry = {"uri": URI_PREFIX + rel, "mimeType": MIME[ext]}
            if img.get("name"):
                entry["name"] = img["name"]
            out_images.append(entry)
            index_for[(old_index, key)] = slot

        for tex in gltf.get("textures", []):
            source = tex.get("source")
            if source is None:
                continue
            key = sampler_key(sampler_of(gltf, tex))
            tex["source"] = index_for.get((source, key), index_for.get((source, None), source))

        keep = [i for i in range(len(gltf.get("bufferViews", [])))
                if i not in {img["bufferView"] for img in images if "bufferView" in img}]
        gltf["images"] = out_images
        bins = repack(gltf, bins, keep)
        write_glb(path, gltf, bins)
        print(f"extracted {sum(1 for i in images if 'bufferView' in i):4d} image(s) from "
              f"{os.path.relpath(path, args.repo)}")

    if written:
        write_folder_meta(args.texdir, "")
        for folder in sorted({rel.split("/")[0] for rel in written}):
            write_folder_meta(os.path.join(args.texdir, folder), folder)

    for entry in manifest.values():
        entry["usedBy"] = sorted(set(entry["usedBy"]))
        entry["materialUsedBy"] = sorted(set(entry["materialUsedBy"]))

    # AN EMPTY RESULT DOES NOT OVERWRITE THE MANIFEST (2026-11-10). This happened, it was not foreseen: `extract`
    # was run over a tree whose textures were ALREADY sidecars, found nothing embedded, and rewrote
    # docs/models/glb-textures.json from 134 records to none. The PNGs survive and `verify` still passes without the
    # manifest, so nothing renders wrong -- but the manifest is the only written account of which image came out of
    # which GLB, and the obvious way to run the obvious command destroyed it. An empty result is the signature of an
    # already-extracted tree, which is now the ordinary state of this repository, so refusing to write is both the
    # safe behaviour and the common case. --allow-empty-manifest forces it for a tree that really has no textures at
    # all; nobody has needed that yet.
    if not manifest and not args.allow_empty_manifest:
        had = 0
        if os.path.exists(args.manifest):
            try:
                with open(args.manifest) as fh:
                    had = len(json.load(fh).get("textures", {}))
            except (OSError, ValueError):
                had = -1
        print(f"\nno embedded images found in {len(paths)} GLB(s): nothing to extract.")
        print(f"manifest left as it was ({had if had >= 0 else 'unreadable'} record(s)) -- "
              f"{os.path.relpath(args.manifest, args.repo)}")
        print("This is what an already-extracted tree looks like. Use `verify` to check it, or "
              "--allow-empty-manifest to write an empty manifest on purpose.")
        return 0

    os.makedirs(os.path.dirname(args.manifest), exist_ok=True)
    with open(args.manifest, "w") as fh:
        json.dump({"note": "generated by tools/models/glb_textures.py extract",
                   "textures": dict(sorted(manifest.items()))}, fh, indent=1)
        fh.write("\n")
    print(f"\n{len(written)} sidecar texture file(s) under {os.path.relpath(args.texdir, args.repo)}")
    print(f"manifest -> {os.path.relpath(args.manifest, args.repo)}")
    return 0


def cmd_embed(args):
    """The inverse: pull every sidecar back into its GLB, so the offline tools can do image surgery."""
    for path in glb_paths(args.models):
        gltf, bins = read_glb(path)
        images = gltf.get("images", [])
        if not any(img.get("uri", "").startswith(URI_PREFIX) for img in images):
            continue
        keep = list(range(len(gltf.get("bufferViews", []))))
        blobs = []
        for img in images:
            uri = img.get("uri", "")
            if not uri.startswith(URI_PREFIX):
                blobs.append(None)
                continue
            src = os.path.join(args.texdir, uri[len(URI_PREFIX):])
            with open(src, "rb") as fh:
                blobs.append(fh.read())
        # Append the images as fresh bufferViews after the existing ones, then repack in one pass.
        extra = []
        for blob in blobs:
            if blob is None:
                extra.append(None)
                continue
            gltf["bufferViews"].append({"buffer": 0, "byteOffset": len(bins), "byteLength": len(blob)})
            bins = bins + blob
            extra.append(len(gltf["bufferViews"]) - 1)
            keep.append(len(gltf["bufferViews"]) - 1)
        for img, blob, view in zip(images, blobs, extra):
            if view is None:
                continue
            img.pop("uri", None)
            img["bufferView"] = view
            img["mimeType"] = MIME[image_kind(blob)]
        bins = repack(gltf, bins, keep)
        write_glb(path, gltf, bins)
        print(f"embedded {sum(1 for e in extra if e is not None):4d} image(s) into "
              f"{os.path.relpath(path, args.repo)}")
    return 0


def accessor_digest(gltf, bins):
    """A fingerprint of every accessor's actual bytes -- the thing a bufferView renumber could ruin."""
    sizes = {5120: 1, 5121: 1, 5122: 2, 5123: 2, 5125: 4, 5126: 4}
    counts = {"SCALAR": 1, "VEC2": 2, "VEC3": 3, "VEC4": 4, "MAT2": 4, "MAT3": 9, "MAT4": 16}
    digest = hashlib.sha1()
    for acc in gltf.get("accessors", []):
        element = sizes[acc["componentType"]] * counts[acc["type"]]
        if "bufferView" not in acc:
            digest.update(b"\0" * element * acc["count"])
            continue
        bv = gltf["bufferViews"][acc["bufferView"]]
        stride = bv.get("byteStride") or element
        base = bv.get("byteOffset", 0) + acc.get("byteOffset", 0)
        for i in range(acc["count"]):
            digest.update(bins[base + i * stride: base + i * stride + element])
    return digest.hexdigest()


IGNORED_IN_COMPARE = ("images", "bufferViews", "buffers")


def structural(gltf):
    """The glTF with everything a bufferView renumber legitimately changes taken out.

    Removing an image's bufferView renumbers the ones after it, so the accessors' INDICES must be allowed
    to move -- what must not move is the bytes they address, and `accessor_digest` holds that separately.
    Everything else -- meshes, materials, textures, samplers, nodes, scenes, skins -- must be identical."""
    copy = json.loads(json.dumps({k: v for k, v in gltf.items() if k not in IGNORED_IN_COMPARE}))
    for acc in copy.get("accessors", []):
        acc.pop("bufferView", None)
        for part in (acc.get("sparse") or {}).values():
            if isinstance(part, dict):
                part.pop("bufferView", None)
    return copy


def cmd_verify(args):
    """Round-trip the tree in memory and prove that only the images moved."""
    bad = 0
    for path in glb_paths(args.models):
        gltf, bins = read_glb(path)
        before_json = structural(gltf)
        before_acc = accessor_digest(gltf, bins)
        embedded = [view_bytes(gltf, bins, img["bufferView"])
                    for img in gltf.get("images", []) if "bufferView" in img]
        external = [img["uri"] for img in gltf.get("images", []) if img.get("uri")]

        if embedded:
            problems = []
        else:
            problems = []
            for uri in external:
                if not uri.startswith(URI_PREFIX):
                    problems.append(f"image uri {uri!r} is not under {URI_PREFIX}")
                    continue
                src = os.path.join(args.texdir, uri[len(URI_PREFIX):])
                if not os.path.exists(src):
                    problems.append(f"image uri {uri!r} points at a file that is not there")
            # Three levels up has to land on Assets/_Project. Check it from the real path.
            here = os.path.dirname(path)
            up = os.path.normpath(os.path.join(here, "..", "..", ".."))
            if os.path.normpath(up) != os.path.normpath(os.path.join(args.repo, "unity/Assets/_Project")):
                problems.append(f"'{URI_PREFIX}' from {here} resolves to {up}, not Assets/_Project")

        # The structural proof: repack with no image views and check nothing else moved.
        drop = {img["bufferView"] for img in gltf.get("images", []) if "bufferView" in img}
        keep = [i for i in range(len(gltf.get("bufferViews", []))) if i not in drop]
        repacked = dict(json.loads(json.dumps(gltf)))
        repacked["images"] = []   # the images are what is being removed; everything else must survive
        new_bins = repack(repacked, bins, keep)
        after_json = structural(repacked)
        if before_json != after_json:
            problems.append("the glTF JSON changed outside images/bufferViews/buffers")
        if accessor_digest(repacked, new_bins) != before_acc:
            problems.append("accessor bytes changed when the image bufferViews were removed")

        state = "embedded" if embedded else ("external" if external else "no textures")
        if problems:
            bad += 1
            print(f"FAIL {os.path.relpath(path, args.repo)} [{state}]")
            for problem in problems:
                print(f"       {problem}")
        else:
            print(f"ok   {os.path.relpath(path, args.repo)} [{state}, {len(embedded) + len(external)}]")
    print(f"\n{bad} problem file(s)")
    return 1 if bad else 0


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.splitlines()[0])
    parser.add_argument("command", choices=("extract", "embed", "verify", "report"))
    parser.add_argument("--repo", default=REPO)
    parser.add_argument("--allow-empty-manifest", action="store_true",
                        help="extract: write an empty manifest even when no GLB holds an embedded image. Without "
                             "this, an empty result leaves the existing manifest alone, because that is the "
                             "signature of an already-extracted tree (see the note in cmd_extract).")
    args = parser.parse_args(argv)
    args.models = os.path.join(args.repo, MODELS_REL)
    args.texdir = os.path.join(args.repo, TEXDIR_REL)
    args.manifest = os.path.join(args.repo, MANIFEST_REL)
    return {"extract": cmd_extract, "embed": cmd_embed,
            "verify": cmd_verify, "report": cmd_report}[args.command](args)


if __name__ == "__main__":
    sys.exit(main())
