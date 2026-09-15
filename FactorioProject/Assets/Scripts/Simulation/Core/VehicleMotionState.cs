using System;

namespace ProjectF.Simulation
{
    [Serializable]
    public struct VehicleMotionState
    {
        public float SignedSpeed;

        public float Advance(float inputAxis, float deltaTime, float maxSpeed,
            float accelerationPerSecond, float decelerationPerSecond, bool clampToMaxSpeed = true)
        {
            float dt = Math.Max(0, deltaTime);
            float axis = Math.Max(-1f, Math.Min(1f, inputAxis));
            bool hasInput = Math.Abs(axis) > 0.001f;
            float limit = Math.Max(0.01f, maxSpeed);
            float target = hasInput ? axis * limit : 0f;
            bool accelerating = hasInput && (Math.Abs(SignedSpeed) <= 0.0001f
                || Sign(SignedSpeed) == Sign(target) && Math.Abs(target) > Math.Abs(SignedSpeed));
            float change = Math.Max(0.01f, accelerating ? accelerationPerSecond : decelerationPerSecond) * dt;
            float difference = target - SignedSpeed;
            SignedSpeed = Math.Abs(difference) <= change ? target : SignedSpeed + Sign(difference) * change;
            if (!hasInput && Math.Abs(SignedSpeed) <= 0.0001f) SignedSpeed = 0f;
            if (clampToMaxSpeed) Clamp(limit);
            return SignedSpeed;
        }
        public void Clamp(float maxSpeed)
        {
            float limit = Math.Max(0.01f, maxSpeed);
            if (Math.Abs(SignedSpeed) > limit) SignedSpeed = Sign(SignedSpeed) * limit;
        }
        private static float Sign(float value) => value >= 0 ? 1f : -1f;
    }
}
