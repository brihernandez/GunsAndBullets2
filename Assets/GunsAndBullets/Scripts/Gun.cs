using UnityEngine;
using System.Collections.Generic;

namespace GNB
{
    public class GunBarrel
    {
        public float RecoilLength = 0.3f;
        public float RecoverSpeed = 1f;

        private Transform barrel = null;
        private Vector3 startLocalPosition = Vector3.zero;
        private float recoil = 0f;

        public GunBarrel(Transform barrel, float recoilLength, float recoverSpeed)
        {
            this.barrel = barrel;
            RecoilLength = recoilLength;
            RecoverSpeed = recoverSpeed;
            startLocalPosition = this.barrel.localPosition;
        }

        public void FireRecoil()
        {
            recoil = RecoilLength;
        }

        public void ResetBarrelOverTime(float deltaTime)
        {
            recoil = Mathf.MoveTowards(recoil, 0f, RecoverSpeed * deltaTime);

            // This means that when a barrel is fully reset it'll never be EXACTLY
            // back at where it started, but this distance should be small enough
            // that hopefully it won't be noticeable.
            if (recoil > 0f)
                barrel.transform.localPosition = startLocalPosition + (Vector3.back * recoil);
        }
    }

    public class Gun : MonoBehaviour
    {
        [Header("Ballistics")]

        [Tooltip("Time (s) between each shot.")]
        public float FireDelay = .2f;

        [Tooltip("Speed (m/s) that the bullet is fired from the barrel.")]
        public float MuzzleVelocity = 200f;

        [Tooltip("Amount of spread the gun has. Higher values result in more spread.")]
        public float Deviation = .1f;

        [Tooltip("Automatically inherit the velocity of a parent Rigidbody when firing bullets.")]
        public bool AutoInheritVelocity = true;

        [Header("Gimballing")]

        [Tooltip("When true, gun will try to gimbal towards the given target position.")]
        public bool UseGimballedAiming = false;

        [Tooltip("When true, the gun will gimbal towards the target ONLY when the target is within gimbal range.")]
        public bool GimbalOnlyWhenInRange = false;

        [Tooltip("How much the gun is allowed to gimbal. Use SetGimbal")]
        [Range(0f, 180f)] public float GimbalRange = 10f;

        [Tooltip("Position gun will try to fire bullets towards.")]
        public Vector3 GimbalTarget = Vector3.zero;

        [Header("Fire Points")]

        [Tooltip("Cycle between fire points when firing rather than firing from all at once.")]
        public bool IsSequentialFiring = false;

        [Tooltip("Where bullets will be fired from. When left blank, this component's transform is used.")]
        [UnityEngine.Serialization.FormerlySerializedAs("Barrels")]
        [SerializeField] private List<Transform> FirePoints = new List<Transform>();

        [Header("Barrel Visuals")]

        [Tooltip("How far back (m) the barrel gets pushed back when fired.")]
        public float RecoilLength = 0.3f;

        [Tooltip("How quickly (m/s) the barrel moves back towards its resting position.")]
        public float RecoilRecoverSpeed = 1f;

        [Tooltip("The list of the barrels used for visually recoiling barrels. This list of barrels should map 1:1 with fire points.")]
        [SerializeField] private List<Transform> RecoilingBarrels = new List<Transform>();

        [Header("Firing")]

        public Bullet BulletPrefab = null;

        [SerializeField] private ParticleSystem MuzzleFlashPrefab = null;
        [Tooltip("Fire bullets from FixedUpdate. If using a physics based project, this should usually be set to true.")]
        public bool FireInFixed = true;

        [Tooltip("Set to true to fire the gun automatically.")]
        public bool IsFiring = false;

        [Tooltip("Bullets automatically ignore the parent rigidbody to prevent self-collision.")]
        public bool IgnoreOwnRigidbody = true;

        [Header("Ammo")]

        public bool UseAmmo = false;

        public int MaxAmmo = 300;

        private Dictionary<Transform, ParticleSystem> firePointToMuzzleFlash = new Dictionary<Transform, ParticleSystem>();
        private List<GunBarrel> barrelVisuals = new List<GunBarrel>();

