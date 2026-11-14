using System;

namespace Island
{
    // Analytic critically damped spring: retargeting preserves position AND velocity.
    public sealed class Spring
    {
        private const double Frequency = 2 * Math.PI / 0.3;
        public double Value;
        public double Velocity;
        public double Target;
        public bool Step(double seconds)
        {
            double offset = Value - Target;
            double j = Velocity + Frequency * offset;
            double decay = Math.Exp(-Frequency * seconds);
            Value = Target + (offset + j * seconds) * decay;
            Velocity = (Velocity - Frequency * j * seconds) * decay;
            if (Math.Abs(Value - Target) < 0.001 && Math.Abs(Velocity) < 0.015)
            {
                Value = Target; Velocity = 0; return false;
            }
            return true;
        }
        public void Snap() { Value = Target; Velocity = 0; }
    }
}
