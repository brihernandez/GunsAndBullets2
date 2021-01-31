using UnityEngine;

namespace GNB.Demo
{
    public class SimpleRigidbodyMotion : MonoBehaviour
    {
        public float MoveForce = 100f;
        public float RotateTorque = 100f;

        public Rigidbody Rigidbody = null;

        private Vector3 bufferedMouseInput = Vector3.zero;

        private void Update()
        {
            bufferedMouseInput += new Vector3(
                x: -Input.GetAxis("Mouse Y"),
                y: Input.GetAxis("Mouse X"));
        }

        private void FixedUpdate()
        {
            var horizontal = Input.GetAxis("Horizontal");
            var vertical = Input.GetAxis("Vertical");

            Rigidbody.AddRelativeForce(
                x: horizontal * MoveForce,
                y: 0f,
                z: vertical * MoveForce,
                mode: ForceMode.Force);

            Rigidbody.AddRelativeTorque(
                x: bufferedMouseInput.x * RotateTorque,
                y: bufferedMouseInput.y * RotateTorque,
                z: 0f,
                mode: ForceMode.Force);

            bufferedMouseInput = Vector3.zero;
        }
    }
}