        private List<Rigidbody> ignoredRigidbodies = new List<Rigidbody>();
        private List<Collider> ignoredColliders = new List<Collider>();

        private float lastShotTime = -float.MaxValue;
        private int firePointIndex = 0;

        public Rigidbody Rigidbody { get; private set; } = null;
        public bool HasRigidbody { get; private set; } = false;

        /// <summary>
        /// Value used when firing for inherited velocity. Normally this is filled in automatically
        /// by a parent Rigidbody, but if no such rigidbody exists, this value can be manually set.
        /// </summary>
        public Vector3 InheritedVelocity { get; set; } = Vector3.zero;

        public bool ReadyToFire => Time.time - lastShotTime >= FireDelay && HasAmmo;

        public bool HasAmmo => !UseAmmo || (UseAmmo && AmmoCount > 0);
        public int AmmoCount { get; private set; } = 300;

        private void Awake()
        {
            Rigidbody = GetComponentInParent<Rigidbody>();
            HasRigidbody = Rigidbody != null;

            if (FirePoints.Count == 0)
            {
                // If no fire points were assigned, fall back on self as a barrel.
                RegisterFirePoint(transform);
            }
            else
            {
                foreach (var firePoint in FirePoints)
                    RegisterFirePoint(firePoint);
            }

            if (RecoilingBarrels.Count > 0)
            {
                foreach (var barrel in RecoilingBarrels)
                    RegisterRecoilingBarrel(barrel);
            }
        }

        private void RegisterFirePoint(Transform firePoint)
        {
            if (firePoint == null)
                return;

            if (MuzzleFlashPrefab != null)
            {
                var muzzleFlash = Instantiate(MuzzleFlashPrefab, firePoint, false);
                firePointToMuzzleFlash.Add(firePoint, muzzleFlash);
            }
        }

        private void RegisterRecoilingBarrel(Transform barrel)
        {
            if (barrel == null)
                return;

            var recoilingBarrel = new GunBarrel(barrel, RecoilLength, RecoilRecoverSpeed);
            barrelVisuals.Add(recoilingBarrel);
        }

        private void Update()
        {
            if (!FireInFixed)
            {
                if (IsFiring)
                    AttemptFireShot(InheritedVelocity);

                foreach (var barrel in barrelVisuals)
                    barrel.ResetBarrelOverTime(Time.deltaTime);
            }
        }

        private void FixedUpdate()
        {
            if (HasRigidbody && AutoInheritVelocity)
                InheritedVelocity = Rigidbody.velocity;

            if (FireInFixed)
            {
                if (IsFiring)
                    AttemptFireShot(InheritedVelocity);

                foreach (var barrel in barrelVisuals)
                    barrel.ResetBarrelOverTime(Time.deltaTime);
            }
        }

        /// <summary>
        /// Restores the ammo count to <see cref="MaxAmmo"/>.
        /// </summary>
        public void ReloadAmmo()
        {
            AmmoCount = MaxAmmo;
        }

        /// <summary>
        /// Directly set the ammo count.
        /// </summary>
        /// <remarks>This ignores the maximum ammo count setting, allowing for the weapon to
        /// be filled with more ammo than <see cref="MaxAmmo"/></remarks>
        public void SetAmmo(int ammo)
        {
            AmmoCount = ammo;
        }

        /// <summary>
        /// Fire a simulated bullet from the gun's current location to see where it lands.
        /// </summary>
        /// <param name="timeStep">Time between each simulation step. The lower the value, the
        /// more precision.</param>
        /// <returns>Tuple where if the first value (hitSomething) is <see langword="true"/>, then
        /// the second value (hitInfo) will be filled out with information on what was hit.</returns>
        public (bool hitSomething, RaycastHit hitInfo) GetPredictedImpactPoint(float timeStep)
        {
            var willHitSomething = false;
            var firePoint = FirePoints[firePointIndex % FirePoints.Count];

            RaycastHit hitInfo = new RaycastHit();

            var simPosition = firePoint.position;
            var simVelocity = firePoint.forward * MuzzleVelocity + InheritedVelocity;

            var simTime = 0f;
            var maxSimTime = BulletPrefab.TimeToLive;
            while (simTime < maxSimTime && !willHitSomething)
            {
                (simPosition, simVelocity) = BulletPrefab.CalculateBulletMotion(simPosition, simVelocity, timeStep);
                (willHitSomething, hitInfo) = BulletPrefab.RunHitDetection(simPosition, simVelocity, timeStep);

                if (willHitSomething)
                    willHitSomething = true;

                simTime += timeStep;
            }

            return (willHitSomething, hitInfo);
        }

