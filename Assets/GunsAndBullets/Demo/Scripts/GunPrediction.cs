using UnityEngine;

namespace GNB.Demo
{
    public class GunPrediction : MonoBehaviour
    {
        public Gun Gun = null;
        public Transform Dummy = null;

        [Min(0.01f)] public float TimeStep = .1f;
        [Min(1)] public int Substeps = 1;

        [Tooltip("Using the actual fixed timestep will result in the most accurate prediction, but can be prohibitively expensive.")]
        public bool useRealFixedTimestep = false;

        private void Update()
        {
            var (hitSomething, hitInfo) = Gun.GetPredictedImpactPoint(
                useRealFixedTimestep ? Time.fixedDeltaTime : TimeStep,
                Substeps);
            if (hitSomething)
            {
                Dummy.gameObject.SetActive(true);
                Dummy.transform.position = hitInfo.point;
            }
            else
            {
                Dummy.gameObject.SetActive(false);
            }
        }
    }
}
