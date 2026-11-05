using UnityEngine;

namespace LeagueVR.Champions
{
    /// <summary>
    /// Hwei's first-person animations. His brush is a wand in the right hand, and every spell paints its own stroke with
    /// the bristles before it leaves them: Devastating Fire a cocked flick from over the shoulder, Severing Bolt a raised
    /// brush slashed down in a lightning zigzag, Molten Fissure a stroke dragged along the ground ahead, Fleeting Current
    /// a flowing wave, Pool of Reflection one circle drawn on the ground, Stirring Lights small fast loops held up high,
    /// Grim Visage a grin then a thrust, Gaze of the Abyss an eye with a jab for its pupil, Crushing Maw a bite (the brush
    /// the upper jaw, the left hand the lower), and Spiraling Despair both hands on the staff, pointed where he looks.
    /// Opening a subject flourishes the brush as the colour soaks in; Wash Brush shakes the paint off.
    /// Poses are laid out from his eyes (right, forward, up) for a 0.6 m arm and scaled by his own; each frame the right
    /// arm reaches for the grip that holds the brush where the move wants it, and the brush turns with the hand.
    /// </summary>
    public class HweiMoves
    {
        public enum Move { None, Flick, Backflick, Inspire, Wash, QQ, QW, QE, WQ, WW, WE, EQ, EW, EE, Ultimate }

        /// <summary>One of the nine subject spells: subject (0 Disaster, 1 Serenity, 2 Torment) and variant (0-2).</summary>
        public static Move Spell(int subject, int variant) => (Move)((int)Move.QQ + subject * 3 + variant);

        /// <summary>
        /// One pose of the brush: the right palm (right, forward, up from the eyes, metres for a 0.6 m arm), where the
        /// bristles point (degrees right of and below the move's forward) and how the hand is turned about the brush
        /// (0: the back of the hand to the right, as in a handshake; 90: palm down; -90: palm up).
        /// </summary>
        readonly struct Key
        {
            public readonly float t, r, f, u, yaw, pitch, roll;

            public Key(float t, float r, float f, float u, float yaw, float pitch, float roll = 0)
            {
                this.t = t;
                this.r = r;
                this.f = f;
                this.u = u;
                this.yaw = yaw;
                this.pitch = pitch;
                this.roll = roll;
            }

            public Key At(float time) => new(time, r, f, u, yaw, pitch, roll);
        }

        /// <summary>The left hand's place in a two-handed move: the palm (right, forward, up from the eyes) and its weight.</summary>
        readonly struct LeftKey
        {
            public readonly float t, r, f, u, w;

            public LeftKey(float t, float r, float f, float u, float w = 1)
            {
                this.t = t;
                this.r = r;
                this.f = f;
                this.u = u;
                this.w = w;
            }
        }

        /// <summary>How long a move lasts, when its spell leaves the brush (0-1) and when the bristles paint a stroke.</summary>
        readonly struct Spec
        {
            public readonly float seconds, release, strokeFrom, strokeTo;
            public readonly Key[] keys;
            public readonly LeftKey[] left;

            public Spec(float seconds, float release, float strokeFrom, float strokeTo, Key[] keys, LeftKey[] left = null)
            {
                this.seconds = seconds;
                this.release = release;
                this.strokeFrom = strokeFrom;
                this.strokeTo = strokeTo;
                this.keys = keys;
                this.left = left;
            }
        }

        // Every move, laid out within his reach (his shoulder is about 0.15 m behind and 0.17 m below his eyes, his arm
        // 0.51 m long): strokes in front keep the hand low and out ahead, raised ones go out to his right side so the arm
        // stays at the edge of the view, and the brush's short end stays at least 0.25 m from the eyes.
        static readonly Spec FlickSpec = new(.3f, .45f, .26f, .6f, new[]
        {
            new Key(.3f, .3f, .16f, -.2f, 30, -40, 15),
            new Key(.45f, .2f, .28f, -.3f, 0, 4),
            new Key(.62f, .18f, .26f, -.36f, -8, 14, -10),
        });

        static readonly Spec BackflickSpec = new(.3f, .45f, .26f, .6f, new[]
        {
            new Key(.3f, .04f, .24f, -.26f, -38, -18, 80),
            new Key(.45f, .19f, .28f, -.3f, 2, 4, 40),
            new Key(.62f, .28f, .24f, -.34f, 22, 12, 20),
        });

