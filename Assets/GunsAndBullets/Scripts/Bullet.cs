using UnityEngine;
using System.Collections.Generic;

namespace GNB
{
    public class Bullet : MonoBehaviour
    {
        [Header("Prefabs")]
        [Tooltip("Effect played when the bullet impacts something.")]
        [SerializeField] private ParticleSystem ImpactFXPrefab = null;
        [Tooltip("Effect played when the bullet explodes.")]
        [SerializeField] private ParticleSystem ExplodeFXPrefab = null;
        [Tooltip("Any trails listed here will be cleaned up nicely on the bullet's destruction. " +
            "Used to prevent unsightly deleted trails.")]
        [SerializeField] private List<TrailRenderer> ChildTrails = new List<TrailRenderer>();

        [Header("Motion")]
        [Tooltip("Layers the bullet will normally hit")]
        public LayerMask RayHitLayers = -1;
        [Tooltip("How long (seconds) the bullet lasts")]
        public float TimeToLive = 5f;
        [Tooltip("Gravity applied to the bullet where 1 is normal gravity.")]
        public float GravityModifier = 0f;
        [Tooltip("When true, the bullet automatically aligns itself to its velocity. Useful in arcing motions.")]
        public bool AlignToVelocity = false;
        [Tooltip("Length of bullet assuming the origin is the \"tail\" and a BulletLength's distance forwards is the \"head\".")]
        public float BulletLength = 1f;
        [Tooltip("This should be set to true when using physics based projects.")]
        [SerializeField] private bool MoveInFixedUpdate = true;

        [Header("Thick Bullets")]
        [Tooltip("Use thick hit detection for the bullet. This is run in addition to normal hit detection.")]
        public bool IsThick = false;
        [Tooltip("The layers the bullet will hit using thick hit detection.")]
        public LayerMask ThickHitLayers = 0;
        [Tooltip("Used only when thick hit detection is enabled.")]
        public float BulletDiameter = 1f;

        [Header("Explosions")]
        public bool ExplodeOnImpact = false;
        public bool ExplodeOnTimeout = false;

#if UNITY_EDITOR
        [Header("Debug")]
        public bool ShowDebugVisuals = false;
#endif

        private HashSet<Rigidbody> ignoredRigidbodies = new HashSet<Rigidbody>();
        private HashSet<Collider> ignoredColliders = new HashSet<Collider>();

        private static RaycastHit[] raycastHits = new RaycastHit[32];

        public Vector3 Velocity { get; private set; } = Vector3.zero;
        public float SecondsSinceFired { get; private set; } = 0f;
        public bool IsFired { get; private set; } = false;

        private void Update()
        {
            if (IsFired && !MoveInFixedUpdate)
                UpdateBullet(Time.deltaTime);
        }

        private void FixedUpdate()
        {
            if (IsFired && MoveInFixedUpdate)
                UpdateBullet(Time.fixedDeltaTime);
        }

        /// <param name="position">Position the bullet will start at.</param>
        /// <param name="rotation">Rotation the bullet will start at.</param>
        /// <param name="inheritedVelocity">Any extra velocity to add to the bullet that it might
        /// be inheriting from its firer.</param>
        /// <param name="muzzleVelocity">Starting forward velocity of the bullet.</param>
        /// <param name="deviation">Maximum random deviation in degrees to apply to the bullet.</param>
        public void Fire(Vector3 position, Quaternion rotation, Vector3 inheritedVelocity, float muzzleVelocity, float deviation)
        {
            // Start position.
            transform.position = position;

            // Calculate a random deviation.
            Vector3 deviationAngle = Vector3.zero;
            deviationAngle.x = Random.Range(-deviation, deviation);
            deviationAngle.y = Random.Range(-deviation, deviation);
            Quaternion deviationRotation = Quaternion.Euler(deviationAngle);

            // Rotate the bullet to the direction requested, plus some random deviation.
            transform.rotation = rotation * deviationRotation;

            Velocity = (transform.forward * muzzleVelocity) + inheritedVelocity;
            IsFired = true;
        }

        /// <summary>
        /// Calculates the motion of the bullet given the starting position and velocity.
        /// Returns a tuple of the resulting position and velocity.
        /// </summary>
        /// <param name="deltaTime">The time to simulate forwards. The smaller this value, the more
        /// accurate the result.</param>
        public (Vector3 position, Vector3 velocity) CalculateBulletMotion(Vector3 position, Vector3 velocity, float deltaTime)
        {
            velocity += Physics.gravity * GravityModifier * deltaTime;
            position += velocity * deltaTime;

            return (position, velocity);
        }

