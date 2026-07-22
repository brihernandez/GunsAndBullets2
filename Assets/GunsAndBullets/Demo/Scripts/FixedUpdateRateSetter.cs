using UnityEngine;

namespace GNB.Demo
{
    public class FixedUpdateRateSetter : MonoBehaviour
    {
        [Range(5, 120)] public int fixedUpdateRateHertz = 30;

        private void Update()
        {
            Time.fixedDeltaTime = 1.0f / fixedUpdateRateHertz;
        }
    }
}
