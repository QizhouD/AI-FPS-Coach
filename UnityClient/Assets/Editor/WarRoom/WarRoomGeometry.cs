using UnityEditor;
using UnityEngine;

namespace FpsAiCoach.Editor
{
    /// <summary>
    /// Primitive helpers for the war room. Every primitive loses its collider on creation: the set is
    /// pure décor with no physics of its own, and UI input goes through the canvases' graphic raycaster
    /// rather than a physics raycast, so a collider here would cost broadphase work and earn nothing.
    /// </summary>
    internal static class WarRoomGeometry
    {
        public static GameObject Group(string name, Transform parent, Vector3 localPosition = default)
        {
            var group = new GameObject(name);
            group.transform.SetParent(parent, false);
            group.transform.localPosition = localPosition;
            return group;
        }

        public static GameObject Box(
            string name,
            Transform parent,
            Vector3 localPosition,
            Vector3 localScale,
            Material material,
            bool castShadows = false,
            bool receiveShadows = false)
        {
            return Primitive(
                PrimitiveType.Cube,
                name,
                parent,
                localPosition,
                localScale,
                material,
                castShadows,
                receiveShadows);
        }

        public static GameObject Sphere(
            string name,
            Transform parent,
            Vector3 localPosition,
            float diameter,
            Material material)
        {
            return Primitive(
                PrimitiveType.Sphere,
                name,
                parent,
                localPosition,
                Vector3.one * diameter,
                material,
                false,
                false);
        }

        /// <summary>Thin bar running along X. Length and thickness are in metres.</summary>
        public static GameObject BarX(
            string name,
            Transform parent,
            Vector3 center,
            float length,
            float thickness,
            Material material)
        {
            return Box(
                name,
                parent,
                center,
                new Vector3(length, thickness, thickness),
                material);
        }

        /// <summary>Thin bar running along Y.</summary>
        public static GameObject BarY(
            string name,
            Transform parent,
            Vector3 center,
            float length,
            float thickness,
            Material material)
        {
            return Box(
                name,
                parent,
                center,
                new Vector3(thickness, length, thickness),
                material);
        }

        /// <summary>Thin bar running along Z, used for the floor rails.</summary>
        public static GameObject BarZ(
            string name,
            Transform parent,
            Vector3 center,
            float length,
            float thickness,
            Material material)
        {
            return Box(
                name,
                parent,
                center,
                new Vector3(thickness, thickness, length),
                material);
        }

        /// <summary>Flags immobile geometry for static batching.</summary>
        public static void MarkStatic(GameObject target)
        {
            GameObjectUtility.SetStaticEditorFlags(
                target,
                StaticEditorFlags.BatchingStatic |
                StaticEditorFlags.OccluderStatic |
                StaticEditorFlags.OccludeeStatic);
        }

        public static void MarkStaticRecursive(GameObject root)
        {
            MarkStatic(root);
            foreach (Transform child in root.transform)
                MarkStaticRecursive(child.gameObject);
        }

        private static GameObject Primitive(
            PrimitiveType type,
            string name,
            Transform parent,
            Vector3 localPosition,
            Vector3 localScale,
            Material material,
            bool castShadows,
            bool receiveShadows)
        {
            var instance = GameObject.CreatePrimitive(type);
            instance.name = name;
            instance.transform.SetParent(parent, false);
            instance.transform.localPosition = localPosition;
            instance.transform.localScale = localScale;

            var renderer = instance.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = castShadows
                    ? UnityEngine.Rendering.ShadowCastingMode.On
                    : UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = receiveShadows;
                renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            }

            var collider = instance.GetComponent<Collider>();
            if (collider != null)
                Object.DestroyImmediate(collider);

            return instance;
        }
    }
}
