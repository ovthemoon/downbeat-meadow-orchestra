using UnityEngine;

namespace Downbeat.Views
{
    /// <summary>Scene-serialized references that code cannot create itself (shaders are only built into the player if something references them).</summary>
    public sealed class ViewAssets : MonoBehaviour
    {
        [SerializeField] Material additiveMaterial;

        public Material AdditiveMaterial => additiveMaterial;
    }
}
