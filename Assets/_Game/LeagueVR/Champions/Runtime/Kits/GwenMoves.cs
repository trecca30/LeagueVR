using UnityEngine;

namespace LeagueVR.Champions
{
    /// <summary>
    /// Gwen's first-person animations, after League's own clips: her basic attacks (Attack1's thrust, Attack2's sweep,
    /// Attack3's backhand, a lunge for a critical strike), Snip Snip! with both hands on the scissors' handles, Hallowed
    /// Mist's flourish (the scissors twirl while the mist blooms from her left palm), Skip 'n Slash's dash with both hands
    /// ready and the two-handed snips while it empowers her, and Needlework's throw (the scissors snip the thread).
    /// Poses are laid out from her eyes and scaled by her arm length; the blades stay below the eye line and away from
    /// the eyes. Each frame the avatar poses the scissors and both arms from these.
    /// </summary>
    public class GwenMoves
    {
        public enum Move { None, Thrust, Sweep, Backhand, Lunge, Snip, Mist, Dash, TwinSnip, Throw }

        /// <summary>A basic attack's hit lands at this fraction of its swing (and a two-handed snip's at its snap).</summary>
        public const float Strike = .5f, TwinStrike = .5f;
        /// <summary>Snip Snip!: the hands join on the scissors for this long before the first snip.</summary>
        public const float SnipSetup = .1f;
        /// <summary>Needlework's flick: the needles leave the fingers this long into it.</summary>
        public const float ThrowRelease = .1f;

        // The scissors in their own (root) space, unscaled: the blades' pivot, the right fist on blade A's handle, the
        // middle of blade B's handle ring (where the left hand takes hold) and the blade tips. Measured on the model.
        static readonly Vector3 Pivot = new(0, -.022f, .08f), RingB = new(-.07f, .1f, -.32f);
        const float TipLength = 1.4f;

        readonly PlayerChampion player;
        readonly GwenAvatar avatar;

        Move move;
        float start, duration, snipInterval;
        int snips;
        Vector3 forward;
        Vector3 leftPalmInGrip;

