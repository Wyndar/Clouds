using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

public class CloudRenderFeature : ScriptableRendererFeature
{
    class CloudRenderPass : ScriptableRenderPass
    {
        static Material copyMaterial;
        static Material CopyMaterial
        {
            get
            {
                if (copyMaterial == null)
                    copyMaterial = new Material(Shader.Find("Hidden/CopyTexture"));
                return copyMaterial;
            }
        }

        class PassData
        {
            public TextureHandle source;
            public Material material;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            var cameraData = frameData.Get<UniversalCameraData>();
            var camera = cameraData.camera;
            if (camera == null)
                return;

            var cloudMaster = camera.GetComponent<CloudMaster>();
            if (cloudMaster == null)
                return;

            if (!cloudMaster.TryPrepareMaterial(out var cloudMaterial))
                return;

            var resources = frameData.Get<UniversalResourceData>();
            TextureHandle cameraColor = resources.cameraColor;
            if (!cameraColor.IsValid())
                return;

            TextureDesc desc = renderGraph.GetTextureDesc(cameraColor);
            desc.clearBuffer = false;
            desc.name = "_CloudsTemp";
            TextureHandle temp = renderGraph.CreateTexture(desc);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>(
                "CopyCamera", out var passData, new ProfilingSampler("CopyCamera")))
            {
                passData.source = cameraColor;
                passData.material = CopyMaterial;

                builder.UseTexture(passData.source, AccessFlags.Read);
                builder.SetRenderAttachment(temp, 0);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc((PassData data, RasterGraphContext ctx) =>
                {
                    ctx.cmd.SetGlobalTexture("_SourceTex", data.source);
                    ctx.cmd.DrawProcedural(Matrix4x4.identity, data.material, 0, MeshTopology.Triangles, 3);
                });
            }

            using (var builder = renderGraph.AddRasterRenderPass<PassData>(
                "CloudRender", out var passData, new ProfilingSampler("CloudRender")))
            {
                passData.source = temp;
                passData.material = cloudMaterial;

                builder.UseTexture(passData.source, AccessFlags.Read);
                builder.SetRenderAttachment(cameraColor, 0);
                builder.AllowGlobalStateModification(true);

                builder.SetRenderFunc((PassData data, RasterGraphContext ctx) =>
                {
                    ctx.cmd.SetGlobalTexture("_MainTex", data.source);
                    ctx.cmd.DrawProcedural(Matrix4x4.identity, data.material, 0, MeshTopology.Triangles, 3);
                });
            }
        }
    }

    CloudRenderPass cloudPass;

    public override void Create()
    {
        cloudPass = new CloudRenderPass();
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        renderer.EnqueuePass(cloudPass);
    }
}