        static readonly Spec InspireSpec = new(.32f, 1, .15f, .8f, new[]
        {
            new Key(.35f, .24f, .24f, -.26f, 16, -32, 30),
            new Key(.6f, .27f, .24f, -.24f, 4, -48, 50),
            new Key(.78f, .22f, .26f, -.3f, -6, -12, 0),
        });

        static readonly Spec WashSpec = new(.48f, 1, 2, 2, new[]
        {
            new Key(.16f, .22f, .26f, -.28f, 0, -22),
            new Key(.32f, .22f, .28f, -.34f, 6, 34),
            new Key(.48f, .22f, .26f, -.28f, 0, -16),
            new Key(.64f, .22f, .28f, -.34f, -6, 34),
        });

        static readonly Spec[] Spells =
        {
            // QQ Devastating Fire: cocked high on his right, snapped forward at the target; the fireball leaves the tip.
            new(.42f, .5f, .28f, .64f, new[]
            {
                new Key(.3f, .32f, .1f, -.08f, 26, -48, 20),
                new Key(.5f, .19f, .28f, -.27f, 0, 2),
                new Key(.68f, .16f, .26f, -.34f, -6, 16, -10),
            }),
            // QW Severing Bolt: raised on his right pointing at the sky, a moment's gathering, then slashed down in a
            // lightning zigzag at the target.
            new(.62f, .55f, .38f, .62f, new[]
            {
                new Key(.28f, .3f, .14f, -.04f, 10, -62),
                new Key(.4f, .3f, .15f, -.02f, 6, -66),
                new Key(.46f, .25f, .22f, -.12f, 14, -38),
                new Key(.51f, .2f, .26f, -.2f, -8, -8),
                new Key(.55f, .18f, .28f, -.26f, 4, 22),
                new Key(.72f, .17f, .27f, -.32f, 0, 30),
            }),
            // QE Molten Fissure: the bristles dragged along the ground ahead, then flicked up as the lava erupts.
            new(.6f, .5f, .22f, .56f, new[]
            {
                new Key(.25f, .2f, .24f, -.38f, 0, 58, 70),
                new Key(.42f, .17f, .28f, -.38f, 0, 38, 70),
                new Key(.5f, .15f, .29f, -.34f, 0, 18, 50),
                new Key(.64f, .18f, .26f, -.24f, 0, -25, 20),
            }),
            // WQ Fleeting Current: a flowing wave from left to right, ending along the current.
            new(.55f, .6f, .15f, .62f, new[]
            {
                new Key(.18f, .04f, .26f, -.32f, -45, 22, 80),
                new Key(.3f, .1f, .28f, -.28f, -15, 8, 80),
                new Key(.42f, .2f, .28f, -.34f, 18, 26, 80),
                new Key(.52f, .24f, .27f, -.28f, 30, 10, 80),
                new Key(.6f, .19f, .28f, -.3f, 0, 14, 40),
            }),
            // WW Pool of Reflection: one wide circle drawn on the ground ahead (procedural, see Shape).
            new(.62f, .58f, .1f, .62f, new[]
            {
                new Key(.12f, .18f, .27f, -.3f, 0, 26, 60),
                new Key(.58f, .18f, .27f, -.3f, 0, 26, 60),
                new Key(.7f, .18f, .26f, -.33f, 0, 22, 30),
            }),
            // WE Stirring Lights: small fast loops with the brush held up high; each loop lets a light go.
            new(.6f, .78f, .1f, .66f, new[]
            {
                new Key(.1f, .22f, .24f, -.22f, 0, -40, 30),
                new Key(.66f, .22f, .24f, -.22f, 0, -40, 30),
                new Key(.8f, .22f, .26f, -.28f, 0, -10, 10),
            }),
            // EQ Grim Visage: a grin drawn left to right, then a thrust that sends the face.
            new(.5f, .52f, .12f, .45f, new[]
            {
                new Key(.15f, .06f, .26f, -.2f, -32, 4, 80),
                new Key(.28f, .14f, .28f, -.27f, -10, 18, 80),
                new Key(.4f, .24f, .27f, -.2f, 24, 2, 80),
                new Key(.52f, .2f, .3f, -.25f, 0, 4, 40),
                new Key(.68f, .18f, .27f, -.3f, 0, 12, 20),
            }),
            // EW Gaze of the Abyss: an eye (upper lid, lower lid) and a jab for its pupil.
            new(.6f, .8f, .08f, .66f, new[]
            {
                new Key(.1f, .08f, .26f, -.22f, -28, 6, 80),
                new Key(.22f, .14f, .27f, -.18f, -2, -8, 80),
                new Key(.34f, .22f, .26f, -.22f, 26, 6, 80),
                new Key(.46f, .14f, .27f, -.28f, 0, 18, 80),
                new Key(.58f, .08f, .26f, -.22f, -26, 6, 80),
                new Key(.7f, .15f, .27f, -.24f, 0, 6, 80),
                new Key(.8f, .15f, .31f, -.24f, 0, 6, 80),
            }),
            // EE Crushing Maw: the brush rises on his right as the upper jaw, the left hand drops as the lower, and they
            // bite together in front.
            new(.66f, .58f, .46f, .6f, new[]
            {
                new Key(.28f, .26f, .2f, -.12f, 0, 36, 85),
                new Key(.45f, .26f, .22f, -.1f, 0, 40, 85),
                new Key(.58f, .16f, .29f, -.26f, 0, 12, 85),
                new Key(.75f, .17f, .27f, -.3f, 0, 14, 60),
            }, new[]
            {
                new LeftKey(0, -.12f, .2f, -.44f, 0),
                new LeftKey(.28f, -.1f, .2f, -.44f),
                new LeftKey(.45f, -.1f, .21f, -.46f),
                new LeftKey(.58f, -.04f, .25f, -.32f),
                new LeftKey(.75f, -.08f, .23f, -.38f),
                new LeftKey(1, -.12f, .2f, -.42f, 0),
            }),
        };

