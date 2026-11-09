using System.Collections.Generic;

namespace Corum2.Core
{
    /// <summary>Traceability block every data asset carries (appendix section 4).</summary>
    public sealed class DataMeta
    {
        public string DomainId = "";
        public int SchemaVersion = 1;
        public string DataVersion = "";
        /// <summary>"experimental" or "approved". Nothing in WP-01 is approved.</summary>
        public string Status = "experimental";
        public string[] PolicyIds = new string[0];
        public string[] RelatedRIds = new string[0];
        public string ChangeRequestId = "";

        public string Tag => DomainId + "@" + DataVersion + "(" + Status + ")";
    }

    /// <summary>MovementProfile (spec 11.2). All numbers are tuning values (U-04), not approved.</summary>
    public sealed class MovementProfile
    {
        public DataMeta Meta = new DataMeta();

        public float WalkSpeed = 3.5f;          // m/s
        public float RunSpeed = 7f;             // m/s
        public float Gravity = 30f;             // m/s^2
        public float JumpHeight = 2.2f;         // m, same for standing/walk/run jump (R-03-02)
        public float ExtraJumpHeight = 1.8f;    // m, measured from the moment of the extra jump
        public float AirControlSpeed = 3.5f;    // m/s, horizontal limit when steering from a slower launch
        public float AirAcceleration = 18f;     // m/s^2
        public float MaxFallSpeed = 22f;        // m/s
        public float SlideSpeed = 9f;           // m/s
        public float SlideDuration = 0.35f;     // s
        public float RollSpeed = 7f;            // m/s
        public float RollDuration = 0.4f;       // s
        public float DashSpeed = 12f;           // m/s, straight air dash (R-03-06)
        public float DashDuration = 0.25f;      // s
        public float DashEndSpeed = 3.5f;       // m/s, horizontal speed kept after the dash
        public float DiveSpeed = 14f;           // m/s, diagonal dive (R-03-06)
        public float DiveAngleDeg = 45f;        // degrees below horizontal
        public float BodyWidth = 0.6f;          // m
        public float StandHeight = 1.8f;        // m
        public float CrouchHeight = 0.9f;       // m, also slide height

        public float JumpVelocity => (float)System.Math.Sqrt(2.0 * Gravity * JumpHeight);
        public float ExtraJumpVelocity => (float)System.Math.Sqrt(2.0 * Gravity * ExtraJumpHeight);
    }

    /// <summary>InputProfile (spec 11.2) subset used by WP-01.</summary>
    public sealed class InputProfile
    {
        public DataMeta Meta = new DataMeta();
        public float StickDeadzone = 0.35f;
        /// <summary>Double-tap press-to-press window in raw input seconds. 0.25 s is the spec's test proposal.</summary>
        public double DoubleTapWindow = 0.25;
    }

    /// <summary>
    /// Separate unlock switches for test scenes (spec 11.6). Not tied to levels; each one is independent.
    /// </summary>
    public sealed class AirUnlocks
    {
        public DataMeta Meta = new DataMeta();
        public bool ExtraJump;
        public bool AirDash;
        public bool Dive;
    }

    public enum EdgePolicy
    {
        /// <summary>Edge acts as a wall. Explicit content policy for an unconnected edge (spec 3.2).</summary>
        Blocked,
        /// <summary>Pushing past the edge switches to the target map immediately (R-01-03).</summary>
        Connected,
    }

    public sealed class EdgeRule
    {
        public EdgePolicy Policy = EdgePolicy.Blocked;
        public string ConnectionId = "";
        public string TargetMapId = "";
        public string TargetSpawnId = "";
    }

    public enum PointDirection
    {
        Up,
        Down,
    }

    /// <summary>Up/down map exit point (R-01-04).</summary>
    public sealed class TransitionPoint
    {
        public string Id = "";
        public Aabb Area;
        public PointDirection Direction;
        public string TargetMapId = "";
        public string TargetSpawnId = "";
    }

    public sealed class SpawnPoint
    {
        public string Id = "";
        public Vec2 Position;
        public int Facing = 1;
    }

    /// <summary>Camera range of a map. Follow speed / vertical behaviour are U-01 (experimental).</summary>
    public sealed class CameraProfile
    {
        public float MinX;
        public float MaxX;
        public float Y = 2.5f;
        /// <summary>Exponential follow rate per second; 0 = lock to target.</summary>
        public float FollowSharpness = 10f;
    }

    public sealed class MapData
    {
        public DataMeta Meta = new DataMeta();
        public string Id = "";
        public float MinX;
        public float MaxX;
        /// <summary>Below this height the test safeguard returns the player to the map's first spawn.</summary>
        public float KillY = -10f;
        /// <summary>Fixed depth of the combat plane in Unity (appendix section 2). Core never moves along it.</summary>
        public float CombatPlaneZ;
        public readonly List<Aabb> Solids = new List<Aabb>();
        public readonly List<SpawnPoint> Spawns = new List<SpawnPoint>();
        public readonly List<TransitionPoint> Points = new List<TransitionPoint>();
        /// <summary>Test enemies placed in this map (WP-02 training dummies).</summary>
        public readonly List<EnemySpawn> Enemies = new List<EnemySpawn>();
        public EdgeRule Left = new EdgeRule();
        public EdgeRule Right = new EdgeRule();
        public CameraProfile Camera = new CameraProfile();

        public SpawnPoint FindSpawn(string id)
        {
            foreach (var s in Spawns)
                if (s.Id == id) return s;
            return null;
        }
    }

    public sealed class WorldData
    {
        public readonly List<MapData> Maps = new List<MapData>();
        public string StartMapId = "";
        public string StartSpawnId = "";

        public MapData FindMap(string id)
        {
            foreach (var m in Maps)
                if (m.Id == id) return m;
            return null;
        }
    }
}
