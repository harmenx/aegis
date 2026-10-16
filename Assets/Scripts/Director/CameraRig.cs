using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Gitmos
{
    [DefaultExecutionOrder(-30)]
    public class CameraRig : MonoBehaviour
    {
        public Transform target;
        public DashboardController dashboard;
        public float distance = 11.5f;
        public float minDistance = 3.35f;
        public float maxDistance = 14f;
        public float yaw = 16f;
        public float pitch = 10f;

        float idleTimer = 4f;

        void Start()
        {
            EnsureGrade();
        }

        void EnsureGrade()
        {
            Volume volume = FindFirstObjectByType<Volume>();
            if (volume == null)
                return;
            VolumeProfile profile = volume.profile;
            if (profile == null)
                return;
            profile.components.RemoveAll(component => component == null);
            if (!profile.TryGet(out Bloom bloom))
                bloom = profile.Add<Bloom>();
            bloom.active = true;
            bloom.threshold.Override(0.55f);
            bloom.intensity.Override(1.15f);
            bloom.scatter.Override(0.62f);
            bloom.tint.Override(new Color(0.8f, 0.88f, 1f));
            bloom.highQualityFiltering.Override(true);

            if (!profile.TryGet(out Vignette vignette))
                vignette = profile.Add<Vignette>();
            vignette.active = true;
            vignette.intensity.Override(0.28f);
            vignette.smoothness.Override(0.45f);
            vignette.color.Override(Color.black);
        }

        void LateUpdate()
        {
            Vector2 pointer = Input.mousePosition;
            bool overUi = dashboard != null && dashboard.PointerOverUi(pointer);

            if (Input.GetMouseButton(0) && !overUi)
            {
                yaw += Input.GetAxis("Mouse X") * 2.2f;
                pitch -= Input.GetAxis("Mouse Y") * 1.5f;
                pitch = Mathf.Clamp(pitch, -18f, 50f);
                idleTimer = 0f;
            }
            else
            {
                idleTimer += Time.deltaTime;
                if (idleTimer > 1.4f)
                    yaw += 5.5f * Time.deltaTime;
            }

            float scroll = Input.mouseScrollDelta.y;
            if (Mathf.Abs(scroll) > 0.001f && !overUi)
                distance = Mathf.Clamp(distance - scroll * 0.62f, minDistance, maxDistance);

            Vector3 look = target != null ? target.position : Vector3.zero;
            Quaternion orbit = Quaternion.Euler(pitch, yaw, 0f);
            transform.position = look + orbit * new Vector3(0f, 0.2f, -distance);
            transform.LookAt(look + Vector3.up * 0.05f);

            float dolly = Mathf.Clamp01(Mathf.InverseLerp(11.5f, minDistance, distance));
            if (dashboard != null)
                dashboard.SetChromeOpacity(Mathf.Lerp(1f, 0.04f, Mathf.SmoothStep(0.05f, 0.9f, dolly)));
        }
    }
}
