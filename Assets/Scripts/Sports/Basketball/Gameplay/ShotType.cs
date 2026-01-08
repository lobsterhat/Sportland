namespace Sportland.Sports.Basketball.Gameplay
{
    public enum ShotType
    {
        StandardJumpShot,      // Stationary jump shot
        RunningJumpShot,       // Jump shot with momentum
        FadeawayJumpShot,      // Jump shot moving away from basket
        Layup,                 // Close range shot while driving
        ReverseLayup,          // Layup on opposite side of rim
        Floater,               // Running shot released early (over defender)
        HookShot,              // Side release, arc toward basket
        Dunk,                  // At rim, force ball through
        BankShot,              // Intentional backboard use
        FreeThrow,             // Uncontested shot from line
        AlleyOop,              // Catch and finish (requires passing)
        PutBack                // Quick shot off rebound
    }

    public struct ShotContext
    {
        public ShotType type;
        public float distanceToBasket;
        public bool isMoving;
        public bool movingTowardBasket;
        public bool movingAwayFromBasket;
        public float releaseHeight;        // Where ball is released vertically
        public float releaseExtension;     // How far toward basket (for layups)
        public bool intentionalBank;       // Player pressed bank shot button
    }
}
