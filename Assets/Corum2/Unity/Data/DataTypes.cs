using System;
using System.Collections.Generic;
using Corum2.Core;
using UnityEngine;

namespace Corum2.Unity
{
    /// <summary>Inspector copy of the Core traceability block (appendix section 4).</summary>
    [Serializable]
    public sealed class MetaFields
    {
        public string domainId = "";
        public int schemaVersion = 1;
        public string dataVersion = "wp01-1";
        [Tooltip("experimental | approved. Values in WP-01 are experimental.")]
        public string status = "experimental";
        public string[] policyIds = new string[0];
        public string[] relatedRIds = new string[0];
        public string changeRequestId = "";

        public DataMeta ToCore() => new DataMeta
        {
            DomainId = domainId,
            SchemaVersion = schemaVersion,
            DataVersion = dataVersion,
            Status = status,
            PolicyIds = policyIds ?? new string[0],
            RelatedRIds = relatedRIds ?? new string[0],
            ChangeRequestId = changeRequestId,
        };
    }

    [Serializable]
    public sealed class ActionBinding
    {
        public LogicalAction action;
        [Tooltip("Input System control paths, e.g. <Gamepad>/buttonSouth, <Keyboard>/space")]
        public string[] paths = new string[0];
        [Tooltip("Policy id when the mapping is not final (e.g. EXP-INPUT-01).")]
        public string policy = "";
    }

    [Serializable]
    public sealed class BoxData
    {
        public Vector2 min;
        public Vector2 max;
        public Aabb ToCore() => new Aabb(min.x, min.y, max.x, max.y);
    }

    [Serializable]
    public sealed class SpawnData
    {
        public string id = "";
        public Vector2 position;
        public int facing = 1;
    }

    [Serializable]
    public sealed class EdgeData
    {
        public EdgePolicy policy = EdgePolicy.Blocked;
        public string connectionId = "";
        public string targetMapId = "";
        public string targetSpawnId = "";
    }

    [Serializable]
    public sealed class PointData
    {
        public string id = "";
        public BoxData area = new BoxData();
        public PointDirection direction;
        public string targetMapId = "";
        public string targetSpawnId = "";
    }
}