        /// <summary>
        /// Set this Collider to be ignored by any bullets to be fired from this gun.
        /// </summary>
        public void AddIgnoredCollider(Collider collider)
        {
            ignoredColliders.Add(collider);
        }

        public void ClearIgnoredColliderList()
        {
            ignoredColliders.Clear();
        }

        /// <summary>
        /// Sets this Rigidbody to be ignored by any bullets fired from this gun.
        /// </summary>
        /// <remarks>This is <i>in addition</i> to the parent Rigidbody, which is automatically
        /// ignored if <see cref="IgnoreOwnRigidbody"/> is set to <see langword="true"/>.</remarks>
        public void AddIgnoredRigidbody(Rigidbody rigidbody)
        {
            ignoredRigidbodies.Add(rigidbody);
        }

        public void ClearIgnoredRigidbodies()
        {
            ignoredRigidbodies.Clear();
        }

        /// <summary>
        /// Fire a single shot. Automatically takes into account inherited velocity.
        /// </summary>
        /// <remarks>For automatic fire, use <see cref="IsFiring"/>.</remarks>
        /// <returns><see langword="true"/> if the shot was fired successfully.</returns>
        public bool FireSingleShot()
        {
            return AttemptFireShot(InheritedVelocity);
        }

        private bool AttemptFireShot(Vector3 inheritedVelocity)
        {
            if (!ReadyToFire)
                return false;

            if (IsSequentialFiring)
            {
                // Cycle between all the fire points.
                var firePoint = FirePoints[firePointIndex % FirePoints.Count];
                FireBulletFromFirePoint(firePoint, inheritedVelocity);
                firePointIndex += 1;

                AmmoCount -= 1;
            }
            else
            {
                // Fire from all barrels at once.
                foreach (var firePoint in FirePoints)
                {
                    FireBulletFromFirePoint(firePoint, inheritedVelocity);
                    firePointIndex += 1;

                    AmmoCount -= 1;
                }
            }

            lastShotTime = Time.time;
            return true;
        }

        private void FireBulletFromFirePoint(Transform firePoint, Vector3 velocity)
        {
            var bullet = Instantiate(BulletPrefab, firePoint.transform.position, firePoint.transform.rotation);

            if (IgnoreOwnRigidbody && HasRigidbody)
                bullet.AddIgnoredRigidbody(Rigidbody);

            if (ignoredRigidbodies.Count > 0)
                bullet.AddIgnoredRigidbodies(ignoredRigidbodies);

            if (ignoredColliders.Count > 0)
                bullet.AddIgnoredColliders(ignoredColliders);

            var bulletRotation = firePoint.transform.rotation;

            var isGimballingAllowed = UseGimballedAiming;
            if (isGimballingAllowed && GimbalOnlyWhenInRange)
            {
                var angleToTarget = Vector3.Angle(
                    from: GimbalTarget - firePoint.position,
                    to: firePoint.forward);

                isGimballingAllowed = angleToTarget < GimbalRange;
            }

            if (isGimballingAllowed)
            {
                bulletRotation = Quaternion.RotateTowards(
                    from: bulletRotation,
                    to: Quaternion.LookRotation(GimbalTarget - firePoint.position, firePoint.up),
                    maxDegreesDelta: GimbalRange);
            }

            bullet.Fire(
                position: firePoint.transform.position,
                rotation: bulletRotation,
                velocity,
                MuzzleVelocity,
                Deviation);

            if (barrelVisuals.Count > 0)
                barrelVisuals[firePointIndex % barrelVisuals.Count].FireRecoil();

            if (firePointToMuzzleFlash.ContainsKey(firePoint))
                firePointToMuzzleFlash[firePoint].Play();
        }
    }
}
