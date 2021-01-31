using UnityEngine;

namespace GNB.Demo
{
    public class GunTester : MonoBehaviour
    {
        public Gun[] guns;
        public Transform GimbalTarget = null;

        private void Update()
        {
            if (Input.GetMouseButton(0) || Input.GetKey(KeyCode.Space))
            {
                foreach (var gun in guns)
                {
                    if (GimbalTarget != null)
                    {
                        gun.UseGimballedAiming = true;
                        gun.GimbalTarget = GimbalTarget.position;
                    }
                    else
                    {
                        gun.UseGimballedAiming = false;
                    }

                    gun.FireSingleShot();
                }
            }
        }
    }
}