        /// <summary>How far along the brush from the right palm the left hand holds it for the ultimate (model units).</summary>
        const float StaffHold = .22f;

        readonly PlayerChampion player;
        readonly ChampionVRAvatar avatar;

        Move move;
        Spec spec;
        float start;
        Vector3 forward;
        Vector3 palmInGrip, leftPalmInGrip;
        bool measured;

        // Spiraling Despair: how far both hands have come up to aim the staff (0-1), where it aims, and the pose it fired from.
        float ultimate;
        Vector3 ultimateTarget;
        bool ultimateHeld;
        Pose firedRight, firedLeft;
        Vector3 firedDirection;

        public HweiMoves(PlayerChampion player, ChampionVRAvatar avatar)
        {
            this.player = player;
            this.avatar = avatar;
        }

        public Move Current => Clock >= 0 ? move : Move.None;

        /// <summary>Seconds into the current move, or -1 when there is none.</summary>
        public float Clock
        {
            get
            {
                if (move == Move.None)
                    return -1;
                float clock = Time.time - start;
                if (clock < spec.seconds)
                    return clock;
                move = Move.None;
                return -1;
            }
        }

        /// <summary>How far through the move (0-1), or -1.</summary>
        public float Progress => Clock >= 0 ? Clock / spec.seconds : -1;

        /// <summary>Seconds from the start of a move until its spell leaves the brush.</summary>
        public static float ReleaseAfter(Move m) => SpecOf(m).seconds * SpecOf(m).release;

        /// <summary>How far through a move (0-1) its spell or paint leaves the brush.</summary>
        public static float ReleaseFraction(Move m) => SpecOf(m).release;

        /// <summary>True while a spell or an attack is being painted and has not left the brush yet.</summary>
        public bool Casting
        {
            get
            {
                float p = Progress;
                return p >= 0 && move is not (Move.Inspire or Move.Wash or Move.Ultimate) && p < spec.release;
            }
        }

        /// <summary>True while the bristles paint the move's stroke (the brush trails paint).</summary>
        public bool Stroking
        {
            get
            {
                float p = Progress;
                return p >= 0 && p >= spec.strokeFrom && p <= spec.strokeTo;
            }
        }

        /// <summary>The ultimate's two-handed aim, 0 (hands free) to 1 (both hands on the staff, pointed).</summary>
        public float UltimateWeight => ultimate;

        static float Smooth(float x) => x * x * (3 - 2 * x);

        static Spec SpecOf(Move m) => m switch
        {
            Move.Flick => FlickSpec,
            Move.Backflick => BackflickSpec,
            Move.Inspire => InspireSpec,
            Move.Wash => WashSpec,
            Move.Ultimate => UltimateSpec,
            >= Move.QQ and <= Move.EE => Spells[m - Move.QQ],
            _ => FlickSpec,
        };

        // The ultimate's fire: a short push of both hands along the staff, held, then back to the controllers.
        static readonly Spec UltimateSpec = new(.5f, .1f, 2, 2, new Key[0]);

