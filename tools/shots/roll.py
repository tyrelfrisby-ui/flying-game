"""The slow roll, flown Ty's way (2026-10-15).

His spec: "the slow roll requires top rudder while in knife edge (90 degrees of bank) and a lot of rudder in the
opposite direction to the roll while inverted to cancel the adverse yaw. while inverted it should target the same
angle of attack as level flight used before the roll began assuming a symmetrical airfoil. if fuselage lift and/or
beta limits preclude maintaining altitude while in knife edge the negative aoa used will have to be higher than
the positive to cancel the slight downward flightpath as the airplane rolls through inverted."

And: the figure is called a slow roll but the RATE may be anything - full aileron is fine. What matters is that
ALTITUDE AND HEADING hold throughout.
"""
import math
from aerofly import clamp


def wrap(d):
    while d > 180: d -= 360
    while d < -180: d += 360
    return d


class SlowRoll:
    def __init__(self, p, alpha_level, alt0, hdg0, aileron=0.85, direction=+1, vs_kias=None, alpha_stall=14.0,
                 k_top=0.75, k_adverse=0.22, k_hdg=0.010, neg_boost_gain=0.0018, kias_ref=130.0):
        self.p = p
        self.alpha_level = alpha_level     # the alpha level flight was using before the roll began
        self.alt0, self.hdg0 = alt0, hdg0
        self.ail = abs(aileron) * direction
        self.dir = direction
        self.k_top, self.k_adv, self.k_hdg = k_top, k_adverse, k_hdg
        self.neg_gain = neg_boost_gain
        self.kias_ref = kias_ref
        self.vs_kias, self.alpha_stall = vs_kias, alpha_stall
        self.rolled = 0.0
        self._prev = None

    def update_roll(self, st):
        if self._prev is None:
            self._prev = st["roll"]
        self.rolled += wrap(st["roll"] - self._prev) * self.dir
        self._prev = st["roll"]
        return self.rolled

    def controls(self, st, dt):
        r = math.radians(st["roll"])
        c, s = math.cos(r), math.sin(r)

        # ALPHA: the same angle of attack level flight used, carried round with the roll. Symmetrical airfoil,
        # so inverted wants the same magnitude with the sign flipped; cos(roll) walks it through zero at knife
        # edge, where the wing is making no vertical lift at all.
        # The angle of attack level flight needs AT THIS SPEED: alpha_1g = alpha_stall * (Vs/V)^2. Freezing the
        # instantaneous alpha at the moment the roll begins is wrong - entering straight out of the half loop's
        # exit it can be several degrees high, and the roll then commands that much alpha all the way round,
        # which is what made it snatch to 5 g.
        a_level = self.alpha_level
        if self.vs_kias:
            a_level = clamp(self.alpha_stall * (self.vs_kias / max(50.0, st["kias"])) ** 2, 0.3, 8.0)
        boost = 1.0
        if c < 0:
            # the knife-edge quarters lose a little height (fuselage lift and sideslip are finite), so the
            # NEGATIVE alpha is made larger than the positive to cancel the descent as it rolls through inverted
            boost = clamp(1.0 + self.neg_gain * (self.alt0 - st["heightFt"]), 1.0, 2.2)
        a_cmd = a_level * c * boost

        # RUDDER: top rudder at knife edge (opposite the bank, holding the nose up while the wing cannot), plus
        # an adverse-yaw term that is INTO the roll upright and OPPOSITE the roll inverted, and a slow trim on
        # heading so the figure finishes on the heading it started.
        top = -self.k_top * s
        adverse = self.k_adv * self.dir * c
        hdg_err = wrap(self.hdg0 - st["heading"])
        # the heading trim stays live at knife edge too: weighting it by |cos(roll)| switched it off exactly
        # where the nose wanders, and 1.5 rolls is an asymmetric figure whose yaw does not self-cancel
        rud = clamp(top + adverse + self.k_hdg * hdg_err * (0.45 + 0.55 * abs(c)), -1.0, 1.0)

        # The elevator is this much more effective as the speed rises, so the angle-of-attack loop is scaled by
        # dynamic pressure. Without it the same few degrees of overshoot that cost 2 g at 156 KIAS cost 5 g at
        # 185, and the roll looks like a snatch rather than a slow roll.
        q_scale = (self.kias_ref / max(60.0, st["kias"])) ** 2
        ele = -self.p.alpha((a_cmd - st["alpha"]) * q_scale, dt)
        return dict(elevator=clamp(ele, -1.0, 1.0), aileron=self.ail, rudder=rud, throttle=1.0)
