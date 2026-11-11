#!/usr/bin/python3
"""Draw the wake test curves (WakeVortexTests writes them as CSV when WAKE_PLOT_DIR is set).

    WAKE_PLOT_DIR=/tmp/wakeplots dotnet test --filter WakeVortexTests
    /usr/bin/python3 tools/wake/plot_wake_tests.py /tmp/wakeplots ~/Developer/agent-lanes/renders/clouds-wake
"""
import csv, os, sys
import matplotlib
matplotlib.use("Agg")
import matplotlib.pyplot as plt
import numpy as np

src, out = sys.argv[1], os.path.expanduser(sys.argv[2])
os.makedirs(out, exist_ok=True)

def rows(name):
    with open(os.path.join(src, name)) as f:
        return list(csv.DictReader(f))

# 1. Decay and descent.
r = rows("wake_decay_descent.csv")
fig, ax = plt.subplots(1, 3, figsize=(15, 4.5))
for case, col in (("calm", "#2a6fdb"), ("turbulent", "#d9480f")):
    d = [x for x in r if x["case"] == case]
    t = np.array([float(x["age_s"]) for x in d])
    ax[0].plot(t, [float(x["alt_m"]) - 1000 for x in d], color=col, label=case)
    ax[1].plot(t, [float(x["gamma_m2s"]) for x in d], color=col, label=case)
    ax[2].plot(t, [float(x["core_m"]) for x in d], color=col, label=case)
ax[0].set_title("Pair height vs flight path (C172-size, 50 m/s)"); ax[0].set_xlabel("age s"); ax[0].set_ylabel("m")
ax[1].set_title("Circulation Γ"); ax[1].set_xlabel("age s"); ax[1].set_ylabel("m²/s")
ax[2].set_title("Core radius (Lamb–Oseen, Squire ν)"); ax[2].set_xlabel("age s"); ax[2].set_ylabel("m")
for a in ax: a.grid(alpha=.3); a.legend()
fig.suptitle("Emergent descent (each line moves on the other's induction) and two-phase decay; ε calm 1e-4, turbulent 1e-2 m²/s³")
fig.tight_layout(); fig.savefig(os.path.join(out, "wake_decay_descent.png"), dpi=110); plt.close(fig)

# 2. Follower roll.
fig, ax = plt.subplots(1, 2, figsize=(12, 4.5))
for side, col in (("right", "#2a6fdb"), ("left", "#d9480f")):
    d = rows(f"wake_follower_roll_{side}_line.csv")
    t = [float(x["t_s"]) for x in d]
    ax[0].plot(t, [float(x["roll_in_wake_deg"]) - float(x["roll_clean_deg"]) for x in d], color=col, label=f"CG on leader's {side} line")
    ax[1].plot(t, [float(x["p_in_wake_degs"]) for x in d], color=col, label=f"{side} line")
ax[0].axhline(0, color="k", lw=.5); ax[1].axhline(0, color="k", lw=.5)
for v in (5.21, -5.21): ax[1].axhline(v, color="grey", ls="--", lw=.8)
ax[1].text(0.02, 5.4, "strip theory 6Γ/(πb²)", color="grey")
ax[0].set_title("2-33 follower 4 s behind a 2-33: roll vs same glider in clean air"); ax[0].set_ylabel("Δroll deg"); ax[0].set_xlabel("s")
ax[1].set_title("roll rate p (+ right wing down)"); ax[1].set_ylabel("deg/s"); ax[1].set_xlabel("s")
for a in ax: a.grid(alpha=.3); a.legend()
fig.tight_layout(); fig.savefig(os.path.join(out, "wake_follower_roll.png"), dpi=110); plt.close(fig)

# 3. Tow box.
d = rows("wake_tow_box.csv")
ys = sorted({float(x["dy_m"]) for x in d}); zs = sorted({float(x["dz_up_m"]) for x in d})
P = np.zeros((len(zs), len(ys))); S = np.zeros_like(P); U = np.zeros_like(P)
for x in d:
    i, j = zs.index(float(x["dz_up_m"])), ys.index(float(x["dy_m"]))
    P[i, j] = float(x["roll_accel_degs2"]); S[i, j] = float(x["extra_sink_m"]); U[i, j] = float(x["slowed_m"])
fig, ax = plt.subplots(1, 3, figsize=(17, 4.8))
ext = [ys[0] - .5, ys[-1] + .5, zs[0] - .5, zs[-1] + .5]
m = np.abs(P).max()
im = ax[0].imshow(P, origin="lower", extent=ext, cmap="RdBu_r", vmin=-m, vmax=m, aspect="auto"); fig.colorbar(im, ax=ax[0], label="deg/s² (+ right wing down)")
ax[0].set_title("2-33 roll acceleration, 61 m behind a Pawnee")
m = np.abs(S).max()
im = ax[1].imshow(S, origin="lower", extent=ext, cmap="PuOr_r", vmin=-m, vmax=m, aspect="auto"); fig.colorbar(im, ax=ax[1], label="m in 0.25 s (+ down)")
ax[1].set_title("extra sink")
m = np.abs(U).max()
im = ax[2].imshow(U, origin="lower", extent=ext, cmap="PuOr_r", vmin=-m, vmax=m, aspect="auto"); fig.colorbar(im, ax=ax[2], label="m in 0.25 s")
ax[2].set_title("slowed (slipstream drag)")
for a in ax: a.set_xlabel("lateral from wake centre m (+ right)"); a.set_ylabel("height above wake centre m"); a.axhline(0, color="k", lw=.3); a.axvline(0, color="k", lw=.3)
fig.suptitle("Boxing the wake: what the tug's lines and slipstream do to the whole glider placed at each point (emergent, from the glider's own strips)")
fig.tight_layout(); fig.savefig(os.path.join(out, "wake_tow_box.png"), dpi=110); plt.close(fig)

# 4. Lift → vortex.
d = rows("wake_gamma_vs_load.csv")
fig, ax = plt.subplots(figsize=(6, 4.5))
ax.plot([float(x["nz"]) for x in d], [float(x["gamma_m2s"]) for x in d], "o-")
ax.set_xlabel("load factor n"); ax.set_ylabel("Γ m²/s"); ax.grid(alpha=.3)
ax.set_title("No lift, no vortex: Γ = nW/(ρVb₀) (900 kg, 11 m, 30 m/s)")
fig.tight_layout(); fig.savefig(os.path.join(out, "wake_gamma_vs_load.png"), dpi=110); plt.close(fig)
print("wrote", out)
