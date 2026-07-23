using UnityEngine;

namespace GNB.Demo
{
    [RequireComponent(typeof(TextMesh))]
    public class AmmoCounter : MonoBehaviour
    {
        public Gun gun = null;
        private TextMesh textMesh = null;
        private int lastAmmo = -1;

        private void Awake()
        {
            TryGetComponent(out textMesh);
            textMesh.text = $"{gun.AmmoCount}/{gun.MaxAmmo}";
            lastAmmo = gun.AmmoCount;
        }

        private void Update()
        {
            if (gun.AmmoCount != lastAmmo)
            {
                textMesh.text = $"{gun.AmmoCount}/{gun.MaxAmmo}";
                lastAmmo = gun.AmmoCount;
            }
        }
    }
}
