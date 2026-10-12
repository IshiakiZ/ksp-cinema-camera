using System;
using System.Collections.Generic;
using UnityEngine;

namespace CineCamera
{
    /// <summary>
    /// How far off what is in the middle of the picture is, in the cockpit. The game draws the cockpit's insides in a space
    /// of their own (at their real size, see InternalSpace), and most of them have nothing a ray can hit: only what can be
    /// clicked has. So the ray from the kerbal's eye is tried against those first, and then against the cockpit's own
    /// pieces, as they are drawn: the boxes round them first (cheap), and then, for the few boxes it goes into, their
    /// triangles. A few times a second is enough for a focus ring that turns smoothly towards it.
    /// </summary>
    sealed class CockpitFocus
    {
        Renderer[] pieces;
        float piecesAt, measuredAt;
        float found = -1f;
        readonly Dictionary<Mesh, KeyValuePair<Vector3[], int[]>> shapes = new Dictionary<Mesh, KeyValuePair<Vector3[], int[]>>();
        readonly List<KeyValuePair<float, Renderer>> boxes = new List<KeyValuePair<float, Renderer>>();

        /// <summary>The distance to what the cockpit's camera looks at, in metres; nothing (less than nought) if it looks out through a window.</summary>
        public float Distance(Camera cockpit)
        {
            if (cockpit == null) return -1f;
            if (Time.unscaledTime < measuredAt) return found;
            measuredAt = Time.unscaledTime + 0.2f;
            Vector3 from = cockpit.transform.position, ahead = cockpit.transform.forward;
            float best = float.MaxValue;
            if (Physics.Raycast(from, ahead, out RaycastHit hit, 30f, (1 << 16) | (1 << 20), QueryTriggerInteraction.Collide)) best = hit.distance;
            if (InternalSpace.Instance != null && (pieces == null || Time.unscaledTime > piecesAt))
            {
                pieces = InternalSpace.Instance.GetComponentsInChildren<Renderer>();
                piecesAt = Time.unscaledTime + 3f;
            }
            if (pieces != null)
            {
                var ray = new Ray(from, ahead);
                boxes.Clear();
                foreach (Renderer piece in pieces)
                {
                    if (piece == null || !piece.enabled || !piece.gameObject.activeInHierarchy) continue;
                    // (glass, and anything else drawn see-through, is looked through: the windows' panes are there to be focused past)
                    Material skin = piece.sharedMaterial;
                    if (skin != null && skin.renderQueue >= 2900) continue;
                    Bounds box = piece.bounds;
                    // (a box round the eye itself, the kerbal's own head or the cockpit's walls, says nothing about what is ahead: its triangles do)
                    float into = 0f;
                    if (!box.Contains(from) && (!box.IntersectRay(ray, out into) || into > 30f || into >= best)) continue;
                    boxes.Add(new KeyValuePair<float, Renderer>(into, piece));
                }
                boxes.Sort((a, b) => a.Key.CompareTo(b.Key));
                int tried = 0;
                foreach (KeyValuePair<float, Renderer> box in boxes)
                {
                    if (box.Key >= best || tried++ > 12) break;
                    float d = Hit(box.Value, ray);
                    if (d > 0f && d < best) best = d;
                }
            }
            found = best < 30f ? best : -1f;
            return found;
        }

        /// <summary>Where the ray first meets the piece's triangles (in metres along it), or nothing (less than nought). A piece whose shape cannot be read counts as its box.</summary>
        float Hit(Renderer piece, Ray ray)
        {
            var filter = piece.GetComponent<MeshFilter>();
            Mesh mesh = filter != null ? filter.sharedMesh : null;
            if (mesh == null || !mesh.isReadable)
            {
                Bounds box = piece.bounds;
                return !box.Contains(ray.origin) && box.IntersectRay(ray, out float near) ? near : -1f;
            }
            if (!shapes.TryGetValue(mesh, out KeyValuePair<Vector3[], int[]> shape))
            {
                try { shape = new KeyValuePair<Vector3[], int[]>(mesh.vertices, mesh.triangles); }
                catch (Exception) { shape = new KeyValuePair<Vector3[], int[]>(new Vector3[0], new int[0]); }
                shapes[mesh] = shape;
            }
            Transform t = piece.transform;
            // (in the piece's own frame, where its triangles are; distances then turned back into metres along the ray)
            Vector3 o = t.InverseTransformPoint(ray.origin), d = t.InverseTransformVector(ray.direction);
            float scale = d.magnitude;
            if (scale < 1e-6f) return -1f;
            d /= scale;
            Vector3[] v = shape.Key;
            int[] tri = shape.Value;
            float nearest = float.MaxValue;
            for (int i = 0; i + 2 < tri.Length; i += 3)
            {
                // (Moller and Trumbore's test, either face)
                Vector3 a = v[tri[i]], e1 = v[tri[i + 1]] - a, e2 = v[tri[i + 2]] - a;
                Vector3 p = Vector3.Cross(d, e2);
                float det = Vector3.Dot(e1, p);
                if (det > -1e-9f && det < 1e-9f) continue;
                float inv = 1f / det;
                Vector3 s = o - a;
                float u = Vector3.Dot(s, p) * inv;
                if (u < 0f || u > 1f) continue;
                Vector3 q = Vector3.Cross(s, e1);
                float w = Vector3.Dot(d, q) * inv;
                if (w < 0f || u + w > 1f) continue;
                float at = Vector3.Dot(e2, q) * inv;
                if (at > 1e-4f && at < nearest) nearest = at;
            }
            return nearest < float.MaxValue ? nearest / scale : -1f;
        }

#if DEV
        public string Said => "cockpit focus: " + (pieces != null ? pieces.Length : 0) + " pieces, " + boxes.Count + " boxes on the line last time, " + shapes.Count + " shapes read, found " + found.ToString("F3") + " m";
#endif

        public void Forget()
        {
            pieces = null;
            shapes.Clear();
            found = -1f;
            measuredAt = 0f;
        }
    }
}
