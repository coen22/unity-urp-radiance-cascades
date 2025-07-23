using UnityEngine;
using UnityEngine.Rendering;
#if UNITY_6000_1_OR_NEWER
using UnityEngine.Rendering.RenderGraphModule;
#endif

namespace AlexMalyutinDev.RadianceCascades
{
#if UNITY_6000_1_OR_NEWER
    public static class RenderGraphUtils
    {
        private static readonly int BlitTextureId = Shader.PropertyToID("_BlitTexture");

        public static void BlitTexture(RasterGraphContext ctx, TextureHandle texture, Material material, int pass)
        {
            ctx.cmd.SetGlobalTexture(BlitTextureId, texture); 
            ctx.cmd.DrawMesh(BlitUtils.GetQuadMesh(), Matrix4x4.identity, material, 0, pass);
        }
    }
#endif
}
