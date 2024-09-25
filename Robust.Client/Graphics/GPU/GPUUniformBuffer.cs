using JetBrains.Annotations;

namespace Robust.Client.Graphics;

/// <summary>
/// This is a generic interface for any kind of uniform-buffer-like data.
/// It represents the boundary between PAL's UBO emulation logic and the UBO contents.
/// It's even user-extendable, in theory.
/// </summary>
[PublicAPI]
public abstract class GPUUniformBufferBase : GPUResource
{
    protected IGPUAbstraction GPU { get; }

    /// <summary>Version number; unique ID of current contents, essentially.</summary>
    public ulong Version { get; private set; }

    /// <summary>UBO implementation.</summary>
    internal GPUBuffer? Buffer { get; }

    /// <summary>If UBOs are available, you must supply a buffer; otherwise, don't.</summary>
    public GPUUniformBufferBase(IGPUAbstraction gpu, GPUBuffer? buffer)
    {
        GPU = gpu;
        Version = gpu.AllocateUniformBufferVersion();
        Buffer = buffer;
    }

    /// <summary>Indicates the contents of this object have been dirtied.</summary>
    public void Dirty()
    {
        if (Buffer != null)
        {
            ApplyIntoUBO(Buffer);
        }
        else
        {
            Version = GPU.AllocateUniformBufferVersion();
        }
    }

    /// <summary>'Applies' current contents into a GPUBuffer (UBOs).</summary>
    protected abstract void ApplyIntoUBO(GPUBuffer buffer);

    /// <summary>'Applies' current contents into a GPUShaderProgram (emulator).</summary>
    public abstract void ApplyIntoShader(GPUShaderProgram program);

    protected override sealed void DisposeImpl()
    {
        Buffer?.Dispose();
    }
}

/// <summary>
///     Represents some set of uniforms that can be backed by a uniform buffer or by regular uniforms.
///     Implies you're using this on a properly setup struct.
/// </summary>
public interface IAppliableUniformSet
{
    /// <summary>
    ///     Applies the uniform set directly to a program.
    /// </summary>
    void Apply(GPUShaderProgram program);
}

/// <summary>Actual uniform buffer implementation.</summary>
[PublicAPI]
public sealed class GPUUniformBuffer<T> : GPUUniformBufferBase where T : unmanaged, IAppliableUniformSet
{
    private T _value;

    public T Value
    {
        get => _value;
        set
        {
            _value = value;
            Dirty();
        }
    }

    public GPUUniformBuffer(IGPUAbstraction gpu, T value, GPUBuffer.Usage usage, string? name = null) : base(gpu, gpu.HasUniformBuffers ? gpu.CreateBuffer(value, usage, name) : null)
    {
        _value = value;
    }

    public override void ApplyIntoShader(GPUShaderProgram program)
    {
        _value.Apply(program);
    }

    protected override void ApplyIntoUBO(GPUBuffer buffer)
    {
        buffer.Reallocate(_value);
    }
}