        /// <summary>
        /// Runs hit detection by projecting if the bullet will hit something when it moves this frame.
        /// </summary>
        /// <param name="position">Position of the bullet right now</param>
        /// <param name="velocity">Velocity of the bullet right now</param>
        /// <param name="deltaTime">Expected frame time for the bullet to move in</param>
        public (bool hitSomething, RaycastHit hitInfo) RunHitDetection(Vector3 position, Vector3 velocity, float deltaTime)
        {
            return IsThick
                ? RunThickHitDetection(position, velocity, deltaTime)
                : RunRayHitDetection(position, velocity, deltaTime);
        }

        /// <summary>
        /// Prevents collision with any colliders owned by this Rigidbody. A common use for this is
        /// to prevent a gun from shooting its owner. Prefer using this to ignore objects when possible.
        /// </summary>
        public void AddIgnoredRigidbody(Rigidbody rigidbody)
        {
            if (rigidbody != null)
                ignoredRigidbodies.Add(rigidbody);
        }

        /// <summary>
        /// Prevents collision with any colliders owned by the Rigidbodies in this list. A common use
        /// for this is to prevent a gun from shooting its owner. Prefer using this to ignore objects
        /// when possible.
        /// </summary>
        /// <param name="rigidbodies"></param>
        public void AddIgnoredRigidbodies(IEnumerable<Rigidbody> rigidbodies)
        {
            foreach (var rigidbody in rigidbodies)
                ignoredRigidbodies.Add(rigidbody);
        }

        /// <summary>
        /// Prevents collision the given collider. Commonly used to prevent a gun from shooting its
        /// owner. When possible, prefer <see cref="AddIgnoredRigidbody(Rigidbody)"/> rather than
        /// naming individual colliders.
        /// </summary>
        public void AddIgnoredCollider(Collider collider)
        {
            if (collider != null)
                ignoredColliders.Add(collider);
        }

        /// <summary>
        /// Prevents collision the given colliders. Commonly used to prevent a gun from shooting its
        /// owner. When possible, prefer <see cref="AddIgnoredRigidbody(Rigidbody)"/> rather than
        /// naming individual colliders.
        /// </summary>
        public void AddIgnoredColliders(IEnumerable<Collider> colliders)
        {
            foreach (var collider in colliders)
                ignoredColliders.Add(collider);
        }

        /// <summary>
        /// Explodes the bullet. Typically used for air bursting explosive weapons.
        /// </summary>
        public void ExplodeBullet(Vector3 explodePosition, Quaternion explodeRotation)
        {
            if (ExplodeFXPrefab != null)
                Instantiate(ExplodeFXPrefab, explodePosition, explodeRotation).Play();

            HandleExplosionDamage(explodePosition);

            CleanUpTrails();
            Destroy(gameObject);
        }

        /// <summary>
        /// Destroys the bullet as if it hit something.
        /// </summary>
        public void DestroyBulletFromImpact(Vector3 impactedPoint, Quaternion impactRotation)
        {
            if (ImpactFXPrefab != null)
                Instantiate(ImpactFXPrefab, impactedPoint, impactRotation).Play();

            CleanUpTrails();
            Destroy(gameObject);
        }

        /// <summary>
        /// Destroys the bullet with no effect.
        /// </summary>
        public void DestroyBulletSilently()
        {
            CleanUpTrails();
            Destroy(gameObject);
        }

        private void UpdateBullet(float deltaTime)
        {
            SecondsSinceFired += deltaTime;
            if (SecondsSinceFired > TimeToLive)
            {
                if (ExplodeOnTimeout)
                    ExplodeBullet(transform.position, transform.rotation);
                else
                    DestroyBulletSilently();
            }
            else
            {
                var (hitSomething, hitInfo) = RunHitDetection(transform.position, Velocity, deltaTime);
                if (hitSomething)
                {
                    HandleImpactDamage(hitInfo);
                    HandleExplosionDamage(hitInfo.point);

                    if (ExplodeOnImpact)
                        ExplodeBullet(hitInfo.point, transform.rotation);
                    else
                        DestroyBulletFromImpact(hitInfo.point, transform.rotation);
                }
                else
                {
                    // Bullet continues motion.
                    var (position, velocity) = CalculateBulletMotion(transform.position, Velocity, deltaTime);
                    transform.position = position;
                    Velocity = velocity;

                    if (AlignToVelocity && velocity.sqrMagnitude > .01f)
                        transform.rotation = Quaternion.LookRotation(velocity, transform.up);
                }
            }
        }

        private void HandleImpactDamage(RaycastHit hitInfo)
        {
            // ==========================================================
            // TODO: Bullet hit something, insert damage handling here!
            // ==========================================================
        }

        private void HandleExplosionDamage(Vector3 explodePos)
        {
            // ==========================================================
            // TODO: Bullet exploded, insert damage handling here!
            // ==========================================================
        }

