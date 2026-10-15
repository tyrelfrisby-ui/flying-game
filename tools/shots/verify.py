"""Check a delivered pair: probe the clip, confirm frame<->row alignment, and build a contact sheet."""
import csv, json, os, subprocess, sys

OUT = os.path.expanduser("~/Movies/Talk Flying Ep1 Re-edit (media)/aero")
TMP = "/private/tmp/aero-vg/verify"


def main(name):
    mov = os.path.join(OUT, f"{name}-marker.mov")
    csvp = os.path.join(OUT, f"{name}.csv")
    pr = subprocess.run(["ffprobe", "-v", "error", "-select_streams", "v",
                         "-show_entries", "stream=width,height,pix_fmt,nb_frames,r_frame_rate,duration",
                         "-of", "json", mov], capture_output=True, text=True)
    st = json.loads(pr.stdout)["streams"][0]
    rows = list(csv.DictReader(open(csvp)))
    nf = int(st["nb_frames"])
    print(f"{name}: {st['width']}x{st['height']} {st['pix_fmt']} {st['r_frame_rate']} "
          f"{float(st['duration']):.3f}s {nf} frames | csv {len(rows)} rows")
    print(f"  alignment: frames == rows -> {nf == len(rows)}; "
          f"t0 = {rows[0]['t_s']}, t_last = {rows[-1]['t_s']} (expect {(len(rows)-1)/30:.4f})")
    F = lambda r, k: float(r[k])
    order, seen = [], set()
    for r in rows:
        if r["phase"] not in seen:
            seen.add(r["phase"]); order.append(r["phase"])
    picks = []
    for ph in order:
        rs = [r for r in rows if r["phase"] == ph]
        g = [F(r, "load_factor_g") for r in rs]; k = [F(r, "kias") for r in rs]
        print(f"  {ph:<14} {len(rs):5d} fr  {F(rs[0],'t_s'):7.2f}-{F(rs[-1],'t_s'):7.2f}s  "
              f"g {min(g):6.2f}..{max(g):5.2f}  KIAS {min(k):5.1f}..{max(k):5.1f}  "
              f"alt {min(F(r,'altitude_ft') for r in rs):5.0f}..{max(F(r,'altitude_ft') for r in rs):5.0f} ft")
        picks.append((ph, rows.index(rs[len(rs)//2])))
    os.makedirs(TMP, exist_ok=True)
    tiles = []
    for ph, idx in picks:
        o = f"{TMP}/{name}_{ph}.png"
        subprocess.run(["ffmpeg", "-y", "-i", mov, "-vf", f"select=eq(n\\,{idx})", "-frames:v", "1", o],
                       check=True, capture_output=True)
        tiles.append((ph, o, idx))
    flats = []
    for ph, o, idx in tiles:
        fo = o.replace(".png", "_flat.png")
        subprocess.run(["ffmpeg", "-y", "-i", o, "-filter_complex",
                        f"color=c=0x1a2230:s=512x512[bg];[bg][0]overlay",
                        "-frames:v", "1", fo], check=True, capture_output=True)
        flats.append(fo)
    n = len(flats)
    args = []
    for fo in flats:
        args += ["-i", fo]
    layout = "|".join(f"{'0' if i == 0 else f'w0*{i}'}_0" for i in range(n))
    subprocess.run(["ffmpeg", "-y", *args, "-filter_complex",
                    f"{''.join(f'[{i}]' for i in range(n))}xstack=inputs={n}:layout={layout},scale={min(260*n,1900)}:-1",
                    "-frames:v", "1", f"{TMP}/{name}_sheet.png"], check=True, capture_output=True)
    print(f"  sheet: {TMP}/{name}_sheet.png")


if __name__ == "__main__":
    main(sys.argv[1])
