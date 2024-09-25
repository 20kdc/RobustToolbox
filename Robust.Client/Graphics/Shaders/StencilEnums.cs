using OpenToolkit.Graphics.OpenGL4;
using TKStencilOp = OpenToolkit.Graphics.OpenGL4.StencilOp;

namespace Robust.Client.Graphics
{
    /// <summary>
    /// Stencil test function.
    /// For performance reasons, this maps to OpenGL enums. Don't abuse this.
    /// </summary>
    public enum StencilFunc
    {
        Always = StencilFunction.Always,
        Never = StencilFunction.Never,
        Less = StencilFunction.Less,
        LessOrEqual = StencilFunction.Lequal,
        Greater = StencilFunction.Greater,
        GreaterOrEqual = StencilFunction.Gequal,
        NotEqual = StencilFunction.Notequal,
        Equal = StencilFunction.Equal,
    }

    public enum StencilOp
    {
        Keep = TKStencilOp.Keep,
        Zero = TKStencilOp.Zero,
        Replace = TKStencilOp.Replace,
        IncrementClamp = TKStencilOp.Incr,
        IncrementWrap = TKStencilOp.IncrWrap,
        DecrementClamp = TKStencilOp.Decr,
        DecrementWrap = TKStencilOp.DecrWrap,
        Invert = TKStencilOp.Invert
    }

    /// <summary>
    /// Represents a blending factor.
    /// For performance reasons, we cheat and use the GL values here, don't rely on this.
    /// </summary>
    public enum BlendFactor
    {
        Zero = BlendingFactor.Zero,
        One = BlendingFactor.One,
        SrcColor = BlendingFactor.SrcColor,
        OneMinusSrcColor = BlendingFactor.OneMinusSrcColor,
        DstColor = BlendingFactor.DstColor,
        OneMinusDstColor = BlendingFactor.OneMinusDstColor,
        SrcAlpha = BlendingFactor.SrcAlpha,
        OneMinusSrcAlpha = BlendingFactor.OneMinusSrcAlpha,
        DstAlpha = BlendingFactor.DstAlpha,
        OneMinusDstAlpha = BlendingFactor.OneMinusDstAlpha,
        ConstantColor = BlendingFactor.ConstantColor,
        OneMinusConstantColor = BlendingFactor.OneMinusConstantColor,
        ConstantAlpha = BlendingFactor.ConstantAlpha,
        OneMinusConstantAlpha = BlendingFactor.OneMinusConstantAlpha,
        // Not accepted in destination field.
        SrcAlphaSaturate = BlendingFactor.SrcAlphaSaturate
    }

    /// <summary>
    /// Represents a blending equation.
    /// For performance reasons, we cheat and use the GL values here, don't rely on this.
    /// </summary>
    public enum BlendEquation
    {
        Add = BlendEquationMode.FuncAdd,
        Subtract = BlendEquationMode.FuncSubtract,
        ReverseSubtract = BlendEquationMode.FuncReverseSubtract
    }

    /// <summary>
    /// Depth test function.
    /// For performance reasons, this maps to OpenGL enums. Don't abuse this.
    /// </summary>
    public enum DepthFunc
    {
        Always = DepthFunction.Always,
        Never = DepthFunction.Never,
        Less = DepthFunction.Less,
        LessOrEqual = DepthFunction.Lequal,
        Greater = DepthFunction.Greater,
        GreaterOrEqual = DepthFunction.Gequal,
        NotEqual = DepthFunction.Notequal,
        Equal = DepthFunction.Equal,
    }

}
