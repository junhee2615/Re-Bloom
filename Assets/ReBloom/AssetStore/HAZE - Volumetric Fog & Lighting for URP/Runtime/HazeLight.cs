using UnityEngine;

namespace Haze.Runtime
{
    public class HazeLight : MonoBehaviour
    {
        [SerializeField, Min(0)] private float _lightContribution = 0;
        [SerializeField, Min(0)] private float _densityBoost = 0;

        public float LightContribution => _lightContribution;
        public float DensityBoost => _densityBoost;
    }
}
