using System.Collections.Generic;
using UnityEngine;

#if UNITY_EDITOR
using UnityEditor;
#endif

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
        [Min(0)] public float TimeToLive = 5f;
        [Tooltip("The faster the bullet goes, the harder drag pushes to slow it down. Higher values make for a bullet that slows down faster.")]
        [Min(0)] public float Drag = 0;
        [Tooltip("Gravity applied to the bullet where 1 is normal gravity.")]
        public float GravityModifier = 0f;
        [Tooltip("When true, the bullet automatically aligns itself to its velocity. Useful in arcing motions.")]
        public bool AlignToVelocity = false;
        [Tooltip("Length of bullet assuming the origin is the \"tail\" and a BulletLength's distance forwards is the \"head\".")]
        [Min(0)] public float BulletLength = 1f;
        [Tooltip("This should be set to true when using physics based projects.")]
        [SerializeField] private bool MoveInFixedUpdate = true;
        [Tooltip("Moves the bullet using a Rigidbody instead of the transform. This is useful if you want to move the bullet in fixed, but use Rigidbody interpolation in order to keep the visuals smooth. The Rigidbody will be forced to kinematic.")]
        [SerializeField] private Rigidbody Rigidbody = null;

        [Header("Thick Bullets")]
        [Tooltip("Use thick hit detection for the bullet. This is run in addition to normal hit detection.")]
        public bool IsThick = false;
        [Tooltip("The layers the bullet will hit using thick hit detection.")]
        public LayerMask ThickHitLayers = 0;
        [Tooltip("Used only when thick hit detection is enabled.")]
        [Min(0)] public float BulletDiameter = 1f;

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

        private bool hasRigidbody = false;

        public Vector3 Acceleration { get; private set; } = Vector3.zero;
        public Vector3 Velocity { get; private set; } = Vector3.zero;
        public float SecondsSinceFired { get; private set; } = 0f;
        public bool IsFired { get; private set; } = false;

        private void Awake()
        {
            hasRigidbody = Rigidbody != null;
            if (hasRigidbody)
                Rigidbody.isKinematic = true;

            if (hasRigidbody && !MoveInFixedUpdate)
                Debug.LogWarning($"Bullet {name} has a Rigidbody, but the bullet is not set to move in FixedUpdate! This can cause unusual behavior and stuttering movement.)", this);
        }

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
            if (hasRigidbody)
                Rigidbody.position = position;

            // Calculate a random deviation.
            Vector3 deviationAngle = Vector3.zero;
            deviationAngle.x = Random.Range(-deviation, deviation);
            deviationAngle.y = Random.Range(-deviation, deviation);
            Quaternion deviationRotation = Quaternion.Euler(deviationAngle);

            // Rotate the bullet to the direction requested, plus some random deviation.
            var finalRotation = rotation * deviationRotation;
            transform.rotation = finalRotation;
            if (hasRigidbody)
                Rigidbody.rotation = finalRotation;

            Velocity = (transform.forward * muzzleVelocity) + inheritedVelocity;
            Acceleration = CalculateAcceleration(Velocity, Physics.gravity.y);
            IsFired = true;
        }

        public Vector3 CalculateAcceleration(Vector3 velocity, float worldGravity)
        {
            // The drag used here is very basic and not really realistic linear drag, but if you
            // prefer to change the drag model, this is the place to do it.
            return new Vector3(
                -velocity.x * Drag,
                -velocity.y * Drag + worldGravity * GravityModifier,
                -velocity.z * Drag);
        }

        /// <summary>
        /// Calculates the motion of the bullet given the starting position and velocity.
        /// Returns a tuple of the resulting position and velocity.
        /// </summary>
        /// <param name="substep">Typically should be left at a value of 1. Used only if <see cref="Drag"/>
        /// is >0. When doing prediction, it can be helpful to raise the substeps if the given
        /// deltaTime does not match the bullets usual timesteps.</param>
        /// <param name="deltaTime">The time to simulate forwards. The smaller this value, the more
        /// accurate the result.</param>
        public (Vector3 position, Vector3 velocity, Vector3 acceleration) CalculateBulletMotion(Vector3 position, Vector3 velocity, Vector3 acceleration, float deltaTime, int substep = 1)
        {
            float worldGravity = Physics.gravity.y;

            if (GravityModifier > 0 || Drag > 0)
            {
                // Bullets with gravity or drag can be done much more accurately, with much larger
                // timesteps, using Velocity Verlet.
                // https://en.wikipedia.org/wiki/Verlet_integration#Algorithmic_representation

                if (Drag > 0)
                {
                    // Drag is much less robust against lower timesteps for prediction, so there's
                    // some additional math to be done, and optional substepping
                    var substepsToUse = Mathf.Max(1, substep);
                    float substepDeltaTime = deltaTime / substepsToUse;
                    for (int i = 0; i < substepsToUse; i++)
                    {
                        var newPosition = position + velocity * substepDeltaTime + acceleration * (substepDeltaTime * substepDeltaTime * 0.5f);
                        position = newPosition;
                        // Velocity verlet only works correctly when acceleration isn't dependent on
                        // velocity. To approximate a changing acceleration (i.e. due to drag), the
                        // acceleration must be predicted next frame which requires a guess of what the
                        // velocity next frame is going to be as well.
                        var futureAcceleration = CalculateAcceleration(velocity, worldGravity);
                        var newAcceleration = CalculateAcceleration(velocity + futureAcceleration * substepDeltaTime, worldGravity);
                        var newVelocity = velocity + (acceleration + newAcceleration) * (substepDeltaTime * 0.5f);
                        velocity = newVelocity;
                        acceleration = newAcceleration;
                    }
                }
                else
                {
                    // When a bullet has only gravity, traditional Velocity Verlet is good enough.
                    var newPosition = position + velocity * deltaTime + acceleration * (deltaTime * deltaTime * 0.5f);
                    var newAcceleration = CalculateAcceleration(velocity, worldGravity);
                    var newVelocity = velocity + (acceleration + newAcceleration) * (deltaTime * 0.5f);
                    position = newPosition;
                    velocity = newVelocity;
                    acceleration = newAcceleration;
                }
            }
            else
            {
                // For bullets with no gravity or drag,
                acceleration = CalculateAcceleration(velocity, 0);
                velocity += acceleration * deltaTime;
                position += velocity * deltaTime;
            }

            return (position, velocity, acceleration);
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
            if (rigidbody)
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
            {
                if (rigidbody)
                    ignoredRigidbodies.Add(rigidbody);
            }
        }

        /// <summary>
        /// Prevents collision the given collider. Commonly used to prevent a gun from shooting its
        /// owner. When possible, prefer <see cref="AddIgnoredRigidbody(Rigidbody)"/> rather than
        /// naming individual colliders.
        /// </summary>
        public void AddIgnoredCollider(Collider collider)
        {
            if (collider)
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
            {
                if (collider)
                    ignoredColliders.Add(collider);
            }
        }

        /// <summary>
        /// Explodes the bullet. Typically used for air bursting explosive weapons.
        /// </summary>
        public void ExplodeBullet(Vector3 explodePosition, Quaternion explodeRotation)
        {
            if (ExplodeFXPrefab)
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
            if (ImpactFXPrefab)
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
                    var (position, velocity, acceleration) = CalculateBulletMotion(
                        transform.position,
                        Velocity, Acceleration,
                        deltaTime);

                    if (hasRigidbody)
                        Rigidbody.MovePosition(position);
                    else
                        transform.position = position;
                    Velocity = velocity;
                    Acceleration = acceleration;

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
        private bool IsHitAllowed(in RaycastHit hit)
        {
            bool isHitAllowed = true;

            var hitRigidbody = hit.rigidbody;
            if (hitRigidbody && ignoredRigidbodies.Count > 0 && ignoredRigidbodies.Contains(hitRigidbody))
                isHitAllowed = false;
            else if (ignoredColliders.Count > 0 && ignoredColliders.Contains(hit.collider))
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

            var (bulletHitSomething, closestHitIndex) = GetClosestValidHit(raycastHits, hitCount);
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

                (bulletHitSomething, closestHitIndex) = GetClosestValidHit(raycastHits, hitCount);
            }

            return (bulletHitSomething, raycastHits[closestHitIndex]);
        }

        private (bool hitSomething, RaycastHit hitInfo) RunRayHitDetection(Vector3 position, Vector3 velocity, float deltaTime)
        {
            int hitCount = Physics.RaycastNonAlloc(
                origin: position,
                direction: velocity,
                maxDistance: BulletLength + velocity.magnitude * deltaTime,
                layerMask: ThickHitLayers | RayHitLayers,
                results: raycastHits);

            var (hitSomething, closestHitIndex) = GetClosestValidHit(raycastHits, hitCount);
            return (hitSomething, raycastHits[closestHitIndex]);
        }

        private (bool hitSomething, int closestHit) GetClosestValidHit(in RaycastHit[] listOfHits, int hitCount)
        {
            if (hitCount == 0)
                return (false, 0);

            int closestIndex = 0;
            float closestDistance = float.MaxValue;
            bool hitSomething = false;

            for (int i = 0; i < hitCount; ++i)
            {
                if (IsHitAllowed(listOfHits[i]))
                {
                    if (listOfHits[i].distance < closestDistance)
                    {
                        closestDistance = listOfHits[i].distance;
                        closestIndex = i;
                        hitSomething = true;
                    }
                }
            }

            return (hitSomething, closestIndex);
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
            Gizmos.DrawLine(Vector3.zero, Vector3.forward * BulletLength);

            var bulletHead = new Vector3(0f, 0f, BulletLength);
            Gizmos.DrawLine(bulletHead - Vector3.right, bulletHead + Vector3.right);
            Gizmos.DrawLine(bulletHead + Vector3.up, bulletHead + Vector3.down);

            // Draw a representative capsule for thick bullets in local space.
            if (IsThick)
            {
                var radius = BulletDiameter / 2;

                var oldHandleMatrix = Handles.matrix;
                var oldHandleColor = Handles.color;

                Handles.matrix = Gizmos.matrix;
                Handles.color = Gizmos.color;

                Handles.DrawWireArc(Vector3.zero, Vector3.up, Vector3.right, 180, radius);
                Handles.DrawWireArc(Vector3.zero, Vector3.right, Vector3.up, -180, radius);

                Handles.DrawWireDisc(Vector3.zero, Vector3.forward, radius);
                Handles.DrawWireDisc(bulletHead, Vector3.forward, radius);

                Handles.DrawWireArc(bulletHead, Vector3.up, Vector3.right, -180, radius);
                Handles.DrawWireArc(bulletHead, Vector3.right, Vector3.up, 180, radius);

                Handles.DrawLine(new Vector3(radius, 0, 0), new Vector3(radius, 0, BulletLength));
                Handles.DrawLine(new Vector3(0, radius, 0), new Vector3(0, radius, BulletLength));
                Handles.DrawLine(new Vector3(-radius, 0, 0), new Vector3(-radius, 0, BulletLength));
                Handles.DrawLine(new Vector3(0, -radius, 0), new Vector3(0, -radius, BulletLength));

                Handles.matrix = oldHandleMatrix;
                Handles.color = oldHandleColor;
            }

            Gizmos.matrix = Matrix4x4.identity;

            // Lines to represent the bullet in motion.
            var velocity = MoveInFixedUpdate ? Velocity * Time.fixedDeltaTime : Velocity * Time.deltaTime;

            // Red lines show the distance covered by the bullet last frame.
            Gizmos.color = Color.red;
            Gizmos.DrawLine(transform.position - velocity, transform.position);

            // Yellow lines show where the bullet will move on the next frame.
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(transform.position + velocity, transform.position);
        }
#endif
    }
}
