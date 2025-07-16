using System.Reflection;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace InternalBridge
{
    // TODO: Move to shared!
    public static class UniversalRendererInternal
    {
        private static readonly FieldInfo m_OpaqueColor = typeof(UniversalRenderer).GetField(
            "m_OpaqueColor",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        private static readonly FieldInfo m_DepthTexture = typeof(UniversalRenderer).GetField(
            "m_DepthTexture",
            BindingFlags.NonPublic | BindingFlags.Instance
        ) ?? typeof(UniversalRenderer).GetField(
            "m_DepthAttachmentHandle",
            BindingFlags.NonPublic | BindingFlags.Instance
        );

        public static RTHandle GetDepthTexture(this UniversalRenderer renderer)
        {
            if (m_DepthTexture != null)
            {
                return (RTHandle) m_DepthTexture.GetValue(renderer);
            }
            return null;
        }

        // TODO: Use with [UnsafeAccessor] when Unity start supporting .NET8
        public static RTHandle GetOpaqueTexture(this ScriptableRenderer renderer)
        {
            return (RTHandle) m_OpaqueColor.GetValue(renderer);
        }
        
        // TODO: Use with [UnsafeAccessor] when Unity start supporting .NET8
        public static RTHandle GetGBuffer(this ScriptableRenderer renderer, int index)
        {
            if (renderer is UniversalRenderer r && r.deferredLights != null)
            {
                var deferred = r.deferredLights;
                var type = deferred.GetType();
                var field = type.GetField("GbufferAttachments", BindingFlags.NonPublic | BindingFlags.Instance)
                            ?? type.GetField("GbufferAttachmentHandles", BindingFlags.NonPublic | BindingFlags.Instance)
                            ?? type.GetField("m_GbufferAttachments", BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null && field.GetValue(deferred) is RTHandle[] handles && index >= 0 && index < handles.Length)
                {
                    return handles[index];
                }
            }
            return null;
        }
    }
}
