using UnityEngine;

namespace Aegis
{
    // Turns an orbit ring slowly about its own axis.
    public class RingSpin : MonoBehaviour
    {
        public float degreesPerSecond = 4f;

        void Update()
        {
            transform.Rotate(Vector3.up, degreesPerSecond * Time.deltaTime, Space.Self);
        }
    }
}