        public GwenMoves(PlayerChampion player, GwenAvatar avatar)
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
                if (clock < duration)
                    return clock;
                move = Move.None;
                return -1;
            }
        }

        /// <summary>How far through the move (0-1), or -1.</summary>
        public float Progress => Clock >= 0 ? Clock / duration : -1;

        static float Smooth(float x) => x * x * (3 - 2 * x);

        public void Stop() => move = Move.None;

        void Begin(Move kind, float seconds, Vector3 toward)
        {
            // Measure where the left palm sits on its controller while it is still free.
            if (!UsesLeftHand(move) || Clock < 0)
                MeasureLeftPalm();
            move = kind;
            start = Time.time;
            duration = seconds;
            forward = player.PlanarDirection(toward);
        }

        void MeasureLeftPalm()
        {
            if (!avatar || avatar.Rig == null || !avatar.Rig.Valid)
                return;
            var grip = avatar.LeftGrip();
            leftPalmInGrip = Quaternion.Inverse(grip.rotation) * (avatar.Palm(true) - grip.position);
        }

        /// <summary>A basic swing at a target: League's Attack1, Attack2 or Attack3 by turn, the lunge for a critical strike.</summary>
        public void StartSwing(Move kind, float seconds, Vector3 toward) => Begin(kind, seconds, toward);

        /// <summary>Snip Snip!: both hands take the scissors in front of her and snip <paramref name="count"/> times.</summary>
        public void StartSnip(int count, float snipping, Vector3 toward)
        {
            snips = Mathf.Max(1, count);
            snipInterval = snips > 1 ? snipping / (snips - 1) : snipping;
            Begin(Move.Snip, SnipSetup + snipping + .2f, toward);
        }

        public void StartMist(Vector3 toward) => Begin(Move.Mist, .7f, toward);

        /// <summary>Skip 'n Slash: both hands ready on the scissors for the dash.</summary>
        public void StartDash(float seconds, Vector3 toward) => Begin(Move.Dash, seconds, toward);

        public void StartTwinSnip(float seconds, Vector3 toward) => Begin(Move.TwinSnip, seconds, toward);

        /// <summary>
        /// Needlework: the scissors snip the thread as the needles go; for a plain release (<paramref name="flick"/>) the left
        /// hand also flicks them out along <paramref name="aim"/>. A real throw keeps the hand where the player threw it.
        /// </summary>
        public void StartThrow(Vector3 aim, bool flick)
        {
            throwAim = aim.normalized;
            throwFlick = flick;
            Begin(Move.Throw, .3f, aim);
        }

        Vector3 throwAim;
        bool throwFlick;

        static bool UsesLeftHand(Move m) => m is Move.Snip or Move.Dash or Move.TwinSnip or Move.Mist or Move.Throw;
        static bool TwoHanded(Move m) => m is Move.Snip or Move.Dash or Move.TwinSnip;
        static bool MovesScissors(Move m) => m is not (Move.None or Move.Throw);

        // ---------- Frame of reference ----------

        Vector3 Eyes => player.head.transform.position;
        Vector3 Right => Vector3.Cross(Vector3.up, forward).normalized;

        /// <summary>Her arm length relative to the 0.56 m these poses were laid out for.</summary>
        float Reach
        {
            get
            {
                var rig = avatar ? avatar.Rig : null;
                if (rig == null || !rig.Valid)
                    return 1;
                var arm = rig.RightArm;
                float reach = Vector3.Distance(arm.upper.position, arm.lower.position) + Vector3.Distance(arm.lower.position, arm.end.position);
                return Mathf.Clamp(reach / .56f, .7f, 1.5f);
            }
        }

        /// <summary>A point from the eyes: right, forward, up (metres for her usual arm length).</summary>
        Vector3 At(float r, float f, float u) => Eyes + (Right * r + forward * f + Vector3.up * u) * Reach;

        /// <summary>The scissors pointing along a direction turned <paramref name="yaw"/> to the right and <paramref name="pitch"/> down
        /// from forward, the blades opening in a vertical plane (roll 0) or a horizontal one (roll 90).</summary>
        Quaternion Aim(float yaw, float pitch, float roll)
        {
            var dir = Quaternion.AngleAxis(yaw, Vector3.up) * Quaternion.AngleAxis(pitch, Right) * forward;
            return Quaternion.LookRotation(dir, Vector3.up) * Quaternion.Euler(0, 0, roll);
        }

        /// <summary>The scissors' root placed so the right fist (their handle) is at <paramref name="fist"/>.</summary>
        Pose Held(Vector3 fist, Quaternion rotation) => new(fist - rotation * (avatar.scissorsHandle * avatar.WeaponScale), rotation);

        /// <summary>The scissors as the right controller holds them (no animation).</summary>
        Pose Controller()
        {
            var grip = avatar.RightGrip();
            var weapon = new Pose(grip.position, grip.rotation * Quaternion.Euler(avatar.scissorsTilt));
            return Held(weapon.position, weapon.rotation);
        }

        /// <summary>
        /// Between two holds of the scissors: the fist moves straight from one to the other and the scissors turn about it
        /// (blending their roots instead swung the fist toward the face when the scissors turned from pointing up to ahead).
        /// </summary>
        Pose Blend(Pose a, Pose b, float w)
        {
            Vector3 handle = avatar.scissorsHandle * avatar.WeaponScale;
            Vector3 fist = Vector3.Lerp(a.position + a.rotation * handle, b.position + b.rotation * handle, w);
            var rotation = Quaternion.Slerp(a.rotation, b.rotation, w);
            return new Pose(fist - rotation * handle, rotation);
        }

        // ---------- Keys ----------

        readonly struct Key
        {
            public readonly float r, f, u, yaw, pitch, roll, open;

            public Key(float r, float f, float u, float yaw, float pitch, float roll, float open)
            {
                this.r = r;
                this.f = f;
                this.u = u;
                this.yaw = yaw;
                this.pitch = pitch;
                this.roll = roll;
                this.open = open;
            }
        }

        // Wind-up, strike and follow-through of each basic attack (fist from the eyes, where the blades point, the blade
        // opening in degrees): read off League's Attack1-3 and Crit, laid out to stay within reach and keep the blades
        // under the eye line. The blades snap shut on the strike.
        static readonly Key[] ThrustKeys = { new(.22f, .06f, -.42f, 5, 15, 0, 24), new(.12f, .32f, -.36f, 0, 4, 0, 0), new(.12f, .28f, -.38f, 0, 6, 0, 8) };
        static readonly Key[] SweepKeys = { new(.36f, .18f, -.38f, 65, 10, 90, 24), new(.08f, .32f, -.38f, 0, 6, 90, 0), new(-.12f, .28f, -.4f, -45, 8, 90, 6) };
        static readonly Key[] BackhandKeys = { new(-.05f, .24f, -.46f, -55, 12, 90, 24), new(.1f, .32f, -.42f, 0, 6, 90, 0), new(.3f, .2f, -.42f, 45, 8, 90, 6) };
        static readonly Key[] LungeKeys = { new(.2f, .04f, -.4f, 5, 20, 0, 30), new(.1f, .36f, -.34f, 0, 2, 0, 0), new(.1f, .32f, -.36f, 0, 4, 0, 20) };

        Key[] Keys(Move m) => m switch
        {
            Move.Thrust => ThrustKeys,
            Move.Sweep => SweepKeys,
            Move.Backhand => BackhandKeys,
            _ => LungeKeys,
        };

        Pose KeyPose(Key k) => Held(At(k.r, k.f, k.u), Aim(k.yaw, k.pitch, k.roll));

        /// <summary>A basic swing: wind up (to 0.35), strike (to 0.5, speeding up), follow through (to 0.7), back to the hand.</summary>
        void Swing(float p, out Pose pose, out float open)
        {
            var keys = Keys(move);
            var hand = Controller();
            if (p < .35f)
            {
                float u = Smooth(p / .35f);
                pose = Blend(hand, KeyPose(keys[0]), u);
                open = Mathf.Lerp(2, keys[0].open, u);
            }
            else if (p < Strike)
            {
                float u = (p - .35f) / (Strike - .35f);
                u *= u;
                pose = Blend(KeyPose(keys[0]), KeyPose(keys[1]), u);
                // Held open until the last instant, then snapped shut on the strike.
                open = keys[0].open * (1 - Smooth(Mathf.Clamp01((u - .6f) / .4f)));
            }
            else if (p < .7f)
            {
                float u = 1 - Mathf.Pow(1 - (p - Strike) / (.7f - Strike), 2);
                pose = Blend(KeyPose(keys[1]), KeyPose(keys[2]), u);
                // The lunge snips twice.
                open = move == Move.Lunge ? keys[2].open * Mathf.Sin(u * Mathf.PI) : keys[2].open * u;
            }
            else
            {
                float u = Smooth((p - .7f) / .3f);
                pose = Blend(KeyPose(keys[2]), hand, u);
                open = Mathf.Lerp(keys[2].open, 2, u);
            }
        }

        /// <summary>Both hands' hold in front of the chest: the scissors level, pointing ahead, the blades opening sideways.</summary>
        Pose TwoHandHold(float push) => Held(At(.06f, .26f + push, -.42f), Aim(0, 8, 90));

        /// <summary>Snip Snip!: the blades open wide between snips and snap shut on each, the last one pushed forward.</summary>
        void Snip(float clock, out Pose pose, out float open, out float hands)
        {
            float setup = SnipSetup, release = duration - .2f;
            float w = clock < setup ? Smooth(clock / setup) : clock > release ? 1 - Smooth((clock - release) / .2f) : 1;
            hands = w;
            float since = clock - setup;
            int k = since < 0 ? -1 : Mathf.Min(snips - 1, Mathf.FloorToInt(since / Mathf.Max(.01f, snipInterval) + .001f));
            float nextSnip = setup + (k + 1) * snipInterval;
            bool last = k >= snips - 1;
            float push = last ? .1f * Smooth(Mathf.Clamp01((clock - (setup + k * snipInterval)) / .08f)) : 0;
            if (since < 0)
                open = 26 * w;
            else if (last)
                open = 0;
            else
            {
                // Reopen after a snip, hold, and close in the last part before the next.
                float sinceSnip = clock - (setup + k * snipInterval), toNext = nextSnip - clock;
                float closing = Mathf.Min(.045f, snipInterval * .4f), opening = Mathf.Min(.07f, snipInterval * .5f);
                open = 26 * Mathf.Min(Smooth(Mathf.Clamp01(sinceSnip / opening)), Smooth(Mathf.Clamp01(toNext / closing)));
            }
            pose = Blend(Controller(), TwoHandHold(push), w);
        }

        /// <summary>
        /// Hallowed Mist's flourish: the scissors rise to point up and ahead on her right, open wide and turn a full circle
        /// about their own length, then snap shut as the mist settles; her left palm opens upward and the mist blooms from it.
        /// </summary>
        void Mist(float clock, out Pose pose, out float open)
        {
            float p = clock / duration;
            float w = p < .2f ? Smooth(p / .2f) : p > .8f ? 1 - Smooth((p - .8f) / .2f) : 1;
            float spin = 360 * Smooth(Mathf.Clamp01((p - .15f) / .6f));
            // Out on her right and low enough that the forearm stays at the edge of the view.
            var raised = Held(At(.3f, .25f, -.24f), Aim(12, -55, spin));
            pose = Blend(Controller(), raised, w);
            open = p < .72f ? 30 * Smooth(Mathf.Clamp01((p - .1f) / .15f)) : 30 * (1 - Smooth(Mathf.Clamp01((p - .72f) / .06f)));
        }

        /// <summary>Skip 'n Slash: both hands on the scissors, held open and ready for the slash at the end of the dash.</summary>
        void Dash(float clock, out Pose pose, out float open, out float hands)
        {
            float w = Smooth(Mathf.Clamp01(clock / .08f));
            hands = w;
            pose = Blend(Controller(), TwoHandHold(.04f), w);
            open = 26 * w;
        }

        /// <summary>The empowered attack: both hands on the scissors, a quick open and snap at the target, then let go.</summary>
        void TwinSnip(float clock, out Pose pose, out float open, out float hands)
        {
            float p = clock / duration;
            float w = p < .2f ? Smooth(p / .2f) : p > .8f ? 1 - Smooth((p - .8f) / .2f) : 1;
            hands = w;
            float push = .1f * Smooth(Mathf.Clamp01((p - .3f) / .2f));
            pose = Blend(Controller(), TwoHandHold(push), w);
            open = p < TwinStrike ? 28 * Smooth(Mathf.Clamp01(p / .3f)) * (1 - Smooth(Mathf.Clamp01((p - .4f) / .1f))) : 0;
        }

        // ---------- What the avatar asks for each frame ----------

        /// <summary>The scissors' root pose during a move, or null while they are held by the right controller.</summary>
        public Pose? Scissors()
        {
            float clock = Clock;
            if (clock < 0 || !MovesScissors(move) || !avatar || player.HandStowed(false))
                return null;
            Evaluate(clock, out var pose, out _, out _);
            return pose;
        }

        /// <summary>The blade opening during a move (degrees), or null.</summary>
        public float? BladeOpen()
        {
            float clock = Clock;
            if (clock < 0)
                return null;
            if (move == Move.Throw)
            {
                // The scissors snip the thread as the needles go.
                float p = clock / duration;
                return 26 * Mathf.Sin(Mathf.Clamp01(p / .45f) * Mathf.PI);
            }
            Evaluate(clock, out _, out float open, out _);
            return open;
        }

        /// <summary>True while a move swings the scissors fast enough to trail light.</summary>
        public bool Trailing
        {
            get
            {
                float p = Progress;
                return p >= 0 && move switch
                {
                    Move.Thrust or Move.Sweep or Move.Backhand or Move.Lunge => p > .3f && p < .72f,
                    Move.Mist => p > .15f && p < .75f,
                    Move.Snip or Move.TwinSnip => true,
                    _ => false,
                };
            }
        }

        void Evaluate(float clock, out Pose pose, out float open, out float hands)
        {
            hands = 0;
            switch (move)
            {
                case Move.Snip:
                    Snip(clock, out pose, out open, out hands);
                    return;
                case Move.Mist:
                    Mist(clock, out pose, out open);
                    return;
                case Move.Dash:
                    Dash(clock, out pose, out open, out hands);
                    return;
                case Move.TwinSnip:
                    TwinSnip(clock, out pose, out open, out hands);
                    return;
                default:
                    Swing(clock / duration, out pose, out open);
                    return;
            }
        }

        /// <summary>
        /// The grip each arm reaches for: the right hand follows its handle as the blade turns, the left takes blade B's
        /// handle for the two-handed moves (and opens upward for the mist, or flicks the needles); otherwise the controllers.
        /// </summary>
        public Pose Grip(bool left, Pose grip)
        {
            float clock = Clock;
            if (clock < 0 || !avatar || player.HandStowed(false) || (left && player.HandStowed(true)))
                return grip;
            if (!left)
            {
                if (!MovesScissors(move))
                    return grip;
                Evaluate(clock, out var pose, out float open, out _);
                return HandleGrip(pose, open, false);
            }
            if (move == Move.Mist)
                return MistPalm(grip, clock / duration);
            if (move == Move.Throw)
                return throwFlick ? Flick(grip, clock) : grip;
            if (!TwoHanded(move))
                return grip;
            Evaluate(clock, out var held, out float spread, out float hands);
            var ring = HandleGrip(held, spread, true);
            return new Pose(Vector3.Lerp(grip.position, ring.position, hands), Quaternion.Slerp(grip.rotation, ring.rotation, hands));
        }

        /// <summary>The blades turn about the pivot on their own axis; each hand turns with its blade.</summary>
        public static Quaternion TurnA(float open) => Quaternion.AngleAxis(-open, Vector3.right);

        public static Quaternion TurnB(float open) => Quaternion.AngleAxis(open, Vector3.right);

        /// <summary>The grip holding blade A's handle (right) or blade B's (left) with the scissors at <paramref name="pose"/>.</summary>
        Pose HandleGrip(Pose pose, float open, bool left)
        {
            float s = avatar.WeaponScale;
            Vector3 handle = left ? RingB : avatar.scissorsHandle;
            var turn = left ? TurnB(open) : TurnA(open);
            Vector3 point = pose.position + pose.rotation * (Pivot + turn * (handle - Pivot)) * s;
            var rotation = pose.rotation * turn * Quaternion.Inverse(Quaternion.Euler(avatar.scissorsTilt));
            return new Pose(point, rotation);
        }

        /// <summary>Hallowed Mist: the left hand opens palm up in front of her chest (the mist blooms from it).</summary>
        Pose MistPalm(Pose grip, float p)
        {
            float w = p < .2f ? Smooth(p / .2f) : p > .8f ? 1 - Smooth((p - .8f) / .2f) : 1;
            Vector3 palm = At(-.14f, .3f, -.36f);
            // Left grip space: +X is the palm's normal, +Z runs along the thumb.
            var up = Quaternion.LookRotation(forward, -Right);
            Vector3 at = palm - up * leftPalmInGrip;
            return new Pose(Vector3.Lerp(grip.position, at, w), Quaternion.Slerp(grip.rotation, up, w));
        }

        /// <summary>Needlework: the left hand draws back a hand's width and snaps out along the aim, letting the needles go.</summary>
        Pose Flick(Pose grip, float clock)
        {
            Vector3 hand = grip.position + grip.rotation * leftPalmInGrip;
            Vector3 wind = hand - throwAim * .12f, snap = hand + throwAim * .2f;
            Vector3 palm = clock < .06f ? Vector3.Lerp(hand, wind, Smooth(clock / .06f))
                : clock < .12f ? Vector3.Lerp(wind, snap, 1 - Mathf.Pow(1 - (clock - .06f) / .06f, 2))
                : Vector3.Lerp(snap, hand, Smooth((clock - .12f) / (duration - .12f)));
            return new Pose(palm - grip.rotation * leftPalmInGrip, grip.rotation);
        }

        /// <summary>Where the flicked needles leave: in front of the posed left palm.</summary>
        public Vector3 ThrowPoint() => avatar.Palm(true) + throwAim * .08f;

        /// <summary>The fingers during a move: closed on the handles for two-handed holds, open for the mist and the throw.</summary>
        public HandCurl? Fingers(bool left)
        {
            float clock = Clock;
            if (clock < 0 || !left)
                return null;
            if (TwoHanded(move))
                return new HandCurl(.55f, .92f, .75f);
            if (move == Move.Mist)
                return new HandCurl(.1f, .05f, .1f, .12f, .15f);
            if (move == Move.Throw)
                return clock < ThrowRelease ? null : new HandCurl(.1f, 0, .05f, .1f, .15f);
            return null;
        }

        /// <summary>Where the mist blooms from: the left palm while the flourish lasts.</summary>
        public Vector3 MistSource() => avatar ? avatar.Palm(true) : player.OffHandOrigin;
    }
}
