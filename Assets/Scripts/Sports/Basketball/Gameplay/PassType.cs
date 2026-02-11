namespace Sportland.Sports.Basketball.Gameplay
{
    public enum PassType
    {
        Direct,      // Fast straight pass to teammate
        Bounce,      // Pass that bounces off the floor
        Lob,         // High arcing pass over defenders
        AlleyOop     // High pass for teammate to catch and dunk
    }

    public enum PassLocation
    {
        Chest,       // Released from chest height
        Overhead     // Released from overhead
    }

    public struct PassContext
    {
        public PassType type;
        public PassLocation location;
        public float releaseHeight;
        public float targetHeight;
        public float speed;
        public float arc;
    }
}