        public void Stop()
        {
            move = Move.None;
            ultimateHeld = false;
            ultimate = 0;
        }

        /// <summary>Starts a move toward <paramref name="toward"/> (a direction; flattened to the ground).</summary>
        public void Begin(Move kind, Vector3 toward, float seconds = 0)
        {
            if (Clock < 0 && ultimate <= 0)
                MeasurePalms();
            spec = SpecOf(kind);
            if (seconds > 0)
                spec = new Spec(seconds, spec.release, spec.strokeFrom, spec.strokeTo, spec.keys, spec.left);
            move = kind;
            start = Time.time;
            forward = player.PlanarDirection(toward);
        }

        /// <summary>Where each palm sits on its controller grip, measured while both hands follow the controllers.</summary>
        public void MeasurePalms()
        {
            if (!avatar || avatar.Rig == null || !avatar.Rig.Valid)
                return;
            var right = avatar.RightGrip();
            var left = avatar.LeftGrip();
            palmInGrip = Quaternion.Inverse(right.rotation) * (avatar.Palm(false) - right.position);
            leftPalmInGrip = Quaternion.Inverse(left.rotation) * (avatar.Palm(true) - left.position);
            measured = true;
        }

        // ---------- Frame of reference ----------

        Vector3 Eyes => player.head.transform.position;
        Vector3 Right => Vector3.Cross(Vector3.up, forward).normalized;

        /// <summary>His arm length relative to the 0.51 m these poses were laid out for (his own at a 1.75 m player).</summary>
        float Reach
        {
            get
            {
                var rig = avatar ? avatar.Rig : null;
                if (rig == null || !rig.Valid)
                    return 1;
                var arm = rig.RightArm;
                float reach = Vector3.Distance(arm.upper.position, arm.lower.position) + Vector3.Distance(arm.lower.position, arm.end.position);
                return Mathf.Clamp(reach / .513f, .7f, 1.5f);
            }
        }

        Vector3 At(float r, float f, float u) => Eyes + (Right * r + forward * f + Vector3.up * u) * Reach;

        /// <summary>A direction turned <paramref name="yaw"/> to the right of the move's forward and <paramref name="pitch"/> below it.</summary>
        Vector3 Dir(float yaw, float pitch) => Quaternion.AngleAxis(yaw, Vector3.up) * Quaternion.AngleAxis(pitch, Right) * forward;

        /// <summary>World length of a distance along the brush given in its own units.</summary>
        float Along(float model) => model * (avatar && avatar.Weapon != null ? avatar.Weapon.scale : .75f) * avatar.BodyScale;

        /// <summary>
        /// The right grip that puts the palm at <paramref name="palm"/> with the bristles along <paramref name="dir"/>, the
        /// back of the hand turned <paramref name="roll"/> degrees up from the right. The inverse of the avatar's hand frame
        /// (bristles along the handle tilted by the prop's tilt, the back of the hand as the frame's up).
        /// </summary>
        Pose BrushGrip(Vector3 palm, Vector3 dir, float roll)
        {
            dir.Normalize();
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            if (side.sqrMagnitude < 1e-4f)
                side = Right;
            side.Normalize();
            Vector3 over = Vector3.Cross(dir, side);
            float a = roll * Mathf.Deg2Rad;
            Vector3 back = side * Mathf.Cos(a) + over * Mathf.Sin(a);
            var tilt = Quaternion.Euler(avatar.PropTilt(avatar.Weapon));
            var rotation = Quaternion.LookRotation(dir, Vector3.Cross(dir, back)) * Quaternion.Inverse(tilt);
            return new Pose(palm - rotation * palmInGrip, rotation);
        }

        Pose KeyGrip(Key k) => BrushGrip(At(k.r, k.f, k.u), Dir(k.yaw, k.pitch), k.roll);

        static Pose Mix(Pose a, Pose b, float w) => new(Vector3.Lerp(a.position, b.position, w), Quaternion.Slerp(a.rotation, b.rotation, w));

        static float CatmullRom(float a, float b, float c, float d, float u) =>
            .5f * (2 * b + (-a + c) * u + (2 * a - 5 * b + 4 * c - d) * u * u + (-a + 3 * b - 3 * c + d) * u * u * u);

