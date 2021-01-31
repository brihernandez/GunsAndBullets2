using UnityEngine;

namespace GNB.Demo
{
    public class GunPrediction : MonoBehaviour
    {
        public Gun Gun = null;
        public Transform Dummy = null;

        [Min(0.01f)]
        public float TimeStep = .1f;

        private void Update()
        {
            var (hitSomething, hitInfo) = Gun.GetPredictedImpactPoint(TimeStep);
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