        /// <summary>
        /// Checks the ignore list to see if this given hit is allowed.
        /// </summary>
        private bool IsHitAllowed(RaycastHit hit)
        {
            bool isHitAllowed = true;

            var hitRigidbody = hit.rigidbody;
            if (hitRigidbody != null && ignoredRigidbodies.Contains(hitRigidbody))
                isHitAllowed = false;
            else if (ignoredColliders.Contains(hit.collider))
                isHitAllowed = false;

            return isHitAllowed;
        }

        private (bool hitSomething, RaycastHit hitInfo) RunThickHitDetection(Vector3 position, Vector3 velocity, float deltaTime)
        {
            // For thick bullets, first do collision detection only on things considered targets.
            int hitCount = Physics.SphereCastNonAlloc(
                origin: position,
                direction: velocity.normalized,
                radius: BulletDiameter * .5f,
                maxDistance: BulletLength + velocity.magnitude * deltaTime,
                results: raycastHits,
                layerMask: ThickHitLayers);

            var (bulletHitSomething, closestHit) = GetClosestValidHit(raycastHits, hitCount);
            if (!bulletHitSomething)
            {
                // If the bullet didn't hit anything, then do normal raycast style hit detection
                // against other objects that we don't care about having generous hit detection.
                // This typically prevents unusual looking hit detection against large objects like
                // terrain or buildings.
                hitCount = Physics.RaycastNonAlloc(
                    origin: position,
                    direction: velocity.normalized,
                    maxDistance: BulletLength + velocity.magnitude * deltaTime,
                    layerMask: RayHitLayers,
                    results: raycastHits);

                (bulletHitSomething, closestHit) = GetClosestValidHit(raycastHits, hitCount);
            }

            return (bulletHitSomething, closestHit);
        }

        private (bool hitSomething, RaycastHit hitInfo) RunRayHitDetection(Vector3 position, Vector3 velocity, float deltaTime)
        {
            int hitCount = Physics.RaycastNonAlloc(
                origin: position,
                direction: velocity,
                maxDistance: BulletLength + velocity.magnitude * deltaTime,
                layerMask: ThickHitLayers | RayHitLayers,
                results: raycastHits);

            return GetClosestValidHit(raycastHits, hitCount);
        }

        private (bool hitSomething, RaycastHit closestHit) GetClosestValidHit(RaycastHit[] listOfHits, int hitCount)
        {
            if (hitCount == 0)
                return (false, new RaycastHit());

            RaycastHit closestHit = new RaycastHit();
            float closestDistance = float.MaxValue;
            bool hitSomething = false;

            if (IsHitAllowed(listOfHits[0]))
            {
                closestHit = listOfHits[0];
                closestDistance = listOfHits[0].distance;
                hitSomething = true;
            }

            for (int i = 0; i < hitCount; ++i)
            {
                if (IsHitAllowed(listOfHits[i]))
                {
                    if (listOfHits[i].distance < closestDistance)
                    {
                        closestDistance = listOfHits[i].distance;
                        closestHit = listOfHits[i];
                        hitSomething = true;
                    }
                }
            }

            return (hitSomething, closestHit);
        }

        private void CleanUpTrails()
        {
            foreach (var trail in ChildTrails)
            {
                trail.emitting = false;
                trail.autodestruct = true;
                trail.transform.SetParent(null);
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!ShowDebugVisuals)
                return;

            Gizmos.matrix = transform.localToWorldMatrix;

            Gizmos.DrawLine(Vector3.right, Vector3.left);
            Gizmos.DrawLine(Vector3.up, Vector3.down);
            Gizmos.DrawLine(Vector3.zero, transform.forward * BulletLength);

            var bulletHead = new Vector3(0f, 0f, BulletLength);
            Gizmos.DrawLine(bulletHead + Vector3.right, bulletHead + Vector3.right);
            Gizmos.DrawLine(bulletHead + Vector3.up, bulletHead + Vector3.down);

            Gizmos.matrix = Matrix4x4.identity;

            if (IsThick)
            {
                var velocity = MoveInFixedUpdate ? Velocity * Time.fixedDeltaTime : Velocity * Time.deltaTime;

                Gizmos.color = Color.red;
                Gizmos.DrawLine(transform.position - velocity, transform.position);
                Gizmos.DrawWireSphere(transform.position, BulletDiameter * .5f);

                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(transform.position + velocity, transform.position);
                Gizmos.DrawWireSphere(transform.position + velocity, BulletDiameter * .5f);
            }
            else
            {
                var velocity = MoveInFixedUpdate ? Velocity * Time.fixedDeltaTime : Velocity * Time.deltaTime;

                Gizmos.color = Color.red;
                Gizmos.DrawLine(transform.position - velocity, transform.position);

                Gizmos.color = Color.yellow;
                Gizmos.DrawLine(transform.position + velocity, transform.position);
            }
        }
#endif
    }
}
