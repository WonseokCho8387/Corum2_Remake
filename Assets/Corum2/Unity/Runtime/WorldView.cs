using System.Collections.Generic;
using Corum2.Core;
using UnityEngine;
using UnityEngine.Rendering;

namespace Corum2.Unity
{
    /// <summary>
    /// Builds temporary block-out visuals from map data (presentation only; no gameplay colliders).
    /// Visual depth is allowed (appendix section 2); the combat plane stays at CombatPlaneZ.
    /// </summary>
    public sealed class WorldView
    {
        readonly Dictionary<string, GameObject> _roots = new Dictionary<string, GameObject>();
        readonly Material _baseMaterial;
        readonly Transform _parent;
        string _active;

        public WorldView(Transform parent)
        {
            _parent = parent;
            var pipe = GraphicsSettings.currentRenderPipeline;
            _baseMaterial = pipe != null ? pipe.defaultMaterial : null;
            if (_baseMaterial == null) Debug.LogError("[Corum2] No render pipeline default material; block-out visuals will be missing.");
        }

        public Material MakeMaterial(Color c)
        {
            var m = new Material(_baseMaterial);
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", c);
            m.color = c;
            return m;
        }

        public void Build(WorldData world, IEnumerable<MapDefinition> defs)
        {
            var colors = new Dictionary<string, Color>();
            foreach (var d in defs)
                if (d != null) colors[d.mapId] = d.groundColor;

            foreach (var map in world.Maps)
            {
                var root = new GameObject("Map_" + map.Id);
                root.transform.SetParent(_parent, false);
                var z = map.CombatPlaneZ;
                var ground = MakeMaterial(colors.TryGetValue(map.Id, out var c) ? c : Color.gray);

                foreach (var s in map.Solids)
                {
                    // Depth 4 m centred slightly behind the plane so the player stands on the front half.
                    Block(root.transform, "Solid", new Vector3(s.CenterX, s.CenterY, z + 1f), new Vector3(s.Width, s.Height, 4f), ground);
                }

                var up = MakeMaterial(new Color(0.2f, 0.85f, 0.35f));
                var down = MakeMaterial(new Color(0.25f, 0.5f, 1f));
                foreach (var p in map.Points)
                {
                    var mat = p.Direction == PointDirection.Up ? up : down;
                    Block(root.transform, "Point_" + p.Id, new Vector3(p.Area.CenterX, p.Area.CenterY, z + 2.6f), new Vector3(p.Area.Width, p.Area.Height, 0.1f), mat);
                    Block(root.transform, "PointArrow_" + p.Id,
                        new Vector3(p.Area.CenterX, p.Direction == PointDirection.Up ? p.Area.MaxY + 0.3f : p.Area.MinY + 0.05f, z + 2.5f),
                        new Vector3(0.3f, 0.3f, 0.3f), mat);
                }

                Edge(root.transform, map, map.MinX, map.Left, z);
                Edge(root.transform, map, map.MaxX, map.Right, z);
                root.SetActive(false);
                _roots[map.Id] = root;
            }
        }

        void Edge(Transform parent, MapData map, float x, EdgeRule rule, float z)
        {
            var color = rule.Policy == EdgePolicy.Connected ? new Color(1f, 0.85f, 0.2f) : new Color(0.85f, 0.2f, 0.2f);
            Block(parent, "Edge_" + rule.Policy, new Vector3(x, 3f, z + 2.6f), new Vector3(0.15f, 6f, 0.1f), MakeMaterial(color));
        }

        static GameObject Block(Transform parent, string name, Vector3 center, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Object.Destroy(go.GetComponent<Collider>()); // gameplay collision lives in Core
            go.transform.SetParent(parent, false);
            go.transform.localPosition = center;
            go.transform.localScale = size;
            go.GetComponent<MeshRenderer>().sharedMaterial = mat;
            return go;
        }

        public void Show(string mapId)
        {
            if (_active == mapId) return;
            if (_active != null && _roots.TryGetValue(_active, out var old)) old.SetActive(false);
            if (_roots.TryGetValue(mapId, out var root)) root.SetActive(true);
            _active = mapId;
        }
    }

    /// <summary>Block-out player: a box from the feet up, a nose on the facing side, color by state.</summary>
    public sealed class PlayerView
    {
        public readonly Transform Root;
        readonly Transform _body;
        readonly Transform _nose;
        readonly Material _mat;
        readonly float _width;

        public PlayerView(Transform parent, WorldView world, float width)
        {
            _width = width;
            Root = new GameObject("PlayerView").transform;
            Root.SetParent(parent, false);
            _mat = world.MakeMaterial(Color.white);
            _body = Prim(Root, "Body", _mat).transform;
            _nose = Prim(Root, "Nose", world.MakeMaterial(new Color(0.1f, 0.1f, 0.1f))).transform;
        }

        static GameObject Prim(Transform parent, string name, Material m)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshRenderer>().sharedMaterial = m;
            return go;
        }

        public void Apply(Vector3 feet, float height, int facing, MotorState state)
        {
            Root.position = feet;
            _body.localPosition = new Vector3(0f, height * 0.5f, 0f);
            _body.localScale = new Vector3(_width, height, _width);
            _nose.localPosition = new Vector3(facing * (_width * 0.5f + 0.08f), height * 0.75f, 0f);
            _nose.localScale = new Vector3(0.16f, 0.16f, 0.16f);
            var c = StateColor(state);
            if (_mat.HasProperty("_BaseColor")) _mat.SetColor("_BaseColor", c);
        }

        static Color StateColor(MotorState s)
        {
            switch (s)
            {
                case MotorState.Walk: return new Color(0.95f, 0.95f, 0.95f);
                case MotorState.Run: return new Color(1f, 0.6f, 0.2f);
                case MotorState.Crouch: return new Color(0.7f, 0.7f, 0.9f);
                case MotorState.Slide: return new Color(0.3f, 0.9f, 1f);
                case MotorState.Roll: return new Color(0.4f, 1f, 0.6f);
                case MotorState.Airborne: return new Color(1f, 1f, 0.5f);
                case MotorState.AirDash: return new Color(1f, 0.3f, 0.3f);
                case MotorState.Dive: return new Color(0.8f, 0.3f, 1f);
                default: return new Color(0.85f, 0.85f, 0.85f);
            }
        }
    }
}
