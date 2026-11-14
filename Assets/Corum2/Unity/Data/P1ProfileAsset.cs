using Corum2.Core.P1;
using UnityEngine;

namespace Corum2.Unity
{
    /// <summary>
    /// Editor-editable P1 numbers (P1 master §8: every [시험] value in the editor, nothing hard-coded). The whole
    /// P1Profile is serialised by Unity; ToCore() hands the simulation a copy so a play session never edits the asset.
    /// </summary>
    [CreateAssetMenu(menuName = "Corum2/P1 Profile", fileName = "P1Profile")]
    public sealed class P1ProfileAsset : ScriptableObject
    {
        public P1Profile profile = new P1Profile();

        public P1Profile ToCore() => JsonUtility.FromJson<P1Profile>(JsonUtility.ToJson(profile));

        [ContextMenu("Reset to P1 test defaults (master v0.1.38)")]
        void ResetDefaults()
        {
            var regions = profile.Regions;
            profile = P1Defaults.Create();
            profile.Regions = regions;
        }
    }
}
