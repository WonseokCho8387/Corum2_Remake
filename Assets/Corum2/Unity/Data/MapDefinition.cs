using System;
using System.Collections.Generic;
using Corum2.Core;
using UnityEngine;

namespace Corum2.Unity
{
    [CreateAssetMenu(menuName = "Corum2/Map Definition", fileName = "Map")]
    public sealed class MapDefinition : ScriptableObject
    {
        public MetaFields meta = new MetaFields { domainId = "map", relatedRIds = new[] { "R-01-01", "R-01-02", "R-01-03", "R-01-04", "R-01-05" } };
        public string mapId = "";
        public float minX;
        public float maxX = 40f;
        public float killY = -10f;
        [Tooltip("Fixed depth of the combat plane (appendix section 2).")]
        public float combatPlaneZ;
        public List<BoxData> solids = new List<BoxData>();
        public List<SpawnData> spawns = new List<SpawnData>();
        public EdgeData left = new EdgeData();
        public EdgeData right = new EdgeData();
        public List<PointData> points = new List<PointData>();
        [Tooltip("WP-02 test enemies (training dummies).")] public List<EnemySpawnData> enemies = new List<EnemySpawnData>();
        [Header("Camera (U-01, EXP-CAM-01)")] public float cameraMinX;
        public float cameraMaxX = 30f;
        public float cameraY = 2.5f;
        public float cameraFollowSharpness = 10f;
        [Header("Presentation only")] public Color groundColor = new Color(0.42f, 0.38f, 0.34f);

        public MapData ToCore()
        {
            var m = new MapData
            {
                Meta = meta.ToCore(),
                Id = mapId,
                MinX = minX,
                MaxX = maxX,
                KillY = killY,
                CombatPlaneZ = combatPlaneZ,
                Left = new EdgeRule { Policy = left.policy, ConnectionId = left.connectionId, TargetMapId = left.targetMapId, TargetSpawnId = left.targetSpawnId },
                Right = new EdgeRule { Policy = right.policy, ConnectionId = right.connectionId, TargetMapId = right.targetMapId, TargetSpawnId = right.targetSpawnId },
                Camera = new CameraProfile { MinX = cameraMinX, MaxX = cameraMaxX, Y = cameraY, FollowSharpness = cameraFollowSharpness },
            };
            foreach (var s in solids) m.Solids.Add(s.ToCore());
            foreach (var s in spawns) m.Spawns.Add(new SpawnPoint { Id = s.id, Position = new Vec2(s.position.x, s.position.y), Facing = s.facing });
            foreach (var p in points)
                m.Points.Add(new TransitionPoint { Id = p.id, Area = p.area.ToCore(), Direction = p.direction, TargetMapId = p.targetMapId, TargetSpawnId = p.targetSpawnId });
            if (enemies != null)
                foreach (var e in enemies)
                    m.Enemies.Add(new EnemySpawn { InstanceId = e.instanceId, EnemyId = e.enemyId, Position = new Vec2(e.position.x, e.position.y), Facing = e.facing });
            return m;
        }
    }
}
