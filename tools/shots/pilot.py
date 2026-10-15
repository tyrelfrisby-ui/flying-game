"""Closed-loop pilots: they move the stick, the rudder and the throttle. The sim decides what the airplane does.

Signs measured on the sim (2026-10-15): elevator NEGATIVE = pull (nose up, +g); aileron POSITIVE = roll right;
rudder POSITIVE = yaw right (drives beta down).
"""
import math
from aerofly import PID, clamp


class Pilot:
    def __init__(self):
        self.g = PID(0.045, 0.05, 0.004, lim=1.0, ilim=0.35)      # g command -> elevator
        self.bank = PID(0.030, 0.005, 0.010, lim=1.0, ilim=0.3)   # bank angle -> aileron (ki: p-factor made the
        #                                                         right turn sit 2 deg shallow than the left)
        self.yaw = PID(0.045, 0.02, 0.0, lim=0.6, ilim=0.25)      # sideslip -> rudder (coordination)
        self.thr = PID(0.020, 0.012, 0.0, lim=1.0, ilim=1.0)      # KIAS -> throttle
        self.pitch = PID(0.020, 0.010, 0.006, lim=1.0, ilim=0.4)  # pitch attitude -> elevator
        self.alpha = PID(0.035, 0.028, 0.004, lim=1.0, ilim=0.9)  # angle of attack -> elevator

    def seed_elevator(self, ele):
        """Start the g loop from the stick position that is ALREADY holding the condition.

        hold_g returns an absolute elevator command, so with zero error it returns -i: a freshly reset loop
        commands a CENTRED stick. In cruise that is near the trim point and harmless; at 55 KIAS, where the
        airplane is holding real back pressure, it drops the nose and the speed runs away."""
        self.g.i = max(-self.g.ilim, min(self.g.ilim, -ele))
        self.g.prev = None
        self.alpha.i = max(-self.alpha.ilim, min(self.alpha.ilim, -ele))
        self.alpha.prev = None

    def reset(self):
        for p in (self.g, self.bank, self.yaw, self.thr, self.pitch):
            p.reset()

    # --- inner loops -------------------------------------------------------
    def hold_g(self, st, g_cmd, dt):
        """Elevator for a commanded normal load factor. Pull is negative elevator."""
        return -self.g(g_cmd - st["loadFactor"], dt)

    def hold_bank(self, st, bank_cmd, dt):
        err = ((bank_cmd - st["roll"] + 180.0) % 360.0) - 180.0   # wrapped: inverted holds are +-180
        return self.bank(err, dt)

    def coordinate(self, st, dt):
        return self.yaw(st["beta"], dt)

    def hold_speed(self, st, kias_cmd, dt, base=0.6):
        return clamp(base + self.thr(kias_cmd - st["kias"], dt), 0.0, 1.0)

    def hold_pitch(self, st, pitch_cmd, dt):
        return -self.pitch(pitch_cmd - st["pitch"], dt)

    def hold_alpha(self, st, alpha_cmd, dt):
        """Elevator for a commanded angle of attack - how a stall recovery is actually flown."""
        return -self.alpha(alpha_cmd - st["alpha"], dt)

    # --- outer loops -------------------------------------------------------
    def level(self, st, alt_ft, dt, bank_cmd=0.0, vs_lim=400.0):
        """Altitude hold: the g a level turn needs (1/cos bank) plus a trim for height error."""
        vs_cmd = clamp(0.6 * (alt_ft - st["heightFt"]), -vs_lim, vs_lim)
        g_ff = 1.0 / max(0.25, math.cos(math.radians(clamp(st["roll"], -75, 75))))
        vs = -st["sinkFpm"]                      # sinkFpm is positive down
        g_cmd = clamp(g_ff + 0.0012 * (vs_cmd - vs), -0.5, 4.0)
        return g_cmd
