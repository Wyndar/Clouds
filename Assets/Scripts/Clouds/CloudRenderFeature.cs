using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.RenderGraphModule;

public class CloudRenderFeature : ScriptableRendererFeature {
    class CloudRenderPass : ScriptableRenderPass {
        readonly string profilerTag = "Cloud Render";
        RTHandle cameraColorTarget;
        RTHandle tempColorTarget;

        public CloudRenderPass () {
            renderPassEvent = RenderPassEvent.AfterRenderingSkybox;
        }

        public override void OnCameraSetup (CommandBuffer cmd, ref RenderingData renderingData) {
            cameraColorTarget = renderingData.cameraData.renderer.cameraColorTargetHandle;
            var descriptor = renderingData.cameraData.cameraTargetDescriptor;
            descriptor.depthBufferBits = 0;
            descriptor.msaaSamples = (int)MSAASamples.None;
            RenderingUtils.ReAllocateIfNeeded (ref tempColorTarget, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_CloudsTempTexture");
        }

        public override void Execute (ScriptableRenderContext context, ref RenderingData renderingData) {
            if (cameraColorTarget == null) {
                return;
            }

            var camera = renderingData.cameraData.camera;
            var cloudMaster = camera.GetComponent<CloudMaster> ();
            if (cloudMaster == null) {
                return;
            }

            if (!cloudMaster.TryPrepareMaterial (out var material)) {
                return;
            }

            var cmd = CommandBufferPool.Get (profilerTag);
            using (new ProfilingScope (cmd, new ProfilingSampler (profilerTag))) {
                cmd.SetRenderTarget (tempColorTarget);
                cmd.SetGlobalTexture ("_MainTex", cameraColorTarget);
                cmd.DrawMesh (RenderingUtils.fullscreenMesh, Matrix4x4.identity, material);

                Blitter.BlitCameraTexture (cmd, tempColorTarget, cameraColorTarget);
            }

            context.ExecuteCommandBuffer (cmd);
            CommandBufferPool.Release (cmd);
        }

        public override void OnCameraCleanup (CommandBuffer cmd) {
            cameraColorTarget = null;
        }

        class PassData {
            public TextureHandle cameraColor;
            public TextureHandle tempColor;
            public Material material;
        }

        public override void RecordRenderGraph (RenderGraph renderGraph, ContextContainer frameData) {
            if (renderGraph == null) {
                return;
            }

            var cameraData = frameData.Get<UniversalCameraData> ();
            var camera = cameraData.camera;
            if (camera == null) {
                return;
            }

            var cloudMaster = camera.GetComponent<CloudMaster> ();
            if (cloudMaster == null) {
                return;
            }

            if (!cloudMaster.TryPrepareMaterial (out var material)) {
                return;
            }

            var resourceData = frameData.Get<UniversalResourceData> ();
            var targetDescriptor = cameraData.cameraTargetDescriptor;
            targetDescriptor.depthBufferBits = 0;
            targetDescriptor.msaaSamples = (int)MSAASamples.None;

            var tempDesc = new TextureDesc (targetDescriptor.width, targetDescriptor.height) {
                colorFormat = targetDescriptor.graphicsFormat,
                depthBufferBits = DepthBits.None,
                msaaSamples = MSAASamples.None,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                name = "_CloudsTempTexture"
            };

            using (var builder = renderGraph.AddRasterRenderPass<PassData> (profilerTag, out var passData, new ProfilingSampler (profilerTag))) {
                var tempTexture = renderGraph.CreateTexture (tempDesc);
                passData.cameraColor = resourceData.activeColorTexture;
                passData.tempColor = tempTexture;
                builder.UseTexture (passData.cameraColor, AccessFlags.ReadWrite);
                builder.UseTexture (passData.tempColor, AccessFlags.ReadWrite);
                builder.SetRenderAttachment (passData.tempColor, 0);
                passData.material = material;

                builder.SetRenderFunc ((PassData data, RasterGraphContext context) => {
                    context.cmd.SetGlobalTexture ("_MainTex", data.cameraColor);
                    context.cmd.DrawMesh (RenderingUtils.fullscreenMesh, Matrix4x4.identity, data.material);

                    context.cmd.BlitTexture (data.tempColor, data.cameraColor);
                });
            }
        }

        public void Dispose () {
            tempColorTarget?.Release ();
        }
    }

    CloudRenderPass cloudPass;

    public override void Create () {
        cloudPass = new CloudRenderPass ();
    }

    public override void AddRenderPasses (ScriptableRenderer renderer, ref RenderingData renderingData) {
        if (cloudPass == null) {
            return;
        }

        renderer.EnqueuePass (cloudPass);
    }

    protected override void Dispose (bool disposing) {
        if (disposing) {
            cloudPass?.Dispose ();
        }
    }
}