        /// <summary>The brush pose a key list gives at progress <paramref name="p"/>, through the keys on a smooth curve.</summary>
        static Key Between(Key[] keys, float p)
        {
            int i = 0;
            while (i < keys.Length - 2 && p > keys[i + 1].t)
                i++;
            var b = keys[i];
            var c = keys[i + 1];
            var a = i > 0 ? keys[i - 1] : b;
            var d = i + 2 < keys.Length ? keys[i + 2] : c;
            float u = Mathf.Clamp01((p - b.t) / Mathf.Max(1e-4f, c.t - b.t));
            return new Key(p, CatmullRom(a.r, b.r, c.r, d.r, u), CatmullRom(a.f, b.f, c.f, d.f, u), CatmullRom(a.u, b.u, c.u, d.u, u),
                CatmullRom(a.yaw, b.yaw, c.yaw, d.yaw, u), CatmullRom(a.pitch, b.pitch, c.pitch, d.pitch, u), Mathf.Lerp(b.roll, c.roll, Smooth(u)));
        }

        /// <summary>When Stirring Lights' loops let each light go (progress through the move).</summary>
        public static readonly float[] LightsAt = { .3f, .46f, .62f };

        /// <summary>
        /// The keys bent by the procedural moves: the pool's circle (one turn, from and back to its start) and the stirring
        /// loops (two turns). Both start and end on their keys, so nothing jumps.
        /// </summary>
        Key Shape(Key k, float p)
        {
            if (move == Move.WW && p > .12f && p < .58f)
            {
                float a = (p - .12f) / .46f * Mathf.PI * 2;
                float c = 1 - Mathf.Cos(a);
                return new Key(p, k.r + .03f * Mathf.Sin(a), k.f, k.u - .01f * c, k.yaw + 26 * Mathf.Sin(a), k.pitch + 9 * c, k.roll);
            }
            if (move == Move.WE && p > .1f && p < .66f)
            {
                float a = (p - .1f) / .56f * Mathf.PI * 2 * 2;
                float c = Mathf.Cos(a) - 1;
                return new Key(p, k.r + .02f * Mathf.Sin(a), k.f, k.u + .02f * c, k.yaw + 11 * Mathf.Sin(a), k.pitch + 10 * c, k.roll);
            }
            return k;
        }

        /// <summary>The right grip during a move: from the controller into the first key, along the keys, back out.</summary>
        Pose RightGrip(Pose controller, float p)
        {
            var keys = spec.keys;
            if (keys.Length == 0)
                return controller;
            var first = keys[0];
            var last = keys[keys.Length - 1];
            if (p <= first.t)
                return Mix(controller, KeyGrip(Shape(first, first.t)), Smooth(p / Mathf.Max(1e-4f, first.t)));
            if (p >= last.t)
                return Mix(KeyGrip(Shape(last, last.t)), controller, Smooth((p - last.t) / Mathf.Max(1e-4f, 1 - last.t)));
            return KeyGrip(Shape(keys.Length == 1 ? first : Between(keys, p), p));
        }

        // ---------- Spiraling Despair: both hands on the staff ----------

        /// <summary>
        /// While R is held both hands come up to the staff and point it where he looks (<paramref name="target"/>), over
        /// 0.22 s; letting go of R without firing lowers them. Call once a frame.
        /// </summary>
        public void HoldUltimate(bool held, Vector3 target)
        {
            if (held && !ultimateHeld && Clock < 0 && ultimate <= 0)
                MeasurePalms();
            ultimateHeld = held;
            ultimateTarget = target;
            ultimate = Mathf.MoveTowards(ultimate, held ? 1 : 0, Time.deltaTime / (held ? .22f : .3f));
        }

        /// <summary>The ultimate fires: both hands push along the staff, hold a moment and go back to the controllers.</summary>
        public void FireUltimate()
        {
            var right = UltimateRight(out var dir);
            firedRight = right;
            firedLeft = UltimateLeft(right, dir);
            firedDirection = dir;
            ultimateHeld = false;
            ultimate = 0;
            Begin(Move.Ultimate, dir);
        }

        /// <summary>
        /// The staff's aim from the right palm toward the target (where he looks): the right hand drawn in by his chest
        /// like a rifle's stock, so the left can reach out along the staff.
        /// </summary>
        Pose UltimateRight(out Vector3 dir)
        {
            var head = player.head.transform;
            forward = player.PlanarDirection(head.forward);
            Vector3 palm = At(.08f, .12f, -.26f);
            dir = ultimateTarget - palm;
            if (dir.sqrMagnitude < .01f)
                dir = head.forward;
            dir.Normalize();
            return BrushGrip(palm, dir, 30);
        }

