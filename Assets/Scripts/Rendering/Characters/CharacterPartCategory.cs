namespace Sportland.Rendering.Characters
{
    public enum CharacterPartCategory
    {
        Body,
        Head,
        Hair,
        Eyes,
        Eyebrows,
        Mouth,
        Nose,
        FacialHair,
        Ears,
        Shadow
    }

    public enum CharacterGender
    {
        Male,
        Female,
        Any
    }

    public enum BodyWeightClass
    {
        Slim,
        Athletic,
        Heavy
    }

    // Named animation states — must match the string names in the Animator controller
    public static class AnimationStates
    {
        public const string Idle   = "Idle";
        public const string Walk   = "Walk";
        public const string Run    = "Run";
        public const string Jump   = "Jump";
        public const string Land   = "Land";
        public const string Shoot  = "Shoot";
        public const string Pass   = "Pass";
        public const string Dive   = "Dive";
        public const string Defend = "Defend";
    }
}
