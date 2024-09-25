namespace Robust.Client.Graphics.Clyde
{
    internal sealed partial class Clyde
    {
        // Setup in PostInject.
        private ClydeDebugStats _debugStats = default!;

        private sealed record ClydeDebugInfo(
            OpenGLVersion OpenGLVersion,
            string Renderer,
            string Vendor,
            string VersionString,
            bool Overriding,
            string WindowingApi) : IClydeDebugInfo;

        private sealed class ClydeDebugStats(PAL pal) : IClydeDebugStats
        {
            private readonly PAL _pal = pal;

            public int LastGLDrawCalls => _pal.LastGLDrawCalls;
            public int LastRenderStateResets => _pal.LastRenderStateResets;
            public int LastClydeDrawCalls { get; set; }
            public int LastBatches { get; set; }
            public (int vertices, int indices) LargestBatchSize => (LargestBatchVertices, LargestBatchIndices);
            public int LargestBatchVertices { get; set; }
            public int LargestBatchIndices { get; set; }
            public int TotalLights { get; set; }
            public int ShadowLights { get; set; }
            public int Occluders { get; set; }
            public int Entities { get; set; }

            public void Reset()
            {
                _pal.LastGLDrawCalls = 0;
                _pal.LastRenderStateResets = 0;
                LastClydeDrawCalls = 0;
                LastBatches = 0;
                LargestBatchVertices = 0;
                LargestBatchIndices = 0;
                TotalLights = 0;
                ShadowLights = 0;
                Occluders = 0;
                Entities = 0;
            }
        }
    }
}
