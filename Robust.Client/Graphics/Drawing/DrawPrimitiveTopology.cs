using OpenToolkit.Graphics.OpenGL4;

namespace Robust.Client.Graphics
{
    /// <summary>
    ///     Determines the type of primitives drawn and how they are laid out from vertices.
    ///     For performance reasons, we cheat and use the GL values here, don't rely on this.
    /// </summary>
    /// <remarks>
    ///     See <see href="https://www.khronos.org/registry/vulkan/specs/1.2-extensions/html/vkspec.html#drawing-point-lists">Vulkan's documentation</see> for descriptions of all these modes.
    /// </remarks>
    public enum DrawPrimitiveTopology
    {
        PointList = PrimitiveType.Points,
        TriangleList = PrimitiveType.Triangles,
        TriangleFan = PrimitiveType.TriangleFan,
        TriangleStrip = PrimitiveType.TriangleStrip,
        LineList = PrimitiveType.Lines,
        LineStrip = PrimitiveType.LineStrip,
        LineLoop = PrimitiveType.LineLoop
    }
}