        /// <summary>The left palm on the staff, under it a third of the way to the bristles, holding it up toward the target.</summary>
        Pose UltimateLeft(Pose right, Vector3 dir)
        {
            Vector3 palm = right.position + right.rotation * palmInGrip;
            Vector3 side = Vector3.Cross(Vector3.up, dir);
            if (side.sqrMagnitude < 1e-4f)
                side = Right;
            Vector3 up = Vector3.Cross(dir, side.normalized).normalized;
            Vector3 onStaff = palm + dir * Along(StaffHold) - up * .03f * avatar.BodyScale;
            // Left grip space: +X is the palm's normal, +Z runs along the thumb; the palm faces up into the staff.
            var rotation = Quaternion.LookRotation(dir, Vector3.Cross(dir, up));
            return new Pose(onStaff - rotation * leftPalmInGrip, rotation);
        }

        // ---------- What the avatar asks for each frame ----------

        /// <summary>
        /// The grip each arm reaches for: the right hand carries the brush through the move (or up into the ultimate's
        /// aim), the left joins for Crushing Maw and Spiraling Despair; otherwise the controllers.
        /// </summary>
        public Pose Grip(bool left, Pose grip)
        {
            if (!avatar || !measured || avatar.Weapon == null || player.HandStowed(false) || (left && player.HandStowed(true)))
                return grip;
            float clock = Clock;
            if (clock >= 0 && move == Move.Ultimate)
                return Fired(left, grip, clock);
            if (clock < 0 || move == Move.None)
            {
                if (ultimate <= 0)
                    return grip;
                var aim = UltimateRight(out var dir);
                var w = Smooth(ultimate);
                return left ? Mix(grip, UltimateLeft(aim, dir), w) : Mix(grip, aim, w);
            }
            float p = clock / spec.seconds;
            if (!left)
                return RightGrip(grip, p);
            return spec.left != null ? LeftGrip(grip, p) : grip;
        }

        Pose Fired(bool left, Pose grip, float clock)
        {
            var held = left ? firedLeft : firedRight;
            float push = clock < .06f ? Smooth(clock / .06f) : clock < .2f ? 1 - Smooth((clock - .06f) / .14f) * .6f : .4f * (1 - Smooth(Mathf.Clamp01((clock - .2f) / .1f)));
            held.position += firedDirection * .05f * push;
            float back = Smooth(Mathf.Clamp01((clock - .22f) / (spec.seconds - .22f)));
            return Mix(held, grip, back);
        }

        Pose LeftGrip(Pose grip, float p)
        {
            var keys = spec.left;
            int i = 0;
            while (i < keys.Length - 2 && p > keys[i + 1].t)
                i++;
            var a = keys[i];
            var b = keys[i + 1];
            float u = Smooth(Mathf.Clamp01((p - a.t) / Mathf.Max(1e-4f, b.t - a.t)));
            Vector3 palm = Vector3.Lerp(At(a.r, a.f, a.u), At(b.r, b.f, b.u), u);
            float w = Mathf.Lerp(a.w, b.w, u);
            // The lower jaw: palm up, fingers pointing ahead.
            var rotation = Quaternion.LookRotation(forward, -Right);
            return Mix(grip, new Pose(palm - rotation * leftPalmInGrip, rotation), Smooth(w));
        }

        /// <summary>The fingers a move gives: a claw for the maw's lower jaw, curled under the staff for the ultimate.</summary>
        public HandCurl? Fingers(bool left)
        {
            if (!left)
                return null;
            float clock = Clock;
            if (clock >= 0 && move == Move.EE)
                return new HandCurl(.45f, .55f, .6f, .62f, .65f);
            if ((clock >= 0 && move == Move.Ultimate && clock < .3f) || ultimate > .5f)
                return new HandCurl(.35f, .5f, .55f, .55f, .55f);
            return null;
        }

        /// <summary>Where the stroke's paint comes off the bristles (in the brush's own space): the middle of the bristles.</summary>
        public static Vector3 PaintPoint => new(0, .01f, -.78f);

        /// <summary>The bristle tip (in the brush's own space), where spells leave the brush.</summary>
        public static Vector3 TipPoint => new(0, .003f, -.95f);

        /// <summary>The direction the ultimate fired along (valid during <see cref="Move.Ultimate"/>).</summary>
        public Vector3 FiredDirection => firedDirection;
    }
}
