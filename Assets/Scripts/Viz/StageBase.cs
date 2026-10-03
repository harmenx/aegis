using UnityEngine;

namespace Gitmos
{
    public abstract class StageBase : MonoBehaviour
    {
        public float Alpha { get; private set; }

        public virtual void SetAlpha(float alpha)
        {
            Alpha = Mathf.Clamp01(alpha);
        }

        protected void PlaceInFrontOf(Camera cam, float distance)
        {
            if (cam == null)
                return;
            Transform camTransform = cam.transform;
            transform.position = camTransform.position + camTransform.forward * distance;
            transform.rotation = Quaternion.LookRotation(camTransform.forward, camTransform.up);
        }

        protected static Mesh BuildQuad(float width, float height)
        {
            var mesh = new Mesh { name = "Quad" };
            float x = width * 0.5f;
            float y = height * 0.5f;
            mesh.vertices = new[]
            {
                new Vector3(-x, -y, 0f),
                new Vector3(-x, y, 0f),
                new Vector3(x, y, 0f),
                new Vector3(x, -y, 0f)
            };
            mesh.uv = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                new Vector2(1f, 0f)
            };
            mesh.colors = new[] { Color.white, Color.white, Color.white, Color.white };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
