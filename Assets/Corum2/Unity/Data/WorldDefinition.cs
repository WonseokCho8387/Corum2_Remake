using System;
using System.Collections.Generic;
using Corum2.Core;
using UnityEngine;

namespace Corum2.Unity
{
    [CreateAssetMenu(menuName = "Corum2/World Definition", fileName = "World")]
    public sealed class WorldDefinition : ScriptableObject
    {
        public List<MapDefinition> maps = new List<MapDefinition>();
        public string startMapId = "";
        public string startSpawnId = "";

        public WorldData ToCore()
        {
            var w = new WorldData { StartMapId = startMapId, StartSpawnId = startSpawnId };
            foreach (var m in maps)
                if (m != null) w.Maps.Add(m.ToCore());
            return w;
        }
    }
}
