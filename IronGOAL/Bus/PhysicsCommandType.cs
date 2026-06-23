namespace IronGOAL.Bus;

public enum PhysicsCommandType
{
    ApplyForce,     // Continuous force on a rigid body, applied for this tick only.
    ApplyImpulse,   // Instantaneous impulse - an immediate change in momentum.
    SetVelocity,    // Override a rigid body's linear velocity directly.
    SetKinematic    // Toggle kinematic mode; Value != 0 means kinematic, 0 means dynamic.
}
