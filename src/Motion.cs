using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Forms;

namespace TaskBoost
{
    // A number that glides to its target instead of jumping there. Controls keep one for each thing that moves
    // (a switch knob, a hover glow, the score ring) and read Value when they paint.
    class Anim
    {
        public float Value;
        // Overshoots a little before settling, like something with weight: used by the switches
        public bool Springy;
        float from, to;
        long start;
        int duration;
        readonly Action changed;

        public Anim(float value, Action changed)
        {
            Value = to = value;
            this.changed = changed;
        }

        public float Target { get { return to; } }
        public bool Moving { get { return Value != to; } }

        public void To(float target, int milliseconds)
        {
            if (to == target) return; // already there, or on its way
            to = target;
            if (!Motion.On || milliseconds <= 0)
            {
                Value = target;
                changed();
                return;
            }
            from = Value;
            start = Motion.Now;
            duration = milliseconds;
            Motion.Add(this);
        }

        public void Jump(float value)
        {
            Value = to = value;
            changed();
        }

        // Called by the shared timer; false once the target is reached
        public bool Step(long now)
        {
            float goal = to, t = Math.Min(1F, (now - start) / (float)duration);
            Value = t >= 1F ? to : from + (to - from) * (Springy ? Motion.Spring(t) : Motion.EaseOut(t));
            changed();
            return t < 1F || to != goal; // still moving, or sent somewhere new by what it just redrew
        }
    }

    // One timer moves every Anim in the app, and stops by itself when nothing is moving,
    // so a window that is standing still costs nothing.
    static class Motion
    {
        const string SettingsFile = "motion.txt";

        // Switched off on the Home tab: everything then jumps straight to its end state
        public static bool On = true;

        static readonly Stopwatch clock = Stopwatch.StartNew();
        static readonly List<Anim> moving = new List<Anim>();
        static Timer timer;
        static Control home;

        public static long Now { get { return clock.ElapsedMilliseconds; } }

        public static void Load()
        {
            On = Settings.Load(SettingsFile, new[] { "on" }).Contains("on");
            Attach();
        }

        public static void Save(bool on)
        {
            On = on;
            Settings.Save(SettingsFile, new[] { on ? "on" : "off" });
        }

        // Ties the timer to the thread that calls this: the one the windows live on. A timer only ticks on the
        // thread that started it, so an animation started from any other thread is handed over to this one;
        // started there, the timer would never tick and every animation in the app would stand still.
        public static void Attach()
        {
            if (home != null) return;
            home = new Control();
            IntPtr created = home.Handle;
        }

        public static void Add(Anim a)
        {
            Attach();
            if (home.InvokeRequired)
            {
                home.BeginInvoke(new Action<Anim>(Add), a);
                return;
            }
            if (!moving.Contains(a)) moving.Add(a);
            if (timer == null)
            {
                timer = new Timer();
                timer.Interval = 15;
                timer.Tick += delegate { Tick(); };
            }
            timer.Start();
        }

        static void Tick()
        {
            long now = Now;
            // A copy, because a step may start or finish other animations
            foreach (Anim a in moving.ToArray())
                if (!a.Step(now)) moving.Remove(a);
            if (moving.Count == 0) timer.Stop();
        }

        // Fast at first, gentle at the end
        public static float EaseOut(float t)
        {
            float u = 1F - t;
            return 1F - u * u * u;
        }

        // Goes a little past the end and comes back
        public static float Spring(float t)
        {
            const float s = 1.70158F;
            float u = t - 1F;
            return 1F + (s + 1F) * u * u * u + s * u * u;
        }
    }
}
